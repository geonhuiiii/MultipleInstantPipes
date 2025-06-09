using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;
using Model; // DecompositionHeuristic, AStar 등의 클래스가 있는 네임스페이스
using UnityEditor;

namespace InstantPipes
{
    public class PathfindingTester : MonoBehaviour
    {
        [Header("Test Configuration")]
        [Tooltip("실행할 테스트 유형")]
        public TestType testType = TestType.SimplePathTest;
        
        [Tooltip("테스트 결과를 시각화할지 여부")]
        public bool visualizeResults = true;

        [Header("Path Parameters")]
        [Tooltip("시작점과 끝점 사이의 거리")]
        public float pathDistance = 10f;
        
        [Tooltip("장애물 테스트 시 장애물 개수")]
        public int obstacleCount = 3;
        
        [Tooltip("다중 경로 테스트 시 경로 개수")]
        public int pathCount = 5;

        [Header("Algorithm Parameters")]
        [Tooltip("격자 크기 - 작을수록 정밀하지만 계산량 증가")]
        public float gridSize = 1.0f;
        
        [Tooltip("최대 반복 횟수")]
        public int maxIterations = 1000;
        
        [Tooltip("경로의 높이")]
        public float height = 5.0f;
        
        [Tooltip("혼돈도 - 높을수록 불규칙한 경로 생성")]
        public float chaos = 0.0f;
        
        [Tooltip("직선 경로 우선도")]
        public float straightPriority = 10.0f;
        
        [Tooltip("장애물 근처 우선도")]
        public float nearObstaclePriority = 0.0f;
        
        [Tooltip("파이프 반경")]
        public float pipeRadius = 0.5f;

        // 테스트 유형 열거형
        public enum TestType
        {
            SimplePathTest,           // 단순 경로 테스트
            ObstacleAvoidanceTest,    // 장애물 회피 테스트
            MultiPathTest,            // 다중 경로 테스트
            CollisionResolutionTest,  // 충돌 해결 테스트
            PerformanceTest           // 성능 테스트
        }

        // 테스트 결과를 저장할 구조체
        private struct TestResult
        {
            public bool success;
            public int pointCount;
            public float executionTime;
            public List<Vector3> path;
            public string message;
        }

        private List<TestResult> testResults = new List<TestResult>();
        private List<GameObject> visualObjects = new List<GameObject>();

        [ContextMenu("Run Selected Test")]
        public void RunSelectedTest()
        {
            ClearPreviousTests();

            switch (testType)
            {
                case TestType.SimplePathTest:
                    RunSimplePathTest();
                    break;
                case TestType.ObstacleAvoidanceTest:
                    RunObstacleTest();
                    break;
                case TestType.MultiPathTest:
                    RunMultiPathTest();
                    break;
                case TestType.CollisionResolutionTest:
                    RunCollisionResolutionTest();
                    break;
                case TestType.PerformanceTest:
                    RunPerformanceTest();
                    break;
            }

            if (visualizeResults)
            {
                VisualizeResults();
            }

            LogResults();
        }

        private void ClearPreviousTests()
        {
            testResults.Clear();
            
            // 이전 시각화 객체 제거
            foreach (var obj in visualObjects)
            {
                if (obj != null)
                {
                    if (Application.isEditor && !Application.isPlaying)
                        DestroyImmediate(obj);
                    else
                        Destroy(obj);
                }
            }
            visualObjects.Clear();
        }

