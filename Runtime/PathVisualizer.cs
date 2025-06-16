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
            }
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