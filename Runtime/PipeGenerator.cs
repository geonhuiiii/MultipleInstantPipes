using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace InstantPipes
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshCollider))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PipeGenerator : MonoBehaviour
    {
        public int miter;
        public float Radius = 1;
        public int EdgeCount = 10;
        public int CurvedSegmentCount = 10;
        public float Curvature = 0.5f;
        public float Height = 5f;

        public bool HasRings;
        public bool HasExtrusion;
        public float RingThickness = 1;
        public float RingRadius = 1.3f;

        public bool HasCaps;
        public float CapThickness = 1;
        public float CapRadius = 1.3f;
        public float CapOffset = 0f;

        public int PipesAmount = 1;

        public Material Material;
        public Material RingMaterial;
        public float RingsUVScale = 1;
        public bool IsSeparateRingsMaterial = false;

        // 경로 생성 파라미터
        [Header("Path Finding Parameters")]
        public float GridSize = 3f;
        public float GridRotationY = 0f;
        public float Chaos = 0f;
        public float StraightPathPriority = 10f;
        public float NearObstaclesPriority = 0f;
        public int MaxIterations = 1000;
        public int MinDistanceBetweenBends = 3;
        
        [Header("Dynamic Grid Settings")]
        [Tooltip("자동으로 그리드 범위를 계산할지 여부")]
        public bool useAutomaticGridBounds = true;
        
        [Tooltip("수동 그리드 범위 (useAutomaticGridBounds가 false일 때 사용)")]
        public Vector3 manualGridCenter = Vector3.zero;
        public Vector3 manualGridSize = new Vector3(100f, 50f, 100f);
        
        [Tooltip("자동 그리드 범위의 여백 (파이프 범위에 추가되는 크기)")]
        public float gridPadding = 20f;
        
        [Tooltip("최소 그리드 크기 (각 축별)")]
        public Vector3 minGridSize = new Vector3(20f, 10f, 20f);
        
        [Tooltip("최대 그리드 크기 (각 축별, 성능 제한용)")]
        public Vector3 maxGridSize = new Vector3(500f, 100f, 500f);
        
        [Tooltip("장애물 검색 범위 (그리드 크기에 따라 자동 조정)")]
        public float obstacleSearchRangeMultiplier = 1.5f;

        // 호환성을 위한 searchRange 프로퍼티
        [System.Obsolete("Use GetCurrentSearchRange() method instead")]
        public float searchRange => GetCurrentSearchRange();

        private Renderer _renderer;
        private MeshCollider _collider;
        private Mesh _mesh;

        public List<Pipe> Pipes = new List<Pipe>();
        private float _maxDistanceBetweenPoints;
        public float MaxCurvature => _maxDistanceBetweenPoints / 2;

        public List<Material> PipeMaterials = new List<Material>();
        public List<float> PipeRadiuses = new List<float>();

        private void OnEnable()
        {
            _renderer = GetComponent<Renderer>();
            _collider = GetComponent<MeshCollider>();

            _mesh = new Mesh { name = "Pipes" };
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _collider.sharedMesh = _mesh;

            UpdateMesh();
        }

        public void UpdateMesh()
        {
            if (Pipes == null || Pipes.Count == 0)
            {
                // Clear mesh if no pipes
                if (_mesh != null) _mesh.Clear();
                if (_collider != null) _collider.sharedMesh = null;
                return;
            }

            _maxDistanceBetweenPoints = 0;
            
            // Make sure we disable the collider while updating to avoid issues
            if (_collider != null) _collider.sharedMesh = null;

            var allSubmeshes = new List<CombineInstance>();
            var allMaterials = new List<Material>();
            
            // Get source shader to use
            Shader shaderToUse = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (Material != null && Material.shader != null)
            {
                shaderToUse = Material.shader;
            }
            
            // Ensure we have enough materials and radii for all pipes
            while (PipeMaterials.Count < Pipes.Count)
            {
                PipeMaterials.Add(new Material(shaderToUse));
                PipeMaterials[PipeMaterials.Count - 1].name = $"Pipe_{PipeMaterials.Count - 1}_Material";
                // Copy default material properties if available
                if (Material != null)
                {
                    PipeMaterials[PipeMaterials.Count - 1].CopyPropertiesFromMaterial(Material);
                }
            }
            
            while (PipeRadiuses.Count < Pipes.Count)
            {
                PipeRadiuses.Add(Radius);
            }
            
            // Generate meshes for each pipe
            for (int i = 0; i < Pipes.Count; i++)
            {
                //Debug.Log($"Generating mesh for pipe {i} with {Pipes[i].Points.Count} points");
                // Check if pipe has valid points
                if (Pipes[i].Points == null || Pipes[i].Points.Count < 2)
                {
                    //Debug.LogWarning($"Pipe {i} has insufficient points, skipping");
                    continue;
                }
                
                // Validate pipe points
                bool hasInvalidPoints = false;
                foreach (var point in Pipes[i].Points)
                {
                    if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                        float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
                    {
                        hasInvalidPoints = true;
                        //Debug.LogError($"Pipe {i} contains invalid points: {point}");
                        break;
                    }
                }
                
                if (hasInvalidPoints)
                {
                    //Debug.LogWarning($"Pipe {i} has invalid points, skipping");
                    continue;
                }
                
                // Store original radius to restore after generating this pipe
                float pipeRadius = (i < PipeRadiuses.Count) ? PipeRadiuses[i] : Radius;
                float originalRadius = Radius;
                Radius = pipeRadius;
                
                // Clear temporary mesh for this pipe
                Mesh tempMesh = new Mesh { name = $"Pipe_{i}" };

                try
                {
                    // Generate sub-meshes
                    var pipeMeshes = Pipes[i].GenerateMeshes(this);
                    
                    // Check if meshes were generated successfully
                    if (pipeMeshes != null && pipeMeshes.Count > 0)
                    {
                        // Add all submeshes to our list
                        foreach (var mesh in pipeMeshes)
                        {
                            if (mesh.vertexCount > 0)
                            {
                                var combineInst = new CombineInstance {
                                    mesh = mesh,
                                    transform = Matrix4x4.identity
                                };
                                allSubmeshes.Add(combineInst);
                            }
                            else
                            {
                                //Debug.LogWarning($"Pipe {i} generated a mesh with 0 vertices")
                            }
                        }
                    }
                    else
                    {
                        //Debug.LogWarning($"Pipe {i} did not generate any meshes");
                    }
                    
                    _maxDistanceBetweenPoints = Mathf.Max(_maxDistanceBetweenPoints, Pipes[i].GetMaxDistanceBetweenPoints());
                }
                catch (System.Exception ex)
                {
                    //Debug.LogError($"Error generating mesh for pipe {i}: {ex.Message}\n{ex.StackTrace}");
                }
                
                // Restore original radius
                Radius = originalRadius;
                
                // Handle materials
                Material pipeMaterial = GetOrCreatePipeMaterial(i, shaderToUse);
                
                // Add materials based on submesh configuration
                if (IsSeparateRingsMaterial && (HasCaps || HasRings))
                {
                    allMaterials.Add(pipeMaterial);
                    
                    // Create a ring material if needed
                    Material ringMat = RingMaterial != null ? RingMaterial : pipeMaterial;
                    allMaterials.Add(ringMat);
                }
                else if (HasCaps || HasRings)
                {
                    allMaterials.Add(pipeMaterial);
                    allMaterials.Add(pipeMaterial);
                }
                else
                {
                    allMaterials.Add(pipeMaterial);
                }
            }
            
            // Combine all submeshes into the final mesh
            if (allSubmeshes.Count > 0)
            {
                try
                {
                    _mesh.Clear();
                    _mesh.CombineMeshes(allSubmeshes.ToArray(), false, false);
                    
                    // Check mesh integrity
                    if (_mesh.vertexCount > 0)
                    {
                        //Debug.Log($"Combined mesh with {_mesh.vertexCount} vertices, {_mesh.triangles.Length/3} triangles");
                        
                        // Update the collider
                        _collider.sharedMesh = _mesh;
                    }
                    else
                    {
                        //Debug.LogError("Combined mesh has 0 vertices!");
                    }
                }
                catch (System.Exception ex)
                {
                    //Debug.LogError($"Error combining meshes: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else
            {
                //Debug.LogWarning("No submeshes to combine!");
                _mesh.Clear();
            }
            
            // Update renderer materials
            //Debug.Log($"Setting {allMaterials.Count} materials");
            _renderer.sharedMaterials = allMaterials.ToArray();
        }

        private Material GetOrCreatePipeMaterial(int pipeIndex, Shader shaderToUse)
        {
            Material pipeMaterial = null;
            
            if (pipeIndex < PipeMaterials.Count && PipeMaterials[pipeIndex] != null)
            {
                pipeMaterial = PipeMaterials[pipeIndex];
            }
            else
            {
                // Use original material's shader if available, otherwise use the default
                if (Material != null)
                {
                    pipeMaterial = new Material(Material.shader != null ? Material.shader : shaderToUse);
                    pipeMaterial.CopyPropertiesFromMaterial(Material);
                }
                else
                {
                    pipeMaterial = new Material(shaderToUse);
                }
                
                pipeMaterial.name = $"Pipe_{pipeIndex}_Material";
                
                // Set common properties
                if (pipeMaterial.HasProperty("_BaseColor"))
                {
                    // URP shader properties
                    if (Material != null)
                    {
                        pipeMaterial.SetColor("_BaseColor", Material.HasProperty("_BaseColor") ? 
                            Material.GetColor("_BaseColor") : Material.color);
                    }
                    // Enable emission if available
                    if (pipeMaterial.HasProperty("_EmissionColor"))
                    {
                        pipeMaterial.EnableKeyword("_EMISSION");
                        pipeMaterial.SetColor("_EmissionColor", pipeMaterial.GetColor("_BaseColor") * 0.5f);
                    }
                }
                else if (pipeMaterial.HasProperty("_Color"))
                {
                    // Standard shader properties
                    if (Material != null)
                    {
                        pipeMaterial.SetColor("_Color", Material.HasProperty("_Color") ? 
                            Material.GetColor("_Color") : Material.color);
                    }
                    // Enable emission if available
                    if (pipeMaterial.HasProperty("_EmissionColor"))
                    {
                        pipeMaterial.EnableKeyword("_EMISSION");
                        pipeMaterial.SetColor("_EmissionColor", pipeMaterial.GetColor("_Color") * 0.5f);
                    }
                }
                
                // Update materials list
                if (pipeIndex < PipeMaterials.Count)
                {
                    PipeMaterials[pipeIndex] = pipeMaterial;
                }
                else
                {
                    PipeMaterials.Add(pipeMaterial);
                }
            }
            
            return pipeMaterial;
        }

        public bool AddPipe(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius = -1, Material material = null)
        {
            int newPipeIndex = Pipes.Count;
            
            // 파이프 반경 설정
            if (radius > 0)
            {
                while (PipeRadiuses.Count <= newPipeIndex)
                {
                    PipeRadiuses.Add(Radius);
                }
                PipeRadiuses[newPipeIndex] = radius;
                
                //Debug.Log($"Setting pipe radius: {radius} for pipe index: {newPipeIndex}");
            }
            
            // 임시 충돌체 생성
            var temporaryColliders = new List<GameObject>();
            var existingPipeColliders = new List<GameObject>();
            
            try
            {
                
                // 재질 생성
                Material newMaterial = null;
                
                // 소스 셰이더 가져오기
                Shader shaderToUse = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (Material != null && Material.shader != null)
                {
                    shaderToUse = Material.shader;
                }
                
                // 재질 설정
                if (material != null)
                {
                    // 새 매테리얼 인스턴스 생성
                    newMaterial = new Material(material.shader != null ? material.shader : shaderToUse);
                    newMaterial.name = $"Pipe_{newPipeIndex}_Material";
                    newMaterial.CopyPropertiesFromMaterial(material);
                    
                    if (newMaterial.HasProperty("_BaseColor") && material.HasProperty("_BaseColor"))
                    {
                        newMaterial.SetColor("_BaseColor", material.GetColor("_BaseColor"));
                        
                        if (newMaterial.HasProperty("_EmissionColor"))
                        {
                            newMaterial.EnableKeyword("_EMISSION");
                            newMaterial.SetColor("_EmissionColor", material.GetColor("_BaseColor") * 0.5f);
                        }
                    }
                    else if (newMaterial.HasProperty("_Color") && material.HasProperty("_Color"))
                    {
                        newMaterial.SetColor("_Color", material.GetColor("_Color"));
                        
                        if (newMaterial.HasProperty("_EmissionColor"))
                        {
                            newMaterial.EnableKeyword("_EMISSION");
                            newMaterial.SetColor("_EmissionColor", material.GetColor("_Color") * 0.5f);
                        }
                    }
                }
                else if (Material != null)
                {
                    newMaterial = new Material(Material.shader != null ? Material.shader : shaderToUse);
                    newMaterial.name = $"Pipe_{newPipeIndex}_Material";
                    newMaterial.CopyPropertiesFromMaterial(Material);
                }
                
                while (PipeMaterials.Count <= newPipeIndex)
                {
                    PipeMaterials.Add(null);
                }
                
                // 기존 재질 정리
                if (PipeMaterials[newPipeIndex] != null && Application.isEditor)
                {
                    UnityEngine.Object.DestroyImmediate(PipeMaterials[newPipeIndex]);
                }
                
                PipeMaterials[newPipeIndex] = newMaterial;
                
                // PathCreatorDstar를 사용하여 단일 파이프 경로 생성
                float pipeRadius = radius > 0 ? radius : Radius;
                
                // PathCreatorDstar 인스턴스 생성 및 설정
                var pathCreator = new PathCreatorDstar();
                pathCreator.Height = Height;
                pathCreator.GridSize = GridSize;
                pathCreator.GridRotationY = GridRotationY;
                pathCreator.Chaos = Chaos;
                pathCreator.StraightPathPriority = StraightPathPriority;
                pathCreator.NearObstaclesPriority = NearObstaclesPriority;
                pathCreator.MaxIterations = MaxIterations;
                
                // 경로 생성
                bool succ = false;
                var path = pathCreator.Create(startPoint, startNormal, endPoint, endNormal, pipeRadius);
                
                if (path.Count > 0)
                {
                    // 파이프 생성
                    Pipes.Add(new Pipe(path));
                    
                    // 메시 업데이트
                    UpdateMesh();
                    
                    //Debug.Log($"파이프 #{newPipeIndex} 생성 성공: {path.Count} 포인트");
                    succ = true;
                }
                else
                {
                    //Debug.LogWarning("PathCreatorDstar에서 경로를 생성하지 못했습니다");
                    succ = false;
                }
                
                return succ;
            }
            finally
            {
            }
        }

        public void InsertPoint(int pipeIndex, int pointIndex)
        {
            var position = Vector3.zero;
            if (pointIndex != Pipes[pipeIndex].Points.Count - 1)
                position = (Pipes[pipeIndex].Points[pointIndex + 1] + Pipes[pipeIndex].Points[pointIndex]) / 2;
            else
                position = Pipes[pipeIndex].Points[pointIndex] + Vector3.one;
            Pipes[pipeIndex].Points.Insert(pointIndex + 1, position);
            UpdateMesh();
        }

        public void RemovePoint(int pipeIndex, int pointIndex)
        {
            Pipes[pipeIndex].Points.RemoveAt(pointIndex);
            UpdateMesh();
        }

        public void RemovePipe(int pipeIndex)
        {
            if (pipeIndex < 0 || pipeIndex >= Pipes.Count)
            {
                //Debug.LogWarning($"Attempted to remove pipe at invalid index: {pipeIndex}, Pipes count: {Pipes.Count}");
                return;
            }
            
            // Log before removing
            //Debug.Log($"Removing pipe at index {pipeIndex}, Pipes count before: {Pipes.Count}");
            
            Pipes.RemoveAt(pipeIndex);
            
            if (pipeIndex < PipeRadiuses.Count)
            {
                PipeRadiuses.RemoveAt(pipeIndex);
            }
            
            if (pipeIndex < PipeMaterials.Count)
            {
                PipeMaterials.RemoveAt(pipeIndex);
            }
            
            for (int i = 0; i < PipeMaterials.Count; i++)
            {
                if (PipeMaterials[i] != null)
                {
                    PipeMaterials[i].name = $"Pipe_{i}_Material";
                }
            }
            
            //Debug.Log($"Pipe removed, Pipes count after: {Pipes.Count}");
            UpdateMesh();
        }

        public bool RegeneratePaths()
        {
            var pipeInfos = new List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius)>();
            var originalPipes = new List<Pipe>(Pipes);
            
            // 기존 파이프의 시작점과 끝점 정보 수집
            for (int i = 0; i < originalPipes.Count; i++)
            {
                var pipe = originalPipes[i];
                if (pipe.Points.Count < 2) continue;
                
                Vector3 startPoint = pipe.Points[0];
                Vector3 startNormal = (pipe.Points[1] - pipe.Points[0]).normalized;
                
                int lastIdx = pipe.Points.Count - 1;
                Vector3 endPoint = pipe.Points[lastIdx];
                Vector3 endNormal = (pipe.Points[lastIdx-1] - pipe.Points[lastIdx]).normalized;
                
                float radius = i < PipeRadiuses.Count ? PipeRadiuses[i] : Radius;
                
                pipeInfos.Add((startPoint, startNormal, endPoint, endNormal, radius));
            }
            
            // 기존 파이프 정보 저장
            var savedMaterials = new List<Material>(PipeMaterials);
            
            // 모든 파이프 제거
            Pipes.Clear();
            
            // 임시 충돌체 생성
            var temporaryColliders = new List<GameObject>();
            
            try
            {
                // 각 파이프 엔드포인트에 임시 충돌체 생성
                foreach (var info in pipeInfos)
                {
                    temporaryColliders.Add(CreateTemporaryCollider(info.startPoint, info.startNormal));
                    temporaryColliders.Add(CreateTemporaryCollider(info.endPoint, info.endNormal));
                }
                bool succ = false;
                // PathCreatorDstar로 모든 경로 재생성
                foreach (var config in pipeInfos){
                    // PathCreatorDstar 인스턴스 생성 및 설정
                    var pathCreator = new PathCreatorDstar();
                    pathCreator.Height = Height;
                    pathCreator.GridSize = GridSize;
                    pathCreator.GridRotationY = GridRotationY;
                    pathCreator.Chaos = Chaos;
                    pathCreator.StraightPathPriority = StraightPathPriority;
                    pathCreator.NearObstaclesPriority = NearObstaclesPriority;
                    pathCreator.MaxIterations = MaxIterations;
                    
                    // 경로 생성
                    var path = pathCreator.Create(config.startPoint, config.startNormal, config.endPoint, config.endNormal, config.radius);
                    
                    if (path.Count > 0)
                    {
                        // 파이프 생성
                        Pipes.Add(new Pipe(path));
                        
                        // 메시 업데이트
                        UpdateMesh();
                        succ = true;
                    }
                    else
                    {
                        //Debug.LogWarning("PathCreatorDstar에서 경로를 생성하지 못했습니다");
                        succ = false;
                    }
                }
                
                // 재질 복원
                PipeMaterials = savedMaterials;
                
                // 메시 업데이트
                UpdateMesh();
                
                return true;
            }
            finally
            {
                // 임시 충돌체 정리
                foreach (var collider in temporaryColliders)
                {
                    if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
                }
            }
        }

        private GameObject CreateTemporaryCollider(Vector3 point, Vector3 normal)
        {
            var tempCollider = new GameObject("TempEndpointCollider");
            tempCollider.transform.position = point + (normal * Height) / 2;
            tempCollider.transform.localScale = new Vector3(Radius * 2, Height*1.1f, Radius * 2); //1.1f 가 원래
            tempCollider.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            tempCollider.AddComponent<CapsuleCollider>();
            
            // PathVisualizer에 콜라이더 추가 (있는 경우)
            var visualizer = FindObjectOfType<PathVisualizer>();
            if (visualizer != null)
            {
                visualizer.TrackTemporaryColliders(tempCollider);
            }
            
            return tempCollider;
        }

        private GameObject CreateObstacleCollider(Vector3 position, Vector3 direction)
        {
            float obstacleRadius = Radius * 2f; // 파이프 반지름의 5배로 콜라이더 크기 증가
            
            var tempCollider = new GameObject("PipeObstacleCollider");
            tempCollider.transform.position = position;
            tempCollider.transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);
            tempCollider.transform.localScale = new Vector3(obstacleRadius, obstacleRadius, obstacleRadius);
            
            // 디버그용 로깅
            //Debug.Log($"Creating obstacle collider at {position}, radius: {Radius}, collider size: {obstacleRadius}");
            
            tempCollider.AddComponent<SphereCollider>();
            
            // PathVisualizer에 콜라이더 추가 (있는 경우)
            var visualizer = FindObjectOfType<PathVisualizer>();
            if (visualizer != null)
            {
                visualizer.TrackTemporaryColliders(tempCollider);
            }
            
            return tempCollider;
        }

        public void SetPipeProperties(int pipeIndex, float radius, Material material)
        {
            if (pipeIndex < 0 || pipeIndex >= Pipes.Count)
                return;
            
            //Debug.Log($"Setting properties for pipe {pipeIndex}, radius: {radius}, material: {(material != null ? material.name : "null")}");
            
            while (PipeRadiuses.Count <= pipeIndex)
            {
                PipeRadiuses.Add(Radius);
            }
            
            PipeRadiuses[pipeIndex] = radius;
            
            if (material != null)
            {
                // Get source shader to use
                Shader shaderToUse = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (Material != null && Material.shader != null)
                {
                    shaderToUse = Material.shader;
                }
                
                // Create a new material instance to avoid sharing
                Material newMaterial = new Material(material.shader != null ? material.shader : shaderToUse);
                newMaterial.name = $"Pipe_{pipeIndex}_Material";
                newMaterial.CopyPropertiesFromMaterial(material);
                
                // Set URP properties if needed
                if (newMaterial.HasProperty("_BaseColor") && material.HasProperty("_BaseColor"))
                {
                    newMaterial.SetColor("_BaseColor", material.GetColor("_BaseColor"));
                    
                    // Enable emission if available
                    if (newMaterial.HasProperty("_EmissionColor"))
                    {
                        newMaterial.EnableKeyword("_EMISSION");
                        newMaterial.SetColor("_EmissionColor", material.GetColor("_BaseColor") * 0.5f);
                    }
                }
                else if (newMaterial.HasProperty("_Color") && material.HasProperty("_Color"))
                {
                    newMaterial.SetColor("_Color", material.GetColor("_Color"));
                    
                    // Enable emission if available
                    if (newMaterial.HasProperty("_EmissionColor"))
                    {
                        newMaterial.EnableKeyword("_EMISSION");
                        newMaterial.SetColor("_EmissionColor", material.GetColor("_Color") * 0.5f);
                    }
                }
                
                while (PipeMaterials.Count <= pipeIndex)
                {
                    PipeMaterials.Add(null);
                }
                
                // Destroy old material if it exists to prevent memory leaks
                if (PipeMaterials[pipeIndex] != null && Application.isEditor)
                {
                    UnityEngine.Object.DestroyImmediate(PipeMaterials[pipeIndex]);
                }
                
                PipeMaterials[pipeIndex] = newMaterial;
                
                //Debug.Log($"Set material for pipe {pipeIndex}: {newMaterial.name}, Shader: {newMaterial.shader.name}");
            }
            
            UpdateMesh();
        }

        // //Debugging helper to log pipe information
        public void LogPipeInfo()
        {
            Debug.Log($"=== PIPE GENERATOR INFO ===");
            Debug.Log($"Total Pipes: {Pipes.Count}");
            Debug.Log($"PipeMaterials: {PipeMaterials.Count}");
            Debug.Log($"PipeRadiuses: {PipeRadiuses.Count}");
            
            for (int i = 0; i < Pipes.Count; i++)
            {
                Debug.Log($"Pipe {i}: Points: {Pipes[i].Points.Count}");
            }
            Debug.Log($"==========================");
        }

        // Method to clear all pipes
        public void ClearAllPipes()
        {
            //Debug.Log($"Clearing all pipes. Current count: {Pipes.Count}");
            
            // Clear all pipes and associated data
            Pipes.Clear();
            PipeMaterials.Clear();
            PipeRadiuses.Clear();
            
            // Update the mesh
            UpdateMesh();
            
            //Debug.Log("All pipes cleared");
        }

        // 다중 파이프 생성 메서드 (파이프 간 충돌 방지)
        public async System.Threading.Tasks.Task<float> AddMultiplePipesAsync(List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)> pipeConfigs)
        {
            float totalPathLength = 0f;
            
            if (pipeConfigs == null || pipeConfigs.Count == 0)
            {
                Debug.LogWarning("[PipeGenerator] 파이프 설정이 없습니다.");
                return 0f;
            }
            
            Debug.Log($"[PipeGenerator] 다중 파이프 생성 시작: {pipeConfigs.Count}개 파이프");
            
            // 파이프 끝점에 임시 충돌체 생성 (메인 스레드)
            var temporaryColliders = new List<GameObject>();
            try
            {
                // 각 파이프의 시작점과 끝점에 임시 충돌체 생성
                foreach (var config in pipeConfigs)
                {
                    temporaryColliders.Add(CreateTemporaryCollider(config.startPoint, config.startNormal));
                    temporaryColliders.Add(CreateTemporaryCollider(config.endPoint, config.endNormal));
                }
                
                // MultiThreadPathFinder 인스턴스 생성
                var pathFinder = new MultiThreadPathFinder(maxConcurrentTasks: 4);
                
                // 성능 설정 전달
                pathFinder.SetPerformanceSettings(
                    MaxIterations,
                    30, // 30초 타임아웃
                    100, // 축당 최대 노드 수
                    50000 // 총 최대 노드 수
                );
                
                // 동적 그리드 범위 계산
                var (gridCenter, searchRange) = CalculateOptimalGridBounds(pipeConfigs);
                var (validatedCenter, validatedRange) = ValidateAndClampGridBounds(gridCenter, searchRange);
                
                Debug.Log($"[PipeGenerator] 그리드 설정 - {GetGridInfoString()}");
                Debug.Log($"[PipeGenerator] 계산된 그리드 - 중심: {validatedCenter}, 검색범위: {validatedRange}");
                
                // 장애물 데이터 초기화 (동적 범위 사용)
                pathFinder.InitializeObstacleData(validatedCenter, validatedRange, -1, GridSize);
                
                // 파이프 요청 생성 및 추가
                for (int i = 0; i < pipeConfigs.Count; i++)
                {
                    var config = pipeConfigs[i];
                    var request = new PathRequest(
                        i, 
                        config.startPoint, 
                        config.startNormal, 
                        config.endPoint, 
                        config.endNormal, 
                        config.radius > 0 ? config.radius : Radius
                    );
                    pathFinder.AddRequest(request);
                }
                
                Debug.Log("[PipeGenerator] 경로 탐색 요청 추가 완료");
                
                // 비동기 경로 탐색 실행
                await pathFinder.ProcessInitialPathsAsync();
                await pathFinder.ProcessPriorityPathsAsync();
                
                Debug.Log("[PipeGenerator] 경로 탐색 완료, 파이프 생성 시작");
                
                // 결과를 바탕으로 파이프 생성 (메인 스레드에서)
                var results = pathFinder.GetAllResults();
                int successCount = 0;
                int collisionCount = 0;
                
                // 기존 파이프들 정리
                Pipes.Clear();
                PipeMaterials.Clear();
                PipeRadiuses.Clear();
                
                for (int i = 0; i < pipeConfigs.Count; i++)
                {
                    var result = results.GetValueOrDefault(i);
                    var config = pipeConfigs[i];
                    
                    if (result != null && result.success && result.path != null && result.path.Count > 0)
                    {
                        // 파이프 생성
                        Pipes.Add(new Pipe(result.path));
                        
                        // 반지름 설정
                        PipeRadiuses.Add(config.radius > 0 ? config.radius : Radius);
                        
                        // 재질 설정
                        if (config.material != null)
                        {
                            // 재질 복사 (인스턴스 생성)
                            Shader shaderToUse = config.material.shader ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                            Material newMaterial = new Material(shaderToUse);
                            newMaterial.name = $"Pipe_{i}_Material";
                            newMaterial.CopyPropertiesFromMaterial(config.material);
                            
                            // URP 속성 설정
                            if (newMaterial.HasProperty("_BaseColor") && config.material.HasProperty("_BaseColor"))
                            {
                                newMaterial.SetColor("_BaseColor", config.material.GetColor("_BaseColor"));
                                
                                if (newMaterial.HasProperty("_EmissionColor"))
                                {
                                    newMaterial.EnableKeyword("_EMISSION");
                                    newMaterial.SetColor("_EmissionColor", config.material.GetColor("_BaseColor") * 0.5f);
                                }
                            }
                            
                            PipeMaterials.Add(newMaterial);
                        }
                        else
                        {
                            PipeMaterials.Add(null);
                        }
                        
                        // 경로 길이 계산
                        totalPathLength += CalculatePathLength(result.path);
                        successCount++;
                        
                        if (result.hasCollision)
                        {
                            collisionCount++;
                        }
                        
                        Debug.Log($"[PipeGenerator] 파이프 {i} 생성 성공 (포인트: {result.path.Count}개, 충돌: {result.hasCollision})");
                    }
                    else
                    {
                        Debug.LogWarning($"[PipeGenerator] 파이프 {i} 생성 실패");
                        
                        // 실패한 파이프도 빈 슬롯 유지
                        PipeRadiuses.Add(config.radius > 0 ? config.radius : Radius);
                        PipeMaterials.Add(null);
                    }
                }
                
                // 메시 업데이트
                UpdateMesh();
                
                Debug.Log($"[PipeGenerator] 다중 파이프 생성 완료 - 성공: {successCount}/{pipeConfigs.Count}, 충돌: {collisionCount}, 총 길이: {totalPathLength:F2}");
                
                // 충돌 발생 알림
                if (collisionCount > 0)
                {
                    Debug.LogWarning($"[PipeGenerator] {collisionCount}개 파이프에서 충돌 발생!");
                }
                
                return totalPathLength;
            }
            finally
            {
                // 임시 충돌체 정리
                foreach (var collider in temporaryColliders)
                {
                    if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
                }
            }
        }
        
        // 기존 동기 버전 (호환성 유지)
        public float AddMultiplePipes(List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)> pipeConfigs)
        {
            try
            {
                // Unity 메인 스레드에서 직접 실행 (동기적 버전)
                return AddMultiplePipesSync(pipeConfigs);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PipeGenerator] 다중 파이프 생성 중 오류: {ex.Message}");
                return 0f;
            }
        }
        
        // 동기적 다중 파이프 생성 (Unity 메인 스레드 안전)
        private float AddMultiplePipesSync(List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)> pipeConfigs)
        {
            float totalPathLength = 0f;
            
            if (pipeConfigs == null || pipeConfigs.Count == 0)
            {
                Debug.LogWarning("[PipeGenerator] 파이프 설정이 없습니다.");
                return 0f;
            }
            
            Debug.Log($"[PipeGenerator] 다중 파이프 생성 시작 (동기): {pipeConfigs.Count}개 파이프");
            
            // 파이프 끝점에 임시 충돌체 생성
            var temporaryColliders = new List<GameObject>();
            try
            {
                // 각 파이프의 시작점과 끝점에 임시 충돌체 생성
                foreach (var config in pipeConfigs)
                {
                    temporaryColliders.Add(CreateTemporaryCollider(config.startPoint, config.startNormal));
                    temporaryColliders.Add(CreateTemporaryCollider(config.endPoint, config.endNormal));
                }
                
                // MultiThreadPathFinder 인스턴스 생성
                var pathFinder = new MultiThreadPathFinder(maxConcurrentTasks: 1); // 동기 처리를 위해 1개만 사용
                
                // 성능 설정 전달
                pathFinder.SetPerformanceSettings(
                    MaxIterations,
                    30, // 30초 타임아웃
                    100, // 축당 최대 노드 수
                    50000 // 총 최대 노드 수
                );
                
                // 동적 그리드 범위 계산
                var (gridCenter, searchRange) = CalculateOptimalGridBounds(pipeConfigs);
                var (validatedCenter, validatedRange) = ValidateAndClampGridBounds(gridCenter, searchRange);
                
                Debug.Log($"[PipeGenerator] 그리드 설정 (동기) - {GetGridInfoString()}");
                Debug.Log($"[PipeGenerator] 계산된 그리드 (동기) - 중심: {validatedCenter}, 검색범위: {validatedRange}");
                
                // 장애물 데이터 초기화 (동적 범위 사용)
                pathFinder.InitializeObstacleData(validatedCenter, validatedRange, -1, GridSize);
                
                // 파이프 요청 생성 및 추가
                for (int i = 0; i < pipeConfigs.Count; i++)
                {
                    var config = pipeConfigs[i];
                    var request = new PathRequest(
                        i, 
                        config.startPoint, 
                        config.startNormal, 
                        config.endPoint, 
                        config.endNormal, 
                        config.radius > 0 ? config.radius : Radius
                    );
                    pathFinder.AddRequest(request);
                }
                
                Debug.Log("[PipeGenerator] 경로 탐색 요청 추가 완료 (동기)");
                
                // 동기적으로 경로 탐색 실행 (Unity 메인 스레드에서)
                var initialTask = pathFinder.ProcessInitialPathsAsync();
                while (!initialTask.IsCompleted)
                {
                    // Unity 메인 스레드가 블록되지 않도록 짧게 대기
                    System.Threading.Thread.Sleep(1);
                }
                
                var priorityTask = pathFinder.ProcessPriorityPathsAsync();
                while (!priorityTask.IsCompleted)
                {
                    System.Threading.Thread.Sleep(1);
                }
                
                Debug.Log("[PipeGenerator] 경로 탐색 완료, 파이프 생성 시작 (동기)");
                
                // 결과를 바탕으로 파이프 생성
                var results = pathFinder.GetAllResults();
                int successCount = 0;
                int collisionCount = 0;
                
                // 기존 파이프들 정리
                Pipes.Clear();
                PipeMaterials.Clear();
                PipeRadiuses.Clear();
                
                for (int i = 0; i < pipeConfigs.Count; i++)
                {
                    var result = results.GetValueOrDefault(i);
                    var config = pipeConfigs[i];
                    
                    if (result != null && result.success && result.path != null && result.path.Count > 0)
                    {
                        // 파이프 생성
                        Pipes.Add(new Pipe(result.path));
                        
                        // 반지름 설정
                        PipeRadiuses.Add(config.radius > 0 ? config.radius : Radius);
                        
                        // 재질 설정
                        if (config.material != null)
                        {
                            // 재질 복사 (인스턴스 생성)
                            Shader shaderToUse = config.material.shader ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                            Material newMaterial = new Material(shaderToUse);
                            newMaterial.name = $"Pipe_{i}_Material";
                            newMaterial.CopyPropertiesFromMaterial(config.material);
                            
                            // URP 속성 설정
                            if (newMaterial.HasProperty("_BaseColor") && config.material.HasProperty("_BaseColor"))
                            {
                                newMaterial.SetColor("_BaseColor", config.material.GetColor("_BaseColor"));
                                
                                if (newMaterial.HasProperty("_EmissionColor"))
                                {
                                    newMaterial.EnableKeyword("_EMISSION");
                                    newMaterial.SetColor("_EmissionColor", config.material.GetColor("_BaseColor") * 0.5f);
                                }
                            }
                            
                            PipeMaterials.Add(newMaterial);
                        }
                        else
                        {
                            PipeMaterials.Add(null);
                        }
                        
                        // 경로 길이 계산
                        totalPathLength += CalculatePathLength(result.path);
                        successCount++;
                        
                        if (result.hasCollision)
                        {
                            collisionCount++;
                        }
                        
                        Debug.Log($"[PipeGenerator] 파이프 {i} 생성 성공 (포인트: {result.path.Count}개, 충돌: {result.hasCollision})");
                    }
                    else
                    {
                        Debug.LogWarning($"[PipeGenerator] 파이프 {i} 생성 실패");
                        
                        // 실패한 파이프도 빈 슬롯 유지
                        PipeRadiuses.Add(config.radius > 0 ? config.radius : Radius);
                        PipeMaterials.Add(null);
                    }
                }
                
                // 메시 업데이트
                UpdateMesh();
                
                Debug.Log($"[PipeGenerator] 다중 파이프 생성 완료 (동기) - 성공: {successCount}/{pipeConfigs.Count}, 충돌: {collisionCount}, 총 길이: {totalPathLength:F2}");
                
                // 충돌 발생 알림
                if (collisionCount > 0)
                {
                    Debug.LogWarning($"[PipeGenerator] {collisionCount}개 파이프에서 충돌 발생!");
                }
                
                return totalPathLength;
            }
            finally
            {
                // 임시 충돌체 정리
                foreach (var collider in temporaryColliders)
                {
                    if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
                }
            }
        }

        // 경로 시각화 업데이트
        public void UpdatePathVisualization()
        {
            var visualizer = FindObjectOfType<PathVisualizer>();
            if (visualizer != null)
            {
                visualizer.UpdateVisualization();
            }
        }
        
        // 경로 시각화를 위한, 일반적인 사용 케이스에 대한 편의 메서드
        public PathVisualizer CreateVisualizer()
        {
            // 기존 시각화 도구 찾기
            var existingVisualizer = FindObjectOfType<PathVisualizer>();
            if (existingVisualizer != null)
            {
                return existingVisualizer;
            }
            
            // 새 시각화 도구 생성
            var go = new GameObject("Path Visualizer");
            var visualizer = go.AddComponent<PathVisualizer>();
            visualizer.pipeGenerator = this;
            
            return visualizer;
        }
        public float CalculatePathLength(List<Vector3> points)
        {
            float length = 0f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                length += Vector3.Distance(points[i], points[i + 1]);
            }

            return length;
        }
        
        // 동적 그리드 범위 계산 메서드들
        
        /// <summary>
        /// 파이프 설정에 따른 최적 그리드 범위 계산
        /// </summary>
        private (Vector3 center, float searchRange) CalculateOptimalGridBounds(List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)> pipeConfigs)
        {
            if (pipeConfigs == null || pipeConfigs.Count == 0)
            {
                Debug.LogWarning("[PipeGenerator] 파이프 설정이 없어 기본 그리드 범위 사용");
                return (Vector3.zero, 100f);
            }
            
            if (!useAutomaticGridBounds)
            {
                // 수동 그리드 설정 사용
                float searchRange = Mathf.Max(manualGridSize.x, manualGridSize.y, manualGridSize.z) * obstacleSearchRangeMultiplier;
                Debug.Log($"[PipeGenerator] 수동 그리드 범위 사용 - 중심: {manualGridCenter}, 크기: {manualGridSize}, 검색범위: {searchRange}");
                return (manualGridCenter, searchRange);
            }
            
            // 자동 그리드 범위 계산
            Vector3 minBounds = pipeConfigs[0].startPoint;
            Vector3 maxBounds = pipeConfigs[0].startPoint;
            
            // 모든 파이프의 시작점과 끝점을 포함하는 바운딩 박스 계산
            foreach (var config in pipeConfigs)
            {
                // 시작점과 끝점 포함
                Vector3[] points = { config.startPoint, config.endPoint };
                
                foreach (var point in points)
                {
                    minBounds.x = Mathf.Min(minBounds.x, point.x);
                    minBounds.y = Mathf.Min(minBounds.y, point.y);
                    minBounds.z = Mathf.Min(minBounds.z, point.z);
                    
                    maxBounds.x = Mathf.Max(maxBounds.x, point.x);
                    maxBounds.y = Mathf.Max(maxBounds.y, point.y);
                    maxBounds.z = Mathf.Max(maxBounds.z, point.z);
                }
                
                // Height를 고려한 실제 경로 시작/끝점도 포함
                Vector3 pathStart = config.startPoint + config.startNormal.normalized * Height;
                Vector3 pathEnd = config.endPoint + config.endNormal.normalized * Height;
                
                Vector3[] pathPoints = { pathStart, pathEnd };
                foreach (var point in pathPoints)
                {
                    minBounds.x = Mathf.Min(minBounds.x, point.x);
                    minBounds.y = Mathf.Min(minBounds.y, point.y);
                    minBounds.z = Mathf.Min(minBounds.z, point.z);
                    
                    maxBounds.x = Mathf.Max(maxBounds.x, point.x);
                    maxBounds.y = Mathf.Max(maxBounds.y, point.y);
                    maxBounds.z = Mathf.Max(maxBounds.z, point.z);
                }
            }
            
            // 여백 추가
            minBounds -= Vector3.one * gridPadding;
            maxBounds += Vector3.one * gridPadding;
            maxBounds.y += 10f; // Y축에 추가 여백
            
            // 최소 크기 보장
            Vector3 currentSize = maxBounds - minBounds;
            Vector3 center = (minBounds + maxBounds) * 0.5f;
            
            for (int i = 0; i < 3; i++)
            {
                if (currentSize[i] < minGridSize[i])
                {
                    float expansion = (minGridSize[i] - currentSize[i]) * 0.5f;
                    minBounds[i] -= expansion;
                    maxBounds[i] += expansion;
                }
            }
            
            // 최대 크기 제한
            currentSize = maxBounds - minBounds;
            for (int i = 0; i < 3; i++)
            {
                if (currentSize[i] > maxGridSize[i])
                {
                    float reduction = (currentSize[i] - maxGridSize[i]) * 0.5f;
                    minBounds[i] += reduction;
                    maxBounds[i] -= reduction;
                    Debug.LogWarning($"[PipeGenerator] {(i == 0 ? "X" : i == 1 ? "Y" : "Z")}축 그리드 크기가 최대값으로 제한됨: {maxGridSize[i]}");
                }
            }
            
            // 최종 중심점과 검색 범위 계산
            center = (minBounds + maxBounds) * 0.5f;
            Vector3 finalSize = maxBounds - minBounds;
            float searchRange = Mathf.Max(finalSize.x, finalSize.y, finalSize.z) * obstacleSearchRangeMultiplier;
            
            Debug.Log($"[PipeGenerator] 자동 그리드 범위 계산 완료 - 중심: {center}, 크기: {finalSize}, 검색범위: {searchRange}");
            
            return (center, searchRange);
        }
        
        /// <summary>
        /// 그리드 범위 검증 및 조정
        /// </summary>
        private (Vector3 center, float searchRange) ValidateAndClampGridBounds(Vector3 center, float searchRange)
        {
            // 검색 범위 제한 (성능 보호)
            float maxSearchRange = Mathf.Max(maxGridSize.x, maxGridSize.y, maxGridSize.z) * obstacleSearchRangeMultiplier;
            if (searchRange > maxSearchRange)
            {
                searchRange = maxSearchRange;
                Debug.LogWarning($"[PipeGenerator] 검색 범위가 최대값으로 제한됨: {searchRange}");
            }
            
            // 최소 검색 범위 보장
            float minSearchRange = Mathf.Max(minGridSize.x, minGridSize.y, minGridSize.z);
            if (searchRange < minSearchRange)
            {
                searchRange = minSearchRange;
                Debug.Log($"[PipeGenerator] 검색 범위가 최소값으로 설정됨: {searchRange}");
            }
            
            return (center, searchRange);
        }
        
        /// <summary>
        /// 현재 그리드 설정 정보를 반환 (디버깅용)
        /// </summary>
        public string GetGridInfoString()
        {
            if (useAutomaticGridBounds)
            {
                return $"자동 그리드 - 여백: {gridPadding}, 최소크기: {minGridSize}, 최대크기: {maxGridSize}";
            }
            else
            {
                return $"수동 그리드 - 중심: {manualGridCenter}, 크기: {manualGridSize}";
            }
        }
        
        /// <summary>
        /// 현재 그리드의 검색 범위를 반환
        /// </summary>
        public float GetCurrentSearchRange()
        {
            if (!useAutomaticGridBounds)
            {
                return Mathf.Max(manualGridSize.x, manualGridSize.y, manualGridSize.z) * obstacleSearchRangeMultiplier;
            }
            
            // 기존 파이프들을 기반으로 자동 계산
            if (Pipes != null && Pipes.Count > 0)
            {
                var pipeConfigs = GetCurrentPipeConfigs();
                if (pipeConfigs.Count > 0)
                {
                    var (gridCenter, searchRange) = CalculateOptimalGridBounds(pipeConfigs);
                    var (validatedCenter, validatedRange) = ValidateAndClampGridBounds(gridCenter, searchRange);
                    return validatedRange;
                }
            }
            
            // 기본값 반환
            return 100f;
        }
        
        /// <summary>
        /// 현재 그리드의 중심점을 반환
        /// </summary>
        public Vector3 GetCurrentGridCenter()
        {
            if (!useAutomaticGridBounds)
            {
                return manualGridCenter;
            }
            
            // 기존 파이프들을 기반으로 자동 계산
            if (Pipes != null && Pipes.Count > 0)
            {
                var pipeConfigs = GetCurrentPipeConfigs();
                if (pipeConfigs.Count > 0)
                {
                    var (gridCenter, searchRange) = CalculateOptimalGridBounds(pipeConfigs);
                    var (validatedCenter, validatedRange) = ValidateAndClampGridBounds(gridCenter, searchRange);
                    return validatedCenter;
                }
            }
            
            // 기본값 반환
            return Vector3.zero;
        }
        
        /// <summary>
        /// 현재 파이프들의 설정을 반환하는 헬퍼 메서드
        /// </summary>
        private List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)> GetCurrentPipeConfigs()
        {
            var pipeConfigs = new List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)>();
            
            for (int i = 0; i < Pipes.Count; i++)
            {
                var pipe = Pipes[i];
                if (pipe.Points.Count >= 2)
                {
                    Vector3 startPoint = pipe.Points[0];
                    Vector3 startNormal = (pipe.Points[1] - pipe.Points[0]).normalized;
                    
                    int lastIdx = pipe.Points.Count - 1;
                    Vector3 endPoint = pipe.Points[lastIdx];
                    Vector3 endNormal = (pipe.Points[lastIdx-1] - pipe.Points[lastIdx]).normalized;
                    
                    float radius = i < PipeRadiuses.Count ? PipeRadiuses[i] : Radius;
                    Material material = i < PipeMaterials.Count ? PipeMaterials[i] : null;
                    
                    pipeConfigs.Add((startPoint, startNormal, endPoint, endNormal, radius, material));
                }
            }
            
            return pipeConfigs;
        }
        
        /// <summary>
        /// Unity Editor에서 그리드 범위 시각화
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!useAutomaticGridBounds)
            {
                // 수동 그리드 범위 시각화
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(manualGridCenter, manualGridSize);
                
                // 검색 범위 시각화
                float searchRange = Mathf.Max(manualGridSize.x, manualGridSize.y, manualGridSize.z) * obstacleSearchRangeMultiplier;
                Gizmos.color = Color.cyan * 0.3f;
                Gizmos.DrawWireSphere(manualGridCenter, searchRange * 0.5f);
            }
            else if (Pipes != null && Pipes.Count > 0)
            {
                // 기존 파이프들로부터 자동 그리드 범위 계산 및 시각화
                var pipeConfigs = new List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)>();
                
                for (int i = 0; i < Pipes.Count; i++)
                {
                    var pipe = Pipes[i];
                    if (pipe.Points.Count >= 2)
                    {
                        Vector3 startPoint = pipe.Points[0];
                        Vector3 startNormal = (pipe.Points[1] - pipe.Points[0]).normalized;
                        
                        int lastIdx = pipe.Points.Count - 1;
                        Vector3 endPoint = pipe.Points[lastIdx];
                        Vector3 endNormal = (pipe.Points[lastIdx-1] - pipe.Points[lastIdx]).normalized;
                        
                        float radius = i < PipeRadiuses.Count ? PipeRadiuses[i] : Radius;
                        Material material = i < PipeMaterials.Count ? PipeMaterials[i] : null;
                        
                        pipeConfigs.Add((startPoint, startNormal, endPoint, endNormal, radius, material));
                    }
                }
                
                if (pipeConfigs.Count > 0)
                {
                    var (gridCenter, searchRange) = CalculateOptimalGridBounds(pipeConfigs);
                    var (validatedCenter, validatedRange) = ValidateAndClampGridBounds(gridCenter, searchRange);
                    
                    // 자동 계산된 그리드 범위 시각화
                    Gizmos.color = Color.green;
                    // 검색 범위를 기반으로 그리드 크기 계산 (정육면체)
                    Vector3 gridSize = Vector3.one * validatedRange;
                    Gizmos.DrawWireCube(validatedCenter, gridSize);
                    
                    // 실제 그리드 영역도 표시 (더 작은 크기)
                    Gizmos.color = Color.yellow * 0.7f;
                    Vector3 actualGridSize = Vector3.one * (validatedRange * 0.8f);
                    Gizmos.DrawWireCube(validatedCenter, actualGridSize);
                    
                    // 파이프 시작/끝점 시각화
                    Gizmos.color = Color.red;
                    foreach (var config in pipeConfigs)
                    {
                        Gizmos.DrawWireSphere(config.startPoint, 1f);
                        Gizmos.DrawWireSphere(config.endPoint, 1f);
                    }
                }
            }
        }
        
        /// <summary>
        /// 에디터에서 그리드 설정 테스트 (디버깅용)
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void TestGridCalculation()
        {
            if (Pipes == null || Pipes.Count == 0)
            {
                Debug.LogWarning("[PipeGenerator] 테스트할 파이프가 없습니다.");
                return;
            }
            
            Debug.Log($"[PipeGenerator] 그리드 계산 테스트 시작");
            Debug.Log($"[PipeGenerator] 현재 설정: {GetGridInfoString()}");
            
            // 기존 파이프들을 기반으로 테스트
            var pipeConfigs = new List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)>();
            
            for (int i = 0; i < Pipes.Count; i++)
            {
                var pipe = Pipes[i];
                if (pipe.Points.Count >= 2)
                {
                    Vector3 startPoint = pipe.Points[0];
                    Vector3 startNormal = (pipe.Points[1] - pipe.Points[0]).normalized;
                    
                    int lastIdx = pipe.Points.Count - 1;
                    Vector3 endPoint = pipe.Points[lastIdx];
                    Vector3 endNormal = (pipe.Points[lastIdx-1] - pipe.Points[lastIdx]).normalized;
                    
                    float radius = i < PipeRadiuses.Count ? PipeRadiuses[i] : Radius;
                    Material material = i < PipeMaterials.Count ? PipeMaterials[i] : null;
                    
                    pipeConfigs.Add((startPoint, startNormal, endPoint, endNormal, radius, material));
                }
            }
            
            if (pipeConfigs.Count > 0)
            {
                var (gridCenter, searchRange) = CalculateOptimalGridBounds(pipeConfigs);
                var (validatedCenter, validatedRange) = ValidateAndClampGridBounds(gridCenter, searchRange);
                
                Debug.Log($"[PipeGenerator] 계산 결과:");
                Debug.Log($"  - 그리드 중심: {validatedCenter}");
                Debug.Log($"  - 검색 범위: {validatedRange}");
                Debug.Log($"  - 예상 그리드 크기: {Vector3.one * validatedRange}");
            }
        }
    }
}