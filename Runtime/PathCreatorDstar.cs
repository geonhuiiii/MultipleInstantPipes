using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using System.Linq;
namespace InstantPipes
{
    // 파이프 경로 요청 정보
    [System.Serializable]
    public class PathRequest
    {
        public int pipeId;
        public Vector3 startPoint;
        public Vector3 startNormal;
        public Vector3 endPoint;
        public Vector3 endNormal;
        public float pipeRadius;
        
        public PathRequest(int id, Vector3 start, Vector3 startNorm, Vector3 end, Vector3 endNorm, float radius)
        {
            pipeId = id;
            startPoint = start;
            startNormal = startNorm;
            endPoint = end;
            endNormal = endNorm;
            pipeRadius = radius;
        }
    }

    // 경로 탐색 결과
    [System.Serializable]
    public class PathResult
    {
        public int pipeId;
        public List<Vector3> path;
        public bool success;
        public bool hasCollision;
        
        public PathResult(int id, List<Vector3> pathPoints, bool isSuccess, bool collision = false)
        {
            pipeId = id;
            path = pathPoints ?? new List<Vector3>();
            success = isSuccess;
            hasCollision = collision;
        }
    }

    // 멀티스레딩 매니저
    public class MultiThreadPathFinder
    {
        private readonly ConcurrentQueue<PathRequest> pendingRequests = new();
        private readonly ConcurrentDictionary<int, PathResult> completedResults = new();
        private readonly SemaphoreSlim semaphore;
        private readonly Dictionary<Vector3, bool> sharedObstacleData = new();
        private readonly object obstacleDataLock = new object();
        private readonly List<PathRequest> allRequests = new(); // 모든 요청을 순서대로 저장
        private readonly object requestsLock = new object();
        // 생성된 파이프 경로들을 저장 (파이프 간 충돌 방지용)
        private readonly List<List<Vector3>> createdPipePaths = new();
        private readonly object pipePathsLock = new object();
        private bool isInitialized = false;
        
        // 동적 그리드 설정 저장
        private float currentGridSize = 3f;
        private Vector3 currentCenter = Vector3.zero;
        private float currentRange = 100f;
        
        // 성능 설정 필드들
        private int maxIterations = 1000;
        private int maxTimeoutSeconds = 30;
        private int maxNodesPerAxis = 100;
        private int maxTotalNodes = 50000;
        
        public MultiThreadPathFinder(int maxConcurrentTasks = 4)
        {
            semaphore = new SemaphoreSlim(maxConcurrentTasks, maxConcurrentTasks);
        }

        // 메인 스레드에서 초기 장애물 데이터 수집
        public void InitializeObstacleData(Vector3 center, float range, LayerMask layerMask, float gridSize)
        {
            lock (obstacleDataLock)
            {
                sharedObstacleData.Clear();
                
                // 동적 그리드 설정 저장
                currentGridSize = gridSize;
                currentCenter = center;
                currentRange = range;
                
                // 생성된 파이프 경로들도 초기화
                lock (pipePathsLock)
                {
                    createdPipePaths.Clear();
                }
                
                Collider[] hits = Physics.OverlapSphere(center, range, layerMask);
                foreach (var hit in hits)
                {
                    Vector3 pos = SnapToGrid(hit.transform.position, gridSize);
                    sharedObstacleData[pos] = true;
                    
                    // 주변 그리드도 장애물로 표시 (안전 마진)
                    Vector3[] directions = {
                        Vector3.forward, Vector3.back, Vector3.left, 
                        Vector3.right, Vector3.up, Vector3.down
                    };
                    
                    foreach (var dir in directions)
                    {
                        Vector3 adjacentPos = SnapToGrid(pos + dir * gridSize, gridSize);
                        sharedObstacleData[adjacentPos] = true;
                    }
                }
                
                isInitialized = true;
                Debug.Log($"[멀티스레딩] 장애물 데이터 초기화 완료: {sharedObstacleData.Count}개 위치 (그리드 크기: {gridSize:F1})");
            }
        }
        
        // 생성된 파이프 경로를 장애물로 추가
        private void AddPipePathAsObstacle(List<Vector3> pipePath, float gridSize, float pipeRadius)
        {
            if (pipePath == null || pipePath.Count == 0) return;
            
            lock (obstacleDataLock)
            {
                int addedObstacles = 0;
                
                foreach (var point in pipePath)
                {
                    Vector3 basePos = SnapToGrid(point, gridSize);
                    
                    // 파이프 반지름을 고려한 안전 거리 계산 (그리드 크기 비례)
                    float safetyMargin = Mathf.Max(pipeRadius * 2.5f, gridSize * 2f);
                    int gridSteps = Mathf.CeilToInt(safetyMargin / gridSize);
                    
                    // 파이프 주변 그리드들을 장애물로 추가
                    for (int x = -gridSteps; x <= gridSteps; x++)
                    {
                        for (int y = -gridSteps; y <= gridSteps; y++)
                        {
                            for (int z = -gridSteps; z <= gridSteps; z++)
                            {
                                Vector3 obstaclePos = basePos + new Vector3(x * gridSize, y * gridSize, z * gridSize);
                                
                                // 거리 체크로 원형 영역만 장애물로 설정
                                if (Vector3.Distance(basePos, obstaclePos) <= safetyMargin)
                                {
                                    if (!sharedObstacleData.ContainsKey(obstaclePos) || !sharedObstacleData[obstaclePos])
                                    {
                                        sharedObstacleData[obstaclePos] = true;
                                        addedObstacles++;
                                    }
                                }
                            }
                        }
                    }
                }
                
                Debug.Log($"[멀티스레딩] 파이프 경로 장애물 추가: {pipePath.Count}개 포인트 → {addedObstacles}개 장애물 격자 (반지름: {pipeRadius:F1}, 그리드: {gridSize:F1})");
            }
        }
        
