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
            this.width = 100;
            this.height = 5;
            Radius = pipeRadius;
            var path = new List<Vector3>();

            Vector3 pathStart = startPoint + startNormal.normalized * Height;
            Vector3 pathEnd = endPoint + endNormal.normalized * Height;
            this.goal = SnapToGrid(pathEnd, GridSize);
            this.start = SnapToGrid(pathStart, GridSize);
            //this.obstacles = None;

            for (int x = 0; x < 10; x++)
                for (int y = 0; y < 10; y++)
                    for (int z = 0; z < 5; z++){
                        Vector3 pos = SnapToGrid(new Vector3(x, y, z), GridSize);
                        nodes[pos] = new Node(pos);
                    }

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
                //if (n.x >= 0 && n.y >= 0 && n.x < width && n.y < height)
                neighbors.Add(n);
                if (AreEqual(n, goal)) Debug.Log($"[이웃에 goal 있음] {pos} → {n}");
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
            
            while (openList.Count > 0 && iteration < MaxIterations)
            {
                var currentKey = openList.Peek();
                var startKey = CalculateKey(start);
                var startNode = GetNode(start);
                
                // 종료 조건: start 노드가 consistent하고(g == rhs) key가 start보다 크거나 같으면 종료
                if ((CompareKey(currentKey, startKey) >= 0) && 
                    (Mathf.Abs(startNode.g - startNode.rhs) < 0.001f))
                {
                    Debug.Log($"[종료] iteration: {iteration}, start g: {startNode.g}, rhs: {startNode.rhs}");
                    break;
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
        }

        public List<Vector3> GetPath()
        {
            List<Vector3> path = new();
            Vector3 current = start;
            Debug.Log("1");
            while (current != goal)
            {
                Debug.Log($"{current.x}, {current.y}, {current.z}");
                float min = Mathf.Infinity;
                Vector3 next = current;
                foreach (var s in GetNeighbors(current))
                {
                    Debug.Log("11");
                    float cost = Cost(current, s) + GetNode(s).g;
                    Debug.Log("22");
                    if (cost < min)
                    {
                        min = cost;
                        next = s;
                    }
                }

                if (min == Mathf.Infinity || next == current)
                    return null; // No path

                current = next;
                path.Add(current);
            }
            Debug.Log("2");

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
