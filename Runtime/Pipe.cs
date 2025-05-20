using System;
using System.Collections.Generic;
using UnityEngine;

namespace InstantPipes
{
    [System.Serializable]
    public class Pipe
    {
        public List<Vector3> Points;

        private List<Vector3> _verts;
        private List<Vector3> _normals;
        private List<Vector2> _uvs;
        private List<int> _triIndices;
        private List<BezierPoint> _bezierPoints;
        private List<PlaneInfo> _planes;
        private PipeGenerator _generator;

        private float _currentAngleOffset;
        private Quaternion _previousRotation;

        private float _ringThickness => _generator.HasExtrusion ? 0 : _generator.RingThickness;

        public Pipe(List<Vector3> points)
        {
            Points = points;
        }

        public float GetMaxDistanceBetweenPoints()
        {
            var maxDistance = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                maxDistance = Mathf.Max(maxDistance, Vector3.Distance(Points[i], Points[i - 1]));
            }
            return maxDistance;
        }

        public List<Mesh> GenerateMeshes(PipeGenerator generator)
        {
            var meshes = new List<Mesh>();

            // Validate input
            if (Points == null || Points.Count < 2)
            {
                Debug.LogError("Cannot generate pipe mesh: insufficient points");
                return meshes;
            }

            _generator = generator;

            ClearMeshInfo();

            var ringPoints = new List<int>();

            // Initial direction and rotation setup with validation
            Vector3 initialDirection = Vector3.forward;
            if (Points.Count >= 2)
            {
                // 시작점에서 끝점으로 향하는 방향
                initialDirection = (Points[1] - Points[0]).normalized;
                if (initialDirection == Vector3.zero) // Handle case where points are too close
                {
                    initialDirection = Vector3.forward;
                    Debug.LogWarning("Pipe start points too close, using default direction");
                }
            }

            // 회전 계산 개선: 모든 주요 축에 대한 처리
            Vector3 upVector = DetermineUpVector(initialDirection);
            
            var rotation = Quaternion.LookRotation(initialDirection, upVector);
            _previousRotation = rotation;
            _bezierPoints.Add(new BezierPoint(Points[0], rotation));

            for (int pipePoint = 1; pipePoint < Points.Count - 1; pipePoint++)
            {
                for (int s = 0; s < _generator.CurvedSegmentCount + 1; s++)
                {
                    _bezierPoints.Add(GetBezierPoint((s / (float)_generator.CurvedSegmentCount), pipePoint));
                    if (s == 0 || s == _generator.CurvedSegmentCount)
                        ringPoints.Add(_bezierPoints.Count - 1);
                }
            }

            // Use stable rotation calculation for final point
            Vector3 finalDirection = Vector3.forward;
            if (Points.Count >= 2)
            {
                // 끝에서 앞쪽으로 향하는 방향
                finalDirection = (Points[Points.Count - 1] - Points[Points.Count - 2]).normalized;
                if (finalDirection == Vector3.zero)
                {
                    finalDirection = initialDirection;
                    Debug.LogWarning("Pipe end points too close, using initial direction");
                }
            }
            
            // 최종 점에 대한 회전 계산 개선
            upVector = DetermineUpVector(finalDirection);
            
            Quaternion finalRotation = Quaternion.LookRotation(finalDirection, upVector);
            // Preserve rotation consistency with previous segments
            finalRotation *= Quaternion.AngleAxis(_currentAngleOffset, Vector3.forward);
            _bezierPoints.Add(new BezierPoint(Points[Points.Count - 1], finalRotation));

            GenerateVertices();
            GenerateUVs();
            GenerateTriangles();

            meshes.Add(new Mesh
            {
                vertices = _verts.ToArray(),
                normals = _normals.ToArray(),
                uv = _uvs.ToArray(),
                triangles = _triIndices.ToArray()
            });
            _verts = new List<Vector3>();
            _normals = new List<Vector3>();
            _uvs = new List<Vector2>();
            _triIndices = new List<int>();

            if (_generator.HasRings)
            {
                if (_generator.HasExtrusion)
                {
                    GenerateVertices(isExtruded: true);
                    GenerateUVs();
                    GenerateTriangles(isExtruded: true);

                    for (int i = 1; i < _bezierPoints.Count - 1; i += _generator.CurvedSegmentCount + 1)
                    {
                        _planes.Add(new PlaneInfo(_bezierPoints[i], _generator.RingRadius + _generator.Radius, false));
                        if (i + _generator.CurvedSegmentCount >= _bezierPoints.Count) break;
                        _planes.Add(new PlaneInfo(_bezierPoints[i + _generator.CurvedSegmentCount], _generator.RingRadius + _generator.Radius, true));
                    }
                }
                else
                {
                    foreach (var point in ringPoints) GenerateDisc(_bezierPoints[point]);
                }
            }

            if (_generator.HasCaps)
            {
                GenerateDisc(_bezierPoints[_bezierPoints.Count - 1]);
                GenerateDisc(_bezierPoints[0]);
            }

            foreach (var plane in _planes) GeneratePlane(plane);

            // 두 번째 메시 생성 및 최적화
            var secondMesh = new Mesh
            {
                vertices = _verts.ToArray(),
                normals = _normals.ToArray(),
                uv = _uvs.ToArray(),
                triangles = _triIndices.ToArray()
            };
            
            // 메시 최적화 - 노멀 재계산 및 바운드 최적화
            if (secondMesh.vertexCount > 0)
            {
                // 메시의 방향이 잘못되었을 경우를 대비해 노멀 재계산
                secondMesh.RecalculateNormals();
                secondMesh.RecalculateBounds();
                // 최적화 (필요시)
                // secondMesh.Optimize();
                
                meshes.Add(secondMesh);
            }

            // 최종 메시 최적화
            foreach (var mesh in meshes)
            {
                if (mesh.vertexCount > 0)
                {
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                }
            }

            return meshes;
        }