        public bool IsObstacleThreadSafe(Vector3 position, float gridSize)
        {
            if (!isInitialized) return false;
            
            Vector3 key = SnapToGrid(position, gridSize);
            lock (obstacleDataLock)
            {
                return sharedObstacleData.ContainsKey(key) && sharedObstacleData[key];
            }
        }
        
        // 경로 요청 추가
        public void AddRequest(PathRequest request)
        {
            pendingRequests.Enqueue(request);
            
            // 순서 보장을 위해 별도 리스트에도 저장
            lock (requestsLock)
            {
                allRequests.Add(request);
            }
        }
        
        // 모든 요청에 대해 초기 경로 탐색 (병렬) - 충돌 미고려 빠른 탐색
        public async Task ProcessInitialPathsAsync()
        {
            var tasks = new List<Task>();
            var requests = new List<PathRequest>();
            
            // 모든 요청을 리스트로 수집
            while (pendingRequests.TryDequeue(out var request))
            {
                requests.Add(request);
            }
            
            Debug.Log($"[멀티스레딩] 초기 경로 탐색 시작: {requests.Count}개 파이프 (병렬, 그리드 크기: {currentGridSize:F1})");
            
            // 병렬로 초기 경로 탐색 (빠른 대략적 경로)
            foreach (var request in requests)
            {
                tasks.Add(ProcessSingleRequestAsync(request, isInitialPass: true));
            }
            
            await Task.WhenAll(tasks);
            Debug.Log("[멀티스레딩] 초기 경로 탐색 완료");
        }
        
        // 순서대로 순차 경로 탐색 (파이프 간 충돌 고려)
        public async Task ProcessPriorityPathsAsync()
        {
            List<PathRequest> orderedRequests;
            
            // 추가된 순서대로 요청 가져오기
            lock (requestsLock)
            {
                orderedRequests = new List<PathRequest>(allRequests);
            }
            
            Debug.Log($"[멀티스레딩] 순차 경로 탐색 시작: {orderedRequests.Count}개 파이프 (순서대로, 충돌 고려, 그리드 크기: {currentGridSize:F1})");
            
            // 순서대로 처리하면서 이전 파이프들을 장애물로 추가
            for (int i = 0; i < orderedRequests.Count; i++)
            {
                var request = orderedRequests[i];
                
                Debug.Log($"[멀티스레딩] 파이프 {request.pipeId} 순차 처리 중 (진행: {i+1}/{orderedRequests.Count})");
                
                // 이전에 생성된 파이프들의 경로를 장애물로 추가 (동적 그리드 크기 사용)
                lock (pipePathsLock)
                {
                    foreach (var existingPath in createdPipePaths)
                    {
                        AddPipePathAsObstacle(existingPath, currentGridSize, request.pipeRadius);
                    }
                }
                
                // 순차 처리로 정확한 경로 탐색
                await ProcessSingleRequestAsync(request, isInitialPass: false);
                
                // 성공한 경로를 저장 (다음 파이프의 장애물로 사용)
                var result = completedResults.GetValueOrDefault(request.pipeId);
                if (result != null && result.success && result.path != null && result.path.Count > 0)
                {
                    lock (pipePathsLock)
                    {
                        createdPipePaths.Add(new List<Vector3>(result.path));
                    }
                    
                    Debug.Log($"[멀티스레딩] 파이프 {request.pipeId} 경로 저장 완료 (포인트: {result.path.Count}개)");
                }
                else
                {
                    Debug.LogWarning($"[멀티스레딩] 파이프 {request.pipeId} 경로 생성 실패");
                }
            }
            
            Debug.Log("[멀티스레딩] 순차 경로 탐색 완료");
        }
        
