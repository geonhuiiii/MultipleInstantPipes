using System.Collections.Generic;
using UnityEngine;
using System;
namespace InstantPipes
{
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
        private bool IsObstacle(Vector3 position)
        {
            Vector3[] directions = new Vector3[]
            {
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right,
                Vector3.up,
                Vector3.down
            };
            // 그리드에 스냅해서 비교 정확도 향상
            Vector3 key = SnapToGrid(position, GridSize);

            if (obstacleCache.TryGetValue(key, out bool isObstacle))
            {
                return isObstacle;
            }

            float checkRadius = Radius * obstacleAvoidanceMargin;
            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 dir = directions[i];

                if (Physics.Raycast(key, dir, out RaycastHit hit, 1f, obstacleLayerMask))
                {
                    obstacleCache[key] = true;
                    return true;
                }
                else
                {
                }
            } // 결과 캐싱
            obstacleCache[key] = false;
            return false;
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
                    Debug.Log($"u: {u}, s: {s}, cost: {cost}, g(s): {g_s}");

                    float total = cost + g_s;
                    if (total < minRhs) minRhs = total;
                }

                uNode.rhs = minRhs;
                Debug.Log($"Updated rhs({u}) = {uNode.rhs}");
            }

            openList.Remove(uNode);

            if (uNode.g != uNode.rhs)
            {
                Debug.Log($"Enqueue {u} with key {CalculateKey(u)}");
                openList.Enqueue(uNode, CalculateKey(u));
            }
        }

        public void ComputeShortestPath()
        {
            var iteration = 0;
            Debug.Log(openList.Peek());
            Debug.Log($"start {CalculateKey(start)}");
            while (openList.Count > 0 &&
                    (CompareKey(openList.Peek(), CalculateKey(start)) < 0 ||
                    GetNode(start).g != GetNode(start).rhs) && MaxIterations > iteration)
            {
                if (CompareKey(openList.Peek(), CalculateKey(start)) < 0)
                    Debug.Log("잠은 잘 수 있겠지");
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
