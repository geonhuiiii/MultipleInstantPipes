using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Utils;

namespace Model
{
    public class AStar
    {
        public float[][] SpaceCoords;
        public List<float[][]> ObstacleCoords;
        public int Dim;
        public List<float[]> PhyVertex;
        public Dictionary<string, float> EdgeCost;
        public List<(float[], string)> Directions;
        public float WPath, WBend, WEnergy;
        public int MinDisBend;
        public Dictionary<string, float> OpenSet;
        public Dictionary<string, int> CloseSet;
        public Node Start;
        public float Radius, Delta;

        public AStar(float[][] spaceCoords, List<float[][]> obstacleCoords, float wPath, float wBend, float wEnergy, int minDisBend)
        {
            SpaceCoords = spaceCoords;
            ObstacleCoords = obstacleCoords ?? new List<float[][]>();
            WPath = wPath;
            WBend = wBend;
            WEnergy = wEnergy;
            MinDisBend = minDisBend;
            Dim = 3;
            
            // 기본 공간 좌표 유효성 확인
            ValidateSpaceCoordinates();
            
            SetDirections();
            InitProperty();
            InitEdgeCost(1f);
        }
        
        // 공간 좌표 유효성 검사 및 수정
        private void ValidateSpaceCoordinates()
        {
            if (SpaceCoords == null || SpaceCoords.Length < 2)
            {
                Debug.LogError("SpaceCoords가 null이거나 불완전합니다. 기본 공간 좌표를 사용합니다.");
                // 기본 10x10x10 공간 설정
                SpaceCoords = new float[][] {
                    new float[] { -5, -5, -5 },
                    new float[] { 5, 5, 5 }
                };
            }
            
            // 각 차원에서 min이 max보다 큰 경우 교환
            for (int i = 0; i < SpaceCoords[0].Length; i++)
            {
                if (SpaceCoords[0][i] > SpaceCoords[1][i])
                {
                    float temp = SpaceCoords[0][i];
                    SpaceCoords[0][i] = SpaceCoords[1][i];
                    SpaceCoords[1][i] = temp;
                    Debug.LogWarning($"차원 {i}에서 최소/최대 좌표가 뒤바뀌었습니다. 자동 교정됨.");
                }
            }
            
            Debug.Log($"공간 좌표: 최소 {string.Join(",", SpaceCoords[0])}, 최대 {string.Join(",", SpaceCoords[1])}");
        }

        public void SetDirections()
        {
            Directions = new List<(float[], string)>();
            if (Dim == 3)
            {
                Directions.Add((new float[] { 0, 1, 0 }, "+y"));
                Directions.Add((new float[] { 0, -1, 0 }, "-y"));
                Directions.Add((new float[] { 1, 0, 0 }, "+x"));
                Directions.Add((new float[] { -1, 0, 0 }, "-x"));
                Directions.Add((new float[] { 0, 0, 1 }, "+z"));
                Directions.Add((new float[] { 0, 0, -1 }, "-z"));
            }
            else
            {
                Directions.Add((new float[] { 0, 1 }, "+y"));
                Directions.Add((new float[] { 0, -1 }, "-y"));
                Directions.Add((new float[] { 1, 0 }, "+x"));
                Directions.Add((new float[] { -1, 0 }, "-x"));
            }
            
            Debug.Log($"방향 설정 완료: {Directions.Count}개 방향");
        }