        private async Task ProcessSingleRequestAsync(PathRequest request, bool isInitialPass)
        {
            await semaphore.WaitAsync();
            
            try
            {
                await Task.Run(() =>
                {
                    var pathCreator = new PathCreatorDstar();
                    pathCreator.SetObstacleDataFromShared(sharedObstacleData, obstacleDataLock);
                    
                    // 성능 설정 전달
                    if (isInitialPass)
                    {
                        // 초기 탐색: 빠른 설정
                        pathCreator.SetPerformanceSettings(
                            maxIterations / 2, // 반복 횟수 절반
                            maxTimeoutSeconds / 2, // 타임아웃 절반
                            maxNodesPerAxis, 
                            maxTotalNodes / 2 // 노드 수 절반
                        );
                    }
                    else
                    {
                        // 순차 탐색: 정확한 설정
                        pathCreator.SetPerformanceSettings(maxIterations, maxTimeoutSeconds, maxNodesPerAxis, maxTotalNodes);
                    }
                    
                    // 디버그 설정 전달 (기본적으로 false - 성능 최적화)
                    pathCreator.SetDebugSettings(false);
                    
                    // 동적 그리드 계산을 위해 모든 요청 리스트 전달
                    List<PathRequest> requestsCopy;
                    lock (requestsLock)
                    {
                        requestsCopy = new List<PathRequest>(allRequests);
                    }
                    pathCreator.SetPathRequests(requestsCopy);
                    
                    var path = pathCreator.Create(
                        request.startPoint, 
                        request.startNormal, 
                        request.endPoint, 
                        request.endNormal, 
                        request.pipeRadius
                    );
                    
                    var result = new PathResult(
                        request.pipeId, 
                        path, 
                        pathCreator.LastPathSuccess, 
                        pathCreator.hasCollision
                    );
                    
                    completedResults.AddOrUpdate(request.pipeId, result, (key, oldValue) => result);
                    
                    string passType = isInitialPass ? "초기" : "순차";
                    Debug.Log($"[멀티스레딩] {passType} 경로 탐색 완료 - ID: {request.pipeId}, 성공: {result.success}, 포인트: {path?.Count ?? 0}개");
                });
            }
            finally
            {
                semaphore.Release();
            }
        }
        
        public PathResult GetResult(int pipeId)
        {
            completedResults.TryGetValue(pipeId, out var result);
            return result;
        }
        
        public Dictionary<int, PathResult> GetAllResults()
        {
            return new Dictionary<int, PathResult>(completedResults);
        }
        
        public void ClearResults()
        {
            completedResults.Clear();
            
            // 요청 리스트와 생성된 파이프 경로들도 함께 지우기
            lock (requestsLock)
            {
                allRequests.Clear();
            }
            
            lock (pipePathsLock)
            {
                createdPipePaths.Clear();
            }
            
            // 장애물 데이터도 초기화 (기본 장애물은 유지하되 동적 그리드 설정 사용)
            if (isInitialized)
            {
                InitializeObstacleData(currentCenter, currentRange, -1, currentGridSize);
            }
        }
        
        private static Vector3 SnapToGrid(Vector3 pos, float gridSize)
        {
            float x = Mathf.Round(pos.x / gridSize) * gridSize;
            float y = Mathf.Round(pos.y / gridSize) * gridSize;
            float z = Mathf.Round(pos.z / gridSize) * gridSize;
            return new Vector3(x, y, z);
        }

        // 성능 설정 메서드
        public void SetPerformanceSettings(int maxIter, int maxTimeout, int maxNodes, int maxTotal)
        {
            maxIterations = maxIter;
            maxTimeoutSeconds = maxTimeout;
            maxNodesPerAxis = maxNodes;
            maxTotalNodes = maxTotal;
        }
        
        // 생성된 파이프 경로 개수 반환 (디버깅용)
        public int GetCreatedPipePathsCount()
        {
            lock (pipePathsLock)
            {
                return createdPipePaths.Count;
            }
        }
        
        // 현재 그리드 설정 정보 반환 (디버깅용)
        public (Vector3 center, float range, float gridSize) GetCurrentGridSettings()
        {
            return (currentCenter, currentRange, currentGridSize);
        }
    }

    [System.Serializable]
    public class PathCreatorDstar
    {
        public float Height = 5;
        public float GridRotationY = 0;
        public float Radius = 1;
        public float GridSize = 3;
        public float Chaos = 0;
        public float StraightPathPriority = 10;
        public float NearObstaclesPriority = 100; // 근접 장애물 회피 활성화
        public int MaxIterations = 1000;
        public bool hasCollision = false;
        public bool LastPathSuccess = true;
        public LayerMask obstacleLayerMask = -1; // 모든 레이어 체크 (기본값)
        public bool strictObstacleAvoidance = false; // 엄격한 장애물 회피 (기본값: false)
        public float obstacleAvoidanceMargin = 1.5f; // 장애물 회피 여백
        
        // 디버그 설정 (성능 최적화)
        private bool enableVerboseLogging = false;
        class Node : IComparable<Node>
        {
            public Vector3 position;
            public float g = Mathf.Infinity;
            public float rhs = Mathf.Infinity;
            private const float positionEpsilon = 0.001f;

            public Node(Vector3 pos)
            {
                position = pos;
            }

            // IComparable<Node> 구현 - position 기준으로 사전식 비교
            public int CompareTo(Node other)
            {
                if (other == null) return 1;
                int cmpX = CompareFloat(position.x, other.position.x);
                if (cmpX != 0) return cmpX;

                int cmpY = CompareFloat(position.y, other.position.y);
                if (cmpY != 0) return cmpY;

                return CompareFloat(position.z, other.position.z);
            }

            private int CompareFloat(float a, float b)
            {
                if (Mathf.Abs(a - b) < positionEpsilon) return 0;
                return a < b ? -1 : 1;
            }

            // 위치가 거의 같은지 비교 (부동소수점 오차 허용)
            public override bool Equals(object obj)
            {
                if (obj is Node other)
                {
                    return Vector3.SqrMagnitude(position - other.position) < positionEpsilon * positionEpsilon;
                }
                return false;
            }

