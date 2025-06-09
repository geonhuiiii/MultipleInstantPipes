using System.Collections.Generic;
using UnityEngine;
using Model;
using Utils;
using System.Linq;

namespace InstantPipes
{
    [ExecuteAlways]
    public class PathVisualizer : MonoBehaviour
    {
        public PipeGenerator pipeGenerator;
        
        [Header("Visualization Settings")]
        public bool showPaths = true;
        public bool showBendPoints = true;
        public bool showColliders = true;
        public bool showLabels = true;
        public float pointSize = 0.2f;
        public float bendPointSize = 0.4f;
        public float lineWidth = 2f;
        
        // 마지막으로 계산된 경로 정보를 저장
        private List<(List<Vector3> pathPoints, List<Vector3> bendPoints, Color color)> _lastCalculatedPaths;
        
        // 디버그용 콜라이더 정보
        [System.Serializable]
        public class DebugCollider
        {
            public Vector3 position;
            public Vector3 size;
            public Quaternion rotation;
            public ColliderType type;
            
            public enum ColliderType { Box, Sphere, Capsule }
        }
        
        public List<DebugCollider> debugColliders = new List<DebugCollider>();
        
        private void OnEnable()
        {
            // PipeGenerator가 설정되지 않은 경우 자동 찾기
            if (pipeGenerator == null)
            {
                pipeGenerator = FindObjectOfType<PipeGenerator>();
            }
        }
        
        // 시각화 정보 수동 업데이트 (버튼에서 호출)
        public void UpdateVisualization()
        {
            if (pipeGenerator == null) return;
            
            // MultiPathCreator를 통해 시각화 정보 가져오기
            if (pipeGenerator.MultiPathCreator != null)
            {
                Debug.Log("경로 시각화 정보 업데이트 중...");
                var decompositionHeuristic = GetDecompositionHeuristic();
                if (decompositionHeuristic != null)
                {
                    _lastCalculatedPaths = decompositionHeuristic.GetVisualDebugInfo();
                    Debug.Log($"시각화 정보 업데이트 완료: {(_lastCalculatedPaths != null ? _lastCalculatedPaths.Count : 0)}개 경로");
                }
                else
                {
                    Debug.LogWarning("DecompositionHeuristic을 가져올 수 없습니다.");
                }
            }
        }
        
        // DecompositionHeuristic 인스턴스 가져오기 (리플렉션 사용)
        private DecompositionHeuristic GetDecompositionHeuristic()
        {
            // 현재 실행 중인 경로 계산 작업이 없을 수 있으므로, 
            // 새로운 경로 계산을 유도하기 위해 임시 경로 생성
            if (pipeGenerator != null && pipeGenerator.MultiPathCreator != null)
            {
                // 기존 파이프가 없는 경우 임시 파이프 구성 준비
                if (pipeGenerator.Pipes.Count == 0)
                {
                    Debug.Log("시각화를 위한 임시 경로 구성 생성");
                    
                    // 임시 시작점과 끝점 설정
                    Vector3 start = transform.position;
                    Vector3 end = transform.position + Vector3.right * 10f;
                    Vector3 normal = Vector3.up;
                    
                    var tempConfigs = new List<(Vector3, Vector3, Vector3, Vector3, float)>
                    {
                        (start, normal, end, normal, 1.0f)
                    };
                    
                    // MultiPathCreator에서 임시 계산 수행
                    pipeGenerator.MultiPathCreator.Height = pipeGenerator.Height;
                    pipeGenerator.MultiPathCreator.GridSize = pipeGenerator.GridSize;
                    //pipeGenerator.MultiPathCreator.CreateMultiplePaths(tempConfigs);
                }
                
                // 현재 상태 저장
                var currentPaths = pipeGenerator.Pipes.Select(p => new List<Vector3>(p.Points)).ToList();
                
                // 리플렉션을 사용하여 DecompositionHeuristic 인스턴스 접근 (필요한 경우)
                // 이 예제에서는 MultiPathCreator가 이미 계산된 경로 정보를 가지고 있다고 가정
                
                // 여기에 실제 DecompositionHeuristic 인스턴스를 가져오는 코드 구현
                // 현재는 클래스 구조를 완전히 알 수 없어 대체 방법 사용
                
                // 더미 데이터 생성 (실제 구현에서는 대체 필요)
                var dummyPathInfo = new List<(List<Vector3> pathPoints, List<Vector3> bendPoints, Color color)>();
                
                foreach (var pipe in pipeGenerator.Pipes)
                {
                    var pathPoints = pipe.Points;
                    var bendPoints = new List<Vector3>();
                    
                    // 굽힘 점 추출 (단순화된 버전)
                    if (pathPoints.Count >= 3)
                    {
                        bendPoints.Add(pathPoints[0]);
                        for (int i = 1; i < pathPoints.Count - 1; i++)
                        {
                            Vector3 prev = pathPoints[i-1];
                            Vector3 current = pathPoints[i];
                            Vector3 next = pathPoints[i+1];
                            
                            // 방향이 변경되는 점을 굽힘 점으로 간주
                            Vector3 dir1 = (current - prev).normalized;
                            Vector3 dir2 = (next - current).normalized;
                            
                            if (Vector3.Dot(dir1, dir2) < 0.9f)
                            {
                                bendPoints.Add(current);
                            }
                        }
                        bendPoints.Add(pathPoints[pathPoints.Count - 1]);
                    }
                    else
                    {
                        // 적은 점이 있는 경우, 모든 점을 굽힘 점으로 간주
                        bendPoints.AddRange(pathPoints);
                    }
                    
                    // 랜덤 색상 생성
                    Color color = new Color(
                        Random.value,
                        Random.value,
                        Random.value,
                        1.0f
                    );
                    
                    dummyPathInfo.Add((new List<Vector3>(pathPoints), bendPoints, color));
                }
                
                return null; // 실제로는 DecompositionHeuristic 인스턴스 반환
            }
            
            return null;
        }
        
