using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Utils;

namespace Model
{
    public class AStar
    {
        public float GridSize = 1;
        public float[][] SpaceCoords;
        public List<float[][]> ObstacleCoords;
        public int Dim = 3;
        public List<Vector3> PhyVertex;
        public Dictionary<string, float> EdgeCost;
        public List<(Vector3, string)> Directions;
        public float WPath, WBend, WEnergy;
        public int MinDisBend;
        public Dictionary<string, float> OpenSet;
        public Dictionary<string, int> CloseSet;
        public Node Start;
        public float Radius, Delta;
        private int maxit = 10000;
        public bool UseDiagonals = false; // 대각선 사용 여부를 제어하는 옵션
        protected int CurrentPipeIndex = -1; // 현재 경로 탐색 중인 파이프 인덱스
        private SortedSet<(float cost, Node node)> priorityQueue;

        // 커스텀 비교자 클래스
        private class PriorityQueueComparer : IComparer<(float cost, Node node)>
        {
            public int Compare((float cost, Node node) x, (float cost, Node node) y)
            {
                // 먼저 비용으로 비교
                int costComparison = x.cost.CompareTo(y.cost);
                if (costComparison != 0)
                    return costComparison;
                
                // 비용이 같으면 노드의 좌표로 비교 (고유성 보장)
                int xComparison = x.node.Coord.x.CompareTo(y.node.Coord.x);
                if (xComparison != 0)
                    return xComparison;
                
                int yComparison = x.node.Coord.y.CompareTo(y.node.Coord.y);
                if (yComparison != 0)
                    return yComparison;
                
                return x.node.Coord.z.CompareTo(y.node.Coord.z);
            }
        }

        // float[] 배열을 Vector3로 변환하는 헬퍼 메서드
        private Vector3 FloatArrayToVector3(float[] array)
        {
            if (array == null || array.Length < 3)
            {
                Debug.LogError("유효하지 않은 배열입니다. 기본 Vector3.zero 반환");
                return Vector3.zero;
            }
            // X, Y, Z 순서를 유지하되, 2D인 경우 Z 좌표 사용
            return new Vector3(array[0], array.Length > 2 ? array[1] : 0, array.Length > 2 ? array[2] : array[1]);
        }

        // Vector3를 float[] 배열로 변환하는 헬퍼 메서드
        private float[] Vector3ToFloatArray(Vector3 vector)
        {
            return new float[] { vector.x, vector.y, vector.z };
        }

        // 두 Vector3 비교 메서드 (SequenceEqual 대신 사용)
        public bool VectorsEqual(Vector3 v1, Vector3 v2)
        {
            const float tolerance = 0.01f;
            return Mathf.Abs(v1.x - v2.x) < tolerance && Mathf.Abs(v1.y - v2.y) < tolerance && Mathf.Abs(v1.z - v2.z) < tolerance;
        }


        public bool coord_valid(float[] coord, float[] coord_lb, float[] coord_rt)
        {
            if (coord == null || coord_lb == null || coord_rt == null) return false;
            
            for (int i = 0; i < coord.Length && i < coord_lb.Length && i < coord_rt.Length; i++)
            {
                if (coord[i] < coord_lb[i] || coord[i] > coord_rt[i])
                {
                    return false;
                }
            }
            return true;
        }

        public static float get_max_distance(Vector3 p1, Vector3 p2)
        {
            float res = 0;
            res = Math.Max(res, Math.Abs(p1.x - p2.x));
            res = Math.Max(res, Math.Abs(p1.y - p2.y));
            res = Math.Max(res, Math.Abs(p1.z - p2.z));
            return res;
        }

        public AStar(int _maxit, float _gridSize, float[][] spaceCoords, List<float[][]> obstacleCoords, float wPath, float wBend, float wEnergy, int minDisBend, bool useDiagonals = false)
        {
            GridSize = _gridSize;
            maxit = _maxit;
            SpaceCoords = spaceCoords;
            ObstacleCoords = obstacleCoords ?? new List<float[][]>();
            WPath = wPath;
            WBend = wBend;
            WEnergy = wEnergy;
            MinDisBend = minDisBend;
            UseDiagonals = false;
            Dim = 3;
            
            // 기본 공간 좌표 유효성 확인
            ValidateSpaceCoordinates();
            
            // 초기화 순서 변경
            set_directions();  // Directions 초기화를 먼저
            InitProperty();    // PhyVertex 초기화
            init_edge_cost(1f); // EdgeCost 초기화는 마지막에
        }
        