            public override int GetHashCode()
            {
                // 부동소수점 오차 고려해 위치를 정수 그리드로 변환 후 해시
                int xHash = Mathf.RoundToInt(position.x / positionEpsilon);
                int yHash = Mathf.RoundToInt(position.y / positionEpsilon);
                int zHash = Mathf.RoundToInt(position.z / positionEpsilon);

                int hash = 17;
                hash = hash * 31 + xHash;
                hash = hash * 31 + yHash;
                hash = hash * 31 + zHash;
                return hash;
            }
        }
        public class PriorityQueue<Node>
        {
            private List<(Node item, (float, float) key)> elements = new();

            public int Count => elements.Count;

            public void Enqueue(Node item, (float, float) key)
            {
                // 기존 노드 제거 (같은 위치인 경우)
                elements.RemoveAll(e => e.item.Equals(item));

                elements.Add((item, key));
                elements.Sort((a, b) =>
                {
                    int cmp1 = a.key.Item1.CompareTo(b.key.Item1);
                    return cmp1 != 0 ? cmp1 : a.key.Item2.CompareTo(b.key.Item2);
                });
            }

            public Node Dequeue()
            {
                var item = elements[0].item;
                elements.RemoveAt(0);
                return item;
            }

            public (float, float) Peek()
            {
                return elements[0].key;
            }

            public void Remove(Node item)
            {
                elements.RemoveAll(e => e.item.Equals(item));
            }
        }
        private Node GetNode(Vector3 pos)
        {
            var p = SnapToGrid(pos, GridSize);
            if (!nodes.TryGetValue(p, out var node))
            {
                node = new Node(p);
                nodes[p] = node;
            }
            return node;
        }
        private Dictionary<Vector3, Node> nodes = new();
        private PriorityQueue<Node> openList = new();
        private Vector3 start, goal;
        private float km = 0;
        private int width, height;
        private HashSet<Vector3> obstacles;
        
        // 동적 그리드 범위 계산을 위한 필드들
        private Vector3 gridMinBounds;
        private Vector3 gridMaxBounds;
        private List<PathRequest> allPathRequests;
        
        bool AreEqual(Vector3 a, Vector3 b, float epsilon = 0.001f)
        {
            return Vector3.SqrMagnitude(a - b) < epsilon * epsilon;
        }

        public List<Vector3> Create(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float pipeRadius)
        {
            obstacleCache.Clear();
            this.width = 100;
            this.height = 5;
            Radius = pipeRadius;
            var path = new List<Vector3>();

            Vector3 pathStart = startPoint + startNormal.normalized * Height;
            Vector3 pathEnd = endPoint + endNormal.normalized * Height;
            this.goal = SnapToGrid(pathEnd, GridSize);
            this.start = SnapToGrid(pathStart, GridSize);

            // 동적 그리드 생성 (요청 리스트가 설정되어 있는 경우)
            CreateDynamicGrid();
            
            // Start와 Goal이 그리드 범위 내에 있는지 검증
            if (!IsWithinGridBounds(start))
            {
                Debug.LogWarning($"[DStar] Start 위치가 그리드 범위를 벗어남: {start}");
                // 가장 가까운 그리드 내 위치로 조정
                start = ClampToGridBounds(start);
            }
            
            if (!IsWithinGridBounds(goal))
            {
                Debug.LogWarning($"[DStar] Goal 위치가 그리드 범위를 벗어남: {goal}");
                // 가장 가까운 그리드 내 위치로 조정
                goal = ClampToGridBounds(goal);
            }

            GetNode(goal).rhs = 0;
            openList.Enqueue(GetNode(goal), CalculateKey(goal));

            path.Add(startPoint);
            path.Add(pathStart);

            LastPathSuccess = true;
            hasCollision = false;

            ComputeShortestPath();

            var foundPath = this.GetPath();
            if (foundPath == null)
            {
                LastPathSuccess = false;
            }
            else
            {
                foreach (var p in foundPath)
                {
                    path.Add(p);
                    if (!hasCollision && Physics.CheckSphere(p, Radius * 1.2f))
                        hasCollision = true;
                }
            }

            path.Add(pathEnd);
            path.Add(endPoint);
            return path;
        }
        
        // 위치를 그리드 범위 내로 제한
        private Vector3 ClampToGridBounds(Vector3 pos)
        {
            float x = Mathf.Clamp(pos.x, gridMinBounds.x, gridMaxBounds.x);
            float y = Mathf.Clamp(pos.y, gridMinBounds.y, gridMaxBounds.y);
            float z = Mathf.Clamp(pos.z, gridMinBounds.z, gridMaxBounds.z);
            return SnapToGrid(new Vector3(x, y, z), GridSize);
        }
        private void InitializeObstacleCache()
        {
            obstacles = new HashSet<Vector3>();
            Collider[] hits = Physics.OverlapSphere(start, 1000f, obstacleLayerMask); // 범위 넓게 잡기
            foreach (var hit in hits)
            {
                Vector3 pos = SnapToGrid(hit.transform.position, GridSize);
                obstacles.Add(pos);
            }
        }
        private (float, float) CalculateKey(Vector3 u)
        {
            float min = Mathf.Min(GetNode(u).g, GetNode(u).rhs);
            return (min + Heuristic(start, u) + km, min);
        }

        private float Heuristic(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
        }

        private float Cost(Vector3 a, Vector3 b)
        {
            if (IsObstacle(b))
            {
                if (strictObstacleAvoidance)
                    return Mathf.Infinity;

                return NearObstaclesPriority;
            }
            return 1;
        }

