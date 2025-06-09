using System.Collections.Generic;
using UnityEngine;
using Model;
using Utils;
using System.Linq;
using Debug = UnityEngine.Debug;

namespace InstantPipes
{
    [System.Serializable]
    public class MultiPathCreator
    {
        public float Height = 1;
        public float GridRotationY = 0;
        public float GridSize = 1.0f;
        public float Chaos = 0;
        public float StraightPathPriority = 10;
        public float NearObstaclesPriority = 0;
        public int MaxIterations = 100000;
        public int MinDistanceBetweenBends = 1;

        // 장애물 필터링 옵션 추가
        [Header("장애물 필터링 옵션")]
        public string[] excludeTags = new string[] { "floor" }; // 제외할 태그들
        public LayerMask excludeLayers = 0; // 제외할 레이어들
        public float endpointExclusionRadius = 0.5f; // 시작점/도착점 주변 제외 반경

        public bool LastPathSuccess = true;

        // 모든 시작점과 끝점을 한 번에 처리하는 메서드
        public List<List<Vector3>> CreateMultiplePaths(
            List<(Vector3 startPosition, Vector3 startNormal, 
                  Vector3 endPosition, Vector3 endNormal, 
                  float radius)> pipeConfigs)
        {
            // 모든 경로에 대한 결과를 저장할 리스트
            List<List<Vector3>> allPaths = new List<List<Vector3>>();
            
            Debug.Log($"다중 경로 생성 시작: {pipeConfigs.Count}개의 파이프 경로 계산");
            
            // 공간 바운딩 박스 계산
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            
            foreach (var config in pipeConfigs)
            {
                // 시작점과 끝점을 고려하여 바운딩 박스 계산
                minX = Mathf.Min(minX, config.startPosition.x, config.endPosition.x);
                minY = Mathf.Min(minY, config.startPosition.y, config.endPosition.y);
                minZ = Mathf.Min(minZ, config.startPosition.z, config.endPosition.z);
                
                maxX = Mathf.Max(maxX, config.startPosition.x, config.endPosition.x);
                maxY = Mathf.Max(maxY, config.startPosition.y, config.endPosition.y);
                maxZ = Mathf.Max(maxZ, config.startPosition.z, config.endPosition.z);
            }
            
            // 바운딩 박스 주변에 여유 공간 추가
            float padding = 0.5f;
            minX -= padding; minY -= padding; minZ -= padding;
            maxX += padding; maxY += padding; maxZ += padding;
            
            Debug.Log($"바운딩 박스 계산 완료: Min({minX}, {minY}, {minZ}), Max({maxX}, {Height+maxY}, {maxZ})");
            
            // AStar 알고리즘에 필요한 공간 좌표
            float[][] spaceCoords = new float[][] {
                new float[] { minX, minY, minZ },
                new float[] { maxX, Height+maxY+10f, maxZ }
            };
            
            // 장애물 탐색 (Scene에서 모든 콜라이더 찾기)
            List<float[][]> obstacleCoords = FindObstacles(pipeConfigs);
            Debug.Log($"장애물 탐색 완료: {obstacleCoords.Count}개의 장애물 발견");
            
            // pipe config 형식 변환
            List<((float[], string), (float[], string), float, float)> pipes = new List<((float[], string), (float[], string), float, float)>();
            
            foreach (var config in pipeConfigs)
            {
                // 시작점과 방향
                Vector3 pathStart = config.startPosition + config.startNormal.normalized * Height;
                float[] startCoord = new float[] { pathStart.x, pathStart.y, pathStart.z };
                string startDir = GetDirectionString(config.startNormal);
                
                // 끝점과 방향
                Vector3 pathEnd = config.endPosition + config.endNormal.normalized * Height;
                float[] endCoord = new float[] { pathEnd.x, pathEnd.y, pathEnd.z };
                string endDir = GetDirectionString(config.endNormal);
                
                // 파이프 정보 추가
                pipes.Add(((startCoord, startDir), (endCoord, endDir), config.radius, 0.1f));
            }
            
            Debug.Log($"경로 계산 시작: 알고리즘 파라미터 - 직선 가중치: {StraightPathPriority}, 장애물 가중치: {NearObstaclesPriority}, 최대 반복: {MaxIterations}");
            
            // 경로 찾기 알고리즘 실행
            try {
                UnityEngine.Debug.Log("DecompositionHeuristic 인스턴스 생성 시작");
                var decomposition = new DecompositionHeuristic(
                    MaxIterations,
                    GridSize,
                    spaceCoords,
                    obstacleCoords,
                    pipes,
                    StraightPathPriority,     // 직선 경로 가중치
                    10f,                      // 곡선 변화 가중치 
                    NearObstaclesPriority,    // 에너지(z축 방향) 가중치
                    MinDistanceBetweenBends   // 최소 곡률 간격
                );
                UnityEngine.Debug.Log("DecompositionHeuristic 인스턴스 생성 완료");
                
                UnityEngine.Debug.Log("MainRun 메서드 호출 시작");
                var (pathsN, bendPointsN) = decomposition.MainRun();
                UnityEngine.Debug.Log("MainRun 메서드 호출 완료");
                
                Debug.Log($"경로 계산 완료: {pathsN.Count}개의 경로 생성됨");

                // 결과 경로를 Unity Vector3 형식으로 변환
            for (int i = 0; i < pipeConfigs.Count; i++)
            {
                var config = pipeConfigs[i];
                
                if (pathsN != null && i < pathsN.Count && pathsN[i] != null && pathsN[i].Count > 0)
                {
                    var pathPoints = pathsN[i];
                    var path = new List<Vector3>();
                    
                    // 시작점 추가
                    path.Add(config.startPosition);
                    path.Add(config.startPosition + config.startNormal.normalized * Height);
                    
                    // 중간 경로 포인트 추가
                    foreach (var point in pathPoints)
                    {
                        path.Add(point.Item1); // point.Item1은 이미 Vector3
                    }
                    
                    // 끝점 추가
                    path.Add(config.endPosition + config.endNormal.normalized * Height);
                    path.Add(config.endPosition);
                    
                    Debug.Log($"파이프 {i} 경로: {path.Count}개 포인트로 구성됨");
                    allPaths.Add(path);
                }
                else
                {
                    // 경로를 찾지 못한 경우 간단한 직선 경로 추가
                    Debug.LogWarning($"파이프 {i}의 경로를 찾지 못했습니다. 간단한 직선 경로를 생성합니다.");
                }
            }
            }
            catch (System.Exception ex) {
                UnityEngine.Debug.LogError($"경로 생성 중 오류 발생: {ex.Message}\n{ex.StackTrace}");
                return new List<List<Vector3>>();
            }
            
            
            
            LastPathSuccess = allPaths.Count == pipeConfigs.Count;
            Debug.Log($"다중 경로 생성 완료: {(LastPathSuccess ? "모든 경로 성공" : "일부 경로 실패")}");
            
            return allPaths;
        }
        
