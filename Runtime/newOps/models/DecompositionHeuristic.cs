using System;
using System.Collections.Generic;
using System.Linq;
using Utils;
using UnityEngine;

namespace Model
{
    public class DecompositionHeuristic : AStar
    {
        public int MaxIt;
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
        ) : base(spaceCoords, obstacleCoords, wPath, wBend, wEnergy, minDisBend)
        {
            MaxIt = maxit;
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

        // float[] 배열을 Vector3로 변환하는 헬퍼 메서드 (AStar에서 상속받았을 수 있으나 명시적으로 재추가)
        private Vector3 FloatArrayToVector3(float[] array)
        {
            if (array == null || array.Length < 3)
            {
                return Vector3.zero;
            }
            // X, Y, Z 순서를 유지하되, 2D인 경우 Z 좌표 사용
            return new Vector3(array[0], array.Length > 2 ? array[1] : 0, array.Length > 2 ? array[2] : array[1]);
        }

        // Vector3를 float[] 배열로 변환하는 헬퍼 메서드 (AStar에서 상속받았을 수 있으나 명시적으로 재추가)
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

        // 메인 실행 
        public (List<List<(Vector3, string)>> pathN, List<List<(Vector3, string)>> bendPointsN) MainRun()
        {
            PathN = new List<List<(Vector3, string)>>();
            BendPointsN = new List<List<(Vector3, string)>>();
            CoveringListN = new List<HashSet<(string, string)>>();
            
            // 첫 번째 단계: 모든 파이프에 대한 초기 경로 생성
            for (int k = 0; k < NPipes; k++)
            {
                var pipe = Pipes[k];
                var (bend, path) = Run(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
                
                // 경로 생성 실패 시 기본 직선 경로 생성
                if (path == null || path.Count == 0)
                {
                    Debug.LogWarning($"파이프 {k}의 초기 경로 생성 실패. 직선 경로로 대체합니다.");
                    path = CreateStraightPath(pipe.Item1, pipe.Item2);
                    bend = CreateStraightBendPath(pipe.Item1, pipe.Item2);
                }
                
                Debug.Log($"초기 경로 {k}: {path.Count}개 포인트");
                PathN.Add(path);
                BendPointsN.Add(bend);
                CoveringListN.Add(GetCoveringList(path, pipe.Item3, pipe.Item4));
            }
            
            // 초기 경로 유효성 검증
            ValidateAllPaths();
            
            // 충돌 검사 및 비용 업데이트 구현
            int maxCollisionResolveAttempts = 25; // 최대 충돌 해결 시도 횟수
            bool hasCollisions = true;
            int attemptCount = 0;
            
            while (hasCollisions && attemptCount < maxCollisionResolveAttempts)
            {
                attemptCount++;
                
                // 모든 파이프 쌍에 대해 충돌 검사
                // 충돌 정보를 상세히 저장하는 구조 추가: (파이프1, 파이프2, 충돌 엣지 수, 충돌 심각도)
                var collisionDetails = new List<(int pipe1, int pipe2, int edgeCount, float severity)>();
                
                // 공간 분할을 위한 간단한 그리드 기반 접근
                var pipeGrids = new Dictionary<string, List<int>>();
                
                Debug.Log("NPipes  " + NPipes.ToString());
                // 각 파이프를 대략적인 공간 그리드에 할당
                for (int i = 0; i < NPipes; i++)
                {
                    if (PathN[i] == null || PathN[i].Count == 0) continue;
                    // 파이프 경로의 시작점과 끝점으로 대략적인 바운딩 박스 생성
                    var startPoint = PathN[i][0].Item1;
                    var endPoint = PathN[i][PathN[i].Count - 1].Item1;
                    
                    // 그리드 크기 (경로 길이에 따라 조정 가능)
                    int gridSize = 1;
                    
                    // 시작점과 끝점을 포함하는 그리드 셀 계산
                    int startX = (int)(startPoint.x / gridSize);
                    int startY = (int)(startPoint.y / gridSize);
                    int startZ = startPoint.z > 0 ? (int)(startPoint.z / gridSize) : 0;
                    
                    int endX = (int)(endPoint.x / gridSize);
                    int endY = (int)(endPoint.y / gridSize);
                    int endZ = endPoint.z > 0 ? (int)(endPoint.z / gridSize) : 0;
                    
                    // 두 점 사이의 모든 그리드 셀 계산 (간단한 방식으로 직선 보간)
                    int minX = Math.Min(startX, endX);
                    int maxX = Math.Max(startX, endX);
                    int minY = Math.Min(startY, endY);
                    int maxY = Math.Max(startY, endY);
                    int minZ = Math.Min(startZ, endZ);
                    int maxZ = Math.Max(startZ, endZ);
                    
                    // 파이프를 포함하는 모든 그리드 셀에 파이프 인덱스 추가
                    for (int x = minX; x <= maxX; x++)
                    for (int y = minY; y <= maxY; y++)
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        string gridKey = $"{x},{y},{z}";
                        if (!pipeGrids.ContainsKey(gridKey))
                            pipeGrids[gridKey] = new List<int>();
                        pipeGrids[gridKey].Add(i);
                    }
                }
                
                // 동일한 그리드 셀에 있는 파이프들 간의 충돌만 검사
                var checkedPairs = new HashSet<string>();
                
                foreach (var grid in pipeGrids)
                {
                    
                    var pipesInGrid = grid.Value;
                    
                    // 이 그리드에 있는 파이프 쌍 검사
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
                            
                            // 두 경로 간 충돌 엣지 찾기
                            var conflictEdges = FindConflictEdges(PathN[i], PathN[j]);
                            
                            // 충돌이 있으면 상세 정보 기록
                            if (conflictEdges.Count > 0)
                            {
                                // 충돌 심각도 계산: 충돌 엣지 수와 파이프 두께에 기반
                                float thickness1 = Pipes[i].Item3; // 파이프1 두께(반경)
                                float thickness2 = Pipes[j].Item3; // 파이프2 두께(반경)
                                
                                // 심각도 = 충돌 엣지 수 * (두 파이프 두께의 합)
                                float severity = conflictEdges.Count * (thickness1 + thickness2);
                                
                                collisionDetails.Add((i, j, conflictEdges.Count, severity));
                                Debug.Log($"파이프 {i}와 {j} 사이에 {conflictEdges.Count}개의 충돌 감지 (심각도: {severity:F2})");
                            }
                        }
                    }
                }
                
                // 심각도에 따라 충돌 정렬 (더 심각한 충돌 먼저 해결)
                collisionDetails.Sort((a, b) => b.severity.CompareTo(a.severity));
                
                // 정렬된 충돌 목록에서 파이프 쌍 추출
                var collisions = collisionDetails.Select(c => (c.pipe1, c.pipe2)).ToList();
                
                // 심각도 기준 충돌 요약 출력
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
                
                // 충돌하는 파이프를 재계산하기 위해 엣지 비용 업데이트
                foreach (var (i, j) in collisions)
                {
                    // 충돌 패널티를 적용할 파이프 선택
                    // 두 파이프 중 더 짧은 경로를 가진 파이프를 선택해 재계산
                    int pipeToUpdate = (PathN[i].Count <= PathN[j].Count) ? i : j;
                    
                    // 충돌 영역에 패널티 적용
                    UpdateEdgeCostForCollision(pipeToUpdate, PathN[i], PathN[j]);
                    
                    // 업데이트된 비용으로 경로 재계산
                    var pipe = Pipes[pipeToUpdate];
                    var (newBend, newPath) = Run(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
                    
                    // 경로 생성 실패 시 대체 경로 생성
                    if (newPath == null || newPath.Count == 0)
                    {
                        Debug.LogWarning($"파이프 {pipeToUpdate}의 경로 재계산 실패. 대체 경로를 시도합니다.");
                        newPath = TryAlternativePath(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
                        newBend = CreateStraightBendPath(pipe.Item1, pipe.Item2);
                    }
                    
                    // 이전 경로와 새 경로가 동일한지 검사
                    bool pathChanged = !PathsEqual(PathN[pipeToUpdate], newPath);
                    
                    if (pathChanged)
                    {
                        Debug.Log($"파이프 {pipeToUpdate}의 경로가 재계산되었습니다. 이전: {PathN[pipeToUpdate].Count}개 포인트, 새 경로: {newPath.Count}개 포인트");
                        PathN[pipeToUpdate] = newPath;
                        BendPointsN[pipeToUpdate] = newBend;
                        CoveringListN[pipeToUpdate] = GetCoveringList(newPath, pipe.Item3, pipe.Item4);
                    }
                    else
                    {
                        Debug.Log($"파이프 {pipeToUpdate}의 경로가 변경되지 않았습니다. 추가 패널티 적용");
                        
                        // 경로가 변경되지 않은 경우 더 강력한 패널티 적용
                        ApplyStrongerPenalty(pipeToUpdate, PathN[i], PathN[j]);
                        
                        // 다시 경로 계산 시도
                        (newBend, newPath) = Run(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
                        
                        // 다시 경로 변경 확인
                        bool pathChangedRetry = !PathsEqual(PathN[pipeToUpdate], newPath);
                        
                        if (pathChangedRetry)
                        {
                            Debug.Log($"강력한 패널티 적용 후 파이프 {pipeToUpdate}의 경로가 재계산되었습니다.");
                            PathN[pipeToUpdate] = newPath;
                            BendPointsN[pipeToUpdate] = newBend;
                            CoveringListN[pipeToUpdate] = GetCoveringList(newPath, pipe.Item3, pipe.Item4);
                        }
                        else
                        {
                            // 다른 파이프 시도
                            int otherPipe = (pipeToUpdate == i) ? j : i;
                            Debug.Log($"다른 파이프 {otherPipe} 재계산 시도");
                            
                            UpdateEdgeCostForCollision(otherPipe, PathN[i], PathN[j]);
                            pipe = Pipes[otherPipe];
                            (newBend, newPath) = Run(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
                            
                            pathChanged = !PathsEqual(PathN[otherPipe], newPath);
                            
                            if (pathChanged)
                            {
                                Debug.Log($"파이프 {otherPipe}의 경로가 재계산되었습니다.");
                                PathN[otherPipe] = newPath;
                                BendPointsN[otherPipe] = newBend;
                                CoveringListN[otherPipe] = GetCoveringList(newPath, pipe.Item3, pipe.Item4);
                            }
                        }
                    }
                }
                
                // 매 반복 후 변경된 경로에 대해 충돌 검사를 다시 수행할 필요가 있는지 확인
                // 이 부분은 다음 루프 반복에서 자동으로 처리됨
            }
            
            if (attemptCount >= maxCollisionResolveAttempts && hasCollisions)
            {
                Debug.LogWarning($"최대 시도 횟수({maxCollisionResolveAttempts})에 도달했습니다. 모든 충돌이 해결되지 않았을 수 있습니다.");
            }
            
            // 최종 검증: 모든 파이프의 최종 경로 유효성 확인
            ValidateAllPaths();
            
            return (PathN, BendPointsN);
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
        
        // 최종 경로의 유효성 검증
        private void ApplyStrongerPenalty(int pipeIndex, List<(Vector3, string)> path1, List<(Vector3, string)> path2)
        {
            // 두 경로 간 충돌 엣지 찾기
            var conflictEdges = FindConflictEdges(path1, path2);
            
            // 충돌하는 각 엣지에 대해 비용 크게 증가
            foreach (var (vertex, direction) in conflictEdges)
            {
                string key = $"{vertex}:{direction}";
                
                // 기존 비용에서 패널티 추가 (기존 비용이 없으면 기본값 사용)
                float currentCost = EdgeCost.ContainsKey(key) ? EdgeCost[key] : 1.0f;
                // 기존 2배에서 10배로 증가
                EdgeCost[key] = currentCost * 10.0f;
                
                Debug.Log($"강력한 패널티: 파이프 {pipeIndex}의 엣지 {key}에 대한 비용이 {currentCost}에서 {EdgeCost[key]}로 크게 증가");
            }
            
            // 충돌 영역 주변에도 패널티 적용
            ApplyPenaltyToSurroundingArea(conflictEdges);
        }
        
        // 충돌 영역 주변에도 패널티 적용
        private void ApplyPenaltyToSurroundingArea(List<(string, string)> conflictEdges)
        {
            // 충돌 엣지 주변 영역에도 추가적인 패널티 적용
            HashSet<string> vertices = new HashSet<string>();
            
            // 먼저 충돌 엣지에서 정점 추출
            foreach (var (vertex, _) in conflictEdges)
            {
                vertices.Add(vertex);
            }
            
            // 주변 정점에 대해 패널티 적용
            foreach (string vertex in vertices)
            {
                // 모든 방향에 대해 패널티 적용
                foreach (var dir in Directions)
                {
                    string key = $"{vertex}:{dir.Item2}";
                    
                    if (EdgeCost.ContainsKey(key))
                    {
                        float currentCost = EdgeCost[key];
                        EdgeCost[key] = currentCost * 2.0f;
                        Debug.Log($"주변 엣지 패널티: {key}에 대한 비용이 {currentCost}에서 {EdgeCost[key]}로 증가");
                    }
                }
            }
        }
        
        // 충돌에 기반하여 엣지 비용 업데이트
        private void UpdateEdgeCostForCollision(int pipeIndex, List<(Vector3, string)> path1, List<(Vector3, string)> path2)
        {
            // 두 경로 간 충돌 엣지 찾기
            var conflictEdges = FindConflictEdges(path1, path2);
            
            // 충돌하는 각 엣지에 대해 비용 증가
            foreach (var (vertex, direction) in conflictEdges)
            {
                string key = $"{vertex}:{direction}";
                
                // 기존 비용에서 패널티 추가 (기존 비용이 없으면 기본값 사용)
                float currentCost = EdgeCost.ContainsKey(key) ? EdgeCost[key] : 1.0f;
                // 비용을 2배에서 5배로 증가
                EdgeCost[key] = currentCost * 5.0f;
                
                Debug.Log($"파이프 {pipeIndex}의 엣지 {key}에 대한 비용이 {currentCost}에서 {EdgeCost[key]}로 증가");
            }
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
    }
} 