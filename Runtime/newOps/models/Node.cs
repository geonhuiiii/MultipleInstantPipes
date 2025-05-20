using System;
using System.Linq;
using UnityEngine;

namespace Model
{
    public class Node
    {
        public (Vector3, string) CoordInfo;
        public Vector3 Coord;
        public Node Parent;
        public string Direction;
        public int Depth;
        public int NCP;
        public float EdgeCost;
        public int Energy;

        public Node((Vector3, string) coordInfo, Node parent = null, float edgeCost = 0f)
        {
            // 좌표 정보 null 체크는 Vector3에서는 필요 없음
            
            // 방향 문자열 null 체크
            if (string.IsNullOrEmpty(coordInfo.Item2))
            {
                Debug.LogWarning("Node 생성 오류: 방향 문자열이 null 또는 빈 문자열입니다. 기본 방향 +x 사용.");
                coordInfo.Item2 = "+x";
            }
            
            CoordInfo = coordInfo;
            Coord = coordInfo.Item1;
            Parent = parent;
            Direction = coordInfo.Item2;

            try
            {
                if (Parent == null)
                {
                    Depth = 1;
                    NCP = 0;
                    EdgeCost = edgeCost;
                    Energy = Direction.EndsWith("z") ? 1 : 0;
                }
                else
                {
                    EdgeCost = Parent.EdgeCost + edgeCost;
                    Energy = Parent.Energy + (Direction.EndsWith("z") ? 1 : 0);
                    if (Parent.Direction != Direction)
                    {
                        NCP = Parent.NCP + 1;
                        Depth = Parent.Depth + 1;
                    }
                    else
                    {
                        NCP = Parent.NCP;
                        Depth = Parent.Depth + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Node 생성 중 오류 발생: {ex.Message}");
                // 기본값 설정
                Depth = 1;
                NCP = 0;
                EdgeCost = edgeCost;
                Energy = 0;
            }
        }

        public static bool operator <(Node a, Node b)
        {
            if (a == null || b == null)
            {
                Debug.LogError("Node 비교 연산 중 null 참조 발생");
                return false;
            }
            
            return a.Coord.x < b.Coord.x;
        }
        
        public static bool operator >(Node a, Node b)
        {
            if (a == null || b == null)
            {
                Debug.LogError("Node 비교 연산 중 null 참조 발생");
                return false;
            }
            
            return a.Coord.x > b.Coord.x;
        }
        
        public override bool Equals(object obj)
        {
            if (obj is Node other)
            {
                return Coord == other.Coord;
            }
            return false;
        }
        
        public override int GetHashCode()
        {
            return Coord.GetHashCode();
        }
        
        public override string ToString()
        {
            return $"Node({Coord.x},{Coord.y},{Coord.z}, Dir={Direction}, NCP={NCP}, Cost={EdgeCost})";
        }
    }
} 