using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InstantPipes
{

    [StructLayout(LayoutKind.Sequential)]
    public struct Vec3 { public float x, y, z; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vec3Int
    {
        public int x, y, z;
        public override bool Equals(object obj) => obj is Vec3Int other && this.Equals(other);
        public bool Equals(Vec3Int other) => x == other.x && y == other.y && z == other.z;
        public override int GetHashCode() => (x, y, z).GetHashCode();
        public static bool operator ==(Vec3Int lhs, Vec3Int rhs) => lhs.Equals(rhs);
        public static bool operator !=(Vec3Int lhs, Vec3Int rhs) => !(lhs == rhs);
    }
    

    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshCollider))]
    [RequireComponent(typeof(MeshRenderer))]
    

    public class PipeGenerator : MonoBehaviour
    {
        // C++ DLL 함수들을 C#에서 사용할 수 있도록 불러옵니다.
        [DllImport("pathfinder")]
        private static extern unsafe void InitializeGrid(int* initialCosts, int countX, int countY, int countZ, Vec3 minBounds, float gridSize);

        [DllImport("pathfinder")]
        private static extern unsafe void UpdateCosts(Vec3Int* cellsToUpdate, int count, int costToAdd);

        [DllImport("pathfinder.dll")]
        public static extern void PrecomputeDistanceTransform();

        [DllImport("pathfinder")]
        private static extern unsafe int FindPath(Vec3 startPos, Vec3 endPos, Vec3* outPath, int maxPathSize,
                       float w_path, float w_bend, float w_energy, float w_proximity,
                       int pipeRadius, int clearance, int minBendDistance);

        [DllImport("pathfinder")]
        private static extern void ReleaseGrid();
        
        unsafe private int* Obstacles = null;
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
        public LayerMask ObstacleLayer; // ★★★ 이 변수를 추가해주세요 ★★★
        public int GridSize = 3;
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
        public PathCreatorDLL MultiPathCreator = new PathCreatorDLL();
        private float _maxDistanceBetweenPoints;
        public float MaxCurvature => _maxDistanceBetweenPoints / 2;

        public List<Material> PipeMaterials = new List<Material>();
        public List<float> PipeRadiuses = new List<float>();
        private NewPathfinderDLL _pathfinder; // 새로운 DLL Wrapper 사용


        private void OnEnable()
        {
            _renderer = GetComponent<Renderer>();
            _collider = GetComponent<MeshCollider>();

            _mesh = new Mesh { name = "Pipes" };
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _collider.sharedMesh = _mesh;

            // UpdateMultiPathCreatorSettings(); // << 기존 설정 함수는 필요 없어짐
            _pathfinder = new NewPathfinderDLL(); // Wrapper 인스턴스 생성

            UpdateMesh();
        }

        private void OnDisable()
        {
            _pathfinder?.Dispose(); // 컴포넌트 비활성화 시 메모리 해제
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
                if (_mesh != null) _mesh.Clear();
                if (_collider != null) _collider.sharedMesh = null;
                return;
            }

            _maxDistanceBetweenPoints = 0;
            if (_collider != null) _collider.sharedMesh = null;

            var allSubmeshes = new List<CombineInstance>();
            var allMaterials = new List<Material>();

            for (int i = 0; i < Pipes.Count; i++)
            {
                if (Pipes[i].Points == null || Pipes[i].Points.Count < 2) continue;

                float pipeRadius = (i < PipeRadiuses.Count) ? PipeRadiuses[i] : Radius;
                float originalRadius = Radius;
                Radius = pipeRadius;
                
                // [수정 시작] 메쉬와 재질의 순서가 어긋나는 문제를 근본적으로 해결하는 로직입니다.
                var pipeMeshes = Pipes[i].GenerateMeshes(this); // 파이프 메쉬는 2개까지 반환될 수 있습니다: [0]=몸통, [1]=링/캡
                Material pipeMaterial = (i < PipeMaterials.Count && PipeMaterials[i] != null) ? PipeMaterials[i] : Material;

                // 첫 번째 메쉬(파이프 몸통)를 처리합니다.
                // 정점이 0보다 큰 유효한 메쉬일 경우에만 메쉬와 재질을 함께 리스트에 추가합니다.
                if (pipeMeshes != null && pipeMeshes.Count > 0 && pipeMeshes[0].vertexCount > 0)
                {
                    allSubmeshes.Add(new CombineInstance { mesh = pipeMeshes[0], transform = Matrix4x4.identity });
                    allMaterials.Add(pipeMaterial);
                }

                // 두 번째 메쉬(링 또는 캡)를 처리합니다.
                // 마찬가지로 유효한 메쉬일 경우에만 메쉬와 그에 맞는 재질을 추가합니다.
                if (pipeMeshes != null && pipeMeshes.Count > 1 && pipeMeshes[1].vertexCount > 0)
                {
                    allSubmeshes.Add(new CombineInstance { mesh = pipeMeshes[1], transform = Matrix4x4.identity });
                    
                    Material ringOrCapMaterial = pipeMaterial; // 기본적으로 파이프 재질을 사용
                    if (IsSeparateRingsMaterial && (HasCaps || HasRings))
                    {
                        ringOrCapMaterial = (RingMaterial != null) ? RingMaterial : pipeMaterial;
                    }
                    allMaterials.Add(ringOrCapMaterial);
                }
                
                _maxDistanceBetweenPoints = Mathf.Max(_maxDistanceBetweenPoints, Pipes[i].GetMaxDistanceBetweenPoints());
                Radius = originalRadius;
                // [수정 끝]
            }
            
            if (allSubmeshes.Count > 0)
            {
                _mesh.Clear();
                if(allSubmeshes.Sum(c => c.mesh.vertexCount) > 65535)
                {
                    _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                }
                else
                {
                    _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
                }
                
                _mesh.CombineMeshes(allSubmeshes.ToArray(), false, false);
                if (_mesh.vertexCount > 0) _collider.sharedMesh = _mesh;
            }
            else
            {
                _mesh.Clear();
            }
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

        unsafe public bool AddPipe(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius = -1, Material material = null, int* _obstacles = null, int obstacleCount = 0)
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
                
                //UnityEngine.Debug.Log($"Setting pipe radius: {radius} for pipe index: {newPipeIndex}");
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
                //_obstacles = MultiPathCreator.Obstacles;
                paths.Add(path);
                if (path.Count > 0)
                {
                    // 파이프 생성
                    Pipes.Add(new Pipe(path));
                    
                    // 메시 업데이트
                    UpdateMesh();
                    
                    //UnityEngine.Debug.Log($"파이프 #{newPipeIndex} 생성 성공: {path.Count} 포인트");
                    succ = true;
                }
                else
                {
                    //UnityEngine.Debug.LogWarning("MultiPathCreator에서 경로를 생성하지 못했습니다");
                    succ = false;
                }
                
                return succ;
            }
            finally
            {
            }
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
                    //temporaryColliders.Add(CreateTemporaryCollider(info.startPoint, info.startNormal));
                    //temporaryColliders.Add(CreateTemporaryCollider(info.endPoint, info.endNormal));
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
                        //UnityEngine.Debug.LogWarning("MultiPathCreator에서 경로를 생성하지 못했습니다");
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
            //UnityEngine.Debug.Log($"Creating obstacle collider at {position}, radius: {Radius}, collider size: {obstacleRadius}");
            
            tempCollider.AddComponent<SphereCollider>();
            
            
            return tempCollider;
        }

        public void SetPipeProperties(int pipeIndex, float radius, Material material)
        {
            if (pipeIndex < 0 || pipeIndex >= Pipes.Count)
                return;
            
            //UnityEngine.Debug.Log($"Setting properties for pipe {pipeIndex}, radius: {radius}, material: {(material != null ? material.name : "null")}");
            
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
                
                //UnityEngine.Debug.Log($"Set material for pipe {pipeIndex}: {newMaterial.name}, Shader: {newMaterial.shader.name}");
            }
            
            UpdateMesh();
        }

        // //UnityEngine.Debugging helper to log pipe information
        public void LogPipeInfo()
        {
            UnityEngine.Debug.Log($"=== PIPE GENERATOR INFO ===");
            UnityEngine.Debug.Log($"Total Pipes: {Pipes.Count}");
            UnityEngine.Debug.Log($"PipeMaterials: {PipeMaterials.Count}");
            UnityEngine.Debug.Log($"PipeRadiuses: {PipeRadiuses.Count}");
            
            for (int i = 0; i < Pipes.Count; i++)
            {
                UnityEngine.Debug.Log($"Pipe {i}: Points: {Pipes[i].Points.Count}");
            }
            UnityEngine.Debug.Log($"==========================");
        }
    // Physics.CheckSphere에 사용할 그리드 중심 위치를 반환하는 함수
        public Vector3 GetSnappedGridCenter(Vector3 position, int minX, int minY, int minZ, int maxX, int maxY,int maxZ)
        {
            // minX, minY, minZ를 기준으로 오프셋 계산
            float targetX = position.x - minX;
            float targetY = position.y - minY;
            float targetZ = position.z - minZ;

            // 가장 가까운 그리드 인덱스 계산 (소수점 버림)
            int gridX = Mathf.FloorToInt(targetX / GridSize);
            int gridY = Mathf.FloorToInt(targetY / GridSize);
            int gridZ = Mathf.FloorToInt(targetZ / GridSize);

            // 그리드 셀의 중심 좌표 계산
            // 예를 들어, GridSize가 10이면, 0번 그리드 셀의 중심은 5, 1번 그리드 셀의 중심은 15
            float snappedX = gridX * GridSize + GridSize / 2f + minX;
            float snappedY = gridY * GridSize + GridSize / 2f + minY;
            float snappedZ = gridZ * GridSize + GridSize / 2f + minZ;

            return new Vector3(snappedX, snappedY, snappedZ);
        }

        // 다중 파이프 생성 메서드

        /// <summary>
        /// Converts a world-space position to its corresponding grid cell index.
        /// </summary>
        private Vector3Int WorldToGrid(Vector3 position, float minX, float minY, float minZ)
        {
            return new Vector3Int(
                Mathf.FloorToInt((position.x - minX) / GridSize),
                Mathf.FloorToInt((position.y - minY) / GridSize),
                Mathf.FloorToInt((position.z - minZ) / GridSize)
            );
        }

        /// <summary>
        /// Generates the 'covering list' for a path, representing the grid cells it occupies.
        /// </summary>
        private HashSet<Vector3Int> GetCoveringList(List<Vector3> path, float minX, float minY, float minZ, int countX, int countY, int countZ)
        {
            var coveringSet = new HashSet<Vector3Int>();
            if (path == null || path.Count < 2) return coveringSet;

            var pathGridCells = new HashSet<Vector3Int>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 start = path[i];
                Vector3 end = path[i + 1];
                float dist = Vector3.Distance(start, end);
                int steps = Mathf.CeilToInt(dist / (GridSize * 0.5f));
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 p = Vector3.Lerp(start, end, (float)s / steps);
                    pathGridCells.Add(WorldToGrid(p, minX, minY, minZ));
                }
            }

            var directions = new Vector3Int[] {
                Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right,
                new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
            };

            foreach (var cell in pathGridCells)
            {
                for (int d = 0; d < directions.Length; d++)
                {
                    var neighbor = cell + directions[d];
                    if (neighbor.x >= 0 && neighbor.x < countX && neighbor.y >= 0 && neighbor.y < countY && neighbor.z >= 0 && neighbor.z < countZ)
                    {
                        coveringSet.Add(neighbor);
                    }
                }
            }
            return coveringSet;
        }
                
        public unsafe float AddMultiplePipes(List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Material material)> pipeConfigs)
        {
            if (pipeConfigs == null || pipeConfigs.Count == 0) return 0;

            // 1. 모든 파이프를 포함하는 경계 상자(Bounding Box) 계산
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (var config in pipeConfigs)
            {
                minX = Mathf.Min(minX, config.startPoint.x, config.endPoint.x);
                minY = Mathf.Min(minY, config.startPoint.y, config.endPoint.y);
                minZ = Mathf.Min(minZ, config.startPoint.z, config.endPoint.z);
                maxX = Mathf.Max(maxX, config.startPoint.x, config.endPoint.x);
                maxY = Mathf.Max(maxY, config.startPoint.y, config.endPoint.y);
                maxZ = Mathf.Max(maxZ, config.startPoint.z, config.endPoint.z);
            }
            float padding = GridSize * 5f;
            minX -= padding; minY += Height; minZ -= padding;
            maxX += padding; maxY += Height + padding; maxZ += padding;
            Vector3 minBounds = new Vector3(minX, minY, minZ);

            int countX = Mathf.CeilToInt((maxX - minX) / GridSize);
            int countY = Mathf.CeilToInt((maxY - minY) / GridSize);
            int countZ = Mathf.CeilToInt((maxZ - minZ) / GridSize);
            
            // 2. 장애물 탐지 및 초기 비용 그리드 생성
            int obstacleCount = countX * countY * countZ;
            int* obstacles = stackalloc int[obstacleCount];
            const int OBSTACLE_COST = 10000; // C++의 OBSTACLE_THRESHOLD와 동일한 값
            Vector3 boxHalfExtents = Vector3.one * GridSize / 2f;

            for (int x = 0; x < countX; x++) {
                for (int y = 0; y < countY; y++) {
                    for (int z = 0; z < countZ; z++) {
                        Vector3 cellCenter = new Vector3(minX + (x + 0.5f) * GridSize, minY + (y + 0.5f) * GridSize, minZ + (z + 0.5f) * GridSize);
                        int index = x + y * countX + z * countX * countY;
                        
                        if (Physics.OverlapBox(cellCenter, boxHalfExtents, Quaternion.identity, ObstacleLayer).Length > 0) {
                            obstacles[index] = OBSTACLE_COST;
                        } else {
                            obstacles[index] = 0;
                        }
                    }
                }
            }

            // 3. C++ PathFinder 초기화
            InitializeGrid(obstacles, countX, countY, countZ, new Vec3 { x = minX, y = minY, z = minZ }, GridSize);
            PrecomputeDistanceTransform(); 
            var paths = new List<List<Vector3>>(new List<Vector3>[pipeConfigs.Count]);
            bool allSucceeded = true;
            
            var sortedPipes = pipeConfigs
                .Select((config, index) => new { Config = config, OriginalIndex = index })
                .OrderByDescending(p => p.Config.radius)
                .ToList();

            // 4. 각 파이프 경로 탐색 및 그리드 업데이트
            foreach (var pipeInfo in sortedPipes)
            {
                var config = pipeInfo.Config;
                int originalIndex = pipeInfo.OriginalIndex;

                var pathBuffer = new Vec3[4096];
                int pathLength;
                fixed (Vec3* pPathBuffer = pathBuffer)
                {
                    pathLength = FindPath(
                        new Vec3 { x = config.startPoint.x, y = config.startPoint.y+Height, z = config.startPoint.z  },
                        new Vec3 { x = config.endPoint.x, y = config.endPoint.y+ Height, z = config.endPoint.z },
                        pPathBuffer, pathBuffer.Length, 1, StraightPathPriority, 2f, NearObstaclesPriority, 1, 1, 1
                    );
                }

                if (pathLength > 0)
                {
                    var finalPath = new List<Vector3> { config.startPoint };
                    for (int i = 0; i < pathLength; i++) {
                        finalPath.Add(new Vector3(pathBuffer[i].x, pathBuffer[i].y, pathBuffer[i].z));
                    }
                    finalPath.Add(config.endPoint);
                    paths[originalIndex] = finalPath;

                    List<Vec3Int> pipeCells = RasterizePathToGrid(finalPath, minBounds, GridSize, countX, countY, countZ, config.radius);
                    
                    Vec3Int* cellsToUpdate = stackalloc Vec3Int[pipeCells.Count];
                    for (int i = 0; i < pipeCells.Count; i++) cellsToUpdate[i] = pipeCells[i];
                    
                    UpdateCosts(cellsToUpdate, pipeCells.Count, OBSTACLE_COST * 5); 
                }
                else
                {
                    UnityEngine.Debug.LogError($"[PipeGenerator] 파이프 #{originalIndex}의 경로를 찾지 못했습니다.");
                    allSucceeded = false;
                    break;
                }
            }

            ReleaseGrid();

            // 5. 결과 처리 및 메시 생성
            Pipes.Clear();
            PipeMaterials.Clear();
            PipeRadiuses.Clear();

            float totalLength = 0f;
            if (allSucceeded)
            {
                for (int i = 0; i < pipeConfigs.Count; i++)
                {
                    var config = pipeConfigs[i];
                    Pipes.Add(new Pipe(paths[i]));
                    PipeRadiuses.Add(config.radius);
                    totalLength += CalculatePathLength(paths[i]);

                    // [수정 시작] 각 파이프에 대해 독립적인 재질 인스턴스를 생성합니다.
                    Material sourceMaterial = config.material;
                    Material instancedMaterial = null;

                    if (sourceMaterial != null)
                    {
                        // AddPipe 함수와 동일한 로직을 사용하여 재질 인스턴스를 생성합니다.
                        Shader shaderToUse = sourceMaterial.shader ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                        if (shaderToUse == null) shaderToUse = Shader.Find("Standard");

                        instancedMaterial = new Material(shaderToUse);
                        instancedMaterial.name = $"Pipe_{i}_{sourceMaterial.name}_Inst";
                        instancedMaterial.CopyPropertiesFromMaterial(sourceMaterial);
                    }
                    PipeMaterials.Add(instancedMaterial); // 원본 재질 대신 새로 생성된 인스턴스를 추가합니다.
                    // [수정 끝]
                }
            }
            UpdateMesh();
            
            return allSucceeded ? totalLength : float.MaxValue;
        }

        // 파이프 경로가 차지하는 모든 그리드 셀을 계산하는 함수 (파이프 두께 고려)
        private List<Vec3Int> RasterizePathToGrid(List<Vector3> path, Vector3 minBounds, float gridSize, int countX, int countY, int countZ, float radius)
        {
            var cellSet = new HashSet<Vec3Int>();
            int extraCells = Mathf.CeilToInt(radius / gridSize);

            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 start = path[i];
                Vector3 end = path[i + 1];
                float distance = Vector3.Distance(start, end);
                if (distance < 0.01f) continue;
                Vector3 direction = (end - start).normalized;

                for (float d = 0; d <= distance; d += gridSize * 0.5f)
                {
                    Vector3 currentPoint = start + direction * d;
                    Vec3Int centerCell = WorldToGrid(currentPoint, minBounds, gridSize);

                    for (int x = -extraCells; x <= extraCells; x++) {
                        for (int y = -extraCells; y <= extraCells; y++) {
                            for (int z = -extraCells; z <= extraCells; z++) {
                                Vec3Int pipeCell = new Vec3Int { x = centerCell.x + x, y = centerCell.y + y, z = centerCell.z + z };
                                if (pipeCell.x >= 0 && pipeCell.x < countX && pipeCell.y >= 0 && pipeCell.y < countY && pipeCell.z >= 0 && pipeCell.z < countZ) {
                                    cellSet.Add(pipeCell);
                                }
                            }
                        }
                    }
                }
            }
            return new List<Vec3Int>(cellSet);
        }
        private Vec3Int WorldToGrid(Vector3 worldPos, Vector3 minBounds, float gridSize)
        {
            return new Vec3Int {
                x = Mathf.FloorToInt((worldPos.x - minBounds.x) / gridSize),
                y = Mathf.FloorToInt((worldPos.y - minBounds.y) / gridSize),
                z = Mathf.FloorToInt((worldPos.z - minBounds.z) / gridSize)
            };
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
            if (pipeIndex < 0 || pipeIndex >= Pipes.Count) return;
            
            Pipes.RemoveAt(pipeIndex);
            if (pipeIndex < PipeRadiuses.Count) PipeRadiuses.RemoveAt(pipeIndex);
            if (pipeIndex < PipeMaterials.Count) PipeMaterials.RemoveAt(pipeIndex);
            
            for (int i = 0; i < PipeMaterials.Count; i++)
            {
                if (PipeMaterials[i] != null) PipeMaterials[i].name = $"Pipe_{i}_Material";
            }
            UpdateMesh();
        }

        public void ClearAllPipes()
        {
            Pipes.Clear();
            PipeMaterials.Clear();
            PipeRadiuses.Clear();
            UpdateMesh();
        }
        
        public float CalculatePathLength(List<Vector3> points)
        {
            float length = 0f;
            if (points == null) return 0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                length += Vector3.Distance(points[i], points[i + 1]);
            }
            return length;
        }
    }
}