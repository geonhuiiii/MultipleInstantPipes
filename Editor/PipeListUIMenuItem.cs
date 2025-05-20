using UnityEditor;
using UnityEngine;

namespace InstantPipes
{
    public static class PipeListUIMenuItem
    {
        [MenuItem("Tools/InstantPipes/Pipe Configuration UI", false, 10)]
        public static void ShowPipeListUI()
        {
            PipeListUI.ShowWindow();
        }
    }
} 