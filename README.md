# 멀티스레딩 파이프 경로 탐색 시스템

Unity에서 D* 알고리즘을 사용한 멀티스레딩 파이프 경로 탐색 시스템입니다.

## 🔧 해결된 문제들

### 1. D* 알고리즘 무한 루프 문제
- **문제**: `ComputeShortestPath()` 함수에서 경로를 찾았음에도 루프에서 나오지 않음
- **해결**: 
  - 부동소수점 오차를 고려한 종료 조건 개선
  - 무한대 비용 처리 로직 수정
  - 최대 반복 횟수 제한 추가

### 2. 멀티스레딩 지원 추가
- **문제**: 모든 파이프가 순차적으로 처리되어 성능이 낮음
- **해결**:
  - Unity Physics API와 분리된 멀티스레딩 아키텍처 구현
  - 초기 장애물 데이터는 메인 스레드에서 수집
  - 경로 탐색은 백그라운드 스레드에서 병렬 처리
  - 추가된 순서 기반 순차 최적화 단계 추가

## 🚀 주요 기능

- **병렬 초기 경로 탐색**: 모든 파이프가 동시에 초기 경로를 탐색
- **순서 기반 최적화**: 추가된 순서대로 순차 경로 최적화
- **스레드 안전**: Unity Physics API 사용 없이 안전한 멀티스레딩
- **충돌 감지**: 경로상 장애물과의 충돌 여부 체크
- **시각화 지원**: LineRenderer를 통한 경로 시각화

## 📦 구성 요소

### 핵심 클래스

1. **`PathCreatorDstar`**: D* 알고리즘 구현체
2. **`MultiThreadPathFinder`**: 멀티스레딩 경로 탐색 매니저
3. **`MultiThreadPipeManager`**: Unity MonoBehaviour 래퍼
4. **`PipePathExample`**: 사용 예시 스크립트

### 데이터 클래스

- **`PathRequest`**: 경로 탐색 요청 정보
- **`PathResult`**: 경로 탐색 결과

## 🎮 사용법

### 1. 기본 설정

```csharp
// GameObject에 MultiThreadPipeManager 컴포넌트 추가
var pipeManager = gameObject.AddComponent<MultiThreadPipeManager>();

// 초기화
await pipeManager.InitializeAsync();
```

### 2. 파이프 요청 추가

```csharp
// 파이프 경로 요청 추가
pipeManager.AddPipeRequest(
    pipeId: 0,
    startPoint: new Vector3(0, 0, 0),
    startNormal: Vector3.up,
    endPoint: new Vector3(10, 5, 0),
    endNormal: Vector3.down,
    radius: 1f
);
```

### 3. 경로 탐색 실행

```csharp
// 모든 파이프 처리 (멀티스레딩)
await pipeManager.ProcessAllPipesAsync();

// 결과 확인
var result = pipeManager.GetPipeResult(0);
if (result.success)
{
    Debug.Log($"경로점 수: {result.path.Count}, 충돌: {result.hasCollision}");
}
```

### 4. 간단한 사용 예시

```csharp
// PipePathExample 컴포넌트 사용
public class MyPipeController : MonoBehaviour
{
    public PipePathExample pipeExample;
    
    async void Start()
    {
        // 예시 실행
        await pipeExample.ProcessMultiplePipesExample();
        
        // 모든 경로 시각화
        pipeExample.VisualizeAllPaths();
    }
}
```

## ⚙️ 설정 옵션

### MultiThreadPipeManager 설정

```csharp
[Header("경로 탐색 설정")]
public LayerMask obstacleLayerMask = -1;    // 장애물 레이어
public float detectionRange = 100f;         // 장애물 탐지 범위
public float gridSize = 3f;                 // 그리드 크기
public int maxConcurrentTasks = 4;          // 최대 동시 실행 스레드 수

[Header("디버그")]
public bool enableDebugLogs = true;         // 디버그 로그 출력
```

### PathCreatorDstar 설정

```csharp
public float Height = 5;                    // 파이프 높이
public float GridRotationY = 0;             // 그리드 회전
public float Radius = 1;                    // 파이프 반지름
public float GridSize = 3;                  // 그리드 크기
public float NearObstaclesPriority = 100;   // 장애물 회피 가중치
public int MaxIterations = 1000;            // 최대 반복 횟수
public float obstacleAvoidanceMargin = 1.5f; // 장애물 회피 여백
```

## 🔍 실행 단계

### 1단계: 초기화
- 씬의 모든 장애물 데이터를 메인 스레드에서 수집
- 그리드 기반으로 장애물 위치 캐싱

### 2단계: 병렬 초기 경로 탐색
- 모든 파이프가 동시에 초기 경로 탐색
- 각 파이프는 독립적인 스레드에서 처리
- 장애물 데이터는 스레드 안전하게 공유

### 3단계: 순서 기반 최적화
- 추가된 순서대로 파이프를 순차 처리
- 이전 파이프의 경로를 고려한 최적화
- 충돌 회피 및 경로 개선

## 🎨 시각화

### 자동 시각화
- `OnDrawGizmosSelected()`: Scene 뷰에서 경로와 탐지 범위 표시
- 성공한 경로: 초록색 선
- 충돌이 있는 경로: 빨간색 선

### 수동 시각화
```csharp
// 특정 파이프 경로 시각화
pipeExample.VisualizePipePath(pipeId);

// 모든 경로 시각화
pipeExample.VisualizeAllPaths();

// 시각화 제거
pipeExample.ClearVisualization();
```

## 🚨 주의사항

1. **Unity Physics API**: 멀티스레딩 환경에서는 Physics API를 사용하지 않습니다
2. **메인 스레드**: 초기 장애물 데이터 수집은 반드시 메인 스레드에서 실행
3. **메모리 관리**: 대량의 파이프 처리 시 메모리 사용량 모니터링 필요
4. **성능**: `maxConcurrentTasks` 값을 시스템 성능에 맞게 조정

## 🔧 문제 해결

### 경로를 찾지 못하는 경우
- `GridSize` 값을 줄여보세요
- `MaxIterations` 값을 늘려보세요
- `obstacleAvoidanceMargin` 값을 조정해보세요

### 성능이 느린 경우
- `maxConcurrentTasks` 값을 늘려보세요
- `detectionRange` 값을 줄여보세요
- 불필요한 디버그 로그를 비활성화하세요

### 메모리 사용량이 높은 경우
- 처리 후 `ClearAllResults()` 호출
- `detectionRange`를 적절히 제한
- 필요 없는 장애물 레이어 제외

## 📄 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다.
