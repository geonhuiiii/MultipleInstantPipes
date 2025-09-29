using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class NewPathfinderDLL : IDisposable
{
    // C++ 구조체와 동일한 레이아웃을 갖도록 정의
    [StructLayout(LayoutKind.Sequential)]
    private struct Vec3
    {
        public float x, y, z;
        public Vec3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
        public Vector3 ToVector3() { return new Vector3(x, y, z); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vec3Int
    {
        public int x, y, z;
        public Vec3Int(Vector3Int v) { x = v.x; y = v.y; z = v.z; }
    }
    
    // 빌드한 DLL 파일 이름 (Pathfinder.dll)
    private const string DLL_NAME = "Pathfinder"; 

    // C++ FindPath 함수 시그니처와 정확히 일치해야 합니다.
    [DllImport(DLL_NAME)]
    private static extern int FindPath(Vec3 startPos, Vec3 endPos, IntPtr outPath, int maxPathSize, float straightPathPriority, float nearObstaclesPriority);

    [DllImport(DLL_NAME)]
    private static extern unsafe void InitializeGrid(int* initialCosts, int countX, int countY, int countZ, Vec3 minBounds, float gridSize);

    [DllImport(DLL_NAME)]
    private static extern unsafe void UpdateCosts(Vec3Int* cellsToUpdate, int count, int costToAdd);

    [DllImport(DLL_NAME)]
    private static extern void ReleaseGrid();

    private bool _isDisposed = false;
    private const int MAX_PATH_SIZE = 2048; // 경로의 최대 길이
    private IntPtr _pathBuffer; // C++이 경로를 쓸 버퍼

    public NewPathfinderDLL()
    {
        // 경로 데이터를 받을 비관리 메모리 할당
        _pathBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<Vec3>() * MAX_PATH_SIZE);
    }
    
    public unsafe void Initialize(int[,,] costGrid, Vector3 minBounds, float gridSize)
    {
        int countX = costGrid.GetLength(0);
        int countY = costGrid.GetLength(1);
        int countZ = costGrid.GetLength(2);

        fixed (int* pCostGrid = costGrid)
        {
            InitializeGrid(pCostGrid, countX, countY, countZ, new Vec3(minBounds), gridSize);
        }
    }

    // ★★★ 이 메서드의 파라미터가 4개가 되도록 수정되었습니다 ★★★
    public List<Vector3> CreatePath(Vector3 start, Vector3 end, float straightPathPriority, float nearObstaclesPriority)
    {
        // C++ 함수 호출 시에도 4개의 인자를 전달합니다.
        int pathLength = FindPath(new Vec3(start), new Vec3(end), _pathBuffer, MAX_PATH_SIZE, straightPathPriority, nearObstaclesPriority);

        var path = new List<Vector3>();
        if (pathLength > 0 && pathLength <= MAX_PATH_SIZE)
        {
            IntPtr current = _pathBuffer;
            for (int i = 0; i < pathLength; i++)
            {
                // Marshal.PtrToStructure<T>(IntPtr) is slow, so we use a block copy.
                var vec = Marshal.PtrToStructure<Vec3>(current);
                path.Add(vec.ToVector3());
                current = IntPtr.Add(current, Marshal.SizeOf<Vec3>());
            }
        }
        return path;
    }
    
    public unsafe void UpdateGridCosts(HashSet<Vector3Int> cells, int costToAdd)
    {
        if (cells == null || cells.Count == 0) return;
        
        var cellArray = new Vec3Int[cells.Count];
        int i = 0;
        foreach(var cell in cells)
        {
            cellArray[i++] = new Vec3Int(cell);
        }
        
        fixed (Vec3Int* pCells = cellArray)
        {
            UpdateCosts(pCells, cellArray.Length, costToAdd);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            ReleaseGrid();
            Marshal.FreeHGlobal(_pathBuffer);
            _pathBuffer = IntPtr.Zero;
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
    
    ~NewPathfinderDLL()
    {
        Dispose();
    }
}