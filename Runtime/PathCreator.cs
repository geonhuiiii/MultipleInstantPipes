using System.Collections.Generic;
using UnityEngine;

namespace InstantPipes
{
    [System.Serializable]
    public class PathCreator
    {
        public float Height = 5;
        public float GridRotationY = 0;
        public float Radius = 1;
        public float GridSize = 3;
        public float Chaos = 0;
        public float StraightPathPriority = 10;
        public float NearObstaclesPriority = 0;
        public int MaxIterations = 1000;

        public bool LastPathSuccess = true;

        public List<Vector3> Create(Vector3 startPosition, Vector3 startNormal, Vector3 endPosition, Vector3 endNormal, float radius = 1.0f)
        {
            Radius = radius;
            var path = new List<Vector3>();
            var pathStart = startPosition + startNormal.normalized * Height;
            var pathEnd = endPosition + endNormal.normalized * Height;
            var baseDirection = (pathEnd - pathStart).normalized;

            var pathPoints = FindPath(new Point(pathStart), new Point(pathEnd), startNormal.normalized);

            path.Add(startPosition);
            path.Add(pathStart);

            LastPathSuccess = true;

            if (pathPoints != null)
            {
                pathPoints.ForEach(pathPoint => path.Add(pathPoint.Position));
            }
            else
            {
                LastPathSuccess = false;
            }

            path.Add(pathEnd);

            return path;
        }
        public List<List<Vector3>> CreateMultiplePaths(
            List<(Vector3 startPosition, Vector3 startNormal, 
                  Vector3 endPosition, Vector3 endNormal, 
                  float radius)> pipeConfigs)
        {
            // 모든 경로에 대한 결과를 저장할 리스트
            List<List<Vector3>> allPaths = new List<List<Vector3>>();
            
            //Debug.Log($"다중 경로 생성 시작: {pipeConfigs.Count}개의 파이프 경로 계산");
            
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
            
            //Debug.Log($"바운딩 박스 계산 완료: Min({minX}, {minY}, {minZ}), Max({maxX}, {Height+maxY}, {maxZ})");
            
            
            // pipe config 형식 변환
            List<((float[], string), (float[], string), float, float)> pipes = new List<((float[], string), (float[], string), float, float)>();
            
            foreach (var config in pipeConfigs)
            {
                var path = Create(config.startPosition, config.startNormal.normalized * Height, config.endPosition, config.endNormal.normalized * Height, config.radius);
                allPaths.Add(path);
            }
            
            
            LastPathSuccess = allPaths.Count == pipeConfigs.Count;
            //Debug.Log($"다중 경로 생성 완료: {(LastPathSuccess ? "모든 경로 성공" : "일부 경로 실패")}");
            
            return allPaths;
        }

        private List<Point> FindPath(Point start, Point target, Vector3 startNormal)
        {
            var toSearch = new List<Point> { start };
            var processed = new List<Point>();
            var visited = new List<Vector3>();
            var priorityFactor = start.GetDistanceTo(target) / 100;
            Random.InitState((int)(Chaos * 100));

            Dictionary<Vector3, Point> pointDictionary = new Dictionary<Vector3, Point>();

            //Debug.Log($"Starting path finding from {start.Position} to {target.Position} with radius: {Radius}");

            int iterations = 0;
            while (toSearch.Count > 0 && iterations < MaxIterations)
            {
                iterations++;

                var current = toSearch[0];
                foreach (var t in toSearch)
                    if (t.F < current.F || t.F == current.F && t.H < current.H) current = t;

                visited.Add(current.Position);
                processed.Add(current);
                toSearch.Remove(current);

                if (Vector3.Distance(current.Position, target.Position) <= GridSize * 1.5f)
                {
                    var currentPathPoint = current;
                    var path = new List<Point>();
                    while (currentPathPoint != start)
                    {
                        if (path.Count == 0 || !AreOnSameLine(path[path.Count - 1].Position, currentPathPoint.Position, currentPathPoint.Connection.Position))
                        {
                            path.Add(currentPathPoint);
                        }
                        currentPathPoint = currentPathPoint.Connection;
                    }
                    path.Reverse();
                    
                    SmoothPath(path, visited);
                    
                    //Debug.Log($"Path found! Points: {path.Count}");
                    return path;
                }

                var neighborPositions = current.GetNeighbors(GridSize, Radius, Quaternion.AngleAxis(GridRotationY, Vector3.up));
                foreach (var position in neighborPositions)
                {
                    if (!pointDictionary.ContainsKey(position))
                    {
                        pointDictionary.Add(position, new Point(position));
                    }
                    current.Neighbors.Add(pointDictionary[position]);
                }

                foreach (var neighbor in current.Neighbors)
                {
                    if (ContainsVector(visited, neighbor.Position)) continue;

                    var costToNeighbor = current.G + GridSize;

                    if (current.Connection != null && (current.Connection.Position - current.Position).normalized != (current.Connection.Position - neighbor.Position).normalized)
                    {
                        costToNeighbor += StraightPathPriority * priorityFactor;
                    }

                    costToNeighbor += Random.Range(-Chaos, Chaos) * priorityFactor;

                    if (NearObstaclesPriority != 0)
                    {
                        float distanceToObstacle = neighbor.GetDistanceToNearestObstacle();
                        
                        if (distanceToObstacle < Radius * 2.0f)
                        {
                            costToNeighbor += 50000;
                            //Debug.Log($"Very close to obstacle: {distanceToObstacle} < {Radius * 2.0f}, adding 50000 to cost");
                        }
                        else if (distanceToObstacle < Radius * 4.0f)
                        {
                            costToNeighbor += NearObstaclesPriority * 20 * priorityFactor;
                            //Debug.Log($"Close to obstacle: {distanceToObstacle} < {Radius * 4.0f}, adding {NearObstaclesPriority * 20 * priorityFactor} to cost");
                        }
                        else
                        {
                            costToNeighbor += NearObstaclesPriority * distanceToObstacle * priorityFactor / 20;
                        }
                    }

                    if (!toSearch.Contains(neighbor) || costToNeighbor < neighbor.G)
                    {
                        neighbor.G = costToNeighbor;
                        neighbor.Connection = current;

                        if (!toSearch.Contains(neighbor))
                        {
                            neighbor.H = neighbor.GetDistanceTo(target);
                            toSearch.Add(neighbor);
                        }
                    }
                }
            }

            //Debug.Log($"Path not found after {iterations} iterations.");
            return null;
        }