        private void ClearMeshInfo()
        {
            _verts = new List<Vector3>();
            _normals = new List<Vector3>();
            _uvs = new List<Vector2>();
            _triIndices = new List<int>();
            _bezierPoints = new List<BezierPoint>();
            _planes = new List<PlaneInfo>();
            _currentAngleOffset = 0f; // Reset angle offset
        }

        private BezierPoint GetBezierPoint(float t, int x)
        {
            // Validate indices to prevent out-of-range errors
            if (x < 1 || x >= Points.Count - 1)
            {
                Debug.LogError($"Invalid point index {x} for pipe with {Points.Count} points");
                // Return a default point with the previous rotation if possible
                return new BezierPoint(
                    Points[Mathf.Clamp(x, 0, Points.Count - 1)], 
                    _previousRotation != Quaternion.identity ? _previousRotation : Quaternion.identity
                );
            }

            Vector3 prev, next;
            float minSegmentLength = 0.01f; // Minimum segment length to avoid numerical issues

            // Calculate previous point with better curvature handling
            Vector3 toPrev = Points[x] - Points[x - 1];
            float prevDist = toPrev.magnitude;
            
            if (prevDist < minSegmentLength)
            {
                // Points are too close, use a small offset
                prev = Points[x] + (x > 1 ? (Points[x-2] - Points[x-1]).normalized : Vector3.forward) * minSegmentLength;
                Debug.LogWarning($"Pipe segment {x-1}->{x} is too short ({prevDist}), using minimal offset");
            }
            else if (prevDist > _generator.Curvature * 2 + _ringThickness)
            {
                prev = Points[x] - toPrev.normalized * _generator.Curvature;
            }
            else
            {
                prev = (Points[x] + Points[x - 1]) / 2 + toPrev.normalized * _ringThickness / 2;
            }

            // Calculate next point with better curvature handling
            Vector3 toNext = Points[x] - Points[x + 1];
            float nextDist = toNext.magnitude;
            
            if (nextDist < minSegmentLength)
            {
                // Points are too close, use a small offset
                next = Points[x] + (x < Points.Count - 2 ? (Points[x+2] - Points[x+1]).normalized : Vector3.forward) * minSegmentLength;
                Debug.LogWarning($"Pipe segment {x}->{x+1} is too short ({nextDist}), using minimal offset");
            }
            else if (nextDist > _generator.Curvature * 2 + _ringThickness)
            {
                next = Points[x] - toNext.normalized * _generator.Curvature;
            }
            else
            {
                next = (Points[x] + Points[x + 1]) / 2 + toNext.normalized * _ringThickness / 2;
            }

            // Special handling for start and end segments
            if (x == 1)
            {
                if (prevDist > _generator.Curvature + _ringThickness * 2.5f)
                    prev = Points[x] - toPrev.normalized * _generator.Curvature;
                else
                    prev = Points[x - 1] + toPrev.normalized * _ringThickness * 2.5f;
            }
            else if (x == Points.Count - 2)
            {
                if (nextDist > _generator.Curvature + _ringThickness * 2.5f)
                    next = Points[x] - toNext.normalized * _generator.Curvature;
                else
                    next = Points[x + 1] + toNext.normalized * _ringThickness * 2.5f;
            }

            // Compute Bezier curve path
            Vector3 a = Vector3.Lerp(prev, Points[x], t);
            Vector3 b = Vector3.Lerp(Points[x], next, t);
            var position = Vector3.Lerp(a, b, t);

            // Compute tangent and up vectors for rotation
            Vector3 tangent = Vector3.zero;
            
            // Ensure we have valid offset for next point calculation
            float tOffset = Mathf.Min(t + 0.001f, 1.0f);
            if (tOffset <= t) tOffset = t + 0.001f; // Force a small offset if we're at 1.0
            
            Vector3 aNext = Vector3.LerpUnclamped(prev, Points[x], tOffset);
            Vector3 bNext = Vector3.LerpUnclamped(Points[x], next, tOffset);
            Vector3 nextPosition = Vector3.Lerp(aNext, bNext, tOffset);
            
            // Calculate forward direction
            Vector3 forward = (nextPosition - position).normalized;
            if (forward.magnitude < 0.001f) // If direction is too small, use previous direction
            {
                forward = _previousRotation * Vector3.forward;
            }
            
            // 개선된 업 벡터 계산 방식 적용
            Vector3 upVector = DetermineUpVector(forward);
            
            // Use LookRotation with our specified up vector
            Quaternion targetRotation = Quaternion.LookRotation(forward, upVector);
            
            // Calculate cross product for tangent
            tangent = Vector3.Cross(a - b, aNext - bNext);
            if (tangent.magnitude > 0.001f)
            {
                tangent.Normalize();
                // Refine rotation using our tangent
                targetRotation = Quaternion.LookRotation(forward, tangent);
            }

            // Maintain consistent rotation along the pipe by gradually adjusting
            if (t == 0)
            {
                // Calculate the correct angle offset to maintain consistency
                float angle = Quaternion.Angle(_previousRotation, targetRotation);
                
                // Find axis of rotation
                Vector3 axis = Vector3.Cross(_previousRotation * Vector3.forward, targetRotation * Vector3.forward);
                if (axis.magnitude > 0.001f)
                {
                    axis.Normalize();
                    // Project axis onto forward vector to get rotation around pipe
                    float dotProduct = Vector3.Dot(axis, forward);
                    _currentAngleOffset = angle * dotProduct;
                    
                    // Handle inverted rotation
                    if (Quaternion.Angle(targetRotation * Quaternion.AngleAxis(_currentAngleOffset, forward), _previousRotation) > 
                        Quaternion.Angle(targetRotation * Quaternion.AngleAxis(-_currentAngleOffset, forward), _previousRotation))
                    {
                        _currentAngleOffset = -_currentAngleOffset;
                    }
                }
                else
                {
                    // Default to zero if we can't determine the axis
                    _currentAngleOffset = 0;
                }
            }
            
            // Apply consistent rotation
            targetRotation *= Quaternion.AngleAxis(_currentAngleOffset, forward);
            
            // Blend with previous rotation for smoother transitions
            if (_previousRotation != Quaternion.identity)
            {
                targetRotation = Quaternion.Slerp(_previousRotation, targetRotation, 0.3f);
            }

            _previousRotation = targetRotation;
            return new BezierPoint(position, targetRotation);
        }

