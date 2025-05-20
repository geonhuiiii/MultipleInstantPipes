# Pipe Wiring Optimization for Unity (C#)

이 프로젝트는 Python 기반 파이프 배선 최적화 알고리즘을 Unity(C#)에서 사용할 수 있도록 완전히 포팅한 코드입니다.

## 폴더 구조

- `Utils/Functions.cs` : 수학 및 경로 관련 유틸 함수
- `Model/Node.cs` : 노드 클래스
- `Model/Astar.cs` : A* 경로 탐색 알고리즘
- `Model/DecompositionHeuristic.cs` : 분해 휴리스틱 알고리즘

## 사용법

1. Unity 프로젝트의 `Assets/Scripts` 폴더에 위 파일들을 복사합니다.
2. 필요한 파라미터(공간, 장애물, 파이프 정보 등)를 C# 객체로 생성합니다.
3. 예시:
    ```csharp
    using Model;
    using Utils;
    using System.Collections.Generic;

    // 공간, 장애물, 파이프 정보 등 정의
    float[][] spaceCoords = new float[][] { new float[] {0,0}, new float[] {100,100} };
    List<float[][]> obstacles = new List<float[][]> { /* ... */ };
    var pipes = new List<( (float[], string), (float[], string), float, float )>
    {
        ( (new float[]{1,1}, "+x"), (new float[]{10,10}, "+y"), 1f, 0f ),
        // ...
    };

    var heuristic = new DecompositionHeuristic(
        maxit: 100,
        spaceCoords: spaceCoords,
        obstacleCoords: obstacles,
        pipes: pipes,
        wPath: 1.0f,
        wBend: 1.0f,
        wEnergy: 1.0f,
        minDisBend: 2
    );

    var (paths, bends) = heuristic.MainRun();
    ```
4. 결과는 각 파이프의 경로 및 벤드 포인트 리스트로 반환됩니다.

## 참고

- Python 원본 논문: [링크](https://doi.org/10.1016/j.omega.2022.102659)
- Python → C# 변환시 자료구조 및 타입에 주의하세요.
- 복잡한 수치 연산이 필요한 경우, Unity의 `Mathf`, `System.Math`, 또는 외부 수치 라이브러리 사용을 권장합니다.
- 좌표는 필요에 따라 Unity의 `Vector2`, `Vector3`로 변환해서 사용해도 됩니다.

## 참고사항

- Python의 모든 기능이 C#으로 1:1 매핑되지는 않습니다. 특히 numpy, 리스트 컴프리헨션 등은 C# 스타일로 변환해야 합니다.
- 실제 Unity 프로젝트에 적용할 때는, 각 함수/클래스의 입출력 타입을 Unity에서 다루기 쉬운 형태(예: Vector2, Vector3, List 등)로 맞추는 것이 좋습니다.
- 만약 Python 코드를 그대로 사용하고 싶다면, Pythonnet, IronPython, 또는 외부 프로세스와의 통신(REST API, 파일, 소켓 등) 방식도 고려할 수 있습니다.

---

**질문이나 추가 구현이 필요하면 언제든 문의해주세요!**
