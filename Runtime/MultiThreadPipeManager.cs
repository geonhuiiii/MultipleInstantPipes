using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using System;

namespace InstantPipes
{
    /// <summary>
    /// 멀티스레딩을 지원하는 파이프 경로 탐색 매니저
    /// 
    /// 사용법:
    /// 1. 초기화: InitializeAsync() 호출
    /// 2. 파이프 추가: AddPipeRequest() 호출
    /// 3. 경로 탐색: ProcessAllPipesAsync() 호출
    /// 4. 결과 확인: GetPipeResult() 또는 GetAllResults() 호출
    /// </summary>
    public class MultiThreadPipeManager : MonoBehaviour
    {
        [Header("경로 탐색 설정")]
        public LayerMask obstacleLayerMask = -1;
        public float detectionRange = 100f;
        public float gridSize = 3f;
        public int maxConcurrentTasks = 4;
        
        [Header("디버그")]
        public bool enableDebugLogs = true;
        
        private MultiThreadPathFinder pathFinder;
        private Vector3 sceneCenter;
        private bool isInitialized = false;
        
        public async Task InitializeAsync()
        {
            if (isInitialized) return;
            
            try
            {
                pathFinder = new MultiThreadPathFinder(maxConcurrentTasks);
                
                // 그리드 크기 설정
                pathFinder.SetGridSize(gridSize);
                
                // 씬의 중심점 계산 (모든 렌더러의 중심)
                CalculateSceneCenter();
                
                if (enableDebugLogs)
                    Debug.Log($"[파이프 매니저] 장애물 데이터 초기화 시작 - 중심: {sceneCenter}, 범위: {detectionRange}, 그리드 크기: {gridSize}");
                
                // 메인 스레드에서 장애물 데이터 수집
                pathFinder.InitializeObstacleData(sceneCenter, detectionRange, obstacleLayerMask, gridSize);
                
                isInitialized = true;
                
                if (enableDebugLogs)
                    Debug.Log("[파이프 매니저] 초기화 완료");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[파이프 매니저] 초기화 실패: {ex.Message}");
                throw;
            }
        }
        
        private void CalculateSceneCenter()
        {
            var renderers = FindObjectsOfType<Renderer>();
            if (renderers.Length == 0)
            {
                sceneCenter = Vector3.zero;
                return;
            }
            
            Vector3 min = renderers[0].bounds.min;
            Vector3 max = renderers[0].bounds.max;
            
            foreach (var renderer in renderers)
            {
                min = Vector3.Min(min, renderer.bounds.min);
                max = Vector3.Max(max, renderer.bounds.max);
            }
            
            sceneCenter = (min + max) * 0.5f;
        }
        
        /// <summary>
        /// 파이프 경로 요청 추가
        /// </summary>
        /// <param name="pipeId">파이프 고유 ID</param>
        /// <param name="startPoint">시작점</param>
        /// <param name="startNormal">시작점 법선</param>
        /// <param name="endPoint">끝점</param>
        /// <param name="endNormal">끝점 법선</param>
        /// <param name="radius">파이프 반지름</param>
        public void AddPipeRequest(int pipeId, Vector3 startPoint, Vector3 startNormal, 
                                  Vector3 endPoint, Vector3 endNormal, float radius)
        {
            if (!isInitialized)
            {
                Debug.LogError("[파이프 매니저] 초기화되지 않음. InitializeAsync()를 먼저 호출하세요.");
                return;
            }
            
            var request = new PathRequest(pipeId, startPoint, startNormal, endPoint, endNormal, radius);
            pathFinder.AddRequest(request);
            
            if (enableDebugLogs)
                Debug.Log($"[파이프 매니저] 파이프 요청 추가 - ID: {pipeId}");
        }
        
