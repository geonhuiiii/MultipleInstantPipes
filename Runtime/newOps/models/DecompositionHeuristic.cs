using System;
using System.Collections.Generic;
using System.Linq;
using Utils;
using UnityEngine;

namespace Model
{
    public class DecompositionHeuristic : AStar
    {
        public List<( (Vector3, string), (Vector3, string), float, float )> Pipes;
        public int NPipes;
        public List<HashSet<(string, string)>> CoveringListN;
        public List<List<(Vector3, string)>> PathN;
        public List<List<(Vector3, string)>> BendPointsN;

        public DecompositionHeuristic(
            int maxit,
            float[][] spaceCoords,
            List<float[][]> obstacleCoords,
            List<( (float[], string), (float[], string), float, float )> pipes,
            float wPath, float wBend, float wEnergy, int minDisBend
        ) : base(maxit, spaceCoords, obstacleCoords, wPath, wBend, wEnergy, minDisBend)
        {
            // 파이프 리스트 변환
            Pipes = new List<((Vector3, string), (Vector3, string), float, float)>();
            foreach (var pipe in pipes)
            {
                Pipes.Add((
                    (FloatArrayToVector3(pipe.Item1.Item1), pipe.Item1.Item2),
                    (FloatArrayToVector3(pipe.Item2.Item1), pipe.Item2.Item2),
                    pipe.Item3,
                    pipe.Item4
                ));
            }
            NPipes = pipes.Count;
        }