        public void InitProperty()
        {
            // 로깅 추가
            Debug.Log($"InitProperty: 장애물 수: {ObstacleCoords?.Count ?? 0}");
            
            if (SpaceCoords == null || SpaceCoords.Length < 2)
            {
                Debug.LogError("SpaceCoords가 null이거나 불완전합니다. 초기화 실패.");
                return;
            }
            
            // 경계값 로깅
            Debug.Log($"공간 영역: 최소 {string.Join(",", SpaceCoords[0])}, 최대 {string.Join(",", SpaceCoords[1])}");
            
            // 장애물 로깅
            if (ObstacleCoords != null)
            {
                foreach (var obs in ObstacleCoords)
                {
                    if (obs != null && obs.Length >= 2)
                    {
                        Debug.Log($"장애물: 최소점 {string.Join(",", obs[0])}, 최대점 {string.Join(",", obs[1])}");
                    }
                    else
                    {
                        Debug.LogWarning("유효하지 않은 장애물 데이터가, 건너뜁니다.");
                    }
                }
            }
            
            // 장애물 고려한 유효 좌표 생성
            PhyVertex = new List<float[]>();
            
            // 그리드 크기 계산 (너무 많은 포인트를 생성하지 않도록)
            int gridDensity = 1; // 기본값, 필요시 조정
            
            if (Dim == 3)
            {
                // 공간 크기 계산
                float spaceWidth = Math.Abs(SpaceCoords[1][0] - SpaceCoords[0][0]);
                float spaceHeight = Math.Abs(SpaceCoords[1][1] - SpaceCoords[0][1]);
                float spaceDepth = Math.Abs(SpaceCoords[1][2] - SpaceCoords[0][2]);
                
                Debug.Log($"공간 크기: 너비={spaceWidth}, 높이={spaceHeight}, 깊이={spaceDepth}");
                
                // 포인트 수 확인
                int pointsX = (int)spaceWidth + 1;
                int pointsY = (int)spaceHeight + 1;
                int pointsZ = (int)spaceDepth + 1;
                int totalPoints = pointsX * pointsY * pointsZ;
                
                Debug.Log($"생성할 총 좌표 수: {totalPoints}");
                
                // 실제 좌표 생성
                for (int i = (int)SpaceCoords[0][0]; i <= (int)SpaceCoords[1][0]; i += gridDensity)
                for (int j = (int)SpaceCoords[0][1]; j <= (int)SpaceCoords[1][1]; j += gridDensity)
                for (int k = (int)SpaceCoords[0][2]; k <= (int)SpaceCoords[1][2]; k += gridDensity)
                {
                    bool isValid = true;
                    foreach (var obs in ObstacleCoords)
                    {
                        if (obs == null || obs.Length < 2) continue;
                        
                        var lb = obs[0];
                        var rt = obs[1];
                        if (CoordValid(new float[] { i, j, k }, lb, rt))
                        {
                            isValid = false;
                            break;
                        }
                    }
                    if (isValid) PhyVertex.Add(new float[] { i, j, k });
                }
            }
            else
            {
                for (int i = (int)SpaceCoords[0][0]; i <= (int)SpaceCoords[1][0]; i += gridDensity)
                for (int j = (int)SpaceCoords[0][1]; j <= (int)SpaceCoords[1][1]; j += gridDensity)
                {
                    bool isValid = true;
                    foreach (var obs in ObstacleCoords)
                    {
                        if (obs == null || obs.Length < 2) continue;
                        
                        var lb = obs[0];
                        var rt = obs[1];
                        if (CoordValid(new float[] { i, j }, lb, rt))
                        {
                            isValid = false;
                            break;
                        }
                    }
                    if (isValid) PhyVertex.Add(new float[] { i, j });
                }
            }
            
            // PhyVertex가 비어있다면 최소한의 좌표 추가
            if (PhyVertex.Count == 0)
            {
                Debug.LogWarning("PhyVertex가 비어있습니다. 기본 좌표를 추가합니다.");
                
                // 공간 중앙에 기본 좌표 추가
                float[] centerPoint = new float[Dim];
                for (int i = 0; i < Dim; i++)
                {
                    centerPoint[i] = (SpaceCoords[0][i] + SpaceCoords[1][i]) / 2;
                }
                PhyVertex.Add(centerPoint);
                
                // 주요 방향으로 추가 좌표 생성
                foreach (var dir in Directions)
                {
                    float[] newPoint = Functions.TupleOperations(centerPoint, dir.Item1, "+");
                    PhyVertex.Add(newPoint);
                }
            }
            
            // Dictionary 초기화
            OpenSet = new Dictionary<string, float>();
            CloseSet = new Dictionary<string, int>();
            
            foreach (var p in PhyVertex)
            {
                string key = string.Join(",", p);
                OpenSet[key] = 0f;
                CloseSet[key] = 0;
            }
            
            // 로깅 추가
            Debug.Log($"유효 좌표 수: {PhyVertex.Count}");
            
            // 일부 좌표 샘플 출력
            int sampleSize = Math.Min(5, PhyVertex.Count);
            for (int i = 0; i < sampleSize; i++)
            {
                Debug.Log($"샘플 좌표 {i}: {string.Join(",", PhyVertex[i])}");
            }
        }