        private List<Vector3> GetNeighbors(Vector3 pos)
        {
            List<Vector3> neighbors = new();
            Vector3[] dirs = {
            new Vector3(1, 0, 0),
            new Vector3(-1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, -1, 0),
            new Vector3(0, 0, 1),
            new Vector3(0, 0, -1)
        };
            foreach (var dir in dirs)
            {
                Vector3 n = SnapToGrid(pos + dir * GridSize, GridSize);
                
                // 그리드 경계 체크 추가 - 무한 노드 생성 방지
                if (IsWithinGridBounds(n))
                {
                    neighbors.Add(n);
                }
            }

            return neighbors;
        }
        
        // 그리드 경계 내에 있는지 확인
        private bool IsWithinGridBounds(Vector3 pos)
        {
            return pos.x >= gridMinBounds.x && pos.x <= gridMaxBounds.x &&
                   pos.y >= gridMinBounds.y && pos.y <= gridMaxBounds.y &&
                   pos.z >= gridMinBounds.z && pos.z <= gridMaxBounds.z;
        }
        private Dictionary<Vector3, bool> obstacleCache = new();
        
        // 멀티스레딩 지원을 위한 필드들
        private Dictionary<Vector3, bool> sharedObstacleData;
        private object sharedObstacleDataLock;
        private bool useSharedObstacleData = false;
        
        // 성능 설정 필드들
        private int customMaxIterations = 1000;
        private int customMaxTimeoutSeconds = 30;
        private int customMaxNodesPerAxis = 100;
        private int customMaxTotalNodes = 50000;
        
        // 공유 장애물 데이터 설정 (멀티스레딩용)
        public void SetObstacleDataFromShared(Dictionary<Vector3, bool> sharedData, object lockObject)
        {
            sharedObstacleData = sharedData;
            sharedObstacleDataLock = lockObject;
            useSharedObstacleData = true;
        }
        
        // 성능 설정 (매니저에서 전달받음)
        public void SetPerformanceSettings(int maxIterations, int maxTimeoutSeconds, int maxNodesPerAxis, int maxTotalNodes)
        {
            customMaxIterations = maxIterations;
            customMaxTimeoutSeconds = maxTimeoutSeconds;
            customMaxNodesPerAxis = maxNodesPerAxis;
            customMaxTotalNodes = maxTotalNodes;
            
            // 기존 MaxIterations 필드도 업데이트
            MaxIterations = maxIterations;
        }
        
        // 디버그 설정
        public void SetDebugSettings(bool enableVerbose)
        {
            enableVerboseLogging = enableVerbose;
        }
        
        // 요청 리스트 설정 (동적 그리드 계산용)
        public void SetPathRequests(List<PathRequest> requests)
        {
            allPathRequests = requests;
            CalculateGridBounds();
        }
        
        // 모든 요청의 시작점과 끝점을 기반으로 그리드 범위 계산
        private void CalculateGridBounds()
        {
            if (allPathRequests == null || allPathRequests.Count == 0)
            {
                // 기본값 설정
                gridMinBounds = new Vector3(-10, -5, -10);
                gridMaxBounds = new Vector3(10, 10, 10);
                Debug.LogWarning("[그리드] 요청이 없어 기본 범위 사용");
                return;
            }
            
            // 첫 번째 요청의 시작점으로 초기화
            var firstRequest = allPathRequests[0];
            Vector3 firstStart = firstRequest.startPoint + firstRequest.startNormal.normalized * Height;
            Vector3 firstEnd = firstRequest.endPoint + firstRequest.endNormal.normalized * Height;
            
            float minX = Mathf.Min(firstStart.x, firstEnd.x);
            float maxX = Mathf.Max(firstStart.x, firstEnd.x);
            float minY = Mathf.Min(firstStart.y, firstEnd.y);
            float maxY = Mathf.Max(firstStart.y, firstEnd.y);
            float minZ = Mathf.Min(firstStart.z, firstEnd.z);
            float maxZ = Mathf.Max(firstStart.z, firstEnd.z);
            
            // 모든 요청의 시작점과 끝점을 검사
            foreach (var request in allPathRequests)
            {
                Vector3 startPos = request.startPoint + request.startNormal.normalized * Height;
                Vector3 endPos = request.endPoint + request.endNormal.normalized * Height;
                
                // 원본 엔드포인트도 포함
                Vector3[] allPoints = { 
                    request.startPoint, request.endPoint, startPos, endPos 
                };
                
                foreach (var point in allPoints)
                {
                    minX = Mathf.Min(minX, point.x);
                    maxX = Mathf.Max(maxX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxY = Mathf.Max(maxY, point.y);
                    minZ = Mathf.Min(minZ, point.z);
                    maxZ = Mathf.Max(maxZ, point.z);
                }
            }
            
            // 실제 환경 크기 계산
            Vector3 environmentSize = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);
            float maxEnvironmentDimension = Mathf.Max(environmentSize.x, environmentSize.y, environmentSize.z);
            
            // 최대 파이프 반지름 계산
            float maxPipeRadius = 0f;
            foreach (var request in allPathRequests)
            {
                maxPipeRadius = Mathf.Max(maxPipeRadius, request.pipeRadius);
            }
            
            // 환경 크기에 따른 적응적 패딩 계산
            float adaptivePadding = CalculateAdaptivePadding(environmentSize, maxPipeRadius, maxEnvironmentDimension);
            
            // 각 축별 개별 패딩 적용 (비대칭 환경 대응)
            float paddingX = Mathf.Max(adaptivePadding, environmentSize.x * 0.2f + maxPipeRadius * 3f);
            float paddingY = Mathf.Max(adaptivePadding, environmentSize.y * 0.15f + Height * 2f); // Y축은 Height 고려
            float paddingZ = Mathf.Max(adaptivePadding, environmentSize.z * 0.2f + maxPipeRadius * 3f);
            
            // 최종 그리드 범위 계산
            minX -= paddingX;
            maxX += paddingX;
            minY -= paddingY;
            maxY += paddingY + Height; // Y축 상단에 추가 여백
            minZ -= paddingZ;
            maxZ += paddingZ;
            
            // 최소 범위 보장 (너무 작은 환경 방지)
            float minRangePerAxis = GridSize * 10f;
            
            EnsureMinimumRange(ref minX, ref maxX, minRangePerAxis);
            EnsureMinimumRange(ref minY, ref maxY, minRangePerAxis);
            EnsureMinimumRange(ref minZ, ref maxZ, minRangePerAxis);
            
            // 성능을 고려한 최대 범위 제한
            float maxAllowedRange = CalculateMaxAllowedRange(maxEnvironmentDimension);
            LimitMaximumRange(ref minX, ref maxX, maxAllowedRange);
            LimitMaximumRange(ref minY, ref maxY, maxAllowedRange);
            LimitMaximumRange(ref minZ, ref maxZ, maxAllowedRange);
            
            gridMinBounds = new Vector3(minX, minY, minZ);
            gridMaxBounds = new Vector3(maxX, maxY, maxZ);
            
            // 최종 그리드 정보 로깅
            LogGridCalculationResults(environmentSize, maxEnvironmentDimension, maxPipeRadius, adaptivePadding);
        }
        