        private void RunSimplePathTest()
        {
            UnityEngine.Debug.Log("===== 단순 경로 테스트 시작 =====");
            
            // 시작점과 끝점 설정
            Vector3 startPoint = transform.position;
            Vector3 endPoint = transform.position + Vector3.right * pathDistance;
            
            // 시작 및 끝 방향 설정
            Vector3 startNormal = Vector3.up;
            Vector3 endNormal = Vector3.up;

            // 경로 찾기 시간 측정
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            
            // MultiPathCreator 인스턴스 생성 및 설정
            MultiPathCreator multiPathCreator = new MultiPathCreator
            {
                GridSize = gridSize,
                Height = height,
                Chaos = chaos,
                StraightPathPriority = straightPriority,
                NearObstaclesPriority = nearObstaclePriority,
                MaxIterations = maxIterations,
                MinDistanceBetweenBends = 3
            };
            
            // 단일 경로 구성 생성
            var configs = new List<(Vector3 startPosition, Vector3 startNormal, Vector3 endPosition, Vector3 endNormal, float radius)>
            {
                (startPoint, startNormal, endPoint, endNormal, pipeRadius)
            };
            
            // 경로 생성
            List<List<Vector3>> paths = multiPathCreator.CreateMultiplePaths(configs);
            
            stopwatch.Stop();
            
            // 경로가 있고 첫 번째 경로가 유효한지 확인
            var path = paths.Count > 0 ? paths[0] : new List<Vector3>();
            
            // 테스트 결과 저장
            TestResult result = new TestResult
            {
                success = path.Count > 2,
                pointCount = path.Count,
                executionTime = stopwatch.ElapsedMilliseconds,
                path = path,
                message = path.Count > 2 
                    ? $"경로 생성 성공 ({path.Count} 포인트)" 
                    : "경로 생성 실패"
            };
            
            testResults.Add(result);
            UnityEngine.Debug.Log($"단순 경로 테스트 완료: {result.message} ({result.executionTime}ms)");
        }

        private void RunObstacleTest()
        {
            UnityEngine.Debug.Log("===== 장애물 회피 테스트 시작 =====");
            
            // 시작점과 끝점 설정
            Vector3 startPoint = transform.position;
            Vector3 endPoint = transform.position + Vector3.right * pathDistance;
            
            // 시작 및 끝 방향 설정
            Vector3 startNormal = Vector3.up;
            Vector3 endNormal = Vector3.up;

            // 랜덤 장애물 생성
            List<GameObject> obstacles = new List<GameObject>();
            for (int i = 0; i < obstacleCount; i++)
            {
                // 시작점과 끝점 사이에 장애물 위치 설정
                float t = (i + 1) / (float)(obstacleCount + 1);
                Vector3 obstaclePos = Vector3.Lerp(startPoint, endPoint, t);
                
                // 경로를 방해하도록 약간의 오프셋 추가
                obstaclePos += Vector3.up * Random.Range(-1f, 1f);
                
                GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.transform.position = obstaclePos;
                obstacle.transform.localScale = Vector3.one * 2f;
                obstacle.name = $"Obstacle_{i}";
                
                obstacles.Add(obstacle);
                visualObjects.Add(obstacle);
            }

            // 경로 찾기 시간 측정
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            
            // MultiPathCreator 인스턴스 생성 및 설정
            MultiPathCreator multiPathCreator = new MultiPathCreator
            {
                GridSize = gridSize,
                Height = height,
                Chaos = chaos,
                StraightPathPriority = straightPriority,
                NearObstaclesPriority = nearObstaclePriority,
                MaxIterations = maxIterations,
                MinDistanceBetweenBends = 3
            };
            
            // 단일 경로 구성 생성
            var configs = new List<(Vector3 startPosition, Vector3 startNormal, Vector3 endPosition, Vector3 endNormal, float radius)>
            {
                (startPoint, startNormal, endPoint, endNormal, pipeRadius)
            };
            
            // 경로 생성
            List<List<Vector3>> paths = multiPathCreator.CreateMultiplePaths(configs);
            
            stopwatch.Stop();
            
            // 경로가 있고 첫 번째 경로가 유효한지 확인
            var path = paths.Count > 0 ? paths[0] : new List<Vector3>();
            
            // 테스트 결과 저장
            TestResult result = new TestResult
            {
                success = path.Count > 2,
                pointCount = path.Count,
                executionTime = stopwatch.ElapsedMilliseconds,
                path = path,
                message = path.Count > 2 
                    ? $"장애물 회피 경로 생성 성공 ({path.Count} 포인트)" 
                    : "장애물 회피 경로 생성 실패"
            };
            
            testResults.Add(result);
            UnityEngine.Debug.Log($"장애물 회피 테스트 완료: {result.message} ({result.executionTime}ms)");
        }