        public void InitEdgeCost(float edgeCost)
        {
            EdgeCost = new Dictionary<string, float>();
            
            Debug.Log("엣지 비용 초기화 시작");
            int processedEdges = 0;
            int highCostEdges = 0;
            
            foreach (var v in PhyVertex)
            {
                foreach (var dir in Directions)
                {
                    string edgeKey = string.Join(",", v) + ":" + dir.Item2;
                    float cost = edgeCost;
                    
                    // 장애물과의 거리 계산
                    bool isNearObstacle = false;
                    float minDistance = float.MaxValue;
                    
                    foreach (var obs in ObstacleCoords)
                    {
                        float[] closestPoint = ClosestPoint(v, obs[0], obs[1]);
                        float distance = Distance(v, closestPoint);
                        
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                        
                        // 장애물과의 거리가 매우 가까우면 (반경의 3배 이내) 높은 비용 설정
                        if (distance < Radius * 3)
                        {
                            isNearObstacle = true;
                            // 거리가 가까울수록 비용 증가 (최대 10배까지)
                            float distanceFactor = 1 - (distance / (Radius * 3));
                            float additionalCost = edgeCost * 9 * distanceFactor;
                            cost += additionalCost;
                            
                            if (processedEdges % 100 == 0 || highCostEdges < 10)
                            {
                                Debug.LogVerbose($"장애물 근처 엣지: {edgeKey}, 거리: {distance}, 추가 비용: {additionalCost}");
                            }
                            
                            highCostEdges++;
                        }
                    }
                    
                    EdgeCost.Add(edgeKey, cost);
                    processedEdges++;
                }
            }
            
            Debug.Log($"엣지 비용 초기화 완료: 총 {processedEdges}개 엣지, 고비용 엣지 {highCostEdges}개");
        }
        
        // 장애물에서 가장 가까운 점 찾기
        private float[] ClosestPoint(float[] point, float[] min, float[] max)
        {
            float[] closest = new float[point.Length];
            
            for (int i = 0; i < point.Length; i++)
            {
                closest[i] = Mathf.Clamp(point[i], min[i], max[i]);
            }
            
            return closest;
        }
        
        // 두 점 사이의 거리 계산
        private float Distance(float[] p1, float[] p2)
        {
            float sumSquared = 0;
            for (int i = 0; i < p1.Length; i++)
            {
                float diff = p1[i] - p2[i];
                sumSquared += diff * diff;
            }
            return (float)Math.Sqrt(sumSquared);
        }

        public static bool CoordValid(float[] coord, float[] lb, float[] rt)
        {
            for (int i = 0; i < coord.Length; i++)
            {
                if (coord[i] < lb[i] || coord[i] > rt[i]) return false;
            }
            return true;
        }

        public void Reinit()
        {
            foreach (var key in OpenSet.Keys.ToList()) OpenSet[key] = 0f;
            foreach (var key in CloseSet.Keys.ToList()) CloseSet[key] = 0;
        }

        public float BaseCost(Node p)
        {
            return WPath * p.EdgeCost + WBend * p.NCP + WEnergy * p.Energy;
        }
        public float HeuristicCost(float[] pCoord, float[] end)
        {
            return Functions.ManhattanDistance(pCoord, end);
        }
        public float TotalCost(Node p, float[] end)
        {
            return BaseCost(p) + HeuristicCost(p.Coord, end);
        }

        public bool IsInOpenSet(float[] pCoord)
        {
            var key = string.Join(",", pCoord);
            return OpenSet.ContainsKey(key) && OpenSet[key] == 0f;
        }
        public bool IsInCloseSet(float[] pCoord)
        {
            var key = string.Join(",", pCoord);
            return CloseSet.ContainsKey(key) && CloseSet[key] == 1;
        }