        private void OnDrawGizmos()
        {
            if (!showPaths && !showBendPoints && !showColliders) return;
            
            // 경로 시각화 정보가 없는 경우 PipeGenerator의 데이터 사용
            if (_lastCalculatedPaths == null && pipeGenerator != null)
            {
                // 파이프 경로 시각화 (기존 파이프 사용)
                VisualizePipeGeneratorPaths();
            }
            else if (_lastCalculatedPaths != null)
            {
                // 계산된 경로 시각화
                VisualizeCalculatedPaths();
            }
            
            // 콜라이더 시각화
            if (showColliders)
            {
                VisualizeColliders();
            }
        }
        
        // PipeGenerator의 경로 시각화
        private void VisualizePipeGeneratorPaths()
        {
            if (pipeGenerator == null || pipeGenerator.Pipes == null) return;
            
            for (int i = 0; i < pipeGenerator.Pipes.Count; i++)
            {
                var pipe = pipeGenerator.Pipes[i];
                
                // 색상 결정 (재질 색상 사용)
                Color pipeColor = Color.white;
                if (i < pipeGenerator.PipeMaterials.Count && pipeGenerator.PipeMaterials[i] != null)
                {
                    if (pipeGenerator.PipeMaterials[i].HasProperty("_BaseColor"))
                    {
                        pipeColor = pipeGenerator.PipeMaterials[i].GetColor("_BaseColor");
                    }
                    else if (pipeGenerator.PipeMaterials[i].HasProperty("_Color"))
                    {
                        pipeColor = pipeGenerator.PipeMaterials[i].GetColor("_Color");
                    }
                }
                else
                {
                    // 랜덤 색상 사용
                    pipeColor = new Color(
                        (float)i / pipeGenerator.Pipes.Count,
                        1.0f - (float)i / pipeGenerator.Pipes.Count,
                        Mathf.Sin((float)i / pipeGenerator.Pipes.Count * Mathf.PI),
                        1.0f
                    );
                }
                
                // 경로 선 그리기
                if (showPaths && pipe.Points.Count >= 2)
                {
                    Gizmos.color = pipeColor;
                    for (int j = 0; j < pipe.Points.Count - 1; j++)
                    {
                        Gizmos.DrawLine(pipe.Points[j], pipe.Points[j + 1]);
                        
                        // 경로 점 그리기
                        if (j < pipe.Points.Count - 1)
                        {
                            Gizmos.DrawSphere(pipe.Points[j], pointSize);
                        }
                    }
                    Gizmos.DrawSphere(pipe.Points[pipe.Points.Count - 1], pointSize);
                }
                
                // 굽힘 점 시각화
                if (showBendPoints && pipe.Points.Count >= 3)
                {
                    Color bendColor = new Color(pipeColor.r, pipeColor.g, pipeColor.b, 0.8f);
                    Gizmos.color = bendColor;
                    
                    // 시작점과 끝점은 항상 굽힘 점으로 간주
                    Gizmos.DrawSphere(pipe.Points[0], bendPointSize);
                    Gizmos.DrawSphere(pipe.Points[pipe.Points.Count - 1], bendPointSize);
                    
                    // 중간 굽힘 점 찾기
                    for (int j = 1; j < pipe.Points.Count - 1; j++)
                    {
                        Vector3 prev = pipe.Points[j-1];
                        Vector3 current = pipe.Points[j];
                        Vector3 next = pipe.Points[j+1];
                        
                        // 방향이 변경되는 점을 굽힘 점으로 간주
                        Vector3 dir1 = (current - prev).normalized;
                        Vector3 dir2 = (next - current).normalized;
                        
                        if (Vector3.Dot(dir1, dir2) < 0.9f)
                        {
                            Gizmos.DrawSphere(current, bendPointSize);
                            
                            // 라벨 표시
                            if (showLabels)
                            {
                                #if UNITY_EDITOR
                                UnityEditor.Handles.Label(current + Vector3.up * bendPointSize * 2, 
                                    $"Bend {j} (Pipe {i})");
                                #endif
                            }
                        }
                    }
                }
                
                // 라벨 표시
                if (showLabels)
                {
                    #if UNITY_EDITOR
                    if (pipe.Points.Count > 0)
                    {
                        // 시작점 라벨
                        UnityEditor.Handles.Label(pipe.Points[0] + Vector3.up * pointSize * 2, 
                            $"Start (Pipe {i})");
                        
                        // 끝점 라벨
                        if (pipe.Points.Count > 1)
                        {
                            UnityEditor.Handles.Label(pipe.Points[pipe.Points.Count - 1] + Vector3.up * pointSize * 2, 
                                $"End (Pipe {i})");
                        }
                    }
                    #endif
                }
            }
        }
        
