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

        private float _ringThickness => _generator.HasExtrusion ? 0 : Mathf.Max(0.1f, _generator.RingThickness);

        // 원통 생성 평면 유형 (기본값: Y-X 평면)
        private PlaneType _cylinderPlane = PlaneType.YX;

        // 평면 유형 열거형
        public enum PlaneType
        {
            YX, // Y-X 평면 (원래 기본값): Z축이 파이프 길이 방향
            XZ, // X-Z 평면 (Y축이 높이): Y축이 파이프 길이 방향
            YZ  // Y-Z 평면 (X축이 길이): X축이 파이프 길이 방향
        }

        public Pipe(List<Vector3> points)
        {
            Points = points;
        }

        public Pipe(List<Vector3> points, PlaneType cylinderPlane)
        {
            Points = points;
            _cylinderPlane = cylinderPlane;
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

        /// <summary>
        /// 파이프 메시를 생성하고 콜라이더 설정에 필요한 정보를 포함하여 반환합니다.
        /// </summary>
        public List<Mesh> GenerateMeshes(PipeGenerator generator)
        {
            return GenerateMeshes(generator, _cylinderPlane);
        }

        /// <summary>
        /// 지정된 평면 방향으로 파이프 메시를 생성합니다.
        /// </summary>
        public List<Mesh> GenerateMeshes(PipeGenerator generator, PlaneType cylinderPlane)
        {
            var meshes = new List<Mesh>();
            _cylinderPlane = cylinderPlane;
            _generator = generator;

            ClearMeshInfo();

            var ringPoints = new List<int>();

            var direction = (Points[0] - Points[1]).normalized;
            
            // 방향에 따른 초기 회전 설정
            Quaternion rotation;
            if (direction != Vector3.zero)
            {
                // 평면 유형에 따라 적절한 up 벡터 선택
                Vector3 upVector = GetUpVector();
                rotation = Quaternion.LookRotation(direction, upVector);
            }
            else
            {
                rotation = Quaternion.identity;
            }
            
            _previousRotation = rotation;
            _bezierPoints.Add(new BezierPoint(Points[0], rotation));

            // 링 생성 간격 조정을 위한 최소 거리 계산
            float minRingDistance = Mathf.Max(0.5f, _generator.Radius * 20f); // 반지름의 2배 이상 간격으로 줄임
            float lastRingPosition = 0f;
            float totalPathLength = 0f;

            // 전체 경로 길이 계산
            for (int i = 1; i < Points.Count; i++)
            {
                totalPathLength += Vector3.Distance(Points[i-1], Points[i]);
            }

            for (int pipePoint = 1; pipePoint < Points.Count - 1; pipePoint++)
            {
                bool shouldAddRingPoint = false;
                
                // 각 파이프 포인트 사이 거리 계산
                float segmentDistance = Vector3.Distance(Points[pipePoint-1], Points[pipePoint]);
                float normalizedPos = segmentDistance / totalPathLength;
                
                // 거리 기반 링 추가 로직 - 더 관대하게 조건 수정
                if (segmentDistance > minRingDistance)
                {
                    shouldAddRingPoint = true;
                }
                
                // 강제로 링 포인트 추가 (링 옵션이 켜져 있을 때 최소한 몇 개의 링은 나타나도록)
                if (_generator.HasRings && pipePoint == Points.Count / 2)
                {
                    shouldAddRingPoint = true;
                }
                
                for (int s = 0; s < _generator.CurvedSegmentCount + 1; s++)
                {
                    float t = s / (float)_generator.CurvedSegmentCount;
                    _bezierPoints.Add(GetBezierPoint(t, pipePoint));
                    
                    // 링 위치 결정 로직 - 더 자주 링을 생성하도록 조건 수정
                    if (shouldAddRingPoint)
                    {
                        if (s == 0 || s == _generator.CurvedSegmentCount / 2 || s == _generator.CurvedSegmentCount)
                        {
                            // 최소 거리 조건 확인 (더 짧은 거리 허용)
                            float currentPos = lastRingPosition + segmentDistance * t;
                            if (currentPos - lastRingPosition >= minRingDistance * 0.5f)
                            {
                                ringPoints.Add(_bezierPoints.Count - 1);
                                lastRingPosition = currentPos;
                            }
                        }
                    }
                }
            }

            _bezierPoints.Add(new BezierPoint(Points[Points.Count - 1], _previousRotation));

            GenerateVertices();
            GenerateUVs();
            GenerateTriangles();

            var mainMesh = new Mesh
            {
                vertices = _verts.ToArray(),
                normals = _normals.ToArray(),
                uv = _uvs.ToArray(),
                triangles = _triIndices.ToArray()
            };
            mainMesh.RecalculateBounds();
            meshes.Add(mainMesh);
            
            _verts = new List<Vector3>();
            _normals = new List<Vector3>();
            _uvs = new List<Vector2>();
            _triIndices = new List<int>();

            // 중간 링 생성 (캡과 별도로 처리)
            if (_generator.HasRings)
            {
                // HasRings가 활성화된 경우 링 포인트가 없더라도 최소한 중간에 링을 생성
                if (ringPoints.Count == 0 && _bezierPoints.Count > 3)
                {
                    // 중간 지점에 링 포인트 강제 추가
                    int middlePointIndex = _bezierPoints.Count / 2;
                    ringPoints.Add(middlePointIndex);
                    Debug.Log($"링 포인트가 없어 중간 지점({middlePointIndex})에 강제로 링 추가");
                }
                
                if (_generator.HasExtrusion)
                {
                    GenerateVertices(isExtruded: true);
                    GenerateUVs();
                    GenerateTriangles(isExtruded: true);

                    // 링 간격 조정
                    int ringStep = Mathf.Max(1, _generator.CurvedSegmentCount / 2);
                    _planes.Clear(); // 기존 평면 정보 초기화
                    
                    // 압출된 링 생성 로직 개선 - 항상 일정 개수의 링이 생성되게 함
                    if (_bezierPoints.Count > 5)
                    {
                        // 최소 2개의 링은 항상 생성
                        int numRings = Mathf.Max(2, _bezierPoints.Count / (_generator.CurvedSegmentCount * 3));
                        int ringInterval = _bezierPoints.Count / (numRings + 1);
                        
                        Debug.Log($"압출 방식 링 생성: 총 {numRings}개 (간격: {ringInterval})");
                        
                        for (int ringIdx = 1; ringIdx <= numRings; ringIdx++)
                        {
                            int pointIndex = ringIdx * ringInterval;
                            if (pointIndex > 0 && pointIndex < _bezierPoints.Count - 2)
                            {
                                _planes.Add(new PlaneInfo(
                                    _bezierPoints[pointIndex], 
                                    _generator.RingRadius + _generator.Radius * 1.2f, 
                                    false
                                ));
                                
                                _planes.Add(new PlaneInfo(
                                    _bezierPoints[pointIndex], 
                                    _generator.RingRadius + _generator.Radius * 1.2f, 
                                    true
                                ));
                            }
                        }
                    }
                    else
                    {
                        // 짧은 파이프의 경우 중간에 링 하나만 생성
                        if (_bezierPoints.Count > 3)
                        {
                            int midPoint = _bezierPoints.Count / 2;
                            _planes.Add(new PlaneInfo(
                                _bezierPoints[midPoint], 
                                _generator.RingRadius + _generator.Radius * 1.2f, 
                                false
                            ));
                            
                            _planes.Add(new PlaneInfo(
                                _bezierPoints[midPoint], 
                                _generator.RingRadius + _generator.Radius * 1.2f, 
                                true
                            ));
                            
                            Debug.Log("짧은 파이프용 압출 링 생성: 중간에 1개");
                        }
                    }
                }
                else
                {
                    // 링 포인트가 있는 경우 처리
                    if (ringPoints.Count > 0)
                    {
                        // 최대 링 개수를 조정 (링 옵션이 켜져 있으면 더 많은 링 허용)
                        int maxRings = Mathf.Min(ringPoints.Count, Mathf.Max(3, ringPoints.Count));
                        int step = Mathf.Max(1, ringPoints.Count / maxRings);
                        
                        // 링 생성 로직 개선 (더 많은 링을 생성)
                        for (int i = 0; i < ringPoints.Count; i += step)
                        {
                            int ringPointIndex = ringPoints[i];
                            if (ringPointIndex > 0 && ringPointIndex < _bezierPoints.Count - 1)
                            {
                                GenerateDisc(_bezierPoints[ringPointIndex]);
                            }
                        }
                        
                        // 디버그 로그로 생성된 링 개수 출력
                        Debug.Log($"총 {ringPoints.Count}개의 링 포인트 중 {(ringPoints.Count + step - 1) / step}개의 링 생성");
                    }
                    else if (_bezierPoints.Count > 3)
                    {
                        // 링 포인트가 없는 경우에도 중간에 하나라도 생성
                        int middlePointIndex = _bezierPoints.Count / 2;
                        GenerateDisc(_bezierPoints[middlePointIndex]);
                        Debug.Log("링 포인트가 없어 중간에 하나의 링만 생성");
                    }
                }
            }

            // 캡 생성 로직은 독립적으로 유지
            if (_generator.HasCaps)
            {
                // 명시적으로 시작과 끝 캡 생성
                GenerateDisc(_bezierPoints[_bezierPoints.Count - 1], true); // 끝 캡
                GenerateDisc(_bezierPoints[0], true); // 시작 캡
            }

            foreach (var plane in _planes) GeneratePlane(plane);

            var secondaryMesh = new Mesh
            {
                vertices = _verts.ToArray(),
                normals = _normals.ToArray(),
                uv = _uvs.ToArray(),
                triangles = _triIndices.ToArray()
            };
            secondaryMesh.RecalculateBounds();
            meshes.Add(secondaryMesh);

            return meshes;
        }

        /// <summary>
        /// 평면 유형에 따른 Up 벡터 반환
        /// </summary>
        private Vector3 GetUpVector()
        {
            switch (_cylinderPlane)
            {
                case PlaneType.YX:
                    return Vector3.up; // Z축 파이프는 Y를 Up으로
                case PlaneType.XZ:
                    return Vector3.forward; // Y축 파이프는 Z를 Up으로
                case PlaneType.YZ:
                    return Vector3.up; // X축 파이프는 Y를 Up으로
                default:
                    return Vector3.up;
            }
        }

        /// <summary>
        /// 평면 유형에 따른 Forward 벡터 반환
        /// </summary>
        private Vector3 GetForwardVector()
        {
            switch (_cylinderPlane)
            {
                case PlaneType.YX:
                    return Vector3.forward; // Z축 방향
                case PlaneType.XZ:
                    return Vector3.up; // Y축 방향
                case PlaneType.YZ:
                    return Vector3.right; // X축 방향
                default:
                    return Vector3.forward;
            }
        }

        /// <summary>
        /// 메시에 콜라이더를 설정합니다.
        /// </summary>
        public static void SetupCollider(GameObject gameObject, Mesh mesh, bool isConvex = true)
        {
            MeshCollider meshCollider = gameObject.GetComponent<MeshCollider>();
            if (meshCollider == null)
                meshCollider = gameObject.AddComponent<MeshCollider>();
                
            meshCollider.sharedMesh = mesh;
            
            // Convex 옵션 활성화 - 물리 시뮬레이션을 빠르고 안정적으로 만듭니다
            meshCollider.convex = isConvex;
            
            // Cooking Options 설정 (Unity 2020.1 이상)
            #if UNITY_2020_1_OR_NEWER
            meshCollider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation 
                                        | MeshColliderCookingOptions.EnableMeshCleaning
                                        | MeshColliderCookingOptions.WeldColocatedVertices;
            #endif
        }

        private void ClearMeshInfo()
        {
            _verts = new List<Vector3>();
            _normals = new List<Vector3>();
            _uvs = new List<Vector2>();
            _triIndices = new List<int>();
            _bezierPoints = new List<BezierPoint>();
            _planes = new List<PlaneInfo>();
        }

        private BezierPoint GetBezierPoint(float t, int x)
        {
            Vector3 prev, next;

            if ((Points[x] - Points[x - 1]).magnitude > _generator.Curvature * 2 + _ringThickness)
                prev = Points[x] - (Points[x] - Points[x - 1]).normalized * _generator.Curvature;
            else
                prev = (Points[x] + Points[x - 1]) / 2 + (Points[x] - Points[x - 1]).normalized * _ringThickness / 2;

            if ((Points[x] - Points[x + 1]).magnitude > _generator.Curvature * 2 + _ringThickness)
                next = Points[x] - (Points[x] - Points[x + 1]).normalized * _generator.Curvature;
            else
                next = (Points[x] + Points[x + 1]) / 2 + (Points[x] - Points[x + 1]).normalized * _ringThickness / 2;

            if (x == 1)
            {
                if ((Points[x] - Points[x - 1]).magnitude > _generator.Curvature + _ringThickness * 2.5f)
                    prev = Points[x] - (Points[x] - Points[x - 1]).normalized * _generator.Curvature;
                else
                    prev = Points[x - 1] + (Points[x] - Points[x - 1]).normalized * _ringThickness * 2.5f;
            }

            else if (x == Points.Count - 2)
            {
                if ((Points[x] - Points[x + 1]).magnitude > _generator.Curvature + _ringThickness * 2.5f)
                    next = Points[x] - (Points[x] - Points[x + 1]).normalized * _generator.Curvature;
                else
                    next = Points[x + 1] + (Points[x] - Points[x + 1]).normalized * _ringThickness * 2.5f;
            }

            Vector3 a = Vector3.Lerp(prev, Points[x], t);
            Vector3 b = Vector3.Lerp(Points[x], next, t);
            var position = Vector3.Lerp(a, b, t);

            Vector3 aNext = Vector3.LerpUnclamped(prev, Points[x], t + 0.001f);
            Vector3 bNext = Vector3.LerpUnclamped(Points[x], next, t + 0.001f);

            var tangent = Vector3.Cross(a - b, aNext - bNext);
            
            // 평면 타입에 따라 적절한 up 벡터 적용
            Vector3 upVector = GetUpVector();
            
            var rotation = (a != b) ? Quaternion.LookRotation((a - b).normalized, tangent != Vector3.zero ? tangent.normalized : upVector) : Quaternion.identity;

            // Rotate new tangent along the forward axis to match the previous part
            if (t == 0)
            {
                _currentAngleOffset = Quaternion.Angle(_previousRotation, rotation);
                var offsetRotation = Quaternion.AngleAxis(_currentAngleOffset, GetForwardVector());
                if (Quaternion.Angle(rotation * offsetRotation, _previousRotation) > 0)
                    _currentAngleOffset *= -1;
            }
            rotation *= Quaternion.AngleAxis(_currentAngleOffset, GetForwardVector());

            _previousRotation = rotation;
            return new BezierPoint(position, rotation);
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
                // 선택된 평면에 따라 정다각형 생성
                for (int i = 0; i < _generator.EdgeCount; i++)
                {
                    float t = i / (float)_generator.EdgeCount;
                    float angRad = t * 6.2831853f;
                    
                    // 선택된 평면에 따라 방향 벡터 계산
                    Vector3 direction;
                    if (_cylinderPlane == PlaneType.YX)
                    {
                        // Y-X 평면 (원래 방식): Z축이 파이프 길이 방향
                        direction = new Vector3(Mathf.Sin(angRad), Mathf.Cos(angRad), 0);
                    }
                    else if (_cylinderPlane == PlaneType.XZ)
                    {
                        // X-Z 평면 (Y축이 높이): Y축이 파이프 길이 방향
                        direction = new Vector3(Mathf.Sin(angRad), 0, Mathf.Cos(angRad));
                    }
                    else // PlaneType.YZ
                    {
                        // Y-Z 평면 (X축이 길이): X축이 파이프 길이 방향
                        direction = new Vector3(0, Mathf.Sin(angRad), Mathf.Cos(angRad));
                    }
                    
                    _normals.Add(_bezierPoints[point].LocalToWorldVector(direction.normalized));
                    _verts.Add(_bezierPoints[point].LocalToWorldPosition(direction * (isExtruded ? _generator.RingRadius + _generator.Radius : _generator.Radius)));
                }

                // 마지막 정점과 첫 정점이 연결되도록 첫 번째 정점을 다시 추가
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
                
                // 원통 측면의 삼각형 생성
                for (int i = 0; i < _generator.EdgeCount; i++)
                {
                    int currentA = rootIndex + i;
                    int currentB = rootIndex + (i + 1) % edges; // 마지막 정점과 첫 정점 연결
                    int nextA = rootIndexNext + i;
                    int nextB = rootIndexNext + (i + 1) % edges; // 마지막 정점과 첫 정점 연결

                    // 사각형을 두 개의 삼각형으로 분할 (시계 방향으로 면이 바깥쪽을 향하게)
                    _triIndices.Add(nextB);
                    _triIndices.Add(nextA);
                    _triIndices.Add(currentA);
                    
                    _triIndices.Add(currentB);
                    _triIndices.Add(nextB);
                    _triIndices.Add(currentA);
                }
            }
        }

        private void GenerateDisc(BezierPoint point, bool isCap = false)
        {
            var rootIndex = _verts.Count;
            bool isFirst = (point.Pos == _bezierPoints[0].Pos);
            bool isLast = (point.Pos == _bezierPoints[_bezierPoints.Count - 1].Pos);

            if (isFirst)
                point.Pos -= point.LocalToWorldVector(Vector3.forward) * (_generator.CapThickness + _generator.CapOffset);
            else if (isLast)
                point.Pos += point.LocalToWorldVector(Vector3.forward) * _generator.CapOffset;
            else
                point.Pos -= point.LocalToWorldVector(Vector3.forward) * _ringThickness / 2;

            var radius = (isLast || isFirst) ? _generator.CapRadius + _generator.Radius : _generator.RingRadius + _generator.Radius;
            var uv = (isLast || isFirst) ? _generator.CapThickness : _ringThickness;

            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < _generator.EdgeCount; i++)
                {
                    float t = i / (float)_generator.EdgeCount;
                    float angRad = t * 6.2831853f;
                    Vector3 direction = new Vector3(Mathf.Sin(angRad), Mathf.Cos(angRad), 0);
                    _normals.Add(point.LocalToWorldVector(direction.normalized));
                    _verts.Add(point.LocalToWorldPosition(direction * radius));
                    _uvs.Add(new Vector2(t, uv * p));
                }

                _normals.Add(_normals[_normals.Count - _generator.EdgeCount]);
                _verts.Add(_verts[_verts.Count - _generator.EdgeCount]);
                _uvs.Add(new Vector2(1, uv * p));

                _planes.Add(new PlaneInfo(point, radius, p == 0));

                if (isLast || isFirst)
                    point.Pos += point.LocalToWorldVector(Vector3.forward) * _generator.CapThickness;
                else
                    point.Pos += point.LocalToWorldVector(Vector3.forward) * _ringThickness;

            }

            var edges = _generator.EdgeCount + 1;

            for (int i = 0; i < edges; i++)
            {
                _triIndices.Add(i + rootIndex);
                _triIndices.Add(edges + i + rootIndex);
                _triIndices.Add(edges + (i + 1) % edges + rootIndex);
                _triIndices.Add(i + rootIndex);
                _triIndices.Add(edges + (i + 1) % edges + rootIndex);
                _triIndices.Add((i + 1) % edges + rootIndex);
            }

        }

        private void GeneratePlane(PlaneInfo plane)
        {
            var edges = _generator.EdgeCount + 1;
            var rootIndex = _verts.Count;

            // 평면 유형에 따른 Forward 벡터 사용
            Vector3 forwardVector = GetForwardVector();

            // 평면 중심점 추가
            Vector3 centerPos = plane.Point.Pos;
            _verts.Add(centerPos);
            Vector3 faceNormal = plane.IsForward ? plane.Point.LocalToWorldVector(forwardVector) : plane.Point.LocalToWorldVector(-forwardVector);
            _normals.Add(faceNormal);
            _uvs.Add(new Vector2(0.5f, 0.5f)); // 중심점 UV

            var planePointVectors = new List<Vector3>();

            // 선택된 평면에 따라 정점 생성
            for (int i = 0; i < _generator.EdgeCount; i++)
            {
                float t = i / (float)_generator.EdgeCount;
                float angRad = t * 6.2831853f;
                
                // 선택된 평면에 따라 방향 벡터 계산
                Vector3 direction;
                if (_cylinderPlane == PlaneType.YX)
                {
                    // Y-X 평면 (원래 방식)
                    direction = new Vector3(Mathf.Sin(angRad), Mathf.Cos(angRad), 0);
                }
                else if (_cylinderPlane == PlaneType.XZ)
                {
                    // X-Z 평면 (Y축이 높이)
                    direction = new Vector3(Mathf.Sin(angRad), 0, Mathf.Cos(angRad));
                }
                else // PlaneType.YZ
                {
                    // Y-Z 평면 (X축이 길이)
                    direction = new Vector3(0, Mathf.Sin(angRad), Mathf.Cos(angRad));
                }
                
                planePointVectors.Add(direction);
                
                // 각 정점 추가
                Vector3 vertPos = plane.Point.LocalToWorldPosition(direction * plane.Radius);
                _verts.Add(vertPos);
                _normals.Add(faceNormal);
                
                // UV 계산
                Vector2 uvCoord;
                if (_cylinderPlane == PlaneType.YX)
                {
                    uvCoord = new Vector2((direction.x + 1) * 0.5f, (direction.y + 1) * 0.5f);
                }
                else if (_cylinderPlane == PlaneType.XZ)
                {
                    uvCoord = new Vector2((direction.x + 1) * 0.5f, (direction.z + 1) * 0.5f);
                }
                else // PlaneType.YZ
                {
                    uvCoord = new Vector2((direction.y + 1) * 0.5f, (direction.z + 1) * 0.5f);
                }
                _uvs.Add(uvCoord * _generator.RingsUVScale);
            }
            
            // 마지막 정점 다시 추가하여 원을 닫음
            Vector3 firstDirection = planePointVectors[0];
            Vector3 lastVertPos = plane.Point.LocalToWorldPosition(firstDirection * plane.Radius);
            _verts.Add(lastVertPos);
            _normals.Add(faceNormal);
            
            // UV 계산
            Vector2 lastUvCoord;
            if (_cylinderPlane == PlaneType.YX)
            {
                lastUvCoord = new Vector2((firstDirection.x + 1) * 0.5f, (firstDirection.y + 1) * 0.5f);
            }
            else if (_cylinderPlane == PlaneType.XZ)
            {
                lastUvCoord = new Vector2((firstDirection.x + 1) * 0.5f, (firstDirection.z + 1) * 0.5f);
            }
            else // PlaneType.YZ
            {
                lastUvCoord = new Vector2((firstDirection.y + 1) * 0.5f, (firstDirection.z + 1) * 0.5f);
            }
            _uvs.Add(lastUvCoord * _generator.RingsUVScale);

            // 평면의 삼각형 인덱스 생성 (부채꼴 형태)
            for (int i = 0; i < _generator.EdgeCount; i++)
            {
                if (plane.IsForward)
                {
                    // 시계 방향 (면이 앞쪽을 향함)
                    _triIndices.Add(rootIndex); // 중심점
                    _triIndices.Add(rootIndex + i + 1);
                    _triIndices.Add(rootIndex + ((i + 1) % _generator.EdgeCount) + 1);
                }
                else
                {
                    // 반시계 방향 (면이 뒤쪽을 향함)
                    _triIndices.Add(rootIndex); // 중심점
                    _triIndices.Add(rootIndex + ((i + 1) % _generator.EdgeCount) + 1);
                    _triIndices.Add(rootIndex + i + 1);
                }
            }
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
            public Vector3 LocalToWorldVector(Vector3 localSpacePos) => Rot * localSpacePos;
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