        public bool IsFeasibleBendPoint(Node p)
        {
            int pNCP = p.NCP;
            int k = 0;
            var cur = p;
            while (cur.Parent != null && cur.Parent.NCP >= pNCP - 1)
            {
                cur = cur.Parent;
                k++;
            }
            return k >= MinDisBend;
        }

        public bool IsEnoughSpace(float[] pCoord, string direction, float radius, float delta)
        {
            Debug.LogVerbose($"IsEnoughSpace 확인: 좌표 {string.Join(",", pCoord)}, 방향 {direction}, 반경 {radius}");
            
            // 파이프 두께를 고려한 offset 계산 (2배 더 크게 설정)
            var shift = Enumerable.Repeat((float)Math.Ceiling(radius * 2 + delta), Dim).ToArray();
            
            // 현재 이동 방향에서는 shift 적용 안함
            if (direction.Contains("x")) shift[0] = 0;
            else if (direction.Contains("y")) shift[1] = 0;
            else if (Dim == 3 && direction.Contains("z")) shift[2] = 0;
            
            Debug.LogVerbose($"계산된 shift: {string.Join(",", shift)}");
            
            var p1 = Functions.TupleOperations(pCoord, shift, "-");
            var p2 = Functions.TupleOperations(pCoord, shift, "+");
            
            Debug.LogVerbose($"검사 범위: {string.Join(",", p1)} ~ {string.Join(",", p2)}");
            
            var ranges = new List<List<int>>();
            for (int i = 0; i < Dim; i++)
                ranges.Add(Enumerable.Range((int)p1[i], (int)(p2[i] - p1[i] + 1)).ToList());
            
            int pointsCount = 0;
            int missingPointsCount = 0;
            
            foreach (var item in CartesianProduct(ranges))
            {
                var key = string.Join(",", item.Select(x => (float)x));
                pointsCount++;
                
                if (!OpenSet.ContainsKey(key)) 
                {
                    missingPointsCount++;
                    Debug.LogVerbose($"장애물 감지: 좌표 {key}");
                    // 하나라도 장애물이 감지되면 즉시 false 반환
                    if (missingPointsCount > 0) 
                    {
                        Debug.LogVerbose($"장애물 충분히 감지됨: {missingPointsCount}개");
                        return false;
                    }
                }
            }
            
            Debug.LogVerbose($"장애물 검사 완료: 총 {pointsCount}개 포인트 중 {missingPointsCount}개 장애물");
            return missingPointsCount == 0;
        }

        // Cartesian product helper
        public static IEnumerable<List<int>> CartesianProduct(List<List<int>> sequences)
        {
            IEnumerable<List<int>> result = new List<List<int>>() { new List<int>() };
            foreach (var seq in sequences)
            {
                result = result.SelectMany(
                    accseq => seq,
                    (accseq, item) =>
                    {
                        var l = new List<int>(accseq) { item };
                        return l;
                    });
            }
            return result;
        }