        // 계산된 경로 시각화
        private void VisualizeCalculatedPaths()
        {
            for (int i = 0; i < _lastCalculatedPaths.Count; i++)
            {
                var (pathPoints, bendPoints, color) = _lastCalculatedPaths[i];
                
                // 경로 선 그리기
                if (showPaths && pathPoints.Count >= 2)
                {
                    Gizmos.color = color;
                    for (int j = 0; j < pathPoints.Count - 1; j++)
                    {
                        Gizmos.DrawLine(pathPoints[j], pathPoints[j + 1]);
                        
                        // 경로 점 그리기
                        Gizmos.DrawSphere(pathPoints[j], pointSize);
                    }
                    Gizmos.DrawSphere(pathPoints[pathPoints.Count - 1], pointSize);
                }
                
                // 굽힘 점 시각화
                if (showBendPoints && bendPoints.Count >= 2)
                {
                    Color bendColor = new Color(color.r, color.g, color.b, 0.8f);
                    Gizmos.color = bendColor;
                    
                    for (int j = 0; j < bendPoints.Count; j++)
                    {
                        Gizmos.DrawSphere(bendPoints[j], bendPointSize);
                        
                        // 라벨 표시
                        if (showLabels)
                        {
                            #if UNITY_EDITOR
                            UnityEditor.Handles.Label(bendPoints[j] + Vector3.up * bendPointSize * 2, 
                                $"Bend {j} (Path {i})");
                            #endif
                        }
                    }
                }
                
                // 라벨 표시
                if (showLabels && pathPoints.Count > 0)
                {
                    #if UNITY_EDITOR
                    // 시작점 라벨
                    UnityEditor.Handles.Label(pathPoints[0] + Vector3.up * pointSize * 2, 
                        $"Start (Path {i})");
                    
                    // 끝점 라벨
                    if (pathPoints.Count > 1)
                    {
                        UnityEditor.Handles.Label(pathPoints[pathPoints.Count - 1] + Vector3.up * pointSize * 2, 
                            $"End (Path {i})");
                    }
                    #endif
                }
            }
        }
        
        // 콜라이더 시각화
        private void VisualizeColliders()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f); // 오렌지색 반투명
            