        private void RunMultiPathTest()
        {
            UnityEngine.Debug.Log("===== 다중 경로 테스트 시작 =====");
            
            // 다중 경로를 위한 설정 준비
            List<(Vector3 startPosition, Vector3 startNormal, Vector3 endPosition, Vector3 endNormal, float radius)> configs = 
                new List<(Vector3, Vector3, Vector3, Vector3, float)>();
            
            // 경로 구성 생성
            for (int i = 0; i < pathCount; i++)
            {
                float angle = (i / (float)pathCount) * Mathf.PI * 2;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2f;
                
                Vector3 startPoint = transform.position + offset;
                Vector3 endPoint = transform.position + Vector3.right * pathDistance + offset;
                
                configs.Add((startPoint, Vector3.up, endPoint, Vector3.up, pipeRadius));
            }
            
            // MultiPathCreator 인스턴스 생성 및 설정
            MultiPathCreator multiPathCreator = new MultiPathCreator
            {
                GridSize = gridSize,
                Height = height,
                Chaos = chaos,
                StraightPathPriority = straightPriority,
                NearObstaclesPriority = nearObstaclePriority,
                MaxIterations = maxIterations,
                MinDistanceBetweenBends = 3
            };
            
            // 경로 찾기 시간 측정
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            
            // 다중 경로 생성
            List<List<Vector3>> paths = multiPathCreator.CreateMultiplePaths(configs);
            
            stopwatch.Stop();
            
            // 각 경로에 대한 테스트 결과 저장
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                TestResult result = new TestResult
                {
                    success = path.Count > 2,
                    pointCount = path.Count,
                    executionTime = stopwatch.ElapsedMilliseconds / paths.Count, // 평균 시간
                    path = path,
                    message = path.Count > 2
                        ? $"다중 경로 {i+1} 생성 성공 ({path.Count} 포인트)"
                        : $"다중 경로 {i+1} 생성 실패"
                };
                
                testResults.Add(result);
            }
            
            UnityEngine.Debug.Log($"다중 경로 테스트 완료: {paths.Count}개 경로 생성 ({stopwatch.ElapsedMilliseconds}ms)");
        }

        private void RunCollisionResolutionTest()
        {
            UnityEngine.Debug.Log("===== 충돌 해결 테스트 시작 =====");
            
            // 충돌이 발생할 만한 경로 구성 생성
            List<(Vector3 startPosition, Vector3 startNormal, Vector3 endPosition, Vector3 endNormal, float radius)> configs = 
                new List<(Vector3, Vector3, Vector3, Vector3, float)>();
            
            // 첫 번째 경로 - 왼쪽에서 오른쪽으로
            Vector3 start1 = transform.position;
            Vector3 end1 = transform.position + Vector3.right * pathDistance;
            configs.Add((start1, Vector3.up, end1, Vector3.up, pipeRadius));
            
            // 두 번째 경로 - 위쪽에서 아래쪽으로 (첫 번째 경로와 교차)
            Vector3 start2 = transform.position + Vector3.right * (pathDistance/2) + Vector3.forward * (pathDistance/2);
            Vector3 end2 = transform.position + Vector3.right * (pathDistance/2) - Vector3.forward * (pathDistance/2);
            configs.Add((start2, Vector3.up, end2, Vector3.up, pipeRadius));
            
            // 세 번째 경로 - 대각선 방향
            Vector3 start3 = transform.position - Vector3.forward * (pathDistance/4);
            Vector3 end3 = transform.position + Vector3.right * pathDistance + Vector3.forward * (pathDistance/4);
            configs.Add((start3, Vector3.up, end3, Vector3.up, pipeRadius));
            
            // MultiPathCreator 인스턴스 생성 및 설정
            MultiPathCreator multiPathCreator = new MultiPathCreator
            {
                GridSize = gridSize,
                Height = height,
                Chaos = chaos,
                StraightPathPriority = straightPriority,
                NearObstaclesPriority = nearObstaclePriority,
                MaxIterations = maxIterations,
                MinDistanceBetweenBends = 3
            };
            
            // 경로 찾기 시간 측정
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            
            // 다중 경로 생성
            List<List<Vector3>> paths = multiPathCreator.CreateMultiplePaths(configs);
            
            stopwatch.Stop();
            
            // 각 경로에 대한 테스트 결과 저장
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                TestResult result = new TestResult
                {
                    success = path.Count > 2,
                    pointCount = path.Count,
                    executionTime = stopwatch.ElapsedMilliseconds / paths.Count, // 평균 시간
                    path = path,
                    message = path.Count > 2
                        ? $"충돌 해결 경로 {i+1} 생성 성공 ({path.Count} 포인트)"
                        : $"충돌 해결 경로 {i+1} 생성 실패"
                };
                
                testResults.Add(result);
            }
            