        // A* 알고리즘 실행
        public (List<(float[], string)> bendPath, List<(float[], string)> path) Run((float[], string) startInfo, (float[], string) endInfo, float radius, float delta)
        {
            Debug.Log($"A* 경로 탐색 시작: 시작점 {string.Join(",", startInfo.Item1)}, 목표점 {string.Join(",", endInfo.Item1)}");
            Debug.Log($"파라미터: 반경 = {radius}, 델타 = {delta}, WPath = {WPath}, WBend = {WBend}, WEnergy = {WEnergy}");
            
            // PhyVertex 확인
            if (PhyVertex == null || PhyVertex.Count == 0)
            {
                Debug.LogError("PhyVertex가 비어있습니다. 경로를 찾을 수 없습니다.");
                return (null, null);
            }
            
            // 시작점과 끝점이 유효한지 확인
            if (!OpenSet.ContainsKey(string.Join(",", startInfo.Item1)))
            {
                Debug.LogWarning("시작점이 유효하지 않습니다. 가장 가까운 유효한 점을 찾습니다.");
                startInfo = (FindNearestValidPoint(startInfo.Item1), startInfo.Item2);
            }
            
            if (!OpenSet.ContainsKey(string.Join(",", endInfo.Item1)))
            {
                Debug.LogWarning("끝점이 유효하지 않습니다. 가장 가까운 유효한 점을 찾습니다.");
                endInfo = (FindNearestValidPoint(endInfo.Item1), endInfo.Item2);
            }
            
            Radius = radius;
            Delta = delta;
            Start = new Node(startInfo, null, 0f);
            var pq = new SortedSet<(float cost, Node node)>(Comparer<(float, Node)>.Create((a, b) =>
            {
                int cmp = a.Item1.CompareTo(b.Item1);
                if (cmp == 0) cmp = a.Item2.GetHashCode().CompareTo(b.Item2.GetHashCode());
                return cmp;
            }));
            pq.Add((0f, Start));
            Reinit();
            
            // 알고리즘 진행 상황 로깅
            int iterationCount = 0;
            int maxIterationsToLog = 100; // 로그 양 제한
            
            while (pq.Count > 0)
            {
                iterationCount++;
                var (curCost, curNode) = pq.Min;
                
                if (iterationCount % 50 == 0 || iterationCount < maxIterationsToLog) {
                    Debug.Log($"반복 {iterationCount}: 현재 좌표 {string.Join(",", curNode.Coord)}, 비용 {curCost}");
                }
                
                pq.Remove(pq.Min);
                if (curNode.Coord.SequenceEqual(endInfo.Item1))
                {
                    Debug.Log($"경로 찾음! 반복 횟수: {iterationCount}, 최종 비용: {curCost}");
                    var (bend, path) = BuildPath(curNode);
                    Debug.Log($"경로 포인트 수: {path.Count}, 굽힘 포인트 수: {bend.Count}");
                    return (bend, path);
                }
                CloseSet[string.Join(",", curNode.Coord)] = 1;
                OpenSet[string.Join(",", curNode.Coord)] = 0f;
                
                int neighborsCount = 0;
                int validNeighborsCount = 0;
                
                foreach (var dir in Directions)
                {
                    neighborsCount++;
                    var nextCoord = Functions.TupleOperations(curNode.Coord, dir.Item1, "+");
                    
                    if (nextCoord == null)
                    {
                        Debug.LogError($"TupleOperations 결과가 null입니다. curNode.Coord = {string.Join(",", curNode.Coord)}, dir.Item1 = {string.Join(",", dir.Item1)}");
                        continue;
                    }
                    
                    var key = string.Join(",", nextCoord);
                    Debug.LogVerbose($"이웃 탐색: {string.Join(",", curNode.Coord)} -> {key} (방향: {dir.Item2})");
                    
                    if (!OpenSet.ContainsKey(key)) 
                    {
                        Debug.LogVerbose($"유효하지 않은 이웃: {key}");
                        continue;
                    }
                    
                    if (IsInCloseSet(nextCoord))
                    {
                        Debug.LogVerbose($"이미 처리된 이웃: {key}");
                        continue;
                    }
                    
                    validNeighborsCount++;
                    
                    // 에지 비용 검색 전 키 유효성 확인
                    string edgeKey = key + ":" + dir.Item2;
                    if (!EdgeCost.ContainsKey(edgeKey))
                    {
                        Debug.LogWarning($"에지 비용 키가 없음: {edgeKey}, 기본값 1.0 사용");
                        EdgeCost[edgeKey] = 1.0f;
                    }
                    
                    float edgeCost = EdgeCost[edgeKey];
                    var nextNode = new Node((nextCoord, dir.Item2), curNode, edgeCost);
                    
                    if (nextNode.NCP == curNode.NCP + 1 && !IsFeasibleBendPoint(nextNode)) 
                    {
                        Debug.LogVerbose($"굽힘점 유효성 검사 실패: {key}");
                        continue;
                    }
                    
                    float totalCost = TotalCost(nextNode, endInfo.Item1);
                    
                    bool hasEnoughSpace = IsEnoughSpace(nextNode.Coord, nextNode.Direction, Radius, Delta);
                    if (!hasEnoughSpace)
                    {
                        // 장애물 비용 대폭 증가 (10배로)
                        float obstaclePenalty = 10f * WPath;
                        Debug.LogVerbose($"장애물 감지: 좌표 {string.Join(",", nextNode.Coord)}, 기존 비용 {totalCost}에 패널티 {obstaclePenalty} 추가");
                        totalCost += obstaclePenalty;
                    }
                    
                    if (OpenSet[key] == 0f)
                    {
                        OpenSet[key] = totalCost;
                        pq.Add((totalCost, nextNode));
                        Debug.LogVerbose($"새 노드 추가: {key}, 비용 {totalCost}");
                    }
                    else if (totalCost < OpenSet[key])
                    {
                        OpenSet[key] = totalCost;
                        pq.Add((totalCost, nextNode));
                        Debug.LogVerbose($"노드 비용 업데이트: {key}, 새 비용 {totalCost}");
                    }
                }
                
                if (iterationCount % 50 == 0 || iterationCount < maxIterationsToLog) {
                    Debug.Log($"반복 {iterationCount}: 총 이웃 {neighborsCount}개, 유효한 이웃 {validNeighborsCount}개");
                }
                
                // 너무 많은 반복을 수행하면 조기 종료 (무한 루프 방지)
                if (iterationCount > 10000)
                {
                    Debug.LogWarning("최대 반복 횟수 초과, 경로 탐색 중단");
                    break;
                }
            }
            
            Debug.Log($"경로를 찾지 못함. 총 반복 횟수: {iterationCount}");
            return (null, null);
        }
        
