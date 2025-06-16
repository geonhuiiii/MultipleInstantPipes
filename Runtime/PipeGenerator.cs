using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

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

        private Renderer _renderer;
        private MeshCollider _collider;
        private Mesh _mesh;

        public List<Pipe> Pipes = new List<Pipe>();
        public PathCreator MultiPathCreator = new PathCreator();
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

            // MultiPathCreator 기본 파라미터 설정
            UpdateMultiPathCreatorSettings();

            UpdateMesh();
        }

        // MultiPathCreator 설정 업데이트
        private void UpdateMultiPathCreatorSettings()
        {
            MultiPathCreator.Height = Height;
            MultiPathCreator.GridSize = GridSize;
            MultiPathCreator.GridRotationY = GridRotationY;
            MultiPathCreator.Chaos = Chaos;
            MultiPathCreator.StraightPathPriority = StraightPathPriority;
            MultiPathCreator.NearObstaclesPriority = NearObstaclesPriority;
            MultiPathCreator.MaxIterations = MaxIterations;
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
            // 설정 업데이트
            UpdateMultiPathCreatorSettings();
            
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
                
                // MultiPathCreator를 사용하여 단일 파이프 경로 생성
                float pipeRadius = radius > 0 ? radius : Radius;
                
                // 다중 경로 생성을 위한 설정 리스트 생성
                bool succ = false;
                var paths = new List<List<Vector3>>();
                var path = MultiPathCreator.Create(startPoint, startNormal, endPoint, endNormal, pipeRadius);
                paths.Add(path);
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
                    //Debug.LogWarning("MultiPathCreator에서 경로를 생성하지 못했습니다");
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
            // 설정 업데이트
            UpdateMultiPathCreatorSettings();
            
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
                // MultiPathCreator로 모든 경로 재생성
                foreach (var config in pipeInfos){
                    // 경로 생성
                    var path = MultiPathCreator.Create(config.startPoint, config.startNormal, config.endPoint, config.endNormal, config.radius);
                    
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
                        //Debug.LogWarning("MultiPathCreator에서 경로를 생성하지 못했습니다");
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
            tempCollider.transform.localScale = new Vector3(Radius * 2, Height*1.1f, Radius * 2);
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

        // 다중 파이프 생성 메서드
        
        public float AddMultiplePipes(List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)> pipeConfigs)
        {
            float shortestPathValue = 0f;
            // 설정 업데이트
            UpdateMultiPathCreatorSettings();

            MultiPathCreator.hasCollision = false;

            // 파이프 끝점에 임시 충돌체 생성 (경로 탐색용)
            var temporaryColliders = new List<GameObject>();
            // 각 파이프의 시작점과 끝점에 임시 충돌체 생성
            foreach (var config in pipeConfigs)
            {
                temporaryColliders.Add(CreateTemporaryCollider(config.startPoint, config.startNormal));
                temporaryColliders.Add(CreateTemporaryCollider(config.endPoint, config.endNormal));
            }


            // MultiPathCreator에 전달할 형식으로 변환
            var configs = pipeConfigs.Select(config =>
                (config.startPoint, config.startNormal, config.endPoint, config.endNormal, config.radius, config.material)
            ).ToList();
            var shortestA = new List<int>();
            var aaaa = 0;
            foreach (var config in configs)
            {
                AddPipe(config.startPoint, config.startNormal, config.endPoint, config.endNormal, config.radius, config.material);
            }
            bool isCollided = MultiPathCreator.hasCollision;

            var shortestPaths = configs;
            for (int j = 0; j < Pipes.Count; j++)
            {
                var path = Pipes[j].Points;
                shortestPathValue += CalculatePathLength(path);
            }

            Pipes.Clear();
            if (isCollided)
                Debug.Log($"총 경로 중 충돌발생!.");

            foreach (var collider in temporaryColliders)
            {
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            }
            return shortestPathValue;
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
    }
}