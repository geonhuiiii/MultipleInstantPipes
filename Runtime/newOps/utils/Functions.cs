using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;  // Unity Debug 클래스 사용을 위해 추가

namespace Utils
{
    public static class Functions
    {
        // 튜플 연산 (2D/3D, 숫자/튜플)
        public static float[] TupleOperations(float[] t1, object t2, string op)
        {
            // Null 체크
            if (t1 == null)
            {
                Debug.LogError("TupleOperations: t1 is null");
                return null;
            }
            
            if (t2 == null)
            {
                Debug.LogError("TupleOperations: t2 is null");
                return null;
            }
            
            if (string.IsNullOrEmpty(op))
            {
                Debug.LogError("TupleOperations: operator is null or empty");
                return null;
            }
            
            try
            {
                float[] result = new float[t1.Length];
                if (t2 is float num)
                {
                    for (int i = 0; i < t1.Length; i++)
                    {
                        result[i] = op switch
                        {
                            "+" => t1[i] + num,
                            "-" => t1[i] - num,
                            "*" => t1[i] * num,
                            "/" => t1[i] / (Math.Abs(num) < float.Epsilon ? 1.0f : num), // 0으로 나누기 방지
                            _ => throw new ArgumentException($"Invalid operator: {op}")
                        };
                    }
                }
                else if (t2 is float[] t2Arr)
                {
                    if (t1.Length != t2Arr.Length)
                    {
                        Debug.LogError($"TupleOperations: Tuple length mismatch. t1={t1.Length}, t2={t2Arr.Length}");
                        // 길이 불일치시 더 짧은 배열 기준으로 처리
                        int minLength = Math.Min(t1.Length, t2Arr.Length);
                        result = new float[t1.Length]; // 원래 t1 길이 유지
                        
                        // 기본값으로 초기화
                        for (int i = 0; i < t1.Length; i++)
                        {
                            result[i] = t1[i];
                        }
                        
                        // 공통 부분만 연산 적용
                        for (int i = 0; i < minLength; i++)
                        {
                            result[i] = op switch
                            {
                                "+" => t1[i] + t2Arr[i],
                                "-" => t1[i] - t2Arr[i],
                                "*" => t1[i] * t2Arr[i],
                                "/" => t1[i] / (Math.Abs(t2Arr[i]) < float.Epsilon ? 1.0f : t2Arr[i]), // 0으로 나누기 방지
                                _ => throw new ArgumentException($"Invalid operator: {op}")
                            };
                        }
                    }
                    else
                    {
                        for (int i = 0; i < t1.Length; i++)
                        {
                            result[i] = op switch
                            {
                                "+" => t1[i] + t2Arr[i],
                                "-" => t1[i] - t2Arr[i],
                                "*" => t1[i] * t2Arr[i],
                                "/" => t1[i] / (Math.Abs(t2Arr[i]) < float.Epsilon ? 1.0f : t2Arr[i]), // 0으로 나누기 방지
                                _ => throw new ArgumentException($"Invalid operator: {op}")
                            };
                        }
                    }
                }
                else
                {
                    Debug.LogError($"TupleOperations: t2 must be float or float[], but got {t2.GetType()}");
                    // 기본 동작: t1 반환
                    return (float[])t1.Clone();
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"TupleOperations 오류: {ex.Message}\n{ex.StackTrace}");
                return (float[])t1.Clone(); // 오류 발생 시 원본 배열 복제본 반환
            }
        }

        // 맨해튼 거리
        public static float ManhattanDistance(float[] t1, float[] t2)
        {
            float sum = 0;
            for (int i = 0; i < t1.Length; i++)
                sum += Math.Abs(t1[i] - t2[i]);
            return sum;
        }

        // 직사각형/직육면체 꼭짓점 생성
        public static List<float[]> GenerateRectangleVertices(float[] point1, float[] point2, int dimension)
        {
            var vertices = new List<float[]>();
            if (dimension == 2)
            {
                float x1 = point1[0], y1 = point1[1];
                float x2 = point2[0], y2 = point2[1];
                vertices.Add(new float[] { x1, y1 });
                vertices.Add(new float[] { x1, y2 });
                vertices.Add(new float[] { x2, y1 });
                vertices.Add(new float[] { x2, y2 });
            }
            else if (dimension == 3)
            {
                float x1 = point1[0], y1 = point1[1], z1 = point1[2];
                float x2 = point2[0], y2 = point2[1], z2 = point2[2];
                vertices.Add(new float[] { x1, y1, z1 });
                vertices.Add(new float[] { x1, y1, z2 });
                vertices.Add(new float[] { x1, y2, z1 });
                vertices.Add(new float[] { x1, y2, z2 });
                vertices.Add(new float[] { x2, y1, z1 });
                vertices.Add(new float[] { x2, y1, z2 });
                vertices.Add(new float[] { x2, y2, z1 });
                vertices.Add(new float[] { x2, y2, z2 });
            }
            else
            {
                throw new ArgumentException("Invalid dimension. Only 2 or 3 supported.");
            }
            return vertices;
        }

        // 경로 생성 (bend points → path)
        public static List<float[]> GeneratePathFromBendPoints(List<float[]> bendPoints)
        {
            int dims = bendPoints[0].Length;
            var path = new List<float[]>();
            for (int k = 1; k < bendPoints.Count; k++)
            {
                var p1 = bendPoints[k - 1];
                var p2 = bendPoints[k];
                for (int dim = 0; dim < dims; dim++)
                {
                    if (p1[dim] == p2[dim]) continue;
                    int step = p1[dim] < p2[dim] ? 1 : -1;
                    for (int coord = (int)p1[dim]; coord != (int)p2[dim]; coord += step)
                    {
                        var coords = (float[])p1.Clone();
                        coords[dim] = coord;
                        path.Add(coords);
                    }
                }
            }
            path.Add(bendPoints.Last());
            return path;
        }

        // 교차 경로 찾기 (세 경로의 교차점 기준으로 새 경로 생성)
        public static List<float[]> FindCrossingPath(List<float[]> path1, List<float[]> path2, List<float[]> path3)
        {
            var set1 = new HashSet<string>(path1.Select(p => string.Join(",", p)));
            var set2 = new HashSet<string>(path2.Select(p => string.Join(",", p)));
            var set3 = new HashSet<string>(path3.Select(p => string.Join(",", p)));
            var intersection1 = set1.Intersect(set2).FirstOrDefault();
            var intersection2 = set2.Intersect(set3).FirstOrDefault();
            if (intersection1 == null || intersection2 == null) return null;
            var idx1 = path1.FindIndex(p => string.Join(",", p) == intersection1);
            var idx2 = path2.FindIndex(p => string.Join(",", p) == intersection1);
            var idx3 = path2.FindIndex(p => string.Join(",", p) == intersection2);
            var idx4 = path3.FindIndex(p => string.Join(",", p) == intersection2);
            var newPath = new List<float[]>();
            newPath.AddRange(path1.Take(idx1));
            newPath.AddRange(path2.Skip(idx2).Take(idx3 - idx2));
            newPath.AddRange(path3.Skip(idx4));
            return newPath;
        }
    }
} 