        // 기존 생성자는 그대로 유지하고 호환성을 위해 UseDiagonals를 true로 설정
        public AStar(int _maxit, float _gridSize, float[][] spaceCoords, List<float[][]> obstacleCoords, float wPath, float wBend, float wEnergy, int minDisBend)
            : this(_maxit, _gridSize, spaceCoords, obstacleCoords, wPath, wBend, wEnergy, minDisBend, true)
        {
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

        public void InitProperty()
        {
            PhyVertex = new List<Vector3>();
            
            if (SpaceCoords != null && SpaceCoords.Length == 2)
            {
                Vector3 min = FloatArrayToVector3(SpaceCoords[0]);
                Vector3 max = FloatArrayToVector3(SpaceCoords[1]);
<<<<<<< HEAD
                max.z += 10.0f;
=======
                
>>>>>>> 3bda48018ec743a20e40231ff9df48323e012642
                // 공간 크기 계산
                Vector3 spaceSize = max - min;
                float maxDimension = Mathf.Max(spaceSize.x, spaceSize.y, spaceSize.z);
                
                // 공간 크기에 따라 동적으로 stepSize 조정
                float stepSize = GridSize;
                
                // 예상 점 수 계산
                int expectedPointsX = Mathf.CeilToInt(spaceSize.x / stepSize) + 1;
                int expectedPointsY = Mathf.CeilToInt(spaceSize.y / stepSize) + 1;
                int expectedPointsZ = Mathf.CeilToInt(spaceSize.z / stepSize) + 1;
                long expectedTotalPoints = (long)expectedPointsX * expectedPointsY * expectedPointsZ;
                
<<<<<<< HEAD
=======
                // 안전장치: 최대 100,000개 점으로 제한
                const long maxAllowedPoints = 1000000;
                if (expectedTotalPoints > maxAllowedPoints)
                {
                    // stepSize를 더 크게 조정
                    float scaleFactor = Mathf.Pow((float)expectedTotalPoints / maxAllowedPoints, 1f/3f);
                    stepSize *= scaleFactor;
                    
                    expectedPointsX = Mathf.CeilToInt(spaceSize.x / stepSize) + 1;
                    expectedPointsY = Mathf.CeilToInt(spaceSize.y / stepSize) + 1;
                    expectedPointsZ = Mathf.CeilToInt(spaceSize.z / stepSize) + 1;
                    expectedTotalPoints = (long)expectedPointsX * expectedPointsY * expectedPointsZ;
                }
>>>>>>> 3bda48018ec743a20e40231ff9df48323e012642
                
                Debug.Log($"공간 크기: {spaceSize}, stepSize: {stepSize:F2}, 예상 점 수: {expectedTotalPoints}");
                
                // 메모리 예약 (성능 최적화)
<<<<<<< HEAD
                int capacityEstimate = (int)expectedTotalPoints;
=======
                int capacityEstimate = Mathf.Min((int)expectedTotalPoints, (int)maxAllowedPoints);
>>>>>>> 3bda48018ec743a20e40231ff9df48323e012642
                PhyVertex.Capacity = capacityEstimate;
                
                int actualPointCount = 0;
                
                // X, Y, Z 축 방향으로 격자점 생성
<<<<<<< HEAD
                for (float x = min.x; x <= max.x ; x += stepSize)
                {
                    for (float y = min.y; y <= max.y ; y += stepSize)
                    {
                        for (float z = min.z; z <= max.z; z += stepSize)
=======
                for (float x = min.x; x <= max.x && actualPointCount < maxAllowedPoints; x += stepSize)
                {
                    for (float y = min.y; y <= max.y && actualPointCount < maxAllowedPoints; y += stepSize)
                    {
                        for (float z = min.z; z <= max.z && actualPointCount < maxAllowedPoints; z += stepSize)
>>>>>>> 3bda48018ec743a20e40231ff9df48323e012642
                        {
                            Vector3 point = new Vector3(x, y, z);
                            
                            // 장애물과 겹치지 않는지 확인
                            bool isValid = true;
                            if (ObstacleCoords != null)
                            {
                                foreach (var obs in ObstacleCoords)
                                {
                                        if (obs == null || obs.Length < 2) continue;
                                    
                                        float[] pointArray = Vector3ToFloatArray(point);
                                        // CoordValid가 false를 반환하면 장애물 내부에 있음
                                        if (!CoordValid(pointArray, obs[0], obs[1]))
                                            {
                                                isValid = false;
                                                Debug.Log($"장애물 내부에 있음: {pointArray[0]}, {pointArray[1]}, {pointArray[2]}");
                                                break;
                                            }
                                    }
                            }
                                        
                            if (isValid)
                            {
                                PhyVertex.Add(point);
                                actualPointCount++;
                            }
                        }
                    }
                }
            }
            
            Debug.Log($"PhyVertex 생성 완료: {PhyVertex.Count}개의 점");
            
        }

        public void reinit(){
            foreach (var key in OpenSet.Keys.ToList()) OpenSet[key] = 0f;
            foreach (var key in CloseSet.Keys.ToList()) CloseSet[key] = 0;
            priorityQueue?.Clear();
        }

        public void init_edge_cost(float edge_cost = 1.0f){
            EdgeCost = new Dictionary<string, float>();
            foreach (var vertex in PhyVertex)
            {
                foreach (var direction in Directions)
                {
                    string edgeKey = $"{vertex.x},{vertex.y},{vertex.z}:{direction.Item2}";
                    EdgeCost[edgeKey] = edge_cost;
                }
            }
        }
        
        public void set_directions(){
            if (Dim == 3){
                Directions = new List<(Vector3, string)>()
                {
                    (new Vector3(0, 1, 0), "+y"),
                    (new Vector3(0, -1, 0), "-y"),
                    (new Vector3(1, 0, 0), "+x"),
                    (new Vector3(-1, 0, 0), "-x"),
                    (new Vector3(0, 0, 1), "+z"),
                    (new Vector3(0, 0, -1), "-z")
                };
            }
        }
        public float base_cost(Node p)
        {
            // 기본 비용 계산 개선
            float pathCost = WPath * p.EdgeCost;
            float bendCost = WBend * p.NCP;
            float energyCost = WEnergy * p.Energy;
            
            // 연속된 직선 구간에 대한 보너스
            float straightBonus = 0f;
            if (p.Parent != null && p.Direction == p.Parent.Direction)
            {
                straightBonus = -0.5f; // 연속된 직선 구간에 대한 보너스
            }
            
            // 급격한 방향 전환에 대한 페널티
            float directionChangePenalty = 0f;
            if (p.Parent != null && p.Parent.Parent != null)
            {
                Vector3 prevDir = (p.Parent.Coord - p.Parent.Parent.Coord).normalized;
                Vector3 currDir = (p.Coord - p.Parent.Coord).normalized;
                float dotProduct = Vector3.Dot(prevDir, currDir);
                if (dotProduct < 0.7f) // 약 45도 이상의 각도 변화
                {
                    directionChangePenalty = 2.0f;
                }
            }
            
            return pathCost + bendCost + energyCost + straightBonus + directionChangePenalty;
        }
        public float heuristic_cost(Vector3 p_coord, Vector3 end)
        {
            // 휴리스틱 비용 계산 개선
            float manhattan = Mathf.Abs(p_coord.x - end.x) + Mathf.Abs(p_coord.y - end.y) + Mathf.Abs(p_coord.z - end.z);
            float euclidean = Vector3.Distance(p_coord, end);
            
            // 맨해튼 거리와 유클리드 거리의 가중 평균
            return 0.7f * manhattan + 0.3f * euclidean;
        }
        public float total_cost(Node p, Vector3 end)
        {
            float baseCost = base_cost(p);
            float heuristicCost = heuristic_cost(p.Coord, end);
            
            // 목표 지점에 가까워질수록 휴리스틱 비용의 가중치를 줄임
            float distanceToGoal = Vector3.Distance(p.Coord, end);
            float heuristicWeight = Mathf.Clamp01(distanceToGoal / 20f); // 20 유닛 이내에서는 휴리스틱 비용 감소
            
            return baseCost + heuristicCost * heuristicWeight;
        }
        public bool is_in_open_set(Vector3 p_coord){
            var key = $"{p_coord.x},{p_coord.y},{p_coord.z}";
            return OpenSet.ContainsKey(key) && OpenSet[key] == 0f;
        }
        public bool is_in_close_set(Vector3 p_coord){
            var key = $"{p_coord.x},{p_coord.y},{p_coord.z}";
            return CloseSet.ContainsKey(key) && CloseSet[key] == 1;
        }
        public bool is_feasible_bend_point(Node p){
            int pNCP = p.NCP;
            int k = 0;
            var cur = p;
            while (cur.Parent != null && cur.Parent.NCP >= pNCP - 1){
                cur = cur.Parent;
                k++;
            }
            return k >= MinDisBend;
        }

        
        
        // 파이프 정보를 저장하는 클래스
        protected class PipeInfo
        {
            public List<Vector3> Points;
            public float Radius;
            public int PipeIndex = -1;  // 파이프 인덱스 (순서 처리용)
            
            public PipeInfo(List<Vector3> points, float radius, int pipeIndex = -1)
            {
                Points = points;
                Radius = radius;
                PipeIndex = pipeIndex;
            }
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
            if (coord == null || lb == null || rt == null) return false;
            
            // 좌표가 장애물 영역 내에 있는지 확인
            for (int i = 0; i < Math.Min(coord.Length, Math.Min(lb.Length, rt.Length)); i++)
            {
                if (coord[i] < lb[i] || coord[i] > rt[i])
                {
                    return true; // 장애물 영역 밖에 있으면 유효
                }
            }
            
            return false; // 장애물 영역 안에 있으면 유효하지 않음
        }

        protected bool IsEnoughSpace(float[] p_coord, string direction, float radius, float delta)
        {
            // 방향에 따른 검사 범위 설정
            float[] shift = new float[Dim];
            for (int i = 0; i < Dim; i++)
            {
                // 여유 공간을 더 크게 설정하고 방향에 따라 조정
                float baseShift = radius * 2f + delta; // 기본 여유 공간 증가
                if (direction.Contains("x"))
                {
                    shift[0] = 0;
                    shift[1] = baseShift;
                    shift[2] = baseShift;
                }
                else if (direction.Contains("y"))
                {
                    shift[0] = baseShift;
                    shift[1] = 0;
                    shift[2] = baseShift;
                }
                else if (direction.Contains("z"))
                {
                    shift[0] = baseShift;
                    shift[1] = baseShift;
                    shift[2] = 0;
                }
                else
                {
                    shift[i] = baseShift;
                }
            }

            // 검사할 영역의 경계 계산
            float[] minBounds = new float[Dim];
            float[] maxBounds = new float[Dim];
            
            for (int i = 0; i < Dim; i++)
            {
                minBounds[i] = p_coord[i] - shift[i];
                maxBounds[i] = p_coord[i] + shift[i];
            }

            // 공간 경계 검사
            for (int i = 0; i < Dim; i++)
            {
                if (minBounds[i] < SpaceCoords[0][i] || maxBounds[i] > SpaceCoords[1][i])
                {
                    return false;
                }
            }

            // 장애물과의 충돌 검사
            if (ObstacleCoords != null)
            {
                foreach (var obs in ObstacleCoords)
                {
                    if (obs == null || obs.Length < 2) continue;

                    // 장애물과 검사 영역이 겹치는지 확인
                    bool hasOverlap = true;
                    for (int i = 0; i < Dim; i++)
                    {
                        // 방향에 따른 안전 마진 조정
                        float safetyMargin = radius * 1.0f;
                        if (direction.Contains("x") && i == 0) safetyMargin = 0;
                        if (direction.Contains("y") && i == 1) safetyMargin = 0;
                        if (direction.Contains("z") && i == 2) safetyMargin = 0;

                        if (maxBounds[i] + safetyMargin < obs[0][i] || minBounds[i] - safetyMargin > obs[1][i])
                        {
                            hasOverlap = false;
                            break;
                        }
                    }

                    if (hasOverlap)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public void process_point(Node curr_p, Vector3 end_info)
        {
            Vector3 curr_p_coord = curr_p.Coord;
            
            // 굽힘점 검사
            if (curr_p.Parent != null && curr_p.NCP == curr_p.Parent.NCP + 1)
            {
                if (!is_feasible_bend_point(curr_p))
                {
                    return;
                }
            }

            float p_cost = total_cost(curr_p, end_info);
            
            // 충분한 공간이 없는 경우 비용 증가
            if (!IsEnoughSpace(Vector3ToFloatArray(curr_p.Coord), curr_p.Direction, Radius, Delta))
            {
                // 방향에 따른 페널티 조정
                float directionPenalty = 1.0f;
                if (curr_p.Parent != null)
                {
                    Vector3 prevDir = (curr_p.Parent.Coord - curr_p.Coord).normalized;
                    Vector3 currDir = Vector3.zero;
                    if (curr_p.Direction.Contains("x")) currDir = new Vector3(1, 0, 0);
                    else if (curr_p.Direction.Contains("y")) currDir = new Vector3(0, 1, 0);
                    else if (curr_p.Direction.Contains("z")) currDir = new Vector3(0, 0, 1);
                    
                    float dotProduct = Vector3.Dot(prevDir, currDir);
                    if (dotProduct < 0.5f) // 60도 이상의 각도 변화
                    {
                        directionPenalty = 2.0f;
                    }
                }
                
                p_cost *= (10.0f * directionPenalty);
                Debug.Log($"공간 부족으로 인한 비용 증가: {p_cost}");
            }
            
            string coordKey = $"{curr_p_coord.x},{curr_p_coord.y},{curr_p_coord.z}";
            
            // OpenSet에 없거나 더 낮은 비용이면 추가/업데이트
            if (!OpenSet.ContainsKey(coordKey))
            {
                OpenSet[coordKey] = p_cost;
                priorityQueue.Add((p_cost, curr_p));
            }
            else if (p_cost < OpenSet[coordKey])
            {
                OpenSet[coordKey] = p_cost;
                
                var toRemove = priorityQueue.Where(item => VectorsEqual(item.node.Coord, curr_p_coord)).ToList();
                foreach (var item in toRemove)
                {
                    priorityQueue.Remove(item);
                }
                
                priorityQueue.Add((p_cost, curr_p));
            }
        }
        
        public (List<(Vector3, string)> bendPath, List<(Vector3, string)> path) build_path(Node p)
        {
            List<(Vector3, string)> path = new List<(Vector3, string)>();
            var cur = p;
            while (cur.Parent != null)
            {
                path.Add((cur.Coord, cur.Direction));
                cur = cur.Parent;
            }
            path.Add((cur.Coord, cur.Direction));
            path.Reverse();
            
            // bendPath 생성 - 방향이 바뀌는 지점들과 중요한 지점들 추출
            List<(Vector3, string)> bend_path = new List<(Vector3, string)>();
            
            if (path.Count <= 1)
            {
                bend_path.AddRange(path);
                return (bend_path, path);
            }
            
            if (path.Count == 2)
            {
                bend_path.Add(path[0]);
                bend_path.Add(path[1]);
                return (bend_path, path);
            }
            
            // 첫 번째 점은 항상 추가 (시작점)
            bend_path.Add(path[0]);
            
            // 중간 점들에서 방향 변화와 각도 변화 확인
            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector3 prevPoint = path[i - 1].Item1;
                Vector3 currPoint = path[i].Item1;
                Vector3 nextPoint = path[i + 1].Item1;
                
                string prevDirection = path[i - 1].Item2;
                string currDirection = path[i].Item2;
                string nextDirection = path[i + 1].Item2;
                
                bool shouldAddBendPoint = false;
                
                // 1. 방향 문자열이 바뀌는 지점
                if (prevDirection != currDirection || currDirection != nextDirection)
                {
                    shouldAddBendPoint = true;
                }
                
                // 2. 실제 방향 벡터의 각도 변화 확인
                Vector3 directionIn = (currPoint - prevPoint).normalized;
                Vector3 directionOut = (nextPoint - currPoint).normalized;
                
                // 방향 벡터의 내적으로 각도 변화 감지
                float dotProduct = Vector3.Dot(directionIn, directionOut);
                float angleThreshold = 0.85f; // 약 30도 이상의 각도 변화
                
                if (dotProduct < angleThreshold)
                {
                    shouldAddBendPoint = true;
                }
                
                // 3. 마지막 bend point로부터 일정 거리 이상 떨어진 경우
                if (bend_path.Count > 0)
                {
                    Vector3 lastBendPoint = bend_path[bend_path.Count - 1].Item1;
                    float distanceFromLastBend = Vector3.Distance(lastBendPoint, currPoint);
                    
                    // 긴 직선 구간에서는 중간 지점 추가 (거리 기반)
                    if (distanceFromLastBend >= 6.0f) // 6 단위 이상 떨어지면 중간점 추가
                    {
                        shouldAddBendPoint = true;
                    }
                }
                
                if (shouldAddBendPoint)
                {
                    bend_path.Add(path[i]);
                }
            }
            
            // 마지막 점은 항상 추가 (끝점)
            bend_path.Add(path[path.Count - 1]);
            
            // 최소 bend point 보장 (시작점과 끝점만 있으면 중간점 하나 추가)
            if (bend_path.Count == 2 && path.Count > 2)
            {
                int midIndex = path.Count / 2;
                bend_path.Insert(1, path[midIndex]);
            }
            
            return (bend_path, path);
        }
        
        // A* 알고리즘 실행
        public (List<(Vector3, string)> bendPath, List<(Vector3, string)> path) Run((Vector3, string) startInfo, (Vector3, string) endInfo, float radius, float delta)
        {
            Radius = radius;
            Delta = delta;
            
            // 시작점과 끝점을 PhyVertex에 추가 (없으면)
            if (!PhyVertex.Any(v => VectorsEqual(v, startInfo.Item1)))
            {
                PhyVertex.Add(startInfo.Item1);
            }
            if (!PhyVertex.Any(v => VectorsEqual(v, endInfo.Item1)))
            {
                PhyVertex.Add(endInfo.Item1);
            }
            
            // 초기화
            OpenSet = new Dictionary<string, float>();
            CloseSet = new Dictionary<string, int>();
            priorityQueue = new SortedSet<(float cost, Node node)>(new PriorityQueueComparer());
            
            Start = new Node(startInfo, edgeCost: 0.0f);
            string startKey = $"{startInfo.Item1.x},{startInfo.Item1.y},{startInfo.Item1.z}";
            OpenSet[startKey] = 0.0f;
            priorityQueue.Add((0, Start));
            
            int maxit = 5000000;
            int currentIteration = 0;
            float bestCost = float.MaxValue;
            Node bestNode = null;
            
            while (priorityQueue.Count > 0 && currentIteration < maxit)
            {
                currentIteration++;
                
                var current = priorityQueue.Min;
                priorityQueue.Remove(current);
                Node curr_p = current.node;
                
                // 목표 지점에 도달했는지 확인 (허용 오차 포함)
                float distanceToGoal = Vector3.Distance(curr_p.Coord, endInfo.Item1);
                if (distanceToGoal < 1.0f)
                {
                    var (bend_path, path) = build_path(curr_p);
                    return (bend_path, path);
                }
                
                // 현재까지 찾은 최적의 경로 업데이트
                float currentCost = total_cost(curr_p, endInfo.Item1);
                if (currentCost < bestCost)
                {
                    bestCost = currentCost;
                    bestNode = curr_p;
                }
                
                string currKey = $"{curr_p.Coord.x},{curr_p.Coord.y},{curr_p.Coord.z}";
                CloseSet[currKey] = 1;
                
                foreach (var direction in Directions)
                {
                    Vector3 nextCoord = curr_p.Coord + direction.Item1;
                    string nextKey = $"{nextCoord.x},{nextCoord.y},{nextCoord.z}";
                    
                    // 경계 검사
                    if (!coord_valid(Vector3ToFloatArray(nextCoord), SpaceCoords[0], SpaceCoords[1]))
                    {
                        continue;
                    }
                    
                    // 이미 방문한 노드는 건너뜀
                    if (CloseSet.ContainsKey(nextKey))
                    {
                        continue;
                    }
                    
                    string edgeKey = $"{curr_p.Coord.x},{curr_p.Coord.y},{curr_p.Coord.z}:{direction.Item2}";
                    float edge_cost = EdgeCost.ContainsKey(edgeKey) ? EdgeCost[edgeKey] : 1.0f;
                    
                    Node next_p = new Node((nextCoord, direction.Item2), parent: curr_p, edgeCost: edge_cost);
                    process_point(next_p, endInfo.Item1);
                }
            }
            
            // 최적의 경로를 찾지 못했지만, 가장 좋은 경로가 있다면 그것을 반환
            if (bestNode != null)
            {
                var (bend_path, path) = build_path(bestNode);
                return (bend_path, path);
            }
            
            // 여기까지 왔다면 경로를 찾지 못한 것이므로 빈 리스트 반환
            return (new List<(Vector3, string)>(), new List<(Vector3, string)>());
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
