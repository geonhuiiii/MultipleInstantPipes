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
        
        // 새로 추가된 필드
        public List<string> IndexCategory;
        public List<List<List<(string, string)>>> CovConflict;
        public float[,] CovConflictNum;
        public HashSet<(string, string)> CovConflictSet;

        public DecompositionHeuristic(
            int maxit,
            float[][] spaceCoords,
            List<float[][]> obstacleCoords,
            List<( (float[], string), (float[], string), float, float )> pipes,
            float wPath, float wBend, float wEnergy, int minDisBend,
            List<string> indexCategory = null
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
            
            // index_category 초기화 - Python 코드와 동일하게 설정
            if (indexCategory == null)
            {
                IndexCategory = new List<string>();
                int parCount = (int)(0.2f * maxit);
                int clusterCount = (int)(0.5f * maxit);
                int seqCount = maxit - parCount - clusterCount;
                
                // I_par: 20%, I_cluster: 50%, I_seq: 30%
                for (int i = 0; i < parCount; i++)
                    IndexCategory.Add("I_par");
                for (int i = 0; i < clusterCount; i++)
                    IndexCategory.Add("I_cluster");
                for (int i = 0; i < seqCount; i++)
                    IndexCategory.Add("I_seq");
            }
            else
            {
                IndexCategory = indexCategory;
            }
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

        // MainRun 메서드 수정 - Python 코드의 main_run 구현
        public (List<List<(Vector3, string)>> pathN, List<List<(Vector3, string)>> bendPointsN) MainRun()
        {
            int stop = 0;
            int it = 0;
            var Kit = new List<((Vector3, string), (Vector3, string), float, float)>(Pipes);
            var Kbar = new List<((Vector3, string), (Vector3, string), float, float)>(Pipes);
            
            PathN = new List<List<(Vector3, string)>>();
            BendPointsN = new List<List<(Vector3, string)>>();
            CoveringListN = new List<HashSet<(string, string)>>();
            
            // 중첩 리스트 초기화
            for (int i = 0; i < NPipes; i++)
            {
                PathN.Add(new List<(Vector3, string)>());
                BendPointsN.Add(new List<(Vector3, string)>());
                CoveringListN.Add(new HashSet<(string, string)>());
            }
            
            List<List<(Vector3, string)>> bendPointsNInit = null;
            
            // 충돌 정보를 위한 리스트 초기화
            CovConflict = new List<List<List<(string, string)>>>();
            for (int i = 0; i < NPipes; i++)
            {
                CovConflict.Add(new List<List<(string, string)>>());
                for (int j = 0; j < NPipes; j++)
                {
                    CovConflict[i].Add(new List<(string, string)>());
                }
            }
            
            Debug.Log($"MainRun 시작: 최대 반복 횟수 {MaxIt}, 파이프 수 {NPipes}");
            
            while (stop == 0 && it < MaxIt)
            {
                Debug.Log($"반복 {it}: 카테고리 {IndexCategory[it]}");
                
                // I_seq 모드에서는 모든 파이프를 다시 처리
                if (IndexCategory[it] == "I_seq")
                {
                    Kit = new List<((Vector3, string), (Vector3, string), float, float)>(Pipes);
                    Kbar = new List<((Vector3, string), (Vector3, string), float, float)>(Pipes);
                }
                
                // Kit의 각 파이프에 대해 경로 계산
                foreach (var pipeK in Kit)
                {
                    int k = GetPipeIndex(pipeK);
                    Debug.Log($"파이프 {k} 처리 중");
                    
                    // 파이프 경로 계산
                    var (bendPointsK, pathK) = Run(pipeK.Item1, pipeK.Item2, pipeK.Item3, pipeK.Item4);
                    
                    // 탐색 설정 초기화
                    ReInit();
                    
                    // 계산된 경로 저장
                    PathN[k] = pathK;
                    BendPointsN[k] = bendPointsK;
                    CoveringListN[k] = GetCoveringList(pathK, pipeK.Item3, pipeK.Item4);
                    
                    // I_seq 모드에서는 순차적으로 엣지 비용 업데이트
                    if (IndexCategory[it] == "I_seq")
                    {
                        Kbar.Remove(pipeK);
                        foreach (var item in Kbar)
                        {
                            int indexItem = GetPipeIndex(item);
                            if (CoveringListN[indexItem].Count > 0)
                            {
                                var edges = CoveringListN[k].Intersect(CoveringListN[indexItem]).ToList();
                                UpdateCost(edges, 10.0f);
                            }
                        }
                    }
                }
                
                // 초기 굽힘 포인트 저장 (첫 반복에서만)
                if (it == 0)
                {
                    bendPointsNInit = new List<List<(Vector3, string)>>(BendPointsN);
                }
                
                // 충돌 검사
                for (int i = 0; i < NPipes; i++)
                {
                    for (int j = 0; j < NPipes; j++)
                    {
                        CovConflict[i][j].Clear();
                    }
                }
                
                foreach (var pipeK in Kit)
                {
                    int k = GetPipeIndex(pipeK);
                    foreach (var pipeKPrime in Pipes)
                    {
                        int kPrime = GetPipeIndex(pipeKPrime);
                        if (k != kPrime)
                        {
                            CovConflict[k][kPrime] = CoveringListN[k].Intersect(CoveringListN[kPrime]).ToList();
                        }
                    }
                }
                
                // 충돌 수치화
                CovConflictNum = new float[NPipes, NPipes];
                for (int i = 0; i < NPipes; i++)
                {
                    for (int j = 0; j < NPipes; j++)
                    {
                        CovConflictNum[i, j] = CovConflict[i][j].Count;
                    }
                }
                
                string conflictDebug = "충돌 매트릭스: \n";
                for (int i = 0; i < NPipes; i++)
                {
                    for (int j = 0; j < NPipes; j++)
                    {
                        conflictDebug += $"{CovConflictNum[i, j] / 6.0f:F2} ";
                    }
                    conflictDebug += "\n";
                }
                Debug.Log(conflictDebug);
                
                // 충돌 여부에 따라 전략 적용
                if (!IsEmptyNestedList(CovConflict))
                {
                    stop = 0;
                    
                    // I_par 모드: 병렬 전략 (모든 충돌 엣지에 비용 적용)
                    if (IndexCategory[it] == "I_par")
                    {
                        CovConflictSet = new HashSet<(string, string)>();
                        for (int k = 0; k < CovConflict.Count; k++)
                        {
                            for (int l = 0; l < CovConflict[k].Count; l++)
                            {
                                foreach (var item in CovConflict[k][l])
                                {
                                    CovConflictSet.Add(item);
                                }
                            }
                        }
                        UpdateCost(CovConflictSet.ToList(), 30.0f);
                        Debug.Log($"I_par 모드: {CovConflictSet.Count}개 충돌 엣지에 비용 30 적용");
                    }
                    
                    // I_cluster 모드: 클러스터 전략 (우선순위에 따라 파이프 선택)
                    if (IndexCategory[it] == "I_cluster")
                    {
                        var KitNext = new HashSet<((Vector3, string), (Vector3, string), float, float)>();
                        foreach (var pipeI in Kit)
                        {
                            int i = GetPipeIndex(pipeI);
                            foreach (var pipeJ in Pipes)
                            {
                                int j = GetPipeIndex(pipeJ);
                                if (CovConflict[i][j].Count > 0)
                                {
                                    if (CmpPriority(pipeI, pipeJ))
                                    {
                                        KitNext.Add(pipeJ);
                                    }
                                    else
                                    {
                                        KitNext.Add(pipeI);
                                    }
                                }
                            }
                        }
                        
                        var conflictEdges = new HashSet<(string, string)>();
                        foreach (var pipeK in KitNext)
                        {
                            int k = GetPipeIndex(pipeK);
                            foreach (var pipeKPrime in Pipes)
                            {
                                int kPrime = GetPipeIndex(pipeKPrime);
                                if (!KitNext.Contains(pipeKPrime) && CovConflict[k][kPrime].Count > 0)
                                {
                                    foreach (var item in CovConflict[k][kPrime])
                                    {
                                        conflictEdges.Add(item);
                                    }
                                }
                            }
                        }
                        
                        UpdateCost(conflictEdges.ToList(), 100.0f);
                        Debug.Log($"I_cluster 모드: {conflictEdges.Count}개 충돌 엣지에 비용 100 적용");
                        
                        // 다음 반복을 위해 Kit 업데이트
                        Kit = KitNext.ToList();
                    }
                    
                    it++;
                }
                else
                {
                    stop = 1;
                    Debug.Log("충돌이 모두 해결되었습니다.");
                }
            }
            
            if (it >= MaxIt)
            {
                Debug.Log($"최대 반복 횟수({MaxIt})에 도달하여 종료합니다.");
            }
            
            return (PathN, bendPointsNInit != null ? bendPointsNInit : BendPointsN);
        }
        
        // 중첩 리스트가 비어있는지 확인
        private bool IsEmptyNestedList(List<List<List<(string, string)>>> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = 0; j < list[i].Count; j++)
                {
                    if (list[i][j].Count > 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // 엣지 리셋 (Python의 reinit 구현)
        private void ReInit()
        {
            // EdgeCost 딕셔너리 리셋
            EdgeCost.Clear();
        }
        
        // 두 경로 간 최소 거리 계산 메서드
        private float CalculateMinDistanceBetweenPaths(List<(Vector3, string)> path1, List<(Vector3, string)> path2)
        {
            if (path1 == null || path1.Count == 0 || path2 == null || path2.Count == 0)
                return float.MaxValue;
                
            float minDistance = float.MaxValue;
            
            // 두 경로의 모든 점 쌍에 대해 거리 계산
            for (int i = 0; i < path1.Count; i++)
            {
                for (int j = 0; j < path2.Count; j++)
                {
                    float distance = Vector3.Distance(path1[i].Item1, path2[j].Item1);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                    }
                }
            }
            
            return minDistance;
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
                float proximityThreshold = (newPipeRadius + existingPipeRadius) * 3.0f;
                
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
            float searchRadius = pipeRadius * 3.0f;
            
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
            
            // 공간을 더 넓혀서 탐색 (높이 제한 완화)
            // AStar 알고리즘의 IsEnoughSpace 메서드가 더 넓은 공간을 탐색하도록 임시 설정
            var (newBend, newPath) = Run(pipe.Item1, pipe.Item2, pipe.Item3, pipe.Item4);
            
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

        // GetNearbyPipes 메서드 구현 - 기존 파이프 정보 제공
        protected override List<PipeInfo> GetNearbyPipes()
        {
            var pipeInfoList = new List<PipeInfo>();
            
            // 모든 생성된 파이프의 정보 수집
            for (int i = 0; i < PathN.Count; i++)
            {
                // 빈 경로는 건너뛰기
                if (PathN[i] == null || PathN[i].Count == 0) continue;
                
                // 경로의 모든 점을 Vector3 리스트로 변환
                var points = new List<Vector3>();
                foreach (var (point, _) in PathN[i])
                {
                    points.Add(point);
                }
                
                // 해당 파이프의 반경 가져오기
                float radius = Pipes[i].Item3;
                
                // PipeInfo 객체 생성 및 추가
                pipeInfoList.Add(new PipeInfo(points, radius));
                
                Debug.Log($"파이프 정보 추가: 인덱스 {i}, 포인트 수 {points.Count}, 반경 {radius}");
            }
            
            return pipeInfoList;
        }

        // 두 파이프의 우선순위 비교 메서드 (Python의 cmp_priority 구현)
        public bool CmpPriority(((Vector3, string), (Vector3, string), float, float) pipeI, 
                               ((Vector3, string), (Vector3, string), float, float) pipeJ)
        {
            float sumRadiusI = pipeI.Item3 + pipeI.Item4;
            float sumRadiusJ = pipeJ.Item3 + pipeJ.Item4;
            
            if (sumRadiusI > sumRadiusJ)
            {
                return true;
            }
            else if (Math.Abs(sumRadiusI - sumRadiusJ) < 0.001f) // 부동소수점 비교
            {
                return GetPipeIndex(pipeI) > GetPipeIndex(pipeJ);
            }
            else
            {
                return false;
            }
        }
        
        // 파이프 인덱스 찾기 (Python의 get_pipe_index 구현)
        public int GetPipeIndex(((Vector3, string), (Vector3, string), float, float) pipe)
        {
            return Pipes.FindIndex(p => 
                p.Item1.Item1 == pipe.Item1.Item1 && 
                p.Item1.Item2 == pipe.Item1.Item2 && 
                p.Item2.Item1 == pipe.Item2.Item1 && 
                p.Item2.Item2 == pipe.Item2.Item2 && 
                Math.Abs(p.Item3 - pipe.Item3) < 0.001f && 
                Math.Abs(p.Item4 - pipe.Item4) < 0.001f);
        }
        
        // 엣지 비용 업데이트 (Python의 update_cost 구현)
        public void UpdateCost(List<(string, string)> edges, float changeCost)
        {
            foreach (var edge in edges)
            {
                string key = $"{edge.Item1}:{edge.Item2}";
                
                if (EdgeCost.ContainsKey(key))
                {
                    EdgeCost[key] += changeCost;
                }
                else
                {
                    EdgeCost[key] = 1.0f + changeCost;
                }
            }
        }

        // 중첩 리스트가 비어있는지 확인 (Python의 is_empty_list 구현)
        public bool IsEmptyList<T>(List<List<T>> list)
        {
            foreach (var sublist in list)
            {
                if (sublist != null && sublist.Count > 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
} 