        // 환경 크기에 따른 적응적 패딩 계산
        private float CalculateAdaptivePadding(Vector3 environmentSize, float maxPipeRadius, float maxDimension)
        {
            // 기본 패딩 (파이프 반지름 기반)
            float basePadding = maxPipeRadius * 5f;
            
            // 환경 크기 비례 패딩
            float environmentPadding = 0f;
            
            if (maxDimension > 200f)
            {
                // 대형 환경: 환경 크기의 15%
                environmentPadding = maxDimension * 0.15f;
            }
            else if (maxDimension > 50f)
            {
                // 중형 환경: 환경 크기의 25%
                environmentPadding = maxDimension * 0.25f;
            }
            else
            {
                // 소형 환경: 환경 크기의 40%
                environmentPadding = maxDimension * 0.4f;
            }
            
            // 최종 패딩 (기본 패딩과 환경 패딩 중 큰 값)
            float finalPadding = Mathf.Max(basePadding, environmentPadding);
            
            // 최소 패딩 보장
            finalPadding = Mathf.Max(finalPadding, GridSize * 5f);
            
            // 최대 패딩 제한 (메모리 및 성능 고려)
            finalPadding = Mathf.Min(finalPadding, 500f);
            
            return finalPadding;
        }
        
        // 최소 범위 보장
        private void EnsureMinimumRange(ref float min, ref float max, float minRange)
        {
            if (max - min < minRange)
            {
                float center = (min + max) * 0.5f;
                min = center - minRange * 0.5f;
                max = center + minRange * 0.5f;
            }
        }
        
        // 최대 범위 제한
        private void LimitMaximumRange(ref float min, ref float max, float maxRange)
        {
            if (max - min > maxRange)
            {
                float center = (min + max) * 0.5f;
                min = center - maxRange * 0.5f;
                max = center + maxRange * 0.5f;
            }
        }
        
        // 성능을 고려한 최대 허용 범위 계산
        private float CalculateMaxAllowedRange(float maxEnvironmentDimension)
        {
            // 환경 크기에 따른 적응적 최대 범위
            if (maxEnvironmentDimension > 500f)
            {
                return 1500f; // 매우 큰 환경
            }
            else if (maxEnvironmentDimension > 200f)
            {
                return 800f; // 큰 환경
            }
            else if (maxEnvironmentDimension > 100f)
            {
                return 400f; // 중간 환경
            }
            else
            {
                return 200f; // 작은 환경
            }
        }
        
        // 그리드 계산 결과 로깅
        private void LogGridCalculationResults(Vector3 environmentSize, float maxDimension, float maxPipeRadius, float padding)
        {
            Vector3 finalGridSize = gridMaxBounds - gridMinBounds;
            int estimatedNodes = Mathf.RoundToInt((finalGridSize.x / GridSize) * (finalGridSize.y / GridSize) * (finalGridSize.z / GridSize));
            
            Debug.Log($"[자동 그리드] 환경 크기: {environmentSize} (최대: {maxDimension:F1})");
            Debug.Log($"[자동 그리드] 최대 파이프 반지름: {maxPipeRadius:F1}, 적응적 패딩: {padding:F1}");
            Debug.Log($"[자동 그리드] 최종 범위: {gridMinBounds} ~ {gridMaxBounds}");
            Debug.Log($"[자동 그리드] 그리드 크기: {finalGridSize}, 예상 노드: {estimatedNodes:N0}개");
            
            // 성능 경고
            if (estimatedNodes > 100000)
            {
                Debug.LogWarning($"[자동 그리드] 예상 노드 수가 많음: {estimatedNodes:N0}개 - 성능 저하 가능성");
            }
            else if (estimatedNodes > 50000)
            {
                Debug.Log($"[자동 그리드] 중간 크기 그리드: {estimatedNodes:N0}개 노드");
            }
            else
            {
                Debug.Log($"[자동 그리드] 최적화된 그리드: {estimatedNodes:N0}개 노드");
            }
        }
        