        // Unity 방향 벡터를 문자열 방향으로 변환 (예: +x, -y, +z 등)
        private string GetDirectionString(Vector3 normal)
        {
            Vector3 absNormal = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            
            if (absNormal.x >= absNormal.y && absNormal.x >= absNormal.z)
                return normal.x >= 0 ? "+x" : "-x";
            else if (absNormal.y >= absNormal.x && absNormal.y >= absNormal.z)
                return normal.y >= 0 ? "+y" : "-y";
            else
                return normal.z >= 0 ? "+z" : "-z";
        }
        
        // Scene에서 장애물 탐색 (콜라이더를 바운딩 박스로 변환)
        // 시작점과 도착점이 있는 오브젝트는 제외
        private List<float[][]> FindObstacles(List<(Vector3 startPosition, Vector3 startNormal, 
                  Vector3 endPosition, Vector3 endNormal, 
                  float radius)> pipeConfigs = null)
        {
            List<float[][]> obstacles = new List<float[][]>();
            Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            
            // 시작점과 도착점 위치 수집 (제외할 오브젝트 판단용)
            HashSet<Vector3> pipeEndpoints = new HashSet<Vector3>();
            if (pipeConfigs != null)
            {
                foreach (var config in pipeConfigs)
                {
                    pipeEndpoints.Add(config.startPosition);
                    pipeEndpoints.Add(config.endPosition);
                }
            }
            
            int excludedByTag = 0;
            int excludedByLayer = 0;
            int excludedByEndpoint = 0;
            int totalColliders = colliders.Length;
            
            foreach (var collider in colliders)
            {
                if (collider.isTrigger) continue;
                
                bool shouldExclude = false;
                string exclusionReason = "";
                
                
                // 2. 레이어 기반 제외 검사
                if (!shouldExclude && excludeLayers != 0)
                {
                    if ((excludeLayers.value & (1 << collider.gameObject.layer)) != 0)
                    {
                        shouldExclude = true;
                        exclusionReason = $"레이어 '{LayerMask.LayerToName(collider.gameObject.layer)}'";
                        excludedByLayer++;
                    }
                }
                
                
                if (shouldExclude)
                {
                    Debug.Log($"장애물 제외: {collider.name} - {exclusionReason}");
                }
                else
                {
                    // 장애물로 추가
                    Bounds bounds = collider.bounds;
                    float padding = 0.5f;
                    // 바운딩 박스의 최소점과 최대점
                    float[] min = new float[] { bounds.min.x - padding, bounds.min.y - padding, bounds.min.z - padding };
                    float[] max = new float[] { bounds.max.x + padding, bounds.max.y + padding, bounds.max.z + padding };
                    
                    obstacles.Add(new float[][] { min, max });
                    Debug.Log($"장애물 추가: {collider.name} (바운딩 박스: {bounds.min} ~ {bounds.max})");
                }
            }
            
            Debug.Log($"장애물 필터링 완료: 총 {totalColliders}개 콜라이더 중 " +
                     $"{obstacles.Count}개 장애물 추가, " +
                     $"{excludedByTag}개 태그 제외, " +
                     $"{excludedByLayer}개 레이어 제외, " +
                     $"{excludedByEndpoint}개 엔드포인트 근접 제외");
            
            return obstacles;
        }
            public float CalculatePathLength(List<Vector3> points)
        {
            float length = 0f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                length += Vector3.Distance(points[i], points[i + 1]);
            }

            return length;
        }
    }
} 