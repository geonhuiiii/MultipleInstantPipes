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
        public bool UseDiagonals = false; // 대각선 사용 여부를 제어하는 옵션

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
        private bool VectorsEqual(Vector3 v1, Vector3 v2)
        {
            return v1 == v2;
        }

        public AStar(float[][] spaceCoords, List<float[][]> obstacleCoords, float wPath, float wBend, float wEnergy, int minDisBend, bool useDiagonals = true)
        {
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
            
            SetDirections();
            InitProperty();
            InitEdgeCost(1f);
        }
        
        // 기존 생성자는 그대로 유지하고 호환성을 위해 UseDiagonals를 true로 설정
        public AStar(float[][] spaceCoords, List<float[][]> obstacleCoords, float wPath, float wBend, float wEnergy, int minDisBend)
            : this(spaceCoords, obstacleCoords, wPath, wBend, wEnergy, minDisBend, true)
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

        public void SetDirections()
        {
            Directions = new List<(Vector3, string)>();
            if (Dim == 3)
            {
                // 기본 6방향 (상하좌우앞뒤)
                Directions.Add((new Vector3(0, 1, 0), "+y"));  // 상
                Directions.Add((new Vector3(0, -1, 0), "-y")); // 하
                Directions.Add((new Vector3(1, 0, 0), "+x"));  // 오른쪽
                Directions.Add((new Vector3(-1, 0, 0), "-x")); // 왼쪽
                Directions.Add((new Vector3(0, 0, 1), "+z"));  // 앞
                Directions.Add((new Vector3(0, 0, -1), "-z")); // 뒤
                
                // 대각선 방향 추가 (UseDiagonals가 true일 경우)
                if (UseDiagonals)
                {
                    // X-Y 평면 대각선 (4개)
                    Directions.Add((new Vector3(1, 1, 0), "+x+y"));   // 우상
                    Directions.Add((new Vector3(-1, 1, 0), "-x+y"));  // 좌상
                    Directions.Add((new Vector3(1, -1, 0), "+x-y"));  // 우하
                    Directions.Add((new Vector3(-1, -1, 0), "-x-y")); // 좌하
                    
                    // Y-Z 평면 대각선 (4개)
                    Directions.Add((new Vector3(0, 1, 1), "+y+z"));   // 상앞
                    Directions.Add((new Vector3(0, 1, -1), "+y-z"));  // 상뒤
                    Directions.Add((new Vector3(0, -1, 1), "-y+z"));  // 하앞
                    Directions.Add((new Vector3(0, -1, -1), "-y-z")); // 하뒤
                    
                    // X-Z 평면 대각선 (4개)
                    Directions.Add((new Vector3(1, 0, 1), "+x+z"));   // 우앞
                    Directions.Add((new Vector3(-1, 0, 1), "-x+z"));  // 좌앞
                    Directions.Add((new Vector3(1, 0, -1), "+x-z"));  // 우뒤
                    Directions.Add((new Vector3(-1, 0, -1), "-x-z")); // 좌뒤
                    
                    // 완전 3D 대각선 (8개)
                    Directions.Add((new Vector3(1, 1, 1), "+x+y+z"));     // 우상앞
                    Directions.Add((new Vector3(-1, 1, 1), "-x+y+z"));    // 좌상앞
                    Directions.Add((new Vector3(1, -1, 1), "+x-y+z"));    // 우하앞
                    Directions.Add((new Vector3(-1, -1, 1), "-x-y+z"));   // 좌하앞
                    Directions.Add((new Vector3(1, 1, -1), "+x+y-z"));    // 우상뒤
                    Directions.Add((new Vector3(-1, 1, -1), "-x+y-z"));   // 좌상뒤
                    Directions.Add((new Vector3(1, -1, -1), "+x-y-z"));   // 우하뒤
                    Directions.Add((new Vector3(-1, -1, -1), "-x-y-z"));  // 좌하뒤
                }
            }
            else
            {
                // 2D일 경우 기본 4방향 (X-Z 평면)
                Directions.Add((new Vector3(0, 0, 1), "+z"));
                Directions.Add((new Vector3(0, 0, -1), "-z"));
                Directions.Add((new Vector3(1, 0, 0), "+x"));
                Directions.Add((new Vector3(-1, 0, 0), "-x"));
                
                // 2D 대각선 방향 (4개)
                if (UseDiagonals)
                {
                    Directions.Add((new Vector3(1, 0, 1), "+x+z"));    // 우앞
                    Directions.Add((new Vector3(-1, 0, 1), "-x+z"));   // 좌앞
                    Directions.Add((new Vector3(1, 0, -1), "+x-z"));   // 우뒤
                    Directions.Add((new Vector3(-1, 0, -1), "-x-z"));  // 좌뒤
                }
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
            PhyVertex = new List<Vector3>();
            
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
                    // XYZ 순서 유지 (Y축이 높이)
                    if (isValid) PhyVertex.Add(new Vector3(i, j, k));
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
                    // 2D 좌표는 XZ 평면에 매핑 (Y=0)
                    if (isValid) PhyVertex.Add(new Vector3(i, 0, j));
                }
            }
            
            // PhyVertex가 비어있다면 최소한의 좌표 추가
            if (PhyVertex.Count == 0)
            {
                Debug.LogWarning("PhyVertex가 비어있습니다. 기본 좌표를 추가합니다.");
                
                // 공간 중앙에 기본 좌표 추가
                Vector3 centerPoint = new Vector3();
                for (int i = 0; i < Math.Min(3, Dim); i++)
                {
                    float value = (SpaceCoords[0][i] + SpaceCoords[1][i]) / 2;
                    if (i == 0) centerPoint.x = value;
                    else if (i == 1) centerPoint.y = value;
                    else if (i == 2) centerPoint.z = value;
                }
                PhyVertex.Add(centerPoint);
                
                // 주요 방향으로 추가 좌표 생성
                foreach (var dir in Directions)
                {
                    Vector3 newPoint = centerPoint + dir.Item1;
                    PhyVertex.Add(newPoint);
                }
            }
            
            // Dictionary 초기화
            OpenSet = new Dictionary<string, float>();
            CloseSet = new Dictionary<string, int>();
            
            foreach (var p in PhyVertex)
            {
                string key = $"{p.x},{p.y},{p.z}";
                OpenSet[key] = 0f;
                CloseSet[key] = 0;
            }
            
            // 로깅 추가
            Debug.Log($"유효 좌표 수: {PhyVertex.Count}");
            
            // 일부 좌표 샘플 출력
            int sampleSize = Math.Min(5, PhyVertex.Count);
            for (int i = 0; i < sampleSize; i++)
            {
                Debug.Log($"샘플 좌표 {i}: {PhyVertex[i].x},{PhyVertex[i].y},{PhyVertex[i].z}");
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
                    string edgeKey = $"{v.x},{v.y},{v.z}:{dir.Item2}";
                    float cost = edgeCost;
                    
                    // 대각선 이동은 비용을 추가 (직선보다 1.414배(√2) 또는 1.732배(√3) 더 높은 비용)
                    if (dir.Item2.Contains("+") && dir.Item2.Split('+').Length > 2)
                    {
                        // 2개의 축을 사용하는 대각선 (√2 = 약 1.414)
                        cost *= 1.414f;
                    }
                    else if (dir.Item2.Contains("-") && dir.Item2.Split('-').Length > 2)
                    {
                        // 2개의 축을 사용하는 대각선 (√2 = 약 1.414)
                        cost *= 1.414f;
                    }
                    else if (dir.Item2.Length > 3) // 예: "+x+y+z" 또는 "-x-y-z" 등 3개 축 사용
                    {
                        // 3개의 축을 모두 사용하는 대각선 (√3 = 약 1.732)
                        cost *= 1.732f;
                    }
                    
                    // 장애물과의 거리 계산
                    bool isNearObstacle = false;
                    float minDistance = float.MaxValue;
                    
                    foreach (var obs in ObstacleCoords)
                    {
                        float[] closestPoint = ClosestPoint(Vector3ToFloatArray(v), obs[0], obs[1]);
                        float distance = Distance(Vector3ToFloatArray(v), closestPoint);
                        
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                        
                        // 장애물과의 거리가 매우 가까우면 (반경의 3배 이내) 높은 비용 설정
                        if (distance < Radius * 3f)
                        {
                            isNearObstacle = true;
                            // 거리가 가까울수록 비용 증가 (최대 10배까지)
                            float distanceFactor = 1 - (distance / (Radius * 3f));
                            float additionalCost = edgeCost * 9 * distanceFactor;
                            cost += additionalCost;
                            
                            if (processedEdges % 100 == 0 || highCostEdges < 10)
                            {
                                Debug.LogVerbose($"장애물 근처 엣지: {edgeKey}, 거리: {distance}, 추가 비용: {additionalCost}");
                            }
                            
                            highCostEdges++;
                        }
                    }
                    
                    // 다른 파이프와의 근접성을 고려한 가중치 적용 (이 기능이 추가됨)
                    if (Radius > 0)
                    {
                        // 이동할 위치 계산
                        Vector3 nextPos = v + dir.Item1;
                        
                        // 다른 파이프들과의 근접성 확인
                        foreach (var otherPipe in GetNearbyPipes())
                        {
                            float minPipeDistance = float.MaxValue;
                            
                            // 파이프의 각 점과의 최소 거리 계산
                            foreach (var pipePoint in otherPipe.Points)
                            {
                                float pipeDistance = Vector3.Distance(nextPos, pipePoint);
                                minPipeDistance = Mathf.Min(minPipeDistance, pipeDistance);
                            }
                            
                            float otherPipeRadius = otherPipe.Radius;
                            float proximityThreshold = (Radius + otherPipeRadius) * 3.0f;
                            
                            // 근접 거리 내에 있으면 가중치 적용
                            if (minPipeDistance < proximityThreshold)
                            {
                                float proximityFactor = 1 - (minPipeDistance / proximityThreshold);
                                float proximityMultiplier = 1 + (4 * proximityFactor); // 최대 5배까지 증가
                                
                                // 거리에 반비례하는 비용 증가
                                float additionalCost = cost * (proximityMultiplier - 1);
                                cost += additionalCost;
                                
                                if (processedEdges % 100 == 0 || highCostEdges < 10)
                                {
                                    Debug.LogVerbose($"다른 파이프 근처 엣지: {edgeKey}, 거리: {minPipeDistance}, 임계값: {proximityThreshold}, 추가 비용: {additionalCost}");
                                }
                                
                                highCostEdges++;
                            }
                        }
                    }
                    
                    EdgeCost.Add(edgeKey, cost);
                    processedEdges++;
                }
            }
            
            Debug.Log($"엣지 비용 초기화 완료: 총 {processedEdges}개 엣지, 고비용 엣지 {highCostEdges}개");
        }
        
        // 근처 파이프 정보를 가져오는 가상 메서드 (실제 구현은 DecompositionHeuristic에서)
        protected virtual List<PipeInfo> GetNearbyPipes()
        {
            // 기본 구현은 빈 리스트 반환
            return new List<PipeInfo>();
        }
        
        // 파이프 정보를 저장하는 클래스
        protected class PipeInfo
        {
            public List<Vector3> Points;
            public float Radius;
            
            public PipeInfo(List<Vector3> points, float radius)
            {
                Points = points;
                Radius = radius;
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
        public float HeuristicCost(Vector3 pCoord, Vector3 end)
        {
            // 맨해튼 거리 계산
            return Mathf.Abs(pCoord.x - end.x) + Mathf.Abs(pCoord.y - end.y) + Mathf.Abs(pCoord.z - end.z);
        }
        public float TotalCost(Node p, Vector3 end)
        {
            return BaseCost(p) + HeuristicCost(p.Coord, end);
        }

        public bool IsInOpenSet(Vector3 pCoord)
        {
            var key = $"{pCoord.x},{pCoord.y},{pCoord.z}";
            return OpenSet.ContainsKey(key) && OpenSet[key] == 0f;
        }
        public bool IsInCloseSet(Vector3 pCoord)
        {
            var key = $"{pCoord.x},{pCoord.y},{pCoord.z}";
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

        // IsEnoughSpace 메서드 개선 - 파이프 반경을 고려한 회피 로직
        public bool IsEnoughSpace(Vector3 pCoord, string direction, float radius, float delta)
        {
            Debug.LogVerbose($"IsEnoughSpace 확인: 좌표 {pCoord}, 방향 {direction}, 반경 {radius}");
            
            // 파이프 두께를 고려한 offset 계산 (3배로 증가)
            float shiftAmount = (float)Math.Ceiling(radius * 3.0f + delta);
            Vector3 shift = new Vector3(shiftAmount, shiftAmount, shiftAmount);
            
            // 현재 이동 방향에서는 shift 적용 안함
            if (direction.Contains("x")) shift.x = 0;
            else if (direction.Contains("y")) shift.y = 0; // Y축 (높이)
            else if (Dim == 3 && direction.Contains("z")) shift.z = 0;
            
            // 높이(Y축) 방향으로는 더 적은 제약 적용 (높이 방향 파이프 분리 허용)
            shift.y = shift.y * 0.5f;
            
            Debug.LogVerbose($"계산된 shift: {shift.x},{shift.y},{shift.z}");
            
            Vector3 p1 = pCoord - shift;
            Vector3 p2 = pCoord + shift;
            
            Debug.LogVerbose($"검사 범위: {p1.x},{p1.y},{p1.z} ~ {p2.x},{p2.y},{p2.z}");
            
            var ranges = new List<List<int>>();
            ranges.Add(Enumerable.Range((int)p1.x, (int)(p2.x - p1.x + 1)).ToList()); // X축
            ranges.Add(Enumerable.Range((int)p1.y, (int)(p2.y - p1.y + 1)).ToList()); // Y축 (높이)
            if (Dim == 3)
                ranges.Add(Enumerable.Range((int)p1.z, (int)(p2.z - p1.z + 1)).ToList()); // Z축
            
            int pointsCount = 0;
            int missingPointsCount = 0;
            int missingPointsThreshold = 2; // 높이 방향 레이어링을 위한 임계값 증가
            List<Vector3> missingPoints = new List<Vector3>();
            
            // 다른 파이프와의 근접성 확인
            bool isNearOtherPipe = false;
            float minPipeDistance = float.MaxValue;
            
            foreach (var otherPipe in GetNearbyPipes())
            {
                foreach (var pipePoint in otherPipe.Points)
                {
                    float distance = Vector3.Distance(pCoord, pipePoint);
                    
                    if (distance < minPipeDistance)
                    {
                        minPipeDistance = distance;
                    }
                    
                    // 두 파이프 반경의 합 * 3 이내에 있는지 확인
                    float proximityThreshold = (radius + otherPipe.Radius) * 3.0f;
                    if (distance < proximityThreshold)
                    {
                        isNearOtherPipe = true;
                        
                        // Y축 방향 이동인 경우 더 관대하게 처리 (수직 레이어링 허용)
                        if (direction.Contains("y"))
                        {
                            // 수직 레이어링을 위한 임계값 완화
                            float heightFactor = 0.5f;
                            if (distance > (radius + otherPipe.Radius) * (1 + heightFactor))
                            {
                                // 충분히 떨어져 있으면 이동 허용
                                isNearOtherPipe = false;
                            }
                        }
                    }
                }
            }
            
            foreach (var item in CartesianProduct(ranges))
            {
                Vector3 checkPoint;
                if (Dim == 3)
                    checkPoint = new Vector3(item[0], item[1], item[2]);
                else
                    checkPoint = new Vector3(item[0], item[1], 0);
                
                var key = $"{checkPoint.x},{checkPoint.y},{checkPoint.z}";
                pointsCount++;
                
                if (!OpenSet.ContainsKey(key)) 
                {
                    missingPointsCount++;
                    missingPoints.Add(checkPoint);
                    Debug.LogVerbose($"장애물 감지: 좌표 {key}");
                }
            }
            
            // 장애물 확인 결과 정리
            bool enoughSpace = false;
            
            // 조건 1: 장애물이 임계값 이하면 통과
            if (missingPointsCount <= missingPointsThreshold)
            {
                Debug.LogVerbose($"장애물이 임계값({missingPointsThreshold}) 이하: {missingPointsCount}개, 통과 허용");
                enoughSpace = true;
            }
            // 조건 2: 장애물이 모두 같은 높이에 있고 Y축 이동인 경우
            else if (missingPoints.Count > 0)
            {
                bool allObstaclesAtSameHeight = true;
                float referenceHeight = missingPoints[0].y;
                
                foreach (var point in missingPoints)
                {
                    if (Math.Abs(point.y - referenceHeight) > 0.1f)
                    {
                        allObstaclesAtSameHeight = false;
                        break;
                    }
                }
                
                if (allObstaclesAtSameHeight && direction.Contains("y"))
                {
                    Debug.LogVerbose($"모든 장애물이 동일 높이에 있어 높이 방향 이동 허용 (Y축 이동)");
                    enoughSpace = true;
                }
            }
            
            // 다른 파이프와 가까운 경우의 처리
            if (isNearOtherPipe)
            {
                // 수직(Y축) 이동인 경우 다른 파이프 피해 가도록 허용
                if (direction.Contains("y"))
                {
                    Debug.LogVerbose($"다른 파이프 근처지만 수직 이동 허용. 거리: {minPipeDistance:F2}");
                    // Y축 이동은 이미 처리됨
                }
                // 수평 이동인 경우 약간의 패널티만 주고 여전히 이동 허용 (완전히 막지 않음)
                else
                {
                    Debug.LogVerbose($"다른 파이프 근처이나 수평 이동 제한적 허용. 거리: {minPipeDistance:F2}");
                    // 수평 이동은 남은 공간이 충분하면 허용
                    if (enoughSpace)
                    {
                        // 이 경우는 이미 enoughSpace가 true로 설정되어 있음
                    }
                }
            }
            
            Debug.LogVerbose($"장애물 검사 완료: 총 {pointsCount}개 포인트 중 {missingPointsCount}개 장애물, 이동 가능: {enoughSpace}");
            return enoughSpace || missingPointsCount == 0;
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
        public (List<(Vector3, string)> bendPath, List<(Vector3, string)> path) Run((Vector3, string) startInfo, (Vector3, string) endInfo, float radius, float delta)
        {
            Debug.Log($"A* 경로 탐색 시작: 시작점 {startInfo.Item1}, 목표점 {endInfo.Item1}");
            Debug.Log($"파라미터: 반경 = {radius}, 델타 = {delta}, WPath = {WPath}, WBend = {WBend}, WEnergy = {WEnergy}");
            
            // PhyVertex 확인
            if (PhyVertex == null || PhyVertex.Count == 0)
            {
                Debug.LogError("PhyVertex가 비어있습니다. 경로를 찾을 수 없습니다.");
                return (null, null);
            }
            
            // 시작점과 끝점이 유효한지 확인
            string startKey = $"{startInfo.Item1.x},{startInfo.Item1.y},{startInfo.Item1.z}";
            if (!OpenSet.ContainsKey(startKey))
            {
                Debug.LogWarning("시작점이 유효하지 않습니다. 가장 가까운 유효한 점을 찾습니다.");
                startInfo = (FindNearestValidPoint(startInfo.Item1), startInfo.Item2);
            }
            
            string endKey = $"{endInfo.Item1.x},{endInfo.Item1.y},{endInfo.Item1.z}";
            if (!OpenSet.ContainsKey(endKey))
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
                    Debug.Log($"반복 {iterationCount}: 현재 좌표 {curNode.Coord}, 비용 {curCost}");
                }
                
                pq.Remove(pq.Min);
                
                // 목표 도달 여부 확인
                if (curNode.Coord == endInfo.Item1)
                {
                    Debug.Log($"경로 찾음! 반복 횟수: {iterationCount}, 최종 비용: {curCost}");
                    var (bend, path) = BuildPath(curNode);
                    Debug.Log($"경로 포인트 수: {path.Count}, 굽힘 포인트 수: {bend.Count}");
                    return (bend, path);
                }
                
                string coordKey = $"{curNode.Coord.x},{curNode.Coord.y},{curNode.Coord.z}";
                CloseSet[coordKey] = 1;
                OpenSet[coordKey] = 0f;
                
                int neighborsCount = 0;
                int validNeighborsCount = 0;
                
                foreach (var dir in Directions)
                {
                    neighborsCount++;
                    Vector3 nextCoord = curNode.Coord + dir.Item1;
                    
                    string key = $"{nextCoord.x},{nextCoord.y},{nextCoord.z}";
                    Debug.LogVerbose($"이웃 탐색: {curNode.Coord} -> {key} (방향: {dir.Item2})");
                    
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
                        Debug.LogVerbose($"장애물 감지: 좌표 {nextNode.Coord}, 기존 비용 {totalCost}에 패널티 {obstaclePenalty} 추가");
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
        private Vector3 FindNearestValidPoint(Vector3 point)
        {
            if (PhyVertex == null || PhyVertex.Count == 0)
            {
                Debug.LogError("PhyVertex가 비어있어 가장 가까운 점을 찾을 수 없습니다.");
                return point;
            }
            
            float minDistance = float.MaxValue;
            Vector3 closest = point;
            
            foreach (var vertex in PhyVertex)
            {
                float distance = Vector3.Distance(point, vertex);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closest = vertex;
                }
            }
            
            Debug.Log($"가장 가까운 유효 좌표 발견: {closest}, 거리: {minDistance}");
            return closest;
        }

        // 경로 재구성
        public (List<(Vector3, string)> bendPath, List<(Vector3, string)> path) BuildPath(Node p)
        {
            var bendPath = new List<(Vector3, string)>();
            var path = new List<(Vector3, string)>();
            bendPath.Add(p.CoordInfo);
            while (true)
            {
                if (p.Parent == null || p.Coord == Start.Coord) break;
                path.Insert(0, p.CoordInfo);
                if (p.NCP == p.Parent.NCP + 1)
                    bendPath.Insert(0, p.Parent.CoordInfo);
                p = p.Parent;
            }
            bendPath.Insert(0, Start.CoordInfo);
            if (bendPath.Count > 1 && bendPath[0].Item1 == bendPath[1].Item1)
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