        private void SmoothPath(List<Point> path, List<Vector3> visited)
        {
            if (path.Count <= 2) return;

            int i = 0;
            while (i < path.Count - 2)
            {
                Point current = path[i];
                
                for (int j = path.Count - 1; j > i + 1; j--)
                {
                    Point target = path[j];
                    
                    bool clearPath = true;
                    Vector3 direction = (target.Position - current.Position).normalized;
                    float distance = Vector3.Distance(current.Position, target.Position);
                    
                    for (float step = 0; step < distance; step += Radius)
                    {
                        Vector3 point = current.Position + direction * step;
                        if (Physics.CheckSphere(point, Radius * 1.2f))
                        {
                            clearPath = false;
                            break;
                        }
                    }
                    
                    if (clearPath)
                    {
                        path.RemoveRange(i + 1, j - i - 1);
                        break;
                    }
                }
                
                i++;
            }
        }

        private bool AreOnSameLine(Vector3 point1, Vector3 point2, Vector3 point3)
        {
            return Vector3.Cross(point2 - point1, point3 - point1).sqrMagnitude < 0.0001f;
        }

        private bool ContainsVector(List<Vector3> list, Vector3 vector)
        {
            foreach (var pos in list)
            {
                if ((vector - pos).sqrMagnitude < 0.0001f) return true;
            }
            return false;
        }

        private class Point
        {
            public Vector3 Position;
            public List<Point> Neighbors;
            public Point Connection;

            public float G;
            public float H;
            public float F => G + H;

            private readonly Vector3[] _directions = { 
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, 
                Vector3.forward, Vector3.back
            };

            public Point(Vector3 position)
            {
                Position = position;
                Neighbors = new List<Point>();
            }

            public float GetDistanceTo(Point other)
            {
                var distance = Vector3.Distance(other.Position, Position);
                return distance * 10;
            }

            public float GetDistanceToNearestObstacle()
            {
                float minDistance = 200;

                foreach (var direction in _directions)
                {
                    if (Physics.Raycast(Position, direction, out RaycastHit hitPoint, 200))
                    {
                        minDistance = Mathf.Min(minDistance, hitPoint.distance);
                    }
                }

                Collider[] colliders = Physics.OverlapSphere(Position, 10f);
                foreach (var collider in colliders)
                {
                    if (collider.isTrigger)
                        continue;
                        
                    Vector3 closestPoint = collider.ClosestPoint(Position);
                    float distance = Vector3.Distance(Position, closestPoint);
                    minDistance = Mathf.Min(minDistance, distance);
                }

                return minDistance;
            }

            public List<Vector3> GetNeighbors(float gridSize, float radius, Quaternion rotation)
            {
                var list = new List<Vector3>();
                
                //Debug.Log($"Getting neighbors with gridSize: {gridSize}, radius: {radius}");

                Vector3[] extendedDirections = new Vector3[_directions.Length];
                System.Array.Copy(_directions, extendedDirections, _directions.Length);

                foreach (var direction in extendedDirections)
                {
                    var rotatedDirection = rotation * direction;
                    
                    if (!Physics.SphereCast(Position, radius * 1.2f, rotatedDirection, out RaycastHit hit, gridSize + radius))
                    {
                        list.Add(Position + rotatedDirection * gridSize);
                    }
                    else
                    {
                        //Debug.Log($"Obstacle detected at distance: {hit.distance} in direction: {rotatedDirection}");
                    }
                }

                return list;
            }
        }
    }
}