            foreach (var collider in debugColliders)
            {
                switch (collider.type)
                {
                    case DebugCollider.ColliderType.Box:
                        Matrix4x4 oldMatrix = Gizmos.matrix;
                        Gizmos.matrix = Matrix4x4.TRS(collider.position, collider.rotation, collider.size);
                        Gizmos.DrawCube(Vector3.zero, Vector3.one);
                        Gizmos.matrix = oldMatrix;
                        break;
                        
                    case DebugCollider.ColliderType.Sphere:
                        float radius = Mathf.Max(collider.size.x, Mathf.Max(collider.size.y, collider.size.z)) / 2f;
                        Gizmos.DrawSphere(collider.position, radius);
                        break;
                        
                    case DebugCollider.ColliderType.Capsule:
                        // 캡슐 콜라이더는 간단하게 구체와 선으로 시각화
                        float capsuleRadius = Mathf.Max(collider.size.x, collider.size.z) / 2f;
                        float capsuleHeight = collider.size.y;
                        Vector3 capTop = collider.position + collider.rotation * Vector3.up * (capsuleHeight / 2f - capsuleRadius);
                        Vector3 capBottom = collider.position + collider.rotation * Vector3.down * (capsuleHeight / 2f - capsuleRadius);
                        
                        Gizmos.DrawSphere(capTop, capsuleRadius);
                        Gizmos.DrawSphere(capBottom, capsuleRadius);
                        Gizmos.DrawLine(capTop, capBottom);
                        break;
                }
                
                // 라벨 표시
                if (showLabels)
                {
                    #if UNITY_EDITOR
                    UnityEditor.Handles.Label(collider.position, $"{collider.type}");
                    #endif
                }
            }
        }
        
        // 콜라이더 추가 헬퍼 메서드
        public void AddBoxCollider(Vector3 position, Vector3 size, Quaternion rotation)
        {
            debugColliders.Add(new DebugCollider
            {
                position = position,
                size = size,
                rotation = rotation,
                type = DebugCollider.ColliderType.Box
            });
        }
        
        public void AddSphereCollider(Vector3 position, float radius)
        {
            debugColliders.Add(new DebugCollider
            {
                position = position,
                size = Vector3.one * radius * 2f,
                rotation = Quaternion.identity,
                type = DebugCollider.ColliderType.Sphere
            });
        }
        
        public void AddCapsuleCollider(Vector3 position, float radius, float height, Quaternion rotation)
        {
            debugColliders.Add(new DebugCollider
            {
                position = position,
                size = new Vector3(radius * 2f, height, radius * 2f),
                rotation = rotation,
                type = DebugCollider.ColliderType.Capsule
            });
        }
        
        // 디버그 콜라이더 지우기
        public void ClearDebugColliders()
        {
            debugColliders.Clear();
        }
        
        // 파이프 생성 시 사용되는 임시 콜라이더를 추적하는 메서드
        public void TrackTemporaryColliders(GameObject tempCollider)
        {
            if (tempCollider == null) return;
            
            if (tempCollider.GetComponent<BoxCollider>() != null)
            {
                BoxCollider boxCollider = tempCollider.GetComponent<BoxCollider>();
                AddBoxCollider(
                    tempCollider.transform.position,
                    Vector3.Scale(tempCollider.transform.localScale, boxCollider.size),
                    tempCollider.transform.rotation
                );
            }
            else if (tempCollider.GetComponent<SphereCollider>() != null)
            {
                SphereCollider sphereCollider = tempCollider.GetComponent<SphereCollider>();
                AddSphereCollider(
                    tempCollider.transform.position,
                    sphereCollider.radius * Mathf.Max(
                        tempCollider.transform.localScale.x,
                        Mathf.Max(tempCollider.transform.localScale.y, tempCollider.transform.localScale.z)
                    )
                );
            }
            else if (tempCollider.GetComponent<CapsuleCollider>() != null)
            {
                CapsuleCollider capsuleCollider = tempCollider.GetComponent<CapsuleCollider>();
                AddCapsuleCollider(
                    tempCollider.transform.position,
                    capsuleCollider.radius * Mathf.Max(tempCollider.transform.localScale.x, tempCollider.transform.localScale.z),
                    capsuleCollider.height * tempCollider.transform.localScale.y,
                    tempCollider.transform.rotation
                );
            }
        }
    }
} 