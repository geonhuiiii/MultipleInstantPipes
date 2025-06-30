using System.Collections;
using UnityEngine;
using System.Threading.Tasks;

namespace InstantPipes
{
    /// <summary>
    /// 멀티스레딩 파이프 경로 탐색 사용 예시
    /// </summary>
    public class PipePathExample : MonoBehaviour
    {
        [Header("파이프 설정")]
        public GameObject[] startPoints;
        public GameObject[] endPoints;
        public float[] pipeRadii = {1f, 1.2f, 0.8f};
        
        [Header("실행")]
        public bool autoStart = true;
        public KeyCode manualTriggerKey = KeyCode.Space;
        
        private MultiThreadPipeManager pipeManager;
        private bool isProcessing = false;
        
        void Start()
        {
            // MultiThreadPipeManager 컴포넌트를 찾거나 추가
            pipeManager = GetComponent<MultiThreadPipeManager>();
            if (pipeManager == null)
            {
                pipeManager = gameObject.AddComponent<MultiThreadPipeManager>();
            }
            
            if (autoStart)
            {
                StartCoroutine(ProcessPipesCoroutine());
            }
        }
        
        void Update()
        {
            if (Input.GetKeyDown(manualTriggerKey) && !isProcessing)
            {
                StartCoroutine(ProcessPipesCoroutine());
            }
        }
        
        /// <summary>
        /// 코루틴으로 파이프 처리 (Unity의 메인 스레드와 호환)
        /// </summary>
        private IEnumerator ProcessPipesCoroutine()
        {
            if (isProcessing)
            {
                Debug.LogWarning("[예시] 이미 처리 중입니다.");
                yield break;
            }
            
            isProcessing = true;
            
            try
            {
                // Task를 사용하여 비동기 처리
                var task = ProcessPipesAsync();
                
                // Task가 완료될 때까지 대기
                while (!task.IsCompleted)
                {
                    yield return null; // 한 프레임 대기
                }
                
                // 예외가 있었다면 다시 throw
                if (task.IsFaulted)
                {
                    Debug.LogError($"[예시] 파이프 처리 중 오류: {task.Exception?.InnerException?.Message}");
                }
            }
            finally
            {
                isProcessing = false;
            }
        }
        
        /// <summary>
        /// 실제 파이프 처리 로직
        /// </summary>
        private async Task ProcessPipesAsync()
        {
            Debug.Log("[예시] 파이프 경로 탐색 시작");
            
            // 1. 매니저 초기화
            await pipeManager.InitializeAsync();
            
            // 2. 기존 결과 지우기
            pipeManager.ClearAllResults();
            
            // 3. 파이프 요청들 추가
            int pipeCount = Mathf.Min(startPoints.Length, endPoints.Length);
            for (int i = 0; i < pipeCount; i++)
            {
                if (startPoints[i] == null || endPoints[i] == null) continue;
                
                Vector3 startPos = startPoints[i].transform.position;
                Vector3 endPos = endPoints[i].transform.position;
                Vector3 startNormal = startPoints[i].transform.up;
                Vector3 endNormal = endPoints[i].transform.up;
                
                float radius = i < pipeRadii.Length ? pipeRadii[i] : 1f;
                
                pipeManager.AddPipeRequest(i, startPos, startNormal, endPos, endNormal, radius);
                
                Debug.Log($"[예시] 파이프 {i} 추가: {startPos} → {endPos}, 반지름: {radius}");
            }
            
            // 4. 모든 파이프 처리 (멀티스레딩)
            await pipeManager.ProcessAllPipesAsync();
            
            // 5. 결과 출력
            DisplayResults();
        }
        
