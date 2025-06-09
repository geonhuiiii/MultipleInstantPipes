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

        // 새로 추가되는 필드들
        public List<string> IndexCategory;
        public List<( (float[], string), (float[], string), float, float )> K0;
        public List<List<List<(string, string)>>> CovConflict;

        public DecompositionHeuristic(
            int maxit,
            float gridSize,
            float[][] spaceCoords,
            List<float[][]> obstacleCoords,
            List<( (float[], string), (float[], string), float, float )> pipes,
            float wPath, float wBend, float wEnergy, int minDisBend,
            List<string> indexCategory = null
        ) : base(maxit, gridSize, spaceCoords, obstacleCoords, wPath, wBend, wEnergy, minDisBend)
        {
            this.NPipes = pipes.Count;
            
            if (indexCategory == null)
            {
                this.IndexCategory = new List<string>();
                
                // I_par 20% 
                int iParCount = (int)(0.2 * maxit);
                for (int i = 0; i < iParCount; i++)
                {
                    this.IndexCategory.Add("I_par");
                }
                
                // I_cluster 50%
                int iClusterCount = (int)(0.5 * maxit);
                for (int i = 0; i < iClusterCount; i++)
                {
                    this.IndexCategory.Add("I_cluster");
                }
                
                // I_seq 나머지
                int iSeqCount = maxit - iParCount - iClusterCount;
                for (int i = 0; i < iSeqCount; i++)
                {
                    this.IndexCategory.Add("I_seq");
                }
            }
            else
            {
                this.IndexCategory = new List<string>(indexCategory);
            }
            
            // pipes 복사
            this.K0 = new List<((float[], string), (float[], string), float, float)>(pipes);
            
            // Pipes를 Vector3 형태로 변환하여 저장
            this.Pipes = new List<((Vector3, string), (Vector3, string), float, float)>();
            foreach (var pipe in pipes)
            {
                var start = (FloatArrayToVector3(pipe.Item1.Item1), pipe.Item1.Item2);
                var end = (FloatArrayToVector3(pipe.Item2.Item1), pipe.Item2.Item2);
                this.Pipes.Add((start, end, pipe.Item3, pipe.Item4));
            }
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
            // 경로의 점들을 추출
            var Pk = new List<Vector3>();
            foreach (var item in path)
            {
                Pk.Add(item.Item1);
            }
            
            // Lk를 Pk로 초기화
            var Lk = new List<Vector3>(Pk);
            
            // 모든 점들에 대해 인접한 점들 탐색
            foreach (var v0 in Pk)
            {
                foreach (var v in Lk.ToList())
                {
                    foreach (var direction in Directions)
                    {
                        // 인접한 정점 계산
                        Vector3 vPrime = v + direction.Item1;
                        
                        // 유효한 점이고 최대 거리가 radius + delta 이하이며 아직 Lk에 없는 경우 추가
                        if (is_in_open_set(vPrime) && 
                            AStar.get_max_distance(v0, vPrime) <= radius + delta && 
                            !Lk.Any(x => VectorsEqual(x, vPrime)))
                        {
                            Lk.Add(vPrime);
                        }
                    }
                }
            }
            
            // 모든 점과 방향의 조합 생성
            var result = new HashSet<(string, string)>();
            foreach (var point in Lk)
            {
                foreach (var direction in Directions)
                {
                    result.Add(($"{point.x},{point.y},{point.z}", direction.Item2));
                }
            }
            
            return result;
        }

        // 두 경로의 커버링 리스트 교집합(충돌 엣지)
        public List<(string, string)> FindConflictEdges(List<(Vector3, string)> path1, List<(Vector3, string)> path2)
        {
            var cov1 = GetCoveringList(path1);
            var cov2 = GetCoveringList(path2);
            return cov1.Intersect(cov2).ToList();
        }
        // 리스트가 비어있는지 확인
        private bool IsEmptyList<T>(List<List<T>> lst)
        {
            foreach (var sublist in lst)
            {
                if (sublist != null && sublist.Any())
                {
                    return false;
                }
            }
            return true;
        }

        // K0에서 파이프의 인덱스 가져오기
        private int GetPipeIndex(((Vector3, string), (Vector3, string), float, float) pipe)
        {
            return Pipes.IndexOf(pipe);
        }
                
        // 엣지 비용 업데이트
        private void UpdateCost(List<(string, string)> edges, float changeCost)
        {
            foreach (var edge in edges)
            {
                string edgeKey = $"{edge.Item1}:{edge.Item2}";
                if (EdgeCost.ContainsKey(edgeKey))
                {
                    EdgeCost[edgeKey] += changeCost;
                }
            }
        }

        // 두 파이프의 우선순위 비교
        private bool ComparePriority(
            ((Vector3, string), (Vector3, string), float, float) pipeI,
            ((Vector3, string), (Vector3, string), float, float) pipeJ)
        {
            float sumI = pipeI.Item3 + pipeI.Item4;
            float sumJ = pipeJ.Item3 + pipeJ.Item4;

            if (sumI > sumJ)
            {
                return true;
            }
            else if (Mathf.Approximately(sumI, sumJ))
            {
                return GetPipeIndex(pipeI) > GetPipeIndex(pipeJ);
            }
            else
            {
                return false;
            }
        }
        // 파이프 엘보우 테스트 메서드
        private (List<(Vector3, string)>, List<(Vector3, string)>) PipeElbowTest(((Vector3, string), (Vector3, string), float, float) pipeK)
        {
            var (start, end, radius, delta) = pipeK;
            var Pk = new List<(Vector3, string)>();
            var Bk = new List<(Vector3, string)>();
            int test = 1;
            int it = 0;
            int maxIt = 10;

            while (test == 1 && it < maxIt)
            {
                var (bendPointsK, pathK) = Run(start, end, radius, delta);
                
                // 첫 번째 반복이거나 이전 결과가 비어있으면 전체 결과 사용
                if (it == 0 || Bk.Count == 0)
                {
                    Bk = bendPointsK?.ToList() ?? new List<(Vector3, string)>();
                    Pk = pathK?.ToList() ?? new List<(Vector3, string)>();
                }
                else
                {
                    // 이후 반복에서는 기존 결과에 새로운 세그먼트 추가
                    if (bendPointsK != null && bendPointsK.Count > 0)
                    {
                        // 중복 제거하고 새로운 bend points 추가
                        var lastBendPoint = Bk.LastOrDefault();
                        foreach (var newBend in bendPointsK)
                        {
                            if (!Bk.Any(b => VectorsEqual(b.Item1, newBend.Item1)))
                            {
                                // 이전 bend point와의 거리 확인
                                if (lastBendPoint.Item1 != null)
                                {
                                    float distance = Vector3.Distance(lastBendPoint.Item1, newBend.Item1);
                                    if (distance >= MinDisBend)
                                    {
                                        Bk.Add(newBend);
                                        lastBendPoint = newBend;
                                    }
                                }
                                else
                                {
                                    Bk.Add(newBend);
                                    lastBendPoint = newBend;
                                }
                            }
                        }
                    }
                    
                    if (pathK != null && pathK.Count > 0)
                    {
                        // 중복 제거하고 새로운 path points 추가
                        var lastPathPoint = Pk.LastOrDefault();
                        foreach (var newPath in pathK)
                        {
                            if (!Pk.Any(p => VectorsEqual(p.Item1, newPath.Item1)))
                            {
                                Pk.Add(newPath);
                            }
                        }
                    }
                }

                // bend points 간 거리와 각도 검증
                bool allDistancesValid = true;
                if (Bk.Count > 1)
                {
                    for (int i = 1; i < Bk.Count; i++)
                    {
                        float distance = Vector3.Distance(Bk[i - 1].Item1, Bk[i].Item1);
                        if (distance < MinDisBend)
                        {
                            allDistancesValid = false;
                            
                            // 거리가 부족한 지점에서 경로 재계산
                            // 문제가 되는 bend point까지만 유지
                            Bk = Bk.Take(i).ToList();
                            
                            // path에서도 해당 지점까지만 유지
                            if (i < Bk.Count && Bk.Count > 0)
                            {
                                var targetPoint = Bk[i - 1].Item1;
                                int pathIndex = Pk.FindIndex(p => VectorsEqual(p.Item1, targetPoint));
                                if (pathIndex >= 0)
                                {
                                    Pk = Pk.Take(pathIndex + 1).ToList();
                                }
                            }
                            
                            // 해당 방향의 비용 증가
                            if (bendPointsK != null && i < bendPointsK.Count)
                            {
                                var problematicPoint = bendPointsK[i];
                                List<(string, string)> edges = new List<(string, string)>();
                                
                                string coordKey = $"{problematicPoint.Item1.x},{problematicPoint.Item1.y},{problematicPoint.Item1.z}";
                                
                                if (problematicPoint.Item2.Contains("x"))
                                {
                                    edges.Add((coordKey, "+x"));
                                    edges.Add((coordKey, "-x"));
                                }
                                else if (problematicPoint.Item2.Contains("y")) 
                                {
                                    edges.Add((coordKey, "+y"));
                                    edges.Add((coordKey, "-y"));
                                }
                                else if (problematicPoint.Item2.Contains("z"))
                                {
                                    edges.Add((coordKey, "+z"));
                                    edges.Add((coordKey, "-z"));
                                }
                                
                                UpdateCost(edges, 10f);
                            }
                            
                            // 다음 시작점 설정
                            if (Bk.Count > 0)
                            {
                                start = (Bk[Bk.Count - 1].Item1, Bk[Bk.Count - 1].Item2);
                            }
                            
                            break;
                        }

                        // 각도 검증 추가
                        if (i > 0 && i < Bk.Count - 1)
                        {
                            Vector3 prevDir = (Bk[i].Item1 - Bk[i-1].Item1).normalized;
                            Vector3 nextDir = (Bk[i+1].Item1 - Bk[i].Item1).normalized;
                            float dotProduct = Vector3.Dot(prevDir, nextDir);
                            
                            // 30도 미만의 각도는 허용하지 않음
                            if (dotProduct > 0.866f) // cos(30도) ≈ 0.866
                            {
                                allDistancesValid = false;
                                Bk.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
                
                if (allDistancesValid)
                {
                    test = 0; // 모든 거리가 유효하면 종료
                }
                
                it++;
            }

            // 최종 검증 및 보정
            if (Bk.Count > 1)
            {
                // 시작점과 끝점이 너무 가까운 경우 중간점 추가
                if (Vector3.Distance(Bk[0].Item1, Bk[Bk.Count-1].Item1) < MinDisBend * 2)
                {
                    Vector3 midPoint = (Bk[0].Item1 + Bk[Bk.Count-1].Item1) * 0.5f;
                    string midDirection = Bk[0].Item2; // 시작점의 방향 사용
                    Bk.Insert(1, (midPoint, midDirection));
                }

                // 연속된 직선 구간이 너무 긴 경우 중간점 추가
                for (int i = 1; i < Bk.Count - 1; i++)
                {
                    float distance = Vector3.Distance(Bk[i-1].Item1, Bk[i].Item1);
                    if (distance > MinDisBend * 3)
                    {
                        Vector3 midPoint = (Bk[i-1].Item1 + Bk[i].Item1) * 0.5f;
                        string midDirection = Bk[i-1].Item2;
                        Bk.Insert(i, (midPoint, midDirection));
                        i++; // 새로 추가된 점을 건너뛰기
                    }
                }
            }

            return (Bk, Pk);
        }
        public (List<List<(Vector3, string)>> pathN, List<List<(Vector3, string)>> bendPointsN) MainRun()
        {
            // 필드 초기화
            PathN = new List<List<(Vector3, string)>>();
            BendPointsN = new List<List<(Vector3, string)>>();
            CoveringListN = new List<HashSet<(string, string)>>();
            
            for (int i = 0; i < NPipes; i++)
            {
                PathN.Add(new List<(Vector3, string)>());
                BendPointsN.Add(new List<(Vector3, string)>());
                CoveringListN.Add(new HashSet<(string, string)>());
            }

            // 첫 번째 패스: 모든 파이프에 대해 기본 경로 생성
            for (int k = 0; k < NPipes; k++)
            {
                var pipeK = Pipes[k];
                var (bendPointsK, pathK) = Run(pipeK.Item1, pipeK.Item2, pipeK.Item3, pipeK.Item4);
                
                PathN[k] = pathK ?? new List<(Vector3, string)>();
                
                // bendPoints가 비어있으면 path에서 추출
                if (bendPointsK == null || bendPointsK.Count == 0)
                {
                    BendPointsN[k] = ExtractBendPointsFromPath(PathN[k]);
                }
                else
                {
                    BendPointsN[k] = bendPointsK;
                }
                
                CoveringListN[k] = GetCoveringList(PathN[k], pipeK.Item3, pipeK.Item4);
                
                reinit();
            }

            int stop = 0;
            int it = 0;
            List<((Vector3, string), (Vector3, string), float, float)> Kit = Pipes.ToList();
            List<((Vector3, string), (Vector3, string), float, float)> Kbar = Pipes.ToList();
            List<List<(Vector3, string)>> bendPointsNInit = new List<List<(Vector3, string)>>();
            
            // 최적화 루프 - 경로가 이미 있으면 건너뛰기
            while (stop == 0 && it < Math.Min(IndexCategory.Count, 5)) // 최대 5번으로 제한
            {
                if (IndexCategory[it] == "I_seq")
                {
                    Kit = Pipes.ToList();
                    Kbar = Pipes.ToList();
                }

                foreach (var pipeK in Kit)
                {
                    int k = GetPipeIndex(pipeK);
                    
                    // 이미 유효한 경로가 있으면 건너뛰기
                    if (PathN[k] != null && PathN[k].Count > 0)
                    {
                        continue;
                    }
                    
                    var (bendPointsK, pathK) = Run(pipeK.Item1, pipeK.Item2, pipeK.Item3, pipeK.Item4);
                    reinit();

                    PathN[k] = pathK ?? new List<(Vector3, string)>();
                    
                    // bendPoints가 비어있으면 path에서 추출
                    if (bendPointsK == null || bendPointsK.Count == 0)
                    {
                        BendPointsN[k] = ExtractBendPointsFromPath(PathN[k]);
                    }
                    else
                    {
                        BendPointsN[k] = bendPointsK;
                    }
                    
                    CoveringListN[k] = GetCoveringList(PathN[k], pipeK.Item3, pipeK.Item4);

                    if (IndexCategory[it] == "I_seq")
                    {
                        Kbar.Remove(pipeK);
                        foreach (var item in Kbar)
                        {
                            int indexItem = GetPipeIndex(item);
                            var edges = CoveringListN[k].Intersect(CoveringListN[indexItem]).ToList();
                            UpdateCost(edges, 10f);
                        }
                    }
                }

                if (it == 0)
                {
                    bendPointsNInit = BendPointsN.Select(x => x.ToList()).ToList();
                }

                // 모든 파이프에 유효한 경로가 있으면 종료
                bool allPathsValid = true;
                for (int i = 0; i < NPipes; i++)
                {
                    if (PathN[i] == null || PathN[i].Count == 0)
                    {
                        allPathsValid = false;
                        break;
                    }
                }
                
                if (allPathsValid)
                {
                    stop = 1;
                    break;
                }

                CovConflict = new List<List<List<(string, string)>>>();
                for (int i = 0; i < NPipes; i++)
                {
                    CovConflict.Add(new List<List<(string, string)>>());
                    for (int j = 0; j < NPipes; j++)
                    {
                        CovConflict[i].Add(new List<(string, string)>());
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

                if (!IsEmptyList(CovConflict))
                {
                    stop = 0;
                    if (IndexCategory[it] == "I_par")
                    {
                        var covConflictSet = new HashSet<(string, string)>();
                        for (int k = 0; k < CovConflict.Count; k++)
                        {
                            foreach (var items in CovConflict[k])
                            {
                                foreach (var item in items)
                                {
                                    covConflictSet.Add(item);
                                }
                            }
                        }
                        UpdateCost(covConflictSet.ToList(), 30f);
                    }
                    else if (IndexCategory[it] == "I_cluster")
                    {
                        var kitNext = new HashSet<((Vector3, string), (Vector3, string), float, float)>();
                        foreach (var pipeI in Kit)
                        {
                            int i = GetPipeIndex(pipeI);
                            foreach (var pipeJ in Pipes)
                            {
                                int j = GetPipeIndex(pipeJ);
                                if (CovConflict[i][j].Any())
                                {
                                    if (ComparePriority(pipeI, pipeJ))
                                    {
                                        kitNext.Add(pipeJ);
            }
            else
            {
                                        kitNext.Add(pipeI);
                                    }
                                }
                            }
                        }

                        var conflictEdges = new HashSet<(string, string)>();
                        foreach (var pipeK in kitNext)
                        {
                            int k = GetPipeIndex(pipeK);
                            foreach (var pipeKPrime in Pipes)
                            {
                                int kPrime = GetPipeIndex(pipeKPrime);
                                if (!kitNext.Contains(pipeKPrime) && CovConflict[k][kPrime].Any())
                                {
                                    foreach (var item in CovConflict[k][kPrime])
                                    {
                                        conflictEdges.Add(item);
                                    }
                                }
                            }
                        }
                        UpdateCost(conflictEdges.ToList(), 100f);
                        Kit = kitNext.ToList();
                    }
                    it++;
                }
                else
                {
                    stop = 1;
                }
            }

            return (PathN, BendPointsN);
        }
        
        // 경로에서 bend points 추출
        private List<(Vector3, string)> ExtractBendPointsFromPath(List<(Vector3, string)> path)
        {
            var bendPoints = new List<(Vector3, string)>();
            
            if (path == null || path.Count == 0)
            {
                return bendPoints;
            }
            
            if (path.Count <= 1)
            {
                // 경로가 1개 이하면 모든 점을 bendPoints에 추가
                bendPoints.AddRange(path);
                return bendPoints;
            }
            
            if (path.Count == 2)
            {
                // 경로가 2개면 시작점과 끝점만 추가
                bendPoints.Add(path[0]);
                bendPoints.Add(path[1]);
                return bendPoints;
            }
            
            // 첫 번째 점은 항상 bend point (시작점)
            bendPoints.Add(path[0]);
            
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
                
                // 방향 벡터의 내적으로 각도 변화 감지 (내적이 1에 가까우면 직선, -1에 가까우면 역방향)
                float dotProduct = Vector3.Dot(directionIn, directionOut);
                float angleThreshold = 0.9f; // 약 25도 이상의 각도 변화
                
                if (dotProduct < angleThreshold)
                {
                    shouldAddBendPoint = true;
                }
                
                // 3. 마지막 bend point로부터 일정 거리 이상 떨어진 경우
                if (bendPoints.Count > 0)
                {
                    Vector3 lastBendPoint = bendPoints[bendPoints.Count - 1].Item1;
                    float distanceFromLastBend = Vector3.Distance(lastBendPoint, currPoint);
                    
                    // 긴 직선 구간에서는 중간 지점 추가 (거리 기반)
                    if (distanceFromLastBend >= 8.0f) // 8 단위 이상 떨어지면 중간점 추가
                    {
                        shouldAddBendPoint = true;
                    }
                }
                
                if (shouldAddBendPoint)
                {
                    bendPoints.Add(path[i]);
                }
            }
            
            // 마지막 점은 항상 bend point (끝점)
            bendPoints.Add(path[path.Count - 1]);
            
            // 최소 bend point 보장 (시작점과 끝점만 있으면 중간점 하나 추가)
            if (bendPoints.Count == 2 && path.Count > 2)
            {
                int midIndex = path.Count / 2;
                bendPoints.Insert(1, path[midIndex]);
            }
            
            return bendPoints;
        }
        
        // 시각화를 위한 디버그 정보 제공
        public List<(List<Vector3> pathPoints, List<Vector3> bendPoints, Color color)> GetVisualDebugInfo()
        {
            var result = new List<(List<Vector3> pathPoints, List<Vector3> bendPoints, Color color)>();
            
            if (PathN == null || BendPointsN == null) return result;
            
            for (int i = 0; i < PathN.Count && i < BendPointsN.Count; i++)
            {
                var pathPoints = PathN[i]?.Select(p => p.Item1).ToList() ?? new List<Vector3>();
                var bendPoints = BendPointsN[i]?.Select(p => p.Item1).ToList() ?? new List<Vector3>();
                
                // 파이프마다 다른 색상 할당
                Color color = Color.HSVToRGB((float)i / Mathf.Max(1, PathN.Count), 0.8f, 0.9f);
                
                result.Add((pathPoints, bendPoints, color));
            }
            
            return result;
        }
    }
} 