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
        private bool isInitialized = false;
        
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
                Debug.Log($"[멀티스레딩] 장애물 데이터 초기화 완료: {sharedObstacleData.Count}개 위치");
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
        
        // 모든 요청에 대해 초기 경로 탐색 (병렬)
        public async Task ProcessInitialPathsAsync()
        {
            var tasks = new List<Task>();
            var requests = new List<PathRequest>();
            
            // 모든 요청을 리스트로 수집
            while (pendingRequests.TryDequeue(out var request))
            {
                requests.Add(request);
            }
            
            Debug.Log($"[멀티스레딩] 초기 경로 탐색 시작: {requests.Count}개 파이프");
            
            // 병렬로 초기 경로 탐색
            foreach (var request in requests)
            {
                tasks.Add(ProcessSingleRequestAsync(request, isInitialPass: true));
            }
            
            await Task.WhenAll(tasks);
            Debug.Log("[멀티스레딩] 초기 경로 탐색 완료");
        }
        
        // 순서대로 순차 경로 탐색
        public async Task ProcessPriorityPathsAsync()
        {
            List<PathRequest> orderedRequests;
            
            // 추가된 순서대로 요청 가져오기
            lock (requestsLock)
            {
                orderedRequests = new List<PathRequest>(allRequests);
            }
            
            Debug.Log($"[멀티스레딩] 순차 경로 탐색 시작: {orderedRequests.Count}개 파이프 (순서대로)");
            
            // 순서대로 처리 (실패한 파이프 또는 추가 최적화가 필요한 경우)
            foreach (var request in orderedRequests)
            {
                var existingResult = completedResults.GetValueOrDefault(request.pipeId);
                
                // 실패했거나 추가 최적화가 필요한 경우 재처리
                if (existingResult == null || !existingResult.success)
                {
                    Debug.Log($"[멀티스레딩] 파이프 {request.pipeId} 순차 최적화 처리");
                    await ProcessSingleRequestAsync(request, isInitialPass: false);
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
                    
                    string passType = isInitialPass ? "초기" : "우선순위";
                    Debug.Log($"[멀티스레딩] {passType} 경로 탐색 완료 - ID: {request.pipeId}, 성공: {result.success}");
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
            
            // 요청 리스트도 함께 지우기
            lock (requestsLock)
            {
                allRequests.Clear();
            }
        }
        
        private static Vector3 SnapToGrid(Vector3 pos, float gridSize)
        {
            float x = Mathf.Round(pos.x / gridSize) * gridSize;
            float y = Mathf.Round(pos.y / gridSize) * gridSize;
            float z = Mathf.Round(pos.z / gridSize) * gridSize;
            return new Vector3(x, y, z);
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
        bool AreEqual(Vector3 a, Vector3 b, float epsilon = 0.001f)
        {
            return Vector3.SqrMagnitude(a - b) < epsilon * epsilon;
        }

        public List<Vector3> Create(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float pipeRadius)
        {
            obstacleCache.Clear();
            Radius = pipeRadius;
            var path = new List<Vector3>();

            Vector3 pathStart = startPoint + startNormal.normalized * Height;
            Vector3 pathEnd = endPoint + endNormal.normalized * Height;
            this.goal = SnapToGrid(pathEnd, GridSize);
            this.start = SnapToGrid(pathStart, GridSize);

            // 시작점과 끝점을 기반으로 그리드 범위 계산
            Vector3[] points = { startPoint, endPoint, pathStart, pathEnd };
            
            // X,Y,Z 각각의 최소값과 최대값 계산
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            
            foreach (var point in points)
            {
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
                minZ = Mathf.Min(minZ, point.z);
                maxZ = Mathf.Max(maxZ, point.z);
            }
            
            // 여유 공간 추가 (그리드 크기의 5배)
            float margin = GridSize * 5;
            minX -= margin;
            maxX += margin;
            minY -= margin;
            maxY += margin;
            minZ -= margin;
            maxZ = Mathf.Max(maxZ + margin, minZ + 10); // Z축 최대 높이는 +10 보장
            
            // 그리드 범위를 GridSize 단위로 정렬
            minX = Mathf.Floor(minX / GridSize) * GridSize;
            maxX = Mathf.Ceil(maxX / GridSize) * GridSize;
            minY = Mathf.Floor(minY / GridSize) * GridSize;
            maxY = Mathf.Ceil(maxY / GridSize) * GridSize;
            minZ = Mathf.Floor(minZ / GridSize) * GridSize;
            maxZ = Mathf.Ceil(maxZ / GridSize) * GridSize;
            
            Debug.Log($"[그리드 범위] X: {minX}~{maxX}, Y: {minY}~{maxY}, Z: {minZ}~{maxZ}");
            
            // 동적 그리드 생성
            for (float x = minX; x <= maxX; x += GridSize)
            {
                for (float y = minY; y <= maxY; y += GridSize)
                {
                    for (float z = minZ; z <= maxZ; z += GridSize)
                    {
                        Vector3 pos = SnapToGrid(new Vector3(x, y, z), GridSize);
                        nodes[pos] = new Node(pos);
                    }
                }
            }
            
            // 크기 정보 업데이트
            this.width = Mathf.RoundToInt((maxX - minX) / GridSize) + 1;
            this.height = Mathf.RoundToInt((maxZ - minZ) / GridSize) + 1;

            GetNode(goal).rhs = 0;
            Debug.Log("삽입");
            openList.Enqueue(GetNode(goal), CalculateKey(goal));
            Debug.Log($"[초기화] goal: {goal}, rhs = {GetNode(goal).rhs}, key = {CalculateKey(goal)}");

            path.Add(startPoint);
            path.Add(pathStart);

            LastPathSuccess = true;
            hasCollision = false;

            ComputeShortestPath();

            var foundPath = this.GetPath();
            if (foundPath == null)
            {
                Debug.LogWarning("[DStar] 경로 탐색 실패");
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
            Debug.Log($"{min + Heuristic(start, u) + km}, {min}");
            return (min + Heuristic(start, u) + km, min);
        }

        private float Heuristic(Vector3 a, Vector3 b)
        {
            // 맨해튼 거리와 유클리드 거리의 조합 사용
            float manhattan = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
            float euclidean = Vector3.Distance(a, b);
            
            // 맨해튼 거리를 기본으로 하되, 유클리드 거리로 보정
            return manhattan * 0.8f + euclidean * 0.2f;
        }

        private float Cost(Vector3 a, Vector3 b)
        {
            if (IsObstacle(b))
            {
                if (strictObstacleAvoidance)
                    return Mathf.Infinity;

                return NearObstaclesPriority;
            }
            
            // 기본 이동 비용
            float baseCost = Vector3.Distance(a, b);
            
            // 방향 변화에 대한 패널티 (직선 경로 선호)
            Vector3 direction = (b - a).normalized;
            
            // 수직/수평 이동을 선호하도록 약간의 보너스
            if (Mathf.Abs(direction.x) > 0.9f || Mathf.Abs(direction.y) > 0.9f || Mathf.Abs(direction.z) > 0.9f)
            {
                baseCost *= 0.95f; // 5% 보너스
            }
            
            return baseCost;
        }

        private List<Vector3> GetNeighbors(Vector3 pos)
        {
            List<Vector3> neighbors = new();
            Vector3[] dirs = {
                new Vector3(1, 0, 0),   // +X
                new Vector3(-1, 0, 0),  // -X
                new Vector3(0, 1, 0),   // +Y
                new Vector3(0, -1, 0),  // -Y
                new Vector3(0, 0, 1),   // +Z
                new Vector3(0, 0, -1)   // -Z
            };
            
            foreach (var dir in dirs)
            {
                Vector3 n = SnapToGrid(pos + dir * GridSize, GridSize);
                
                // 동적 그리드 경계 체크: 생성된 노드 중에 있는지 확인
                if (nodes.ContainsKey(n))
                {
                    neighbors.Add(n);
                    
                    if (AreEqual(n, goal)) 
                    {
                        Debug.Log($"[이웃에 goal 있음] {pos} → {n}");
                    }
                }
                else
                {
                    // 경계를 벗어난 노드는 제외
                    Debug.Log($"[경계 외부] {n}은 그리드 범위 밖");
                }
            }

            return neighbors;
        }
        private Dictionary<Vector3, bool> obstacleCache = new();
        
        // 멀티스레딩 지원을 위한 필드들
        private Dictionary<Vector3, bool> sharedObstacleData;
        private object sharedObstacleDataLock;
        private bool useSharedObstacleData = false;
        
        // 공유 장애물 데이터 설정 (멀티스레딩용)
        public void SetObstacleDataFromShared(Dictionary<Vector3, bool> sharedData, object lockObject)
        {
            sharedObstacleData = sharedData;
            sharedObstacleDataLock = lockObject;
            useSharedObstacleData = true;
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
                Debug.Log($"Updated rhs({u}) = {uNode.rhs}");
            }

            openList.Remove(uNode);

            if (Mathf.Abs(uNode.g - uNode.rhs) > 0.001f) // 부동소수점 오차 고려
            {
                Debug.Log($"Enqueue {u} with key {CalculateKey(u)}");
                openList.Enqueue(uNode, CalculateKey(u));
            }
        }

        public void ComputeShortestPath()
        {
            var iteration = 0;
            var lastStartG = float.MaxValue;
            var noChangeCount = 0;
            const int maxNoChangeIterations = 10; // 변화 없이 반복되는 최대 횟수
            
            while (openList.Count > 0 && iteration < MaxIterations)
            {
                var currentKey = openList.Peek();
                var startKey = CalculateKey(start);
                var startNode = GetNode(start);
                
                // 개선된 종료 조건들
                bool isConsistent = Mathf.Abs(startNode.g - startNode.rhs) < 0.001f;
                bool hasValidPath = startNode.g != Mathf.Infinity && startNode.rhs != Mathf.Infinity;
                bool keyCondition = CompareKey(currentKey, startKey) >= 0;
                
                // 추가 조건: 더 이상 개선이 없는 경우
                if (Mathf.Abs(startNode.g - lastStartG) < 0.001f)
                {
                    noChangeCount++;
                }
                else
                {
                    noChangeCount = 0;
                    lastStartG = startNode.g;
                }
                
                // 종료 조건: start가 consistent하고 유효한 경로가 있으며 key 조건을 만족하는 경우
                if (isConsistent && hasValidPath && keyCondition)
                {
                    Debug.Log($"[종료] 정상 종료 - iteration: {iteration}, start g: {startNode.g}, rhs: {startNode.rhs}");
                    break;
                }
                
                // 추가 종료 조건: 변화 없이 오래 반복되는 경우
                if (noChangeCount >= maxNoChangeIterations)
                {
                    Debug.Log($"[종료] 변화 없음으로 조기 종료 - iteration: {iteration}, start g: {startNode.g}");
                    if (startNode.g != Mathf.Infinity)
                    {
                        // 일부라도 경로가 있으면 성공으로 처리
                        break;
                    }
                    else
                    {
                        // 경로가 전혀 없으면 실패
                        LastPathSuccess = false;
                        break;
                    }
                }
                
                // 추가 종료 조건: openList의 모든 노드가 start보다 나쁜 key를 가지는 경우
                if (openList.Count > 0)
                {
                    bool allWorse = true;
                    var tempElements = new List<(Node, (float, float))>();
                    
                    // openList의 상위 몇 개 노드만 체크 (성능 고려)
                    int checkCount = Mathf.Min(openList.Count, 5);
                    for (int i = 0; i < checkCount && openList.Count > 0; i++)
                    {
                        var node = openList.Dequeue();
                        var nodeKey = CalculateKey(node.position);
                        tempElements.Add((node, nodeKey));
                        
                        if (CompareKey(nodeKey, startKey) < 0)
                        {
                            allWorse = false;
                        }
                    }
                    
                    // 노드들을 다시 openList에 추가
                    foreach (var (node, nodeKey) in tempElements)
                    {
                        openList.Enqueue(node, nodeKey);
                    }
                    
                    if (allWorse && hasValidPath)
                    {
                        Debug.Log($"[종료] 더 이상 유용한 업데이트 없음 - iteration: {iteration}");
                        break;
                    }
                }
                
                iteration++;
                var u = openList.Dequeue();
                var uPos = u.position;
                
                Debug.Log($"[처리중] iteration: {iteration}, pos: {uPos}, g: {u.g}, rhs: {u.rhs}");
                
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
            
            if (iteration >= MaxIterations)
            {
                Debug.LogWarning($"[DStar] 최대 반복 횟수 도달: {MaxIterations}");
                LastPathSuccess = false;
            }
            
            // 최종 상태 로깅
            var finalStartNode = GetNode(start);
            Debug.Log($"[DStar] 최종 상태 - g: {finalStartNode.g}, rhs: {finalStartNode.rhs}, iterations: {iteration}");
        }

        public List<Vector3> GetPath()
        {
            List<Vector3> path = new();
            Vector3 current = start;
            int maxPathLength = nodes.Count * 2; // 최대 경로 길이를 더 여유롭게 설정
            int pathLength = 0;
            Vector3 lastPosition = Vector3.zero;
            int stuckCount = 0; // 같은 위치에 머무는 횟수
            const int maxStuckCount = 3; // 같은 위치에 최대 머무를 수 있는 횟수
            
            Debug.Log($"[경로 탐색] 시작: {start} → 목표: {goal}");
            
            while (!AreEqual(current, goal) && pathLength < maxPathLength)
            {
                float bestScore = Mathf.Infinity;
                Vector3 next = current;
                bool foundBetterPath = false;
                
                foreach (var s in GetNeighbors(current))
                {
                    float gCost = GetNode(s).g;
                    float moveCost = Cost(current, s);
                    float totalCost = moveCost + gCost;
                    
                    // 휴리스틱을 추가하여 목표 방향 선호
                    float heuristicToGoal = Heuristic(s, goal);
                    float combinedScore = totalCost + heuristicToGoal * 0.1f; // 약간의 휴리스틱 가중치
                    
                    if (combinedScore < bestScore && totalCost < Mathf.Infinity)
                    {
                        bestScore = combinedScore;
                        next = s;
                        foundBetterPath = true;
                    }
                }

                // 더 나은 경로가 없는 경우
                if (!foundBetterPath)
                {
                    Debug.LogWarning($"[경로 탐색] 경로를 찾을 수 없음 - 현재: {current}, 최고 점수: {bestScore}");
                    return null;
                }
                
                // 제자리에 머물러 있는 경우 체크
                if (AreEqual(next, current))
                {
                    Debug.LogWarning($"[경로 탐색] 제자리 머물기 감지 - 현재: {current}");
                    return null;
                }
                
                // 스마트한 순환 감지: 같은 위치를 연속으로 방문하는 경우만 체크
                if (AreEqual(next, lastPosition))
                {
                    stuckCount++;
                    if (stuckCount >= maxStuckCount)
                    {
                        Debug.LogWarning($"[경로 탐색] 진동/정체 감지 - {next}에서 {stuckCount}회 반복");
                        
                        // 진동이 감지되면 다른 경로 시도
                        Vector3 alternativePath = Vector3.zero;
                        float alternativeBestScore = Mathf.Infinity;
                        bool foundAlternative = false;
                        
                        foreach (var s in GetNeighbors(current))
                        {
                            // 최근에 방문한 위치가 아닌 다른 경로 찾기
                            if (!AreEqual(s, next) && !AreEqual(s, lastPosition))
                            {
                                float gCost = GetNode(s).g;
                                float moveCost = Cost(current, s);
                                float totalCost = moveCost + gCost;
                                float heuristicToGoal = Heuristic(s, goal);
                                float combinedScore = totalCost + heuristicToGoal * 0.1f;
                                
                                if (combinedScore < alternativeBestScore && totalCost < Mathf.Infinity)
                                {
                                    alternativeBestScore = combinedScore;
                                    alternativePath = s;
                                    foundAlternative = true;
                                }
                            }
                        }
                        
                        if (foundAlternative)
                        {
                            Debug.Log($"[경로 탐색] 대안 경로 발견: {alternativePath}, 점수: {alternativeBestScore}");
                            next = alternativePath;
                            bestScore = alternativeBestScore;
                            stuckCount = 0; // 리셋
                        }
                        else
                        {
                            Debug.LogWarning("[경로 탐색] 대안 경로 없음 - 탐색 실패");
                            return null;
                        }
                    }
                }
                else
                {
                    stuckCount = 0; // 다른 위치로 이동하면 리셋
                }

                lastPosition = current;
                current = next;
                path.Add(current);
                pathLength++;
                
                Debug.Log($"[경로 탐색] 단계 {pathLength}: {current}, 점수: {bestScore:F2}, 목표거리: {Vector3.Distance(current, goal):F2}");
                
                // 너무 긴 경로는 중간에 체크
                if (pathLength > 0 && pathLength % 50 == 0)
                {
                    Debug.Log($"[경로 탐색] 진행 상황: {pathLength}단계, 목표까지 거리: {Vector3.Distance(current, goal):F2}");
                }
            }
            
            // 목표에 도달했는지 확인
            if (AreEqual(current, goal))
            {
                Debug.Log($"[경로 탐색] 성공! 총 {pathLength}단계로 목표 도달");
                return path;
            }
            else if (pathLength >= maxPathLength)
            {
                Debug.LogWarning($"[경로 탐색] 최대 경로 길이 초과: {maxPathLength}");
                
                // 부분 경로라도 목표에 가까우면 반환
                if (Vector3.Distance(current, goal) < GridSize * 2)
                {
                    Debug.Log("[경로 탐색] 목표에 가까운 부분 경로 반환");
                    return path;
                }
                return null;
            }
            
            Debug.LogWarning("[경로 탐색] 알 수 없는 이유로 실패");
            return null;
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