        private void GenerateUVs()
        {
            float length = 0;
            for (int i = 1; i < _bezierPoints.Count; i++)
                length += (_bezierPoints[i].Pos - _bezierPoints[i - 1].Pos).magnitude;

            float currentUV = 0;
            for (int i = 0; i < _bezierPoints.Count; i++)
            {
                if (i != 0)
                    currentUV += (_bezierPoints[i].Pos - _bezierPoints[i - 1].Pos).magnitude / length;

                for (int edge = 0; edge < _generator.EdgeCount; edge++)
                {
                    _uvs.Add(new Vector2(edge / (float)_generator.EdgeCount, currentUV * length));
                }
                _uvs.Add(new Vector2(1, currentUV * length));
            }
        }

        private void GenerateVertices(bool isExtruded = false)
        {
            for (int point = 0; point < _bezierPoints.Count; point++)
            {
                // 각 점에서 로컬 좌표계 기준 원 생성
                for (int i = 0; i < _generator.EdgeCount; i++)
                {
                    float t = i / (float)_generator.EdgeCount;
                    float angRad = t * 6.2831853f; // 2π
                    
                    // 원형 좌표 생성 (XY 평면 상의 원)
                    Vector3 direction = new Vector3(Mathf.Sin(angRad), Mathf.Cos(angRad), 0);
                    
                    // 방향 벡터를 구한 노멀을 통해 파이프 반지름 계산
                    float radius = isExtruded ? _generator.RingRadius + _generator.Radius : _generator.Radius;
                    
                    // 노멀과 정점 계산 (원을 현재 방향에 맞게 회전)
                    _normals.Add(_bezierPoints[point].LocalToWorldVector(-direction.normalized));
                    _verts.Add(_bezierPoints[point].LocalToWorldPosition(direction * radius));
                }

                // 원의 마지막 정점 (첫 정점과 동일하게 처리)
                _normals.Add(_normals[_normals.Count - _generator.EdgeCount]);
                _verts.Add(_verts[_verts.Count - _generator.EdgeCount]);
            }
        }