        // 동적 그리드 노드 생성
        private void CreateDynamicGrid()
        {
            if (gridMinBounds == Vector3.zero && gridMaxBounds == Vector3.zero)
            {
                CalculateGridBounds();
            }
            
            nodes.Clear();
            
            // 그리드 크기 제한 (성능 및 메모리 보호)
            Vector3 gridSize = gridMaxBounds - gridMinBounds;
            
            // 그리드가 너무 클 경우 범위 축소
            if (gridSize.x / GridSize > customMaxNodesPerAxis)
            {
                float center = (gridMinBounds.x + gridMaxBounds.x) * 0.5f;
                float halfRange = customMaxNodesPerAxis * GridSize * 0.5f;
                gridMinBounds.x = center - halfRange;
                gridMaxBounds.x = center + halfRange;
                Debug.LogWarning($"[그리드] X축 범위가 너무 커서 축소: {gridMinBounds.x} ~ {gridMaxBounds.x}");
            }
            
            if (gridSize.y / GridSize > customMaxNodesPerAxis)
            {
                float center = (gridMinBounds.y + gridMaxBounds.y) * 0.5f;
                float halfRange = customMaxNodesPerAxis * GridSize * 0.5f;
                gridMinBounds.y = center - halfRange;
                gridMaxBounds.y = center + halfRange;
                Debug.LogWarning($"[그리드] Y축 범위가 너무 커서 축소: {gridMinBounds.y} ~ {gridMaxBounds.y}");
            }
            
            if (gridSize.z / GridSize > customMaxNodesPerAxis)
            {
                float center = (gridMinBounds.z + gridMaxBounds.z) * 0.5f;
                float halfRange = customMaxNodesPerAxis * GridSize * 0.5f;
                gridMinBounds.z = center - halfRange;
                gridMaxBounds.z = center + halfRange;
                Debug.LogWarning($"[그리드] Z축 범위가 너무 커서 축소: {gridMinBounds.z} ~ {gridMaxBounds.z}");
            }
            
            // 안전한 그리드 생성
            int nodeCount = 0;
            
            for (float x = gridMinBounds.x; x <= gridMaxBounds.x && nodeCount < customMaxTotalNodes; x += GridSize)
            {
                for (float y = gridMinBounds.y; y <= gridMaxBounds.y && nodeCount < customMaxTotalNodes; y += GridSize)
                {
                    for (float z = gridMinBounds.z; z <= gridMaxBounds.z && nodeCount < customMaxTotalNodes; z += GridSize)
                    {
                        Vector3 pos = SnapToGrid(new Vector3(x, y, z), GridSize);
                        if (!nodes.ContainsKey(pos))
                        {
                            nodes[pos] = new Node(pos);
                            nodeCount++;
                        }
                        
                        // 안전장치: 너무 많은 노드 생성 방지
                        if (nodeCount >= customMaxTotalNodes)
                        {
                            Debug.LogWarning($"[그리드] 최대 노드 수 도달로 생성 중단: {customMaxTotalNodes}개");
                            break;
                        }
                    }
                }
            }
            
            // 경고가 필요한 경우에만 로그 출력
            if (nodeCount > 50000)
            {
                Debug.LogWarning($"[그리드] 대용량 노드 생성: {nodeCount}개 (범위: {gridMinBounds} ~ {gridMaxBounds})");
            }
        }
        
        private bool IsObstacle(Vector3 position)
        {
            Vector3 key = SnapToGrid(position, GridSize);

            // 캐시 확인
            if (obstacleCache.TryGetValue(key, out bool cachedResult))
            {
                return cachedResult;
            }

            bool isObstacle = false;
            
            if (useSharedObstacleData)
            {
                // 멀티스레딩 환경에서는 공유 데이터 사용
                lock (sharedObstacleDataLock)
                {
                    isObstacle = sharedObstacleData.ContainsKey(key) && sharedObstacleData[key];
                }
            }
            else
            {
                // 메인 스레드에서는 Physics API 사용
            Vector3[] directions = new Vector3[]
            {
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right,
                Vector3.up,
                Vector3.down
            };

            float checkRadius = Radius * obstacleAvoidanceMargin;
            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 dir = directions[i];

                if (Physics.Raycast(key, dir, out RaycastHit hit, 1f, obstacleLayerMask))
                {
                        isObstacle = true;
                        break;
                }
                }
                }

