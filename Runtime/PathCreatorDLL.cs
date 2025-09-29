using System.Collections.Generic;
using System.Net;
using UnityEngine;
using System.Runtime.InteropServices;
using System;
using System.Threading.Tasks;


namespace InstantPipes
{
    

    [StructLayout(LayoutKind.Sequential)]
    public struct VECTOR3
    {
        public float x;
        public float y;
        public float z;

        public VECTOR3(Vector3 vector)
        {
            x = vector.x;
            y = vector.y;
            z = vector.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Obstacle
    {
        public VECTOR3 Position;
        public float distance;

        public Obstacle(VECTOR3 position, float distance)
        {
            Position = position;
            this.distance = distance;
        }
    }

    [System.Serializable]
    public unsafe class PathCreatorDLL
    {
        private NewPathfinderDLL _pathfinder;

        [DllImport("pathfinder")] 
        unsafe private static extern void FindPath(VECTOR3 start, VECTOR3 end, VECTOR3 *outdata, ref int returnSize);
        
        public float Height = 5;
        public float GridRotationY = 0;
        public float Radius = 1;
        public float GridSize = 3;
        public float Chaos = 0;
        public float StraightPathPriority = 10;
        public float NearObstaclesPriority = 0;
        public int MaxIterations = 1000;
        public bool hasCollision = false;

        public bool LastPathSuccess = true;
        int pathCounts = 0;
        VECTOR3* flatArray = null;
        public int* obstaclesArray = null;
        public int obstacleCount = 0;
        Vector3[] directions = new Vector3[]
        {
            Vector3.up,
            Vector3.down,
            Vector3.left,
            Vector3.right,
            Vector3.forward,
            Vector3.back
        };
        
        
        public List<Vector3> Create(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float pipeRadius)
        {
            hasCollision = false;
            Radius = pipeRadius;
            var path = new List<Vector3>();
            var pathStart = startPoint + startNormal.normalized * Height;
            var pathEnd = endPoint + endNormal.normalized * Height;
            var baseDirection = (pathEnd - pathStart).normalized;
            var gridSize = GridSize;
            float minX = Mathf.Min(pathStart.x, pathEnd.x) - gridSize * 2.0f;
            float maxX = Mathf.Max(pathStart.x, pathEnd.x) + gridSize * 2.0f;
            float minY = Mathf.Min(pathStart.y, pathEnd.y);
            float maxY = Mathf.Max(pathStart.y, pathEnd.y) + gridSize * 2.0f;
            float minZ = Mathf.Min(pathStart.z, pathEnd.z) - gridSize * 2.0f;
            float maxZ = Mathf.Max(pathStart.z, pathEnd.z) + gridSize * 2.0f;

            int countX = Mathf.FloorToInt((maxX - minX) / gridSize);
            int countY = Mathf.FloorToInt((maxY - minY) / gridSize);
            int countZ = Mathf.FloorToInt((maxZ - minZ) / gridSize);


            VECTOR3 start = new VECTOR3(pathStart);
            VECTOR3 end = new VECTOR3(pathEnd);
            var pathPoints = new List<Point>();
            VECTOR3* flatArray = stackalloc VECTOR3[countX * countY * countZ];
            DateTime tstart = DateTime.UtcNow;
            FindPath(start, end, flatArray, ref pathCounts);
            UnityEngine.Debug.Log("Pathfinding task completed");

            for (int i = 0; i < pathCounts; i++)
            {
                pathPoints.Add(new Point(flatArray[i].ToVector3()));
                //*(obstaclesArray + (int)(MathF.Round(flatArray[i].x/gridSize)) % countX + (int)(MathF.Round(flatArray[i].y/gridSize)) / countX % countY + (int)MathF.Round(flatArray[i].z/gridSize) / (countX * countY) % countZ) = 1;
            }




            path.Add(startPoint);
            path.Add(pathStart);

            LastPathSuccess = true;

            if (pathPoints != null)
            {

                pathPoints.ForEach(pathPoint => path.Add(pathPoint.Position));
                pathPoints.RemoveAt(0);
                pathPoints.RemoveAt(0);
                pathPoints.RemoveAt(pathPoints.Count - 1);
                pathPoints.RemoveAt(pathPoints.Count - 1);
                foreach (var pp in pathPoints)
                {
                    if (hasCollision == false)
                        hasCollision = pp.GetCollision(Radius * 0.5f);
                    if (hasCollision)
                        break;
                }
            }
            else
            {
                LastPathSuccess = false;
            }

            path.Add(pathEnd);
            path.Add(endPoint);
            DateTime tend = DateTime.UtcNow;
            TimeSpan elapsed = tend - tstart;

            UnityEngine.Debug.Log($"DLL 경로 생성 실행 시간: {elapsed.TotalMilliseconds} ms");

            return path;
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
                        if (Physics.CheckSphere(point, Radius * 1.5f))
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


                return minDistance;
            }

            public bool GetCollision(float radius)
            {
                return Physics.CheckSphere(Position, radius);
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

                    if (!Physics.SphereCast(Position, radius, rotatedDirection, out RaycastHit hit, gridSize + radius))
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