        private void GenerateTriangles(bool isExtruded = false)
        {
            var edges = _generator.EdgeCount + 1;
            for (int s = 0; s < _bezierPoints.Count - 1; s++)
            {
                if (isExtruded && s % (_generator.CurvedSegmentCount + 1) == 0) continue;
                if (!isExtruded && _generator.HasRings && _generator.HasExtrusion && s % (_generator.CurvedSegmentCount + 1) != 0) continue;

                int rootIndex = s * edges;
                int rootIndexNext = (s + 1) * edges;
                for (int i = 0; i < edges; i++)
                {
                    int currentA = rootIndex + i;
                    int currentB = rootIndex + (i + 1) % edges;
                    int nextA = rootIndexNext + i;
                    int nextB = rootIndexNext + (i + 1) % edges;

                    _triIndices.Add(currentA);
                    _triIndices.Add(nextA);
                    _triIndices.Add(nextB);
                    _triIndices.Add(currentA);
                    _triIndices.Add(nextB);
                    _triIndices.Add(currentB);
                }
            }
        }

        private void GenerateDisc(BezierPoint point)
        {
            var rootIndex = _verts.Count;
            bool isFirst = (point.Pos == _bezierPoints[0].Pos);
            bool isLast = (point.Pos == _bezierPoints[_bezierPoints.Count - 1].Pos);

            // 위치 조정
            if (isFirst)
                point.Pos -= point.LocalToWorldVector(Vector3.forward) * (_generator.CapThickness + _generator.CapOffset);
            else if (isLast)
                point.Pos += point.LocalToWorldVector(Vector3.forward) * _generator.CapOffset;
            else
                point.Pos -= point.LocalToWorldVector(Vector3.forward) * _ringThickness / 2;

            var radius = (isLast || isFirst) ? _generator.CapRadius + _generator.Radius : _generator.RingRadius + _generator.Radius;
            var uv = (isLast || isFirst) ? _generator.CapThickness : _ringThickness;

            // 디스크의 앞/뒤면 생성
            for (int p = 0; p < 2; p++)
            {
                // 원형 정점 생성
                for (int i = 0; i < _generator.EdgeCount; i++)
                {
                    float t = i / (float)_generator.EdgeCount;
                    float angRad = t * 6.2831853f;
                    
                    // 원형 좌표 (XY 평면 상의 원)
                    Vector3 direction = new Vector3(Mathf.Sin(angRad), Mathf.Cos(angRad), 0);
                    
                    // 노멀과 정점 계산
                    _normals.Add(point.LocalToWorldVector(-direction.normalized));
                    _verts.Add(point.LocalToWorldPosition(direction * radius));
                    _uvs.Add(new Vector2(t, uv * p));
                }

                // 원의 마지막 정점 (첫 정점과 동일하게 처리)
                _normals.Add(_normals[_normals.Count - _generator.EdgeCount]);
                _verts.Add(_verts[_verts.Count - _generator.EdgeCount]);
                _uvs.Add(new Vector2(1, uv * p));

                _planes.Add(new PlaneInfo(point, radius, p == 0));

                // 다음 링을 위한 위치 조정
                if (isLast || isFirst)
                    point.Pos += point.LocalToWorldVector(Vector3.forward) * _generator.CapThickness;
                else
                    point.Pos += point.LocalToWorldVector(Vector3.forward) * _ringThickness;
            }

            // 원통 면의 삼각형 생성
            var edges = _generator.EdgeCount + 1;
            for (int i = 0; i < edges; i++)
            {
                _triIndices.Add(edges + (i + 1) % edges + rootIndex);
                _triIndices.Add(edges + i + rootIndex);
                _triIndices.Add(i + rootIndex);
                _triIndices.Add((i + 1) % edges + rootIndex);
                _triIndices.Add(edges + (i + 1) % edges + rootIndex);
                _triIndices.Add(i + rootIndex);
            }
        }