            UnityEngine.Debug.Log($"충돌 해결 테스트 완료: {paths.Count}개 경로 생성 ({stopwatch.ElapsedMilliseconds}ms)");
        }

        private void RunPerformanceTest()
        {
            UnityEngine.Debug.Log("===== 성능 테스트 시작 =====");
            
            // 다양한 파라미터 조합으로 테스트
            float[] gridSizes = { 0.5f, 1.0f, 2.0f };
            int[] iterationCounts = { 500, 1000, 2000 };
            
            int testCount = 0;
            float totalTime = 0;
            
            foreach (float testGridSize in gridSizes)
            {
                foreach (int testIterations in iterationCounts)
                {
                    // 시작점과 끝점 설정
                    Vector3 startPoint = transform.position + Vector3.right * (testCount * 3);
                    Vector3 endPoint = startPoint + Vector3.right * pathDistance;
                    
                    // PathCreator 인스턴스 생성 및 설정
                    PathCreator pathCreator = new PathCreator
                    {
                        GridSize = testGridSize,
                        Height = height,
                        Chaos = chaos,
                        StraightPathPriority = straightPriority,
                        NearObstaclesPriority = nearObstaclePriority,
                        MaxIterations = testIterations,
                        Radius = pipeRadius
                    };
                    
                    // 경로 찾기 시간 측정
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    
                    // 경로 생성
                    var path = pathCreator.Create(startPoint, Vector3.up, endPoint, Vector3.up, 1);
                    
                    stopwatch.Stop();
                    totalTime += stopwatch.ElapsedMilliseconds;
                    
                    // 테스트 결과 저장
                    TestResult result = new TestResult
                    {
                        success = pathCreator.LastPathSuccess,
                        pointCount = path.Count,
                        executionTime = stopwatch.ElapsedMilliseconds,
                        path = path,
                        message = $"GridSize: {testGridSize}, Iterations: {testIterations}"
                    };
                    
                    testResults.Add(result);
                    testCount++;
                    
                    UnityEngine.Debug.Log($"성능 테스트 #{testCount}: {result.message}, {(result.success ? "성공" : "실패")}, " +
                                 $"실행 시간: {result.executionTime}ms, 포인트 수: {result.pointCount}");
                }
            }
            
            UnityEngine.Debug.Log($"성능 테스트 완료: {testCount}개 테스트 수행, 평균 실행 시간: {totalTime/testCount}ms");
        }

        private void VisualizeResults()
        {
            UnityEngine.Debug.Log("테스트 결과 시각화 중...");
            
            for (int i = 0; i < testResults.Count; i++)
            {
                var result = testResults[i];
                
                if (result.path == null || result.path.Count == 0)
                    continue;
                
                // 시작점 및 끝점 시각화
                CreateSphere(result.path[0], Color.green, $"Start_{i}");
                CreateSphere(result.path[result.path.Count - 1], Color.red, $"End_{i}");
                
                // 경로 시각화
                for (int j = 1; j < result.path.Count - 1; j++)
                {
                    CreateSphere(result.path[j], Color.yellow, $"Point_{i}_{j}");
                }
                
                // 경로 선 시각화
                for (int j = 0; j < result.path.Count - 1; j++)
                {
                    CreateLine(result.path[j], result.path[j + 1], i);
                }
            }
        }

        private void CreateSphere(Vector3 position, Color color, string name)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = position;
            sphere.transform.localScale = Vector3.one * 0.2f;
            sphere.name = name;
            
            if (sphere.TryGetComponent<Renderer>(out var renderer))
            {
                renderer.material.color = color;
            }
            
            visualObjects.Add(sphere);
        }

        private void CreateLine(Vector3 start, Vector3 end, int resultIndex)
        {
            GameObject line = new GameObject($"Line_{resultIndex}");
            LineRenderer lineRenderer = line.AddComponent<LineRenderer>();
            
            lineRenderer.startWidth = 0.1f;
            lineRenderer.endWidth = 0.1f;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
            
            // 각 테스트 결과마다 다른 색상 사용
            Color lineColor = new Color(
                Mathf.Sin(resultIndex * 0.5f) * 0.5f + 0.5f,
                Mathf.Cos(resultIndex * 0.3f) * 0.5f + 0.5f,
                Mathf.Sin(resultIndex * 0.7f) * 0.5f + 0.5f
            );
            
            lineRenderer.startColor = lineColor;
            lineRenderer.endColor = lineColor;
            
            // 기본 머티리얼 설정
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            
            visualObjects.Add(line);
        }

        private void LogResults()
        {
            UnityEngine.Debug.Log("===== 테스트 결과 요약 =====");
            
            int successCount = 0;
            float totalTime = 0;
            int totalPoints = 0;
            
            foreach (var result in testResults)
            {
                if (result.success) successCount++;
                totalTime += result.executionTime;
                totalPoints += result.pointCount;
            }
            
            UnityEngine.Debug.Log($"총 테스트: {testResults.Count}");
            UnityEngine.Debug.Log($"성공한 테스트: {successCount} ({(float)successCount/testResults.Count*100:F1}%)");
            UnityEngine.Debug.Log($"평균 실행 시간: {totalTime/testResults.Count:F2}ms");
            UnityEngine.Debug.Log($"평균 경로 포인트 수: {(float)totalPoints/testResults.Count:F1}");
            UnityEngine.Debug.Log("==========================");
        }