            // 결과 캐싱
            obstacleCache[key] = isObstacle;
            return isObstacle;
        }
        private void UpdateVertex(Vector3 u)
        {
            u = SnapToGrid(u, GridSize);
            var uNode = GetNode(u);

            if (!AreEqual(u, goal))
            {
                float minRhs = Mathf.Infinity;
                foreach (var s in GetNeighbors(u))
                {
                    float cost = Cost(u, s);
                    float g_s = GetNode(s).g;

                    if (cost != Mathf.Infinity) // 무한대 비용이 아닌 경우만 고려
                    {
                    float total = cost + g_s;
                    if (total < minRhs) minRhs = total;
                    }
                }

                uNode.rhs = minRhs;
            }

            openList.Remove(uNode);

            if (Mathf.Abs(uNode.g - uNode.rhs) > 0.001f) // 부동소수점 오차 고려
            {
                openList.Enqueue(uNode, CalculateKey(u));
            }
        }

        public void ComputeShortestPath()
        {
            var iteration = 0;
            var startTime = System.DateTime.Now;
            
            while (openList.Count > 0 && iteration < MaxIterations)
            {
                // 타임아웃 체크 (100번마다)
                if (iteration % 100 == 0)
                {
                    var elapsedTime = (System.DateTime.Now - startTime).TotalSeconds;
                    if (elapsedTime > customMaxTimeoutSeconds)
                    {
                        Debug.LogError($"[DStar] 타임아웃 발생 ({customMaxTimeoutSeconds}초) - 반복: {iteration}");
                        LastPathSuccess = false;
                        break;
                    }
                }
                
                var currentKey = openList.Peek();
                var startKey = CalculateKey(start);
                var startNode = GetNode(start);
                
                // 종료 조건: start 노드가 consistent하고(g == rhs) key가 start보다 크거나 같으면 종료
                if ((CompareKey(currentKey, startKey) >= 0) && 
                    (Mathf.Abs(startNode.g - startNode.rhs) < 0.001f))
                {
                    break;
                }
                
                iteration++;
                
                var u = openList.Dequeue();
                var uPos = u.position;
                
                if (u.g > u.rhs)
                {
                    u.g = u.rhs;
                    foreach (var s in GetNeighbors(uPos))
                        UpdateVertex(s);
                }
                else
                {
                    u.g = Mathf.Infinity;
                    UpdateVertex(uPos);
                    foreach (var s in GetNeighbors(uPos))
                        UpdateVertex(s);
                }
            }
            
            var totalTime = (System.DateTime.Now - startTime).TotalSeconds;
            
            if (iteration >= MaxIterations)
            {
                Debug.LogError($"[DStar] 최대 반복 횟수 도달: {MaxIterations}, 시간: {totalTime:F2}초");
                LastPathSuccess = false;
            }
            else if (openList.Count == 0)
            {
                Debug.LogWarning($"[DStar] OpenList 소진으로 종료 - 반복: {iteration}, 시간: {totalTime:F2}초");
            }
        }

        public List<Vector3> GetPath()
        {
            List<Vector3> path = new();
            Vector3 current = start;
            int maxPathSteps = 10000;
            int steps = 0;
            var startTime = System.DateTime.Now;
            
            while (!AreEqual(current, goal) && steps < maxPathSteps)
            {
                // 타임아웃 체크 (1000번마다)
                if (steps % 1000 == 0 && steps > 0)
                {
                    var elapsedTime = (System.DateTime.Now - startTime).TotalSeconds;
                    if (elapsedTime > 10)
                    {
                        Debug.LogError($"[GetPath] 타임아웃 발생 (10초) - 단계: {steps}");
                        return null;
                    }
                }
                
                float min = Mathf.Infinity;
                Vector3 next = current;
                bool foundValidNeighbor = false;
                
                foreach (var s in GetNeighbors(current))
                {
                    float cost = Cost(current, s) + GetNode(s).g;
                    
                    if (cost < min && cost != Mathf.Infinity)
                    {
                        min = cost;
                        next = s;
                        foundValidNeighbor = true;
                    }
                }

                if (!foundValidNeighbor || min == Mathf.Infinity || AreEqual(next, current))
                {
                    return null; // No path
                }

                current = next;
                path.Add(current);
                steps++;
            }
            
            var totalTime = (System.DateTime.Now - startTime).TotalSeconds;
            
            if (steps >= maxPathSteps)
            {
                Debug.LogError($"[GetPath] 최대 단계 수 도달: {maxPathSteps}, 시간: {totalTime:F2}초");
                return null;
            }

            return path;
        }

        private int Compare(Vector3 a, Vector3 b, float epsilon = 0.0001f)
        {
            if (Mathf.Abs(a.x - b.x) > epsilon)
                return a.x < b.x ? -1 : 1;

            if (Mathf.Abs(a.y - b.y) > epsilon)
                return a.y < b.y ? -1 : 1;

            if (Mathf.Abs(a.z - b.z) > epsilon)
                return a.z < b.z ? -1 : 1;

            return 0; // 모든 축이 유사하면 같다고 판단
        }
        private int CompareKey((float, float) a, (float, float) b)
        {
            if (a.Item1 < b.Item1) return -1;
            if (a.Item1 > b.Item1) return 1;
            if (a.Item2 < b.Item2) return -1;
            if (a.Item2 > b.Item2) return 1;
            return 0;
        }
        Vector3 SnapToGrid(Vector3 pos, float gridSize)
        {
            float x = Mathf.Round(pos.x / gridSize) * gridSize;
            float y = Mathf.Round(pos.y / gridSize) * gridSize;
            float z = Mathf.Round(pos.z / gridSize) * gridSize;
            return new Vector3(x, y, z);
        }
    }
}