        private void GeneratePlane(PlaneInfo plane)
        {
            var edges = _generator.EdgeCount + 1;
            var rootIndex = _verts.Count;

            var planePointVectors = new List<Vector3>();

            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < _generator.EdgeCount; i++)
                {
                    float t = i / (float)_generator.EdgeCount;
                    float angRad = t * 6.2831853f;
                    Vector3 direction = new Vector3(Mathf.Sin(angRad), Mathf.Cos(angRad), 0);
                    planePointVectors.Add(direction);
                }
                planePointVectors.Add(planePointVectors[planePointVectors.Count - 1]);
            }

            for (int i = 0; i < planePointVectors.Count; i++)
            {
                _verts.Add(plane.Point.LocalToWorldPosition(planePointVectors[i] * plane.Radius));
                if (i > _generator.EdgeCount)
                    _normals.Add(plane.Point.LocalToWorldVector(plane.IsForward ? Vector3.back : Vector3.forward));
                else
                    _normals.Add(plane.Point.LocalToWorldVector(plane.IsForward ? Vector3.forward : Vector3.back));
                _uvs.Add(planePointVectors[i] * _generator.RingsUVScale);
            }

            for (int i = 1; i < edges - 1; i++)
            {
                if (plane.IsForward)
                {
                    _triIndices.Add(i + 1 + rootIndex);
                    _triIndices.Add(i + rootIndex);
                    _triIndices.Add(0 + rootIndex);
                }
                else
                {
                    _triIndices.Add(edges + rootIndex);
                    _triIndices.Add(edges + i + rootIndex);
                    _triIndices.Add(edges + i + 1 + rootIndex);
                }
            }
        }

        // 방향에 따른 적절한 업 벡터 결정 메서드 추가
        private Vector3 DetermineUpVector(Vector3 direction)
        {
            // 정규화된 방향
            Vector3 normalizedDir = direction.normalized;
            
            // 방향 벡터의 절대값 계산
            float absX = Mathf.Abs(normalizedDir.x);
            float absY = Mathf.Abs(normalizedDir.y);
            float absZ = Mathf.Abs(normalizedDir.z);
            
            // 기본 업 벡터
            Vector3 upVector = Vector3.up;
            
            // 방향이 어느 축과 가장 정렬되어 있는지 확인
            if (absY > absX && absY > absZ)
            {
                // Y축과 정렬 - Z축을 업 벡터로 사용
                upVector = normalizedDir.y > 0 ? Vector3.forward : Vector3.back;
            }
            else if (absZ > absX && absZ > absY)
            {
                // Z축과 정렬 - Y축을 업 벡터로 사용
                upVector = Vector3.up;
            }
            else
            {
                // X축과 정렬 - Y축을 업 벡터로 사용
                // 만약 X축과 Y축이 모두 정렬되어 있다면 Z축 사용
                if (absY > 0.9f)
                {
                    upVector = Vector3.forward;
                }
            }
            
            // 방향 벡터와 업 벡터가 거의 평행한 경우 다른 벡터 선택
            if (Mathf.Abs(Vector3.Dot(normalizedDir, upVector)) > 0.9f)
            {
                if (upVector == Vector3.up || upVector == Vector3.down)
                {
                    upVector = Vector3.forward;
                }
                else if (upVector == Vector3.forward || upVector == Vector3.back)
                {
                    upVector = Vector3.up;
                }
                else
                {
                    upVector = Vector3.right;
                }
            }
            
            return upVector;
        }

        private struct BezierPoint
        {
            public Vector3 Pos;
            public Quaternion Rot;

            public BezierPoint(Vector3 pos, Quaternion rot)
            {
                this.Pos = pos;
                this.Rot = rot;
            }

            public Vector3 LocalToWorldPosition(Vector3 localSpacePos) => Rot * localSpacePos + Pos;
            
            // 수정된 벡터 변환 메서드 (회전만 적용, 위치 변환 없음)
            public Vector3 LocalToWorldVector(Vector3 localSpaceVec) => Rot * localSpaceVec;
        }

        private struct PlaneInfo
        {
            public BezierPoint Point;
            public float Radius;
            public bool IsForward;

            public PlaneInfo(BezierPoint point, float radius, bool isForward)
            {
                this.Point = point;
                this.Radius = radius;
                this.IsForward = isForward;
            }
        }
    }
}