#if UNITY_EDITOR
        // 에디터에서 테스트 결과 시각화를 위한 기즈모
        private void OnDrawGizmos()
        {
            if (!visualizeResults || testResults.Count == 0)
                return;
                
            foreach (var result in testResults)
            {
                if (result.path == null || result.path.Count < 2)
                    continue;
                    
                for (int i = 0; i < result.path.Count - 1; i++)
                {
                    Gizmos.color = result.success ? Color.green : Color.red;
                    Gizmos.DrawLine(result.path[i], result.path[i + 1]);
                    
                    if (i > 0 && i < result.path.Count - 1)
                    {
                        Gizmos.DrawSphere(result.path[i], 0.1f);
                    }
                }
                
                // 시작점과 끝점 강조
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(result.path[0], 0.2f);
                Gizmos.DrawSphere(result.path[result.path.Count - 1], 0.2f);
            }
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(PathfindingTester))]
    public class PathfindingTesterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            PathfindingTester tester = (PathfindingTester)target;
            
            EditorGUILayout.Space(10);
            
            if (GUILayout.Button("Run Test", GUILayout.Height(30)))
            {
                tester.RunSelectedTest();
            }
            
            EditorGUILayout.HelpBox(
                "1. 테스트 유형을 선택하세요\n" +
                "2. 알고리즘 매개변수를 조정하세요\n" +
                "3. 'Run Test' 버튼을 클릭하세요\n" +
                "4. 콘솔에서 결과를 확인하고 Scene 뷰에서 시각화된 경로를 확인하세요", 
                MessageType.Info);
        }
    }
#endif
}