        /// <summary>
        /// 경로 탐색 결과를 출력합니다
        /// </summary>
        private void DisplayResults()
        {
            var results = pipeManager.GetAllResults();
            
            Debug.Log($"[예시] === 경로 탐색 결과 ({results.Count}개 파이프) ===");
            
            int successCount = 0;
            int collisionCount = 0;
            
            foreach (var kvp in results)
            {
                var result = kvp.Value;
                
                if (result.success)
                {
                    successCount++;
                    if (result.hasCollision) collisionCount++;
                    
                    Debug.Log($"[예시] 파이프 {result.pipeId}: ✓ 성공, " +
                             $"경로점 {result.path.Count}개, " +
                             $"충돌: {(result.hasCollision ? "있음" : "없음")}");
                }
                else
                {
                    Debug.LogWarning($"[예시] 파이프 {result.pipeId}: ✗ 실패");
                }
            }
            
            Debug.Log($"[예시] === 요약: 성공 {successCount}/{results.Count}, 충돌 {collisionCount}개 ===");
        }
        
        /// <summary>
        /// 특정 파이프의 경로를 GameObject로 시각화
        /// </summary>
        public void VisualizePipePath(int pipeId, Material lineMaterial = null)
        {
            var result = pipeManager.GetPipeResult(pipeId);
            if (result == null || !result.success || result.path.Count < 2)
            {
                Debug.LogWarning($"[예시] 파이프 {pipeId}의 유효한 경로가 없습니다.");
                return;
            }
            
            // 기존 시각화 오브젝트 제거
            var existingViz = GameObject.Find($"PipePath_{pipeId}");
            if (existingViz != null)
            {
                DestroyImmediate(existingViz);
            }
            
            // 새 시각화 오브젝트 생성
            var vizObject = new GameObject($"PipePath_{pipeId}");
            var lineRenderer = vizObject.AddComponent<LineRenderer>();
            
            // LineRenderer 설정
            lineRenderer.material = lineMaterial ?? CreateDefaultLineMaterial();
            lineRenderer.startWidth = 0.1f;
            lineRenderer.endWidth = 0.1f;
            lineRenderer.positionCount = result.path.Count;
            lineRenderer.useWorldSpace = true;
            
            // 경로 점들 설정
            lineRenderer.SetPositions(result.path.ToArray());
            
            // 색상 설정 (충돌이 있으면 빨간색, 없으면 초록색)
            Color pathColor = result.hasCollision ? Color.red : Color.green;
            lineRenderer.startColor = pathColor;
            lineRenderer.endColor = pathColor;
            
            Debug.Log($"[예시] 파이프 {pipeId} 경로 시각화 완료");
        }
        
        private Material CreateDefaultLineMaterial()
        {
            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = Color.green;
            return material;
        }
        
        /// <summary>
        /// 모든 파이프 경로를 시각화
        /// </summary>
        [ContextMenu("모든 경로 시각화")]
        public void VisualizeAllPaths()
        {
            var results = pipeManager.GetAllResults();
            foreach (var kvp in results)
            {
                VisualizePipePath(kvp.Key);
            }
        }
        
        /// <summary>
        /// 모든 시각화 제거
        /// </summary>
        [ContextMenu("시각화 제거")]
        public void ClearVisualization()
        {
            var vizObjects = GameObject.FindObjectsOfType<LineRenderer>();
            foreach (var viz in vizObjects)
            {
                if (viz.name.StartsWith("PipePath_"))
                {
                    DestroyImmediate(viz.gameObject);
                }
            }
            Debug.Log("[예시] 모든 경로 시각화 제거됨");
        }
        
        private void OnDrawGizmos()
        {
            // 시작점들 표시
            if (startPoints != null)
            {
                Gizmos.color = Color.blue;
                for (int i = 0; i < startPoints.Length; i++)
                {
                    if (startPoints[i] != null)
                    {
                        Gizmos.DrawWireSphere(startPoints[i].transform.position, 0.5f);
                        Gizmos.DrawRay(startPoints[i].transform.position, startPoints[i].transform.up * 2f);
                    }
                }
            }
            
            // 끝점들 표시
            if (endPoints != null)
            {
                Gizmos.color = Color.red;
                for (int i = 0; i < endPoints.Length; i++)
                {
                    if (endPoints[i] != null)
                    {
                        Gizmos.DrawWireSphere(endPoints[i].transform.position, 0.5f);
                        Gizmos.DrawRay(endPoints[i].transform.position, endPoints[i].transform.up * 2f);
                    }
                }
            }
        }
    }
} 