        // 가장 가까운 유효한 좌표 찾기
        private float[] FindNearestValidPoint(float[] point)
        {
            if (PhyVertex == null || PhyVertex.Count == 0)
            {
                Debug.LogError("PhyVertex가 비어있어 가장 가까운 점을 찾을 수 없습니다.");
                return point;
            }
            
            float minDistance = float.MaxValue;
            float[] closest = null;
            
            foreach (var vertex in PhyVertex)
            {
                float distance = Distance(point, vertex);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closest = vertex;
                }
            }
            
            if (closest != null)
            {
                Debug.Log($"가장 가까운 유효 좌표 발견: {string.Join(",", closest)}, 거리: {minDistance}");
                return closest;
            }
            
            Debug.LogWarning("가장 가까운 점을 찾지 못함, 원본 좌표 반환");
            return point;
        }

        // 경로 재구성
        public (List<(float[], string)> bendPath, List<(float[], string)> path) BuildPath(Node p)
        {
            var bendPath = new List<(float[], string)>();
            var path = new List<(float[], string)>();
            bendPath.Insert(0, p.CoordInfo);
            while (true)
            {
                if (p.Parent == null || p.Coord.SequenceEqual(Start.Coord)) break;
                path.Insert(0, p.CoordInfo);
                if (p.NCP == p.Parent.NCP + 1)
                    bendPath.Insert(0, p.Parent.CoordInfo);
                p = p.Parent;
            }
            bendPath.Insert(0, Start.CoordInfo);
            if (bendPath.Count > 1 && bendPath[0].Item1.SequenceEqual(bendPath[1].Item1))
                bendPath.RemoveAt(0);
            path.Insert(0, Start.CoordInfo);
            return (bendPath, path);
        }
        private static class Debug
        {
            public static bool VerboseLogging = true; // 모든 로그를 보려면 true로 설정
            
            public static void Log(string message)
            {
                #if UNITY_EDITOR
                UnityEngine.Debug.Log($"[AStar] {message}");
                #else
                // 에디터 외에서도 로깅하기 위해 조건 제거
                UnityEngine.Debug.Log($"[AStar] {message}");
                #endif
            }
            
            public static void LogWarning(string message)
            {
                #if UNITY_EDITOR
                UnityEngine.Debug.LogWarning($"[AStar] {message}");
                #else
                UnityEngine.Debug.LogWarning($"[AStar] {message}");
                #endif
            }
            
            public static void LogError(string message)
            {
                #if UNITY_EDITOR
                UnityEngine.Debug.LogError($"[AStar] {message}");
                #else
                UnityEngine.Debug.LogError($"[AStar] {message}");
                #endif
            }
            
            public static void LogVerbose(string message)
            {
                if (VerboseLogging)
                {
                    Log(message);
                }
            }
        }
    }
    
} 