        /// <summary>
        /// 모든 파이프의 경로를 탐색합니다.
        /// 1단계: 모든 파이프가 동시에 초기 경로 탐색
        /// 2단계: 추가된 순서대로 순차 경로 탐색
        /// </summary>
        public async Task ProcessAllPipesAsync()
        {
            if (!isInitialized)
            {
                Debug.LogError("[파이프 매니저] 초기화되지 않음. InitializeAsync()를 먼저 호출하세요.");
                return;
            }
            
            try
            {
                if (enableDebugLogs)
                    Debug.Log("[파이프 매니저] 경로 탐색 시작");
                
                // 1단계: 모든 파이프가 동시에 초기 경로 탐색
                await pathFinder.ProcessInitialPathsAsync();
                
                // 2단계: 추가된 순서대로 순차 경로 탐색
                await pathFinder.ProcessPriorityPathsAsync();
                
                if (enableDebugLogs)
                {
                    var results = pathFinder.GetAllResults();
                    int successCount = 0;
                    foreach (var result in results.Values)
                    {
                        if (result.success) successCount++;
                    }
                    Debug.Log($"[파이프 매니저] 경로 탐색 완료 - 성공: {successCount}/{results.Count}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[파이프 매니저] 경로 탐색 실패: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 특정 파이프의 결과를 가져옵니다.
        /// </summary>
        public PathResult GetPipeResult(int pipeId)
        {
            if (!isInitialized)
            {
                Debug.LogWarning("[파이프 매니저] 초기화되지 않음");
                return null;
            }
            
            return pathFinder.GetResult(pipeId);
        }
        
        /// <summary>
        /// 모든 파이프의 결과를 가져옵니다.
        /// </summary>
        public Dictionary<int, PathResult> GetAllResults()
        {
            if (!isInitialized)
            {
                Debug.LogWarning("[파이프 매니저] 초기화되지 않음");
                return new Dictionary<int, PathResult>();
            }
            
            return pathFinder.GetAllResults();
        }
        
        /// <summary>
        /// 모든 결과를 지웁니다.
        /// </summary>
        public void ClearAllResults()
        {
            if (isInitialized)
            {
                pathFinder.ClearResults();
                if (enableDebugLogs)
                    Debug.Log("[파이프 매니저] 모든 결과 지움");
            }
        }
        
        /// <summary>
        /// 사용 예시 - 여러 파이프를 한 번에 처리
        /// </summary>
        public async Task ProcessMultiplePipesExample()
        {
            // 초기화
            await InitializeAsync();
            
            // 파이프 요청들 추가
            AddPipeRequest(0, new Vector3(0, 0, 0), Vector3.up, new Vector3(10, 5, 0), Vector3.down, 1f);
            AddPipeRequest(1, new Vector3(5, 0, 5), Vector3.up, new Vector3(15, 5, 5), Vector3.down, 1.5f);
            AddPipeRequest(2, new Vector3(-5, 0, -5), Vector3.up, new Vector3(5, 5, -5), Vector3.down, 0.8f);
            
            // 모든 파이프 처리
            await ProcessAllPipesAsync();
            
            // 결과 확인
            var allResults = GetAllResults();
            foreach (var kvp in allResults)
            {
                var result = kvp.Value;
                Debug.Log($"파이프 {result.pipeId}: 성공={result.success}, 충돌={result.hasCollision}, 경로점 수={result.path.Count}");
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            if (!isInitialized) return;
            
            // 탐지 범위 표시
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(sceneCenter, detectionRange);
            
            // 경로 결과 표시
            var results = GetAllResults();
            if (results != null)
            {
                foreach (var kvp in results)
                {
                    var result = kvp.Value;
                    if (result.success && result.path.Count > 1)
                    {
                        Gizmos.color = result.hasCollision ? Color.red : Color.green;
                        
                        for (int i = 0; i < result.path.Count - 1; i++)
                        {
                            Gizmos.DrawLine(result.path[i], result.path[i + 1]);
                        }
                    }
                }
            }
        }
    }
} 