        // float[] 배열을 Vector3로 변환하는 헬퍼 메서드
        private Vector3 FloatArrayToVector3(float[] array)
        {
            if (array == null || array.Length < 3)
            {
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

        // 경로의 커버링 리스트 생성
        public HashSet<(string, string)> GetCoveringList(List<(Vector3, string)> path, float radius = 1, float delta = 0)
        {
            // Check if path is null or empty
            if (path == null || path.Count == 0)
            {
                return new HashSet<(string, string)>();
            }
            
            var Pk = path.Select(item => item.Item1).ToList();
            var Lk = new List<Vector3>(Pk);
            foreach (var v0 in Pk)
            {
                foreach (var v in Lk.ToList())
                {
                    foreach (var dir in Directions)
                    {
                        Vector3 vPrime = v + dir.Item1;
                        if (IsInOpenSet(vPrime) && GetMaxDistance(v0, vPrime) <= radius + delta && !Lk.Any(x => x == vPrime))
                        {
                            Lk.Add(vPrime);
                        }
                    }
                }
            }
            var result = new HashSet<(string, string)>();
            foreach (var v in Lk)
                foreach (var dir in Directions)
                    result.Add(($"{v.x},{v.y},{v.z}", dir.Item2));
            return result;
        }

        // 두 경로의 커버링 리스트 교집합(충돌 엣지)
        public List<(string, string)> FindConflictEdges(List<(Vector3, string)> path1, List<(Vector3, string)> path2)
        {
            var cov1 = GetCoveringList(path1);
            var cov2 = GetCoveringList(path2);
            return cov1.Intersect(cov2).ToList();
        }

        // 두 좌표의 최대 거리
        public static float GetMaxDistance(Vector3 p1, Vector3 p2)
        {
            float res = 0;
            res = Mathf.Max(res, Mathf.Abs(p1.x - p2.x));
            res = Mathf.Max(res, Mathf.Abs(p1.y - p2.y));
            res = Mathf.Max(res, Mathf.Abs(p1.z - p2.z));
            return res;
        }

        // MainRun 메서드 수정 - 파이프 간 근접 가중치 적용
        public (List<List<(Vector3, string)>> pathN, List<List<(Vector3, string)>> bendPointsN) MainRun()
        {
            PathN = new List<List<(Vector3, string)>>();
            BendPointsN = new List<List<(Vector3, string)>>();
            CoveringListN = new List<HashSet<(string, string)>>();
            
            // 파이프 생성 시간 기록 (충돌 시 우선순위 결정용)
            var pipeCreationTimes = new Dictionary<int, float>();
            float currentTime = 0;
            
            // 첫 번째 단계: 모든 파이프에 대한 초기 경로 생성
            for (int k = 0; k < NPipes; k++)
            {
                Debug.Log($"파이프 {k} 경로 생성 시작");
                
                // 현재 파이프 인덱스를 AStar에 설정
                var currentPipeIndexField = this.GetType().BaseType.GetField("CurrentPipeIndex", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);

                if (currentPipeIndexField != null)
                {
                    currentPipeIndexField.SetValue(this, k);
                    Debug.Log($"현재 파이프 인덱스 {k}를 AStar에 설정");
                }
                
                // 기존에 생성된 파이프와의 근접성에 따른 가중치 적용
                if (k > 0 && PathN.Count > 0)
                {
                    ApplyProximityWeights(k);
                }
                
                // 엣지 비용 재계산 (임시 장애물 고려)
                InitEdgeCost(1f);
                
                var pipe = Pipes[k];
                var (bend, path) = Run(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
                
                // 현재 파이프 인덱스 초기화
                if (currentPipeIndexField != null)
                {
                    currentPipeIndexField.SetValue(this, -1);
                }
                
                // 경로 생성 실패 시 기본 직선 경로 생성
                if (path == null || path.Count == 0)
                {
                    Debug.LogWarning($"파이프 {k}의 초기 경로 생성 실패. 직선 경로로 대체합니다.");
                    path = CreateStraightPath(pipe.Item1, pipe.Item2);
                    bend = CreateStraightBendPath(pipe.Item1, pipe.Item2);
                }
                
                Debug.Log($"파이프 {k} 경로 생성 완료: {path.Count}개 포인트");
                PathN.Add(path);
                BendPointsN.Add(bend);
                CoveringListN.Add(GetCoveringList(path, pipe.Item3, pipe.Item4));
                
                // 파이프 생성 시간 기록 (충돌 처리 시 우선순위 결정에 사용)
                pipeCreationTimes[k] = currentTime;
                currentTime += 1.0f;
            }
            
            // 초기 경로 유효성 검증
            ValidateAllPaths();
            
            // 충돌 검사 및 비용 업데이트 구현
            int maxCollisionResolveAttempts = 25; // 최대 충돌 해결 시도 횟수
            bool hasCollisions = true;
            int attemptCount = 0;
            
            // 파이프 간의 높이 조정 기록 (중복 조정 방지)
            var heightAdjustments = new Dictionary<string, float>();
            
            while (hasCollisions && attemptCount < maxCollisionResolveAttempts)
            {
                attemptCount++;
                
                // 모든 파이프 쌍에 대해 충돌 검사 및 근접 검사
                var collisionDetails = new List<(int pipe1, int pipe2, int edgeCount, float severity)>();
                var proximityPairs = new List<(int pipe1, int pipe2, float distance, float threshold)>();
                
                // 공간 분할을 위한 그리드 기반 접근
                var pipeGrids = new Dictionary<string, List<int>>();
                
                Debug.Log("NPipes  " + NPipes.ToString());
                // 각 파이프를 대략적인 공간 그리드에 할당
                for (int i = 0; i < NPipes; i++)
                {
                    if (PathN[i] == null || PathN[i].Count == 0) continue;
                    var startPoint = PathN[i][0].Item1;
                    var endPoint = PathN[i][PathN[i].Count - 1].Item1;
                    
                    int gridSize = 3;
                    
                    // 그리드 셀 계산
                    int startX = (int)(startPoint.x / gridSize);
                    int startY = (int)(startPoint.y / gridSize);
                    int startZ = startPoint.z > 0 ? (int)(startPoint.z / gridSize) : 0;
                    
                    int endX = (int)(endPoint.x / gridSize);
                    int endY = (int)(endPoint.y / gridSize);
                    int endZ = endPoint.z > 0 ? (int)(endPoint.z / gridSize) : 0;
                    
                    // 바운딩 박스 계산
                    int minX = Math.Min(startX, endX);
                    int maxX = Math.Max(startX, endX);
                    int minY = Math.Min(startY, endY);
                    int maxY = Math.Max(startY, endY);
                    int minZ = Math.Min(startZ, endZ);
                    int maxZ = Math.Max(startZ, endZ);
                    
                    // 해당 파이프의 반경을 고려하여 그리드 확장
                    float pipeRadius = Pipes[i].Item3;
                    int radiusGrids = (int)Math.Ceiling(pipeRadius * 3);
                    
                    for (int x = minX - radiusGrids; x <= maxX + radiusGrids; x++)
                    for (int y = minY - radiusGrids; y <= maxY + radiusGrids; y++)
                    for (int z = minZ - radiusGrids; z <= maxZ + radiusGrids; z++)
                    {
                        string gridKey = $"{x},{y},{z}";
                        if (!pipeGrids.ContainsKey(gridKey))
                            pipeGrids[gridKey] = new List<int>();
                        pipeGrids[gridKey].Add(i);
                    }
                }
                
                // 동일한 그리드 셀에 있는 파이프들 간의 검사
                var checkedPairs = new HashSet<string>();
                
                foreach (var grid in pipeGrids)
                {
                    var pipesInGrid = grid.Value;
                    
                    for (int idx1 = 0; idx1 < pipesInGrid.Count; idx1++)
                    {
                        int i = pipesInGrid[idx1];
                        
                        for (int idx2 = idx1 + 1; idx2 < pipesInGrid.Count; idx2++)
                        {
                            int j = pipesInGrid[idx2];
                            
                            // 이미 검사한 쌍인지 확인
                            string pairKey = i < j ? $"{i}:{j}" : $"{j}:{i}";
                            if (checkedPairs.Contains(pairKey)) continue;
                            checkedPairs.Add(pairKey);
                            
                            if (PathN[i] == null || PathN[j] == null) continue;
                            
                            // 두 경로 간 최소 거리 계산
                            float minDistance = CalculateMinDistanceBetweenPaths(PathN[i], PathN[j]);
                            
                            // 두 파이프 반경의 합 * 3 계산 (근접 임계값)
                            float radiusI = Pipes[i].Item3;
                            float radiusJ = Pipes[j].Item3;
                            float proximityThreshold = (radiusI + radiusJ) * 2.0f;
                            
                            // 충돌 검사 (실제 충돌하는 경우) - 임계값을 더 엄격하게 설정
                            float actualCollisionThreshold = (radiusI + radiusJ) * 1.05f; // 10% 여유 공간
                            if (minDistance < actualCollisionThreshold)
                            {
                                var conflictEdges = FindConflictEdges(PathN[i], PathN[j]);
                                if (conflictEdges.Count > 0)
                                {
                                    // 충돌 심각도 계산
                                    float severity = conflictEdges.Count * (radiusI + radiusJ) * (actualCollisionThreshold - minDistance);
                                    collisionDetails.Add((i, j, conflictEdges.Count, severity));
                                    Debug.Log($"파이프 {i}와 {j} 사이에 실제 충돌 감지: 거리={minDistance:F2}, 임계값={actualCollisionThreshold:F2}, 충돌 엣지={conflictEdges.Count}개, 심각도={severity:F2}");
                                }
                                else
                                {
                                    Debug.Log($"파이프 {i}와 {j} 사이 거리가 가깝지만({minDistance:F2} < {actualCollisionThreshold:F2}) 충돌 엣지는 없음");
                                }
                            }
                            // 근접 검사 (충돌은 아니지만 가까운 경우)
                            else if (minDistance < proximityThreshold)
                            {
                                proximityPairs.Add((i, j, minDistance, proximityThreshold));
                                Debug.Log($"파이프 {i}와 {j} 사이 거리({minDistance:F2})가 임계값({proximityThreshold:F2}) 미만");
                            }
                        }
                    }
                }
                
                // 가까운 파이프 쌍에 대해 가중치 적용
                foreach (var (pipe1, pipe2, distance, threshold) in proximityPairs)
                {
                    // 거리에 반비례하는 가중치 계산 (거리가 가까울수록 더 높은 가중치)
                    float proximityFactor = 1.0f - (distance / threshold);
                    float weightMultiplier = 1.0f + (4.0f * proximityFactor); // 최대 5배까지 증가
                    
                    Debug.Log($"파이프 {pipe1}와 {pipe2} 사이 근접성 가중치 적용: 거리={distance:F2}, 가중치={weightMultiplier:F2}배");
                    
                    // 두 파이프 모두에 가중치 적용 (더 나중에 생성된 파이프에 더 높은 가중치)
                    int newerPipe = pipeCreationTimes[pipe1] < pipeCreationTimes[pipe2] ? pipe2 : pipe1;
                    ApplyProximityWeightsBetweenPaths(newerPipe, PathN[pipe1], PathN[pipe2], weightMultiplier);
                }
                
                // 심각도에 따라 충돌 정렬
                collisionDetails.Sort((a, b) => b.severity.CompareTo(a.severity));
                var collisions = collisionDetails.Select(c => (c.pipe1, c.pipe2)).ToList();
                
                // 충돌 요약 출력
                if (collisionDetails.Count > 0)
                {
                    Debug.Log($"충돌 요약 (심각도 순):");
                    foreach (var (pipe1, pipe2, count, severity) in collisionDetails)
                    {
                        Debug.Log($"  파이프 {pipe1}-{pipe2}: {count}개 충돌, 심각도 {severity:F2}");
                    }
                }
                
                // 충돌이 없으면 루프 종료
                if (collisions.Count == 0)
                {
                    hasCollisions = false;
                    Debug.Log($"충돌 해결 완료: {attemptCount}번 시도 후 모든 충돌이 해결됨");
                    break;
                }
                
                Debug.Log($"충돌 해결 시도 {attemptCount}: {collisions.Count}개의 충돌 쌍 발견");
                
                // 충돌 해결 전략 구현
                foreach (var (i, j) in collisions)
                {
                    // 기존 로직대로 높이 조정 및 경로 재계산
                    int olderPipe = pipeCreationTimes[i] < pipeCreationTimes[j] ? i : j;
                    int newerPipe = olderPipe == i ? j : i;
                    
                    string adjustmentKey = olderPipe < newerPipe ? $"{olderPipe}:{newerPipe}" : $"{newerPipe}:{olderPipe}";
                    
                    if (!heightAdjustments.ContainsKey(adjustmentKey))
                    {
                        // 높이 조정 전에 경로 생성 순서 변경 시도
                        bool orderChangeResolved = false;
                        
                        // 경로 생성 순서 변경 시도를 기록하는 딕셔너리
                        string orderChangeKey = $"OrderChange_{adjustmentKey}";
                        bool alreadyTriedOrderChange = heightAdjustments.ContainsKey(orderChangeKey);
                        
                        if (!alreadyTriedOrderChange)
                        {
                            Debug.Log($"파이프 {olderPipe}와 {newerPipe} 사이 충돌 발생. 경로 생성 순서 변경 시도.");
                            
                            // 기존 파이프 정보 백업
                            var oldPipeInfo_old = Pipes[olderPipe];
                            var oldPipeInfo_new = Pipes[newerPipe];
                            
                            // 경로 생성 순서 바꾸기 (생성 시간 조정)
                            float tempTime = pipeCreationTimes[olderPipe];
                            pipeCreationTimes[olderPipe] = pipeCreationTimes[newerPipe] + 0.1f; // 더 나중에 생성된 것으로 변경
                            
                            // 경로 순서를 바꾸어 재계산 (먼저 새 파이프(newer)의 경로를 생성하고, 후에 기존 파이프(older)의 경로 생성)
                            RecalculatePipePath(newerPipe);
                            RecalculatePipePath(olderPipe);
                            
                            // 순서 변경 시도를 기록
                            heightAdjustments[orderChangeKey] = 1f;
                            
                            // 충돌이 해결되었는지 확인
                            List<(string, string)> remainingConflicts = FindConflictEdges(PathN[olderPipe], PathN[newerPipe]);
                            
                            if (remainingConflicts.Count == 0)
                            {
                                Debug.Log($"경로 생성 순서 변경으로 파이프 {olderPipe}와 {newerPipe} 사이의 충돌이 해결되었습니다.");
                                orderChangeResolved = true;
                            }
                            else
                            {
                                Debug.LogWarning($"경로 생성 순서 변경으로 충돌이 해결되지 않았습니다. 충돌 엣지: {remainingConflicts.Count}개");
                                
                                // 충돌이 해결되지 않았으면 높이 조정 방식으로 진행
                                orderChangeResolved = false;
                            }
                        }
                        
                        // 순서 변경으로 해결되지 않은 경우 높이 조정을 적용
                        if (!orderChangeResolved)
                        {
                            float olderPipeRadius = Pipes[olderPipe].Item3;
                            float newerPipeRadius = Pipes[newerPipe].Item3;
                            float heightAdjustment = (olderPipeRadius + newerPipeRadius) * 2f;
                            
                            var olderPipe_start = Pipes[olderPipe].Item1;
                            var olderPipe_end = Pipes[olderPipe].Item2;
                            
                            Debug.Log($"경로 생성 순서 변경으로 해결되지 않아 파이프 {olderPipe}의 높이를 {heightAdjustment} 만큼 증가시킵니다.");
                            
                            Vector3 adjustedStartPos = new Vector3(
                                olderPipe_start.Item1.x,
                                olderPipe_start.Item1.y + heightAdjustment,
                                olderPipe_start.Item1.z
                            );
                            
                            Vector3 adjustedEndPos = new Vector3(
                                olderPipe_end.Item1.x,
                                olderPipe_end.Item1.y + heightAdjustment,
                                olderPipe_end.Item1.z
                            );
                            
                            Pipes[olderPipe] = (
                                (adjustedStartPos, olderPipe_start.Item2), 
                                (adjustedEndPos, olderPipe_end.Item2),
                                olderPipeRadius,
                                Pipes[olderPipe].Item4
                            );
                            
                            heightAdjustments[adjustmentKey] = heightAdjustment;
                            
                            // 두 파이프 모두 경로 재계산
                            RecalculatePipePath(olderPipe);
                            RecalculatePipePath(newerPipe);
                        }
                    }
                    else
                    {
                        Debug.Log($"파이프 {olderPipe}와 {newerPipe}는 이미 높이 조정을 했지만 여전히 충돌합니다. 경로 비용 조정.");
                        
                        // 충돌 엣지에 대한 비용 증가 (더 높은 가중치 적용)
                        UpdateEdgeCostForCollision(olderPipe, PathN[i], PathN[j], 8.0f); // 가중치 증가
                        UpdateEdgeCostForCollision(newerPipe, PathN[i], PathN[j], 10.0f); // 더 높은 가중치
                        
                        RecalculatePipePath(olderPipe);
                        RecalculatePipePath(newerPipe);
                    }
                }
            }
            
            if (attemptCount >= maxCollisionResolveAttempts && hasCollisions)
            {
                Debug.LogWarning($"최대 시도 횟수({maxCollisionResolveAttempts})에 도달했습니다. 모든 충돌이 해결되지 않았을 수 있습니다.");
            }
            
            // 최종 검증: 모든 파이프의 최종 경로 유효성 확인
            ValidateAllPaths();
            
            return (PathN, BendPointsN);
        }
        
        // 두 경로 간 최소 거리 계산 메서드 개선
        private float CalculateMinDistanceBetweenPaths(List<(Vector3, string)> path1, List<(Vector3, string)> path2)
        {
            if (path1 == null || path1.Count == 0 || path2 == null || path2.Count == 0)
                return float.MaxValue;
                
            float minDistance = float.MaxValue;
            
            // 경로의 선분 간 최소 거리 계산 (단순 점 거리가 아닌 선분 거리)
            for (int i = 0; i < path1.Count - 1; i++)
            {
                Vector3 p1Start = path1[i].Item1;
                Vector3 p1End = path1[i + 1].Item1;
                
                for (int j = 0; j < path2.Count - 1; j++)
                {
                    Vector3 p2Start = path2[j].Item1;
                    Vector3 p2End = path2[j + 1].Item1;
                    
                    // 두 선분 간의 최단 거리 계산
                    float segmentDistance = CalculateDistanceBetweenSegments(p1Start, p1End, p2Start, p2End);
                    
                    if (segmentDistance < minDistance)
                    {
                        minDistance = segmentDistance;
                    }
                }
            }
            
            // 추가로 모든 점 쌍 간의 거리도 확인 (안전장치)
            foreach (var point1 in path1)
            {
                foreach (var point2 in path2)
                {
                    float pointDistance = Vector3.Distance(point1.Item1, point2.Item1);
                    if (pointDistance < minDistance)
                    {
                        minDistance = pointDistance;
                    }
                }
            }
            
            return minDistance;
        }
        
        // 두 선분 간의 최단 거리 계산
        private float CalculateDistanceBetweenSegments(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1; // 첫 번째 선분의 방향 벡터
            Vector3 d2 = q2 - p2; // 두 번째 선분의 방향 벡터
            Vector3 r = p1 - p2;  // 두 선분의 시작점 간 벡터
            
            float a = Vector3.Dot(d1, d1); // |d1|^2
            float e = Vector3.Dot(d2, d2); // |d2|^2
            float f = Vector3.Dot(d2, r);
            
            // 두 선분이 모두 점인 경우
            if (a <= Mathf.Epsilon && e <= Mathf.Epsilon)
            {
                return Vector3.Distance(p1, p2);
            }
            
            float s, t;
            
            // 첫 번째 선분이 점인 경우
            if (a <= Mathf.Epsilon)
            {
                s = 0.0f;
                t = f / e; // t = (p1 - p2) · d2 / |d2|^2
                t = Mathf.Clamp01(t);
            }
            // 두 번째 선분이 점인 경우
            else if (e <= Mathf.Epsilon)
            {
                t = 0.0f;
                s = Mathf.Clamp01(-Vector3.Dot(r, d1) / a);
            }
            else
            {
                // 일반적인 경우
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b; // |d1|^2 * |d2|^2 - (d1 · d2)^2
                
                if (denom != 0.0f)
                {
                    s = Mathf.Clamp01((b * f - Vector3.Dot(r, d1) * e) / denom);
                }
                else
                {
                    s = 0.0f;
                }
                
                t = (b * s + f) / e;
                
                if (t < 0.0f)
                {
                    t = 0.0f;
                    s = Mathf.Clamp01(-Vector3.Dot(r, d1) / a);
                }
                else if (t > 1.0f)
                {
                    t = 1.0f;
                    s = Mathf.Clamp01((b - Vector3.Dot(r, d1)) / a);
                }
            }
            
            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            
            return Vector3.Distance(c1, c2);
        }
        
        // 새 파이프를 생성할 때 기존 파이프와의 근접성에 대한 가중치 적용
        private void ApplyProximityWeights(int newPipeIndex)
        {
            var newPipeStart = Pipes[newPipeIndex].Item1.Item1;
            var newPipeEnd = Pipes[newPipeIndex].Item2.Item1;
            float newPipeRadius = Pipes[newPipeIndex].Item3;
            
            Debug.Log($"파이프 {newPipeIndex}에 대한 근접성 가중치 계산 시작");
            
            // 이미 생성된 파이프들과의 거리 검사
            for (int i = 0; i < PathN.Count; i++)
            {
                if (PathN[i] == null || PathN[i].Count == 0) continue;
                
                float existingPipeRadius = Pipes[i].Item3;
                float proximityThreshold = (newPipeRadius + existingPipeRadius) * 1.5f;
                
                // 새 파이프의 시작점/끝점과 기존 경로 간의 거리 계산
                float minStartDistance = float.MaxValue;
                float minEndDistance = float.MaxValue;
                
                foreach (var point in PathN[i])
                {
                    float startDistance = Vector3.Distance(newPipeStart, point.Item1);
                    float endDistance = Vector3.Distance(newPipeEnd, point.Item1);
                    
                    minStartDistance = Mathf.Min(minStartDistance, startDistance);
                    minEndDistance = Mathf.Min(minEndDistance, endDistance);
                }
                
                // 최소 거리가 임계값보다 작으면 가중치 적용
                if (minStartDistance < proximityThreshold || minEndDistance < proximityThreshold)
                {
                    float minDistance = Mathf.Min(minStartDistance, minEndDistance);
                    float proximityFactor = 1.0f - (minDistance / proximityThreshold);
                    float weightMultiplier = 1.0f + (4.0f * proximityFactor); // 최대 5배까지 증가
                    
                    Debug.Log($"파이프 {newPipeIndex}와 기존 파이프 {i} 사이 근접성 가중치 적용: 거리={minDistance:F2}, 가중치={weightMultiplier:F2}배");
                    
                    // 기존 경로 주변에 가중치 적용
                    ApplyProximityWeightToPath(PathN[i], proximityThreshold, weightMultiplier);
                }
            }
        }
        
        // 파이프 주위에 근접성 가중치 적용
        private void ApplyProximityWeightToPath(List<(Vector3, string)> path, float proximityThreshold, float weightMultiplier)
        {
            if (path == null || path.Count == 0) return;
            
            // 경로의 각 점 주변에 가중치 적용
            foreach (var (point, direction) in path)
            {
                // 각 방향에 대해 주변 영역에 가중치 적용
                foreach (var dir in Directions)
                {
                    // 현재 점에서 탐색 방향으로 proximityThreshold 거리만큼 탐색
                    for (float distance = 0.5f; distance <= proximityThreshold; distance += 0.5f)
                    {
                        Vector3 proximityPoint = point + dir.Item1 * distance;
                        
                        // 각 방향에 대한 엣지 비용 증가
                        ApplyProximityWeightToPoint(proximityPoint, weightMultiplier * (1 - distance / proximityThreshold));
                    }
                }
            }
        }
        
        // 특정 점 주변에 가중치 적용
        private void ApplyProximityWeightToPoint(Vector3 point, float weightMultiplier)
        {
            string pointKey = $"{point.x},{point.y},{point.z}";
            
            // 각 방향에 대한 엣지 비용 증가
            foreach (var dir in Directions)
            {
                string edgeKey = $"{pointKey}:{dir.Item2}";
                
                if (EdgeCost.ContainsKey(edgeKey))
                {
                    float currentCost = EdgeCost[edgeKey];
                    EdgeCost[edgeKey] = currentCost * weightMultiplier;
                }
                else
                {
                    // 엣지 비용이 없는 경우, 기본값에 가중치 적용
                    EdgeCost[edgeKey] = 1.0f * weightMultiplier;
                }
            }
        }
        
        // 두 경로 사이의 근접성 가중치 적용
        private void ApplyProximityWeightsBetweenPaths(int targetPipeIndex, List<(Vector3, string)> path1, List<(Vector3, string)> path2, float weightMultiplier)
        {
            float pipeRadius = Pipes[targetPipeIndex].Item3;
            float searchRadius = pipeRadius * 1.5f;
            
            Debug.Log($"파이프 {targetPipeIndex}에 주변 파이프와의 근접성 가중치 적용 (가중치: {weightMultiplier:F2}배)");
            
            // 두 경로의 모든 점 쌍에 대해 가중치 적용
            foreach (var point1 in path1)
            {
                foreach (var point2 in path2)
                {
                    float distance = Vector3.Distance(point1.Item1, point2.Item1);
                    
                    if (distance < searchRadius)
                    {
                        // 거리에 반비례하는 가중치 계산
                        float proximityFactor = 1.0f - (distance / searchRadius);
                        float localWeight = weightMultiplier * proximityFactor;
                        
                        // 두 점 사이의 중간 지점 주변에 가중치 적용
                        Vector3 midPoint = (point1.Item1 + point2.Item1) / 2.0f;
                        
                        // 중간 지점 주변 영역에 가중치 적용
                        for (float r = 0; r <= searchRadius; r += searchRadius / 4)
                        {
                            foreach (var dir in Directions)
                            {
                                Vector3 weightPoint = midPoint + dir.Item1 * r;
                                ApplyProximityWeightToPoint(weightPoint, localWeight * (1 - r / searchRadius));
                            }
                        }
                    }
                }
            }
        }
        
        // 충돌 엣지에 가중치 적용 메서드 개선
        private void UpdateEdgeCostForCollision(int pipeIndex, List<(Vector3, string)> path1, List<(Vector3, string)> path2, float weightMultiplier = 5.0f)
        {
            // 두 경로 간 충돌 엣지 찾기
            var conflictEdges = FindConflictEdges(path1, path2);
            
            Debug.Log($"파이프 {pipeIndex}에 충돌 가중치 적용: {conflictEdges.Count}개 엣지, 가중치 {weightMultiplier:F2}배");
            
            // 충돌하는 각 엣지에 대해 비용 증가
            foreach (var (vertex, direction) in conflictEdges)
            {
                string key = $"{vertex}:{direction}";
                
                // 기존 비용에서 패널티 추가
                float currentCost = EdgeCost.ContainsKey(key) ? EdgeCost[key] : 1.0f;
                EdgeCost[key] = currentCost * weightMultiplier;
                
                Debug.Log($"파이프 {pipeIndex}의 엣지 {key}에 대한 비용이 {currentCost}에서 {EdgeCost[key]}로 증가");
            }
            
            // 충돌 영역 주변에도 가중치 적용
            ApplyWeightToSurroundingArea(conflictEdges, weightMultiplier * 0.5f);
        }
        
        // 충돌 영역 주변에 가중치 적용
        private void ApplyWeightToSurroundingArea(List<(string, string)> conflictEdges, float weightMultiplier)
        {
            // 충돌 엣지 주변 영역에 가중치 적용
            HashSet<string> vertices = new HashSet<string>();
            
            // 먼저 충돌 엣지에서 정점 추출
            foreach (var (vertex, _) in conflictEdges)
            {
                vertices.Add(vertex);
            }
            
            // 주변 정점에 대해 가중치 적용
            foreach (string vertex in vertices)
            {
                // 정점 좌표 파싱
                string[] coords = vertex.Split(',');
                if (coords.Length < 3) continue;
                
                if (float.TryParse(coords[0], out float x) &&
                    float.TryParse(coords[1], out float y) &&
                    float.TryParse(coords[2], out float z))
                {
                    Vector3 vertexPos = new Vector3(x, y, z);
                    
                    // 주변 영역에 가중치 적용
                    for (float dx = -1; dx <= 1; dx += 0.5f)
                    for (float dy = -1; dy <= 1; dy += 0.5f)
                    for (float dz = -1; dz <= 1; dz += 0.5f)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue; // 자기 자신 제외
                        
                        Vector3 nearbyPoint = new Vector3(x + dx, y + dy, z + dz);
                        float distance = Vector3.Distance(vertexPos, nearbyPoint);
                        float localWeight = weightMultiplier * (1 - distance / 1.732f); // √3 = 약 1.732
                        
                        if (localWeight > 0)
                        {
                            ApplyProximityWeightToPoint(nearbyPoint, localWeight);
                        }
                    }
                }
            }
        }
        
        // 파이프 경로 재계산 메서드
        private void RecalculatePipePath(int pipeIndex)
        {
            var pipe = Pipes[pipeIndex];
            
            Debug.Log($"파이프 {pipeIndex} 경로 재계산 시작: {pipe.Item1.Item1} -> {pipe.Item2.Item1}");

            // 현재 파이프 인덱스를 AStar에 전달하기 위한 필드 설정
            var currentPipeIndexField = this.GetType().BaseType.GetField("CurrentPipeIndex", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.CreateInstance);

            if (currentPipeIndexField != null)
            {
                currentPipeIndexField.SetValue(this, pipeIndex);
                Debug.Log($"현재 파이프 인덱스 {pipeIndex}를 AStar에 전달");
            }
            
            // 엣지 비용 재계산 (임시 장애물 및 기존 경로 고려)
            InitEdgeCost(1f);
            
            // 공간을 더 넓혀서 탐색 (높이 제한 완화)
            // AStar 알고리즘의 IsEnoughSpace 메서드가 더 넓은 공간을 탐색하도록 임시 설정
            var (newBend, newPath) = Run(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
            
            // 파이프 인덱스 필드 초기화
            if (currentPipeIndexField != null)
            {
                currentPipeIndexField.SetValue(this, -1);
            }
            
            // 경로 생성 실패 시 대체 경로 생성
            if (newPath == null || newPath.Count == 0)
            {
                Debug.LogWarning($"파이프 {pipeIndex}의 경로 재계산 실패. 대체 경로를 시도합니다.");
                newPath = TryAlternativePath(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
                newBend = CreateStraightBendPath(pipe.Item1, pipe.Item2);
            }
            
            // 이전 경로와 새 경로가 동일한지 검사
            bool pathChanged = !PathsEqual(PathN[pipeIndex], newPath);
            
            if (pathChanged)
            {
                Debug.Log($"파이프 {pipeIndex}의 경로가 재계산되었습니다. 이전: {PathN[pipeIndex].Count}개 포인트, 새 경로: {newPath.Count}개 포인트");
                PathN[pipeIndex] = newPath;
                BendPointsN[pipeIndex] = newBend;
                CoveringListN[pipeIndex] = GetCoveringList(newPath, pipe.Item3, pipe.Item4);
            }
            else
            {
                Debug.LogWarning($"파이프 {pipeIndex}의 경로가 변경되지 않았습니다. 더 넓은 공간이나 다른 전략이 필요할 수 있습니다.");
            }
        }
        
        // 모든 경로의 유효성 검사 및 수정
        private void ValidateAllPaths()
        {
            Debug.Log("모든 경로 유효성 검사 시작");
            
            for (int i = 0; i < NPipes; i++)
            {
                // null 또는 빈 경로 확인
                if (PathN[i] == null || PathN[i].Count == 0)
                {
                    Debug.LogWarning($"파이프 {i}의 경로가 비어 있습니다. 직선 경로로 대체합니다.");
                    
                    var pipe = Pipes[i];
                    PathN[i] = CreateStraightPath(pipe.Item1, pipe.Item2);
                    
                    // 굽힘 경로도 비어있으면 직선 굽힘 경로 생성
                    if (BendPointsN[i] == null || BendPointsN[i].Count == 0)
                    {
                        BendPointsN[i] = CreateStraightBendPath(pipe.Item1, pipe.Item2);
                    }
                    
                    // 커버링 리스트 업데이트
                    CoveringListN[i] = GetCoveringList(PathN[i], pipe.Item3, pipe.Item4);
                }
                else
                {
                    // 시작점과 끝점 확인
                    var startCoord = Pipes[i].Item1.Item1;
                    var endCoord = Pipes[i].Item2.Item1;
                    
                    var firstPathCoord = PathN[i][0].Item1;
                    var lastPathCoord = PathN[i][PathN[i].Count - 1].Item1;
                    
                    // 시작점과 끝점이 연결되어 있는지 확인
                    bool startConnected = firstPathCoord == startCoord;
                    bool endConnected = lastPathCoord == endCoord;
                    
                    if (!startConnected || !endConnected)
                    {
                        Debug.LogWarning($"파이프 {i}의 경로 연결 문제 감지. 경로를 복구합니다.");
                        
                        // 경로 복구 시도
                        var pipe = Pipes[i];
                        var fixedPath = new List<(Vector3, string)>(PathN[i]);
                        
                        // 시작점 연결이 잘못된 경우 수정
                        if (!startConnected)
                        {
                            fixedPath.Insert(0, (startCoord, GetDirectionFromPoints(startCoord, fixedPath[0].Item1)));
                        }
                        
                        // 끝점 연결이 잘못된 경우 수정
                        if (!endConnected)
                        {
                            fixedPath.Add((endCoord, GetDirectionFromPoints(fixedPath[fixedPath.Count - 1].Item1, endCoord)));
                        }
                        
                        PathN[i] = fixedPath;
                        
                        // 굽힘 경로도 수정 필요한 경우
                        if (BendPointsN[i] == null || BendPointsN[i].Count < 2)
                        {
                            BendPointsN[i] = ExtractBendPoints(fixedPath);
                        }
                        
                        // 커버링 리스트 업데이트
                        CoveringListN[i] = GetCoveringList(PathN[i], pipe.Item3, pipe.Item4);
                    }
                }
            }
            
            Debug.Log("모든 경로 유효성 검사 완료");
        }
        
        // 직선 경로 생성
        private List<(Vector3, string)> CreateStraightPath(
            (Vector3, string) start, 
            (Vector3, string) end)
        {
            var path = new List<(Vector3, string)>();
            
            // 시작점 추가
            path.Add(start);
            
            // 직선 경로를 위한 중간 점 계산 (필요시)
            if (start.Item1 != end.Item1)
            {
                // 중간 점 생성 (시작점과 끝점의 중간)
                Vector3 midPoint = new Vector3(
                    (start.Item1.x + end.Item1.x) / 2,
                    (start.Item1.y + end.Item1.y) / 2,
                    (start.Item1.z + end.Item1.z) / 2
                );
                
                string midDirection = GetDirectionFromPoints(start.Item1, end.Item1);
                path.Add((midPoint, midDirection));
            }
            
            // 끝점 추가
            path.Add(end);
            
            Debug.Log($"직선 경로 생성: {path.Count}개 포인트");
            return path;
        }
        
        // 직선 굽힘 경로 생성
        private List<(Vector3, string)> CreateStraightBendPath(
            (Vector3, string) start, 
            (Vector3, string) end)
        {
            var bendPath = new List<(Vector3, string)>();
            
            // 시작점과 끝점 추가
            bendPath.Add(start);
            bendPath.Add(end);
            
            return bendPath;
        }
        
        // 대체 경로 생성 시도
        private List<(Vector3, string)> TryAlternativePath(
            (Vector3, string) start, 
            (Vector3, string) end, 
            float radius, 
            float delta)
        {
            Debug.Log("대체 경로 생성 시도");
            
            // 다양한 전략으로 경로 생성 시도
            // 1. 중간 지점을 다르게 설정하여 우회 경로 시도
            Vector3 startPoint = start.Item1;
            Vector3 endPoint = end.Item1;
            
            // 방향 벡터 계산
            Vector3 direction = endPoint - startPoint;
            
            // 수직 방향 찾기 (X-Z 평면 상에서 수직 방향)
            // 이제 Y축을 높이로 사용하므로, X-Z 평면에서 수직 벡터 생성
            Vector3 perpendicular = new Vector3(-direction.z, 0, direction.x);
            
            // 벡터 정규화
            float length = Mathf.Sqrt(perpendicular.x * perpendicular.x + perpendicular.z * perpendicular.z);
            
            if (length > 0)
            {
                perpendicular /= length;
            }
            
            // 오프셋 거리 설정 (반경의 몇 배)
            float offset = radius * 3;
            
            // 새로운 중간 점 계산 (Y축 높이는 유지)
            Vector3 midPoint = new Vector3(
                (startPoint.x + endPoint.x) / 2 + perpendicular.x * offset,
                (startPoint.y + endPoint.y) / 2,
                (startPoint.z + endPoint.z) / 2 + perpendicular.z * offset
            );
            
            // 새 경로 생성
            var newPath = new List<(Vector3, string)>();
            
            // 시작점 추가
            newPath.Add(start);
            
            // 중간점 방향 계산
            string dirToMid = GetDirectionFromPoints(startPoint, midPoint);
            newPath.Add((midPoint, dirToMid));
            
            // 끝점 방향 계산
            string dirToEnd = GetDirectionFromPoints(midPoint, endPoint);
            newPath.Add((endPoint, dirToEnd));
            
            Debug.Log($"대체 경로 생성 완료: {newPath.Count}개 포인트");
            return newPath;
        }
        
        // 두 점 사이의 방향을 문자열로 반환
        private string GetDirectionFromPoints(Vector3 from, Vector3 to)
        {
            // 두 점 간의 방향 벡터 계산
            Vector3 diff = to - from;
            
            // 가장 큰 차이를 보이는 축의 방향 반환 (Y축을 높이로 처리)
            float absX = Math.Abs(diff.x);
            float absY = Math.Abs(diff.y);
            float absZ = Math.Abs(diff.z);
            
            if (absX > absY && absX > absZ)
            {
                // X축이 주요 방향
                return diff.x > 0 ? "+x" : "-x";
            }
            else if (absY > absX && absY > absZ)
            {
                // Y축이 주요 방향 (높이)
                return diff.y > 0 ? "+y" : "-y";
            }
            else
            {
                // Z축이 주요 방향
                return diff.z > 0 ? "+z" : "-z";
            }
        }
        
        // 경로에서 굽힘 점 추출
        private List<(Vector3, string)> ExtractBendPoints(List<(Vector3, string)> path)
        {
            if (path == null || path.Count < 2)
                return path;
                
            var bendPoints = new List<(Vector3, string)>();
            
            // 시작점 추가
            bendPoints.Add(path[0]);
            
            // 방향이 바뀌는 점 추가
            for (int i = 1; i < path.Count - 1; i++)
            {
                if (path[i].Item2 != path[i-1].Item2)
                {
                    bendPoints.Add(path[i]);
                }
            }
            
            // 끝점 추가
            bendPoints.Add(path[path.Count - 1]);
            
            // 굽힘 점이 너무 적으면 시작/끝점만 유지
            if (bendPoints.Count < 2)
            {
                bendPoints.Clear();
                bendPoints.Add(path[0]);
                bendPoints.Add(path[path.Count - 1]);
            }
            
            return bendPoints;
        }
        
        // 최종 경로의 유효성 검증
        private bool PathsEqual(List<(Vector3, string)> path1, List<(Vector3, string)> path2)
        {
            if (path1 == null || path2 == null)
                return path1 == path2;
            
            if (path1.Count != path2.Count)
                return false;
            
            for (int i = 0; i < path1.Count; i++)
            {
                if (path1[i].Item1 != path2[i].Item1 || path1[i].Item2 != path2[i].Item2)
                    return false;
            }
            
            return true;
        }
        
        // 로깅을 위한 Debug 클래스 추가
        private static class Debug
        {
            public static void Log(string message)
            {
                #if UNITY_EDITOR
                UnityEngine.Debug.Log($"[DecompositionHeuristic] {message}");
                #else
                // 에디터 외에서도 로깅하기 위해 조건 제거
                UnityEngine.Debug.Log($"[DecompositionHeuristic] {message}");
                #endif
            }
            
            public static void LogWarning(string message)
            {
                #if UNITY_EDITOR
                UnityEngine.Debug.LogWarning($"[DecompositionHeuristic] {message}");
                #else
                UnityEngine.Debug.LogWarning($"[DecompositionHeuristic] {message}");
                #endif
            }
        }
        
        // 디버그 시각화를 위한 메서드
        public List<(List<Vector3> pathPoints, List<Vector3> bendPoints, Color color)> GetVisualDebugInfo()
        {
            if (PathN == null || BendPointsN == null) return null;
            
            var result = new List<(List<Vector3> pathPoints, List<Vector3> bendPoints, Color color)>();
            
            // 랜덤 색상 생성 헬퍼 함수
            System.Random random = new System.Random();
            System.Func<Color> getRandomColor = () => {
                return new Color(
                    (float)random.NextDouble(),
                    (float)random.NextDouble(), 
                    (float)random.NextDouble(),
                    1.0f
                );
            };
            
            for (int i = 0; i < NPipes; i++)
            {
                if (PathN[i] == null || PathN[i].Count == 0) continue;
                
                // 경로 포인트와 굽힘 포인트를 Vector3로 변환
                var pathPoints = new List<Vector3>();
                var bendPoints = new List<Vector3>();
                
                foreach (var point in PathN[i])
                {
                    pathPoints.Add(point.Item1);
                }
                
                if (BendPointsN[i] != null)
                {
                    foreach (var point in BendPointsN[i])
                    {
                        bendPoints.Add(point.Item1);
                    }
                }
                
                // 이 파이프에 대한 색상 생성
                Color pipeColor = getRandomColor();
                
                result.Add((pathPoints, bendPoints, pipeColor));
            }
            
            return result;
        }

        // GetNearbyPipes 메서드 구현 - 현재 파이프보다 앞서 생성된 파이프 정보만 제공
        protected override List<PipeInfo> GetNearbyPipes()
        {
            var pipeInfoList = new List<PipeInfo>();
            
            // 현재 파이프 인덱스 가져오기 (reflection 사용)
            var currentPipeIndexField = this.GetType().BaseType.GetField("CurrentPipeIndex", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            int currentPipeIndex = -1;
            if (currentPipeIndexField != null)
            {
                currentPipeIndex = (int)currentPipeIndexField.GetValue(this);
            }
            
            Debug.Log($"GetNearbyPipes 호출: 현재 파이프 인덱스 = {currentPipeIndex}, 총 파이프 수 = {Pipes.Count}");
            
            // 모든 파이프를 고려 (현재 파이프 제외)
            for (int i = 0; i < Pipes.Count; i++)
            {
                // 현재 생성 중인 파이프는 제외
                if (i == currentPipeIndex) continue;
                
                var points = new List<Vector3>();
                float radius = Pipes[i].Item3;
                
                // 이미 경로가 생성된 파이프인 경우 (현재 파이프보다 앞서 생성됨)
                if (i < PathN.Count && PathN[i] != null && PathN[i].Count > 0)
                {
                    // 실제 생성된 경로의 모든 점 사용
                    foreach (var (point, _) in PathN[i])
                    {
                        points.Add(point);
                    }
                    Debug.Log($"기존 경로 파이프 {i}: {points.Count}개 포인트, 반경 {radius}");
                }
                else
                {
                    // 아직 경로가 생성되지 않은 파이프인 경우 시작점과 도착점을 임시 장애물로 처리
                    var criticalPoints = new List<Vector3>
                    {
                        Pipes[i].Item1.Item1, // 시작점
                        Pipes[i].Item2.Item1  // 도착점
                    };
                    
                    // 각 중요 지점(시작점, 도착점)에 대해 Y축 높이까지 장애물 생성
                    foreach (var criticalPoint in criticalPoints)
                    {
                        // Y축 높이까지 전체 영역을 장애물로 생성
                        points.AddRange(CreateVerticalObstacleColumn(criticalPoint, radius));
                    }
                    
                    Debug.Log($"임시 장애물 파이프 {i}: 중요 지점 {criticalPoints.Count}개, Y축 높이까지 장애물 생성, 총 임시 포인트 {points.Count}개, 반경 {radius}");
                }
                
                // PipeInfo 객체 생성 및 추가
                pipeInfoList.Add(new PipeInfo(points, radius, i));
            }
            
            Debug.Log($"총 {pipeInfoList.Count}개의 파이프 정보 반환 (경로 생성된 파이프 + 임시 장애물)");
            return pipeInfoList;
        }
        
        // 특정 점에서 Y축 높이까지 전체 영역을 장애물로 생성
        private List<Vector3> CreateVerticalObstacleColumn(Vector3 centerPoint, float radius)
        {
            var obstaclePoints = new List<Vector3>();
            
            // 파이프 반경의 2배 영역을 장애물로 설정
            float obstacleRadius = radius * 1.05f;
            int gridDensity = Mathf.Max(1, (int)obstacleRadius); // 최소 1, 반경에 비례한 밀도
            
            // 공간 좌표에서 Y축 범위 가져오기
            float minY = SpaceCoords[0][1]; // 최소 Y 좌표
            float maxY = SpaceCoords[1][1]; // 최대 Y 좌표
            
            Debug.Log($"Y축 장애물 생성: 중심점 {centerPoint}, Y 범위 {minY} ~ {maxY}, 반경 {obstacleRadius}");
            
            // Y축 전체 높이에 대해 장애물 생성
            for (float y = minY; y <= maxY; y += 0.5f) // 0.5 간격으로 Y축 전체 커버
            {
                // 각 Y 레벨에서 X-Z 평면에 원형 장애물 영역 생성
                for (int x = -gridDensity; x <= gridDensity; x++)
                {
                    for (int z = -gridDensity; z <= gridDensity; z++)
                    {
                        Vector3 offset = new Vector3(x, 0, z); // Y는 별도 처리
                        float horizontalDistance = offset.magnitude;
                        
                        // 수평 거리가 장애물 반경 내에 있는 점들만 추가
                        if (horizontalDistance <= obstacleRadius)
                        {
                            Vector3 obstaclePoint = new Vector3(
                                centerPoint.x + x,
                                y, // 현재 Y 레벨
                                centerPoint.z + z
                            );
                            obstaclePoints.Add(obstaclePoint);
                        }
                    }
                }
                
                // 추가로 주요 방향에 더 많은 포인트 생성 (각 Y 레벨에서)
                foreach (var dir in Directions)
                {
                    // Y 방향은 이미 전체 높이를 커버하므로 X, Z 방향만 처리
                    if (!dir.Item2.Contains("y"))
                    {
                        for (float dist = 0.5f; dist <= obstacleRadius; dist += 0.5f)
                        {
                            Vector3 directionOffset = new Vector3(dir.Item1.x, 0, dir.Item1.z) * dist;
                            Vector3 directionPoint = new Vector3(
                                centerPoint.x + directionOffset.x,
                                y, // 현재 Y 레벨
                                centerPoint.z + directionOffset.z
                            );
                            obstaclePoints.Add(directionPoint);
                        }
                    }
                }
            }
            
            Debug.Log($"Y축 장애물 컬럼 생성 완료: 중심점 {centerPoint}, Y 범위 {minY}~{maxY}, 수평 반경 {obstacleRadius}, 총 포인트 수 {obstaclePoints.Count}");
            return obstaclePoints;
        }
        
        // 특정 점 주변에 임시 장애물 포인트들을 생성 (기존 메서드 유지)
        private List<Vector3> CreateTemporaryObstaclePoints(Vector3 centerPoint, float radius)
        {
            var obstaclePoints = new List<Vector3>();
            
            // 파이프 반경의 2배 영역을 장애물로 설정
            float obstacleRadius = radius * 2.0f;
            int gridDensity = Mathf.Max(1, (int)obstacleRadius); // 최소 1, 반경에 비례한 밀도
            
            // 중심점 추가
            obstaclePoints.Add(centerPoint);
            
            // 중심점 주변에 격자 형태로 장애물 포인트 생성
            for (int x = -gridDensity; x <= gridDensity; x++)
            {
                for (int y = -gridDensity; y <= gridDensity; y++)
                {
                    for (int z = -gridDensity; z <= gridDensity; z++)
                    {
                        Vector3 offset = new Vector3(x, y, z);
                        float distance = offset.magnitude;
                        
                        // 장애물 반경 내에 있는 점들만 추가
                        if (distance <= obstacleRadius)
                        {
                            Vector3 obstaclePoint = centerPoint + offset;
                            obstaclePoints.Add(obstaclePoint);
                        }
                    }
                }
            }
            
            // 추가로 주요 방향에 더 많은 포인트 생성 (더 강한 회피 효과)
            foreach (var dir in Directions)
            {
                for (float dist = 0.5f; dist <= obstacleRadius; dist += 0.5f)
                {
                    Vector3 directionPoint = centerPoint + dir.Item1 * dist;
                    obstaclePoints.Add(directionPoint);
                }
            }
            
            Debug.Log($"임시 장애물 생성: 중심점 {centerPoint}, 반경 {obstacleRadius}, 포인트 수 {obstaclePoints.Count}");
            return obstaclePoints;
        }
    }
} 