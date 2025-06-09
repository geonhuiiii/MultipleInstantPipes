using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Threading;
using System.Threading.Tasks; // Task 클래스 사용을 위해 추가
using System.Diagnostics;


namespace InstantPipes
{
    public class PipeListUI : EditorWindow
    {
        private static float timer = 0f;
        private PipeGenerator _generator;
        private Vector2 _scrollPosition;

        // List of pipe configurations
        private List<PipeConfig> _pipeConfigs = new List<PipeConfig>();

        // Active toggle states
        private bool _startPointToggle = false;
        private bool _endPointToggle = false;

        // Active configuration
        private PipeConfig _activeConfig;

        // Preview objects
        private GameObject _previewCircle;

        // Start and end points
        private Vector3 _startPoint;
        private Vector3 _endPoint;
        private Vector3 _startNormal;
        private Vector3 _endNormal;
        private bool _hasStartPoint = false;
        private bool _hasEndPoint = false;

        // 경로 생성 진행 상태와 취소를 위한 변수
        private bool _isGeneratingPaths = false;
        private bool _isRegeneratingPaths = false;
        private float _currentProgressValue = 0f;
        private string _currentProgressText = "";
        private CancellationTokenSource _cancellationTokenSource;

        [MenuItem("Window/InstantPipes/Pipe Configuration UI")]
        public static void ShowWindow()
        {
            GetWindow<PipeListUI>("Pipe Configuration");
        }

        private void OnEnable()
        {
            // Try to find a PipeGenerator in the scene
            _generator = FindObjectOfType<PipeGenerator>();

            // Load saved pipe configs
            LoadPipeConfigs();

            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            // Save pipe configs
            SavePipeConfigs();

            SceneView.duringSceneGui -= OnSceneGUI;

            // Destroy preview object if it exists
            if (_previewCircle != null)
            {
                DestroyImmediate(_previewCircle);
                _previewCircle = null;
            }
        }

        private void LoadPipeConfigs()
        {
            _pipeConfigs.Clear();

            int count = EditorPrefs.GetInt("PipeListUI_ConfigCount", 0);

            if (count == 0)
            {
                // Create default pipe config if no saved configs
                _pipeConfigs.Add(new PipeConfig("Default Pipe", Color.white, 1.0f));
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    string name = EditorPrefs.GetString($"PipeListUI_Config_{i}_Name", $"Pipe {i + 1}");

                    // Load color
                    float r = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_ColorR", 1.0f);
                    float g = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_ColorG", 1.0f);
                    float b = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_ColorB", 1.0f);
                    float a = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_ColorA", 1.0f);
                    Color color = new Color(r, g, b, a);

                    float radius = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_Radius", 1.0f);

                    PipeConfig config = new PipeConfig(name, color, radius);

                    // Load point data
                    config.HasStartPoint = EditorPrefs.GetBool($"PipeListUI_Config_{i}_HasStartPoint", false);
                    config.HasEndPoint = EditorPrefs.GetBool($"PipeListUI_Config_{i}_HasEndPoint", false);

                    if (config.HasStartPoint)
                    {
                        float startX = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_StartPointX", 0);
                        float startY = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_StartPointY", 0);
                        float startZ = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_StartPointZ", 0);
                        config.StartPoint = new Vector3(startX, startY, startZ);

                        float startNormalX = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_StartNormalX", 0);
                        float startNormalY = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_StartNormalY", 1);
                        float startNormalZ = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_StartNormalZ", 0);
                        config.StartNormal = new Vector3(startNormalX, startNormalY, startNormalZ);
                    }

                    if (config.HasEndPoint)
                    {
                        float endX = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_EndPointX", 0);
                        float endY = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_EndPointY", 0);
                        float endZ = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_EndPointZ", 0);
                        config.EndPoint = new Vector3(endX, endY, endZ);

                        float endNormalX = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_EndNormalX", 0);
                        float endNormalY = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_EndNormalY", 1);
                        float endNormalZ = EditorPrefs.GetFloat($"PipeListUI_Config_{i}_EndNormalZ", 0);
                        config.EndNormal = new Vector3(endNormalX, endNormalY, endNormalZ);
                    }

                    // Load associated pipe indices
                    int pipeCount = EditorPrefs.GetInt($"PipeListUI_Config_{i}_PipeCount", 0);
                    for (int j = 0; j < pipeCount; j++)
                    {
                        int pipeIndex = EditorPrefs.GetInt($"PipeListUI_Config_{i}_Pipe_{j}", -1);
                        if (pipeIndex >= 0)
                        {
                            config.AssociatedPipeIndices.Add(pipeIndex);
                        }
                    }

                    _pipeConfigs.Add(config);
                }
            }

            // Set active config to first one if available
            if (_pipeConfigs.Count > 0)
            {
                int activeIndex = EditorPrefs.GetInt("PipeListUI_ActiveConfigIndex", 0);
                if (activeIndex >= 0 && activeIndex < _pipeConfigs.Count)
                {
                    _activeConfig = _pipeConfigs[activeIndex];
                }
                else
                {
                    _activeConfig = _pipeConfigs[0];
                }
            }

            // Set current active points from active config
            if (_activeConfig != null)
            {
                _hasStartPoint = _activeConfig.HasStartPoint;
                _hasEndPoint = _activeConfig.HasEndPoint;

                if (_hasStartPoint)
                {
                    _startPoint = _activeConfig.StartPoint;
                    _startNormal = _activeConfig.StartNormal;
                }

                if (_hasEndPoint)
                {
                    _endPoint = _activeConfig.EndPoint;
                    _endNormal = _activeConfig.EndNormal;
                }
            }

            // Update all pipe materials to match their configurations
            UpdateAllPipeMaterials();
        }

        private void SavePipeConfigs()
        {
            EditorPrefs.SetInt("PipeListUI_ConfigCount", _pipeConfigs.Count);

            for (int i = 0; i < _pipeConfigs.Count; i++)
            {
                PipeConfig config = _pipeConfigs[i];

                EditorPrefs.SetString($"PipeListUI_Config_{i}_Name", config.Name);

                // Save color
                EditorPrefs.SetFloat($"PipeListUI_Config_{i}_ColorR", config.Color.r);
                EditorPrefs.SetFloat($"PipeListUI_Config_{i}_ColorG", config.Color.g);
                EditorPrefs.SetFloat($"PipeListUI_Config_{i}_ColorB", config.Color.b);
                EditorPrefs.SetFloat($"PipeListUI_Config_{i}_ColorA", config.Color.a);

                EditorPrefs.SetFloat($"PipeListUI_Config_{i}_Radius", config.Radius);

                // Save point data
                EditorPrefs.SetBool($"PipeListUI_Config_{i}_HasStartPoint", config.HasStartPoint);
                EditorPrefs.SetBool($"PipeListUI_Config_{i}_HasEndPoint", config.HasEndPoint);

                if (config.HasStartPoint)
                {
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_StartPointX", config.StartPoint.x);
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_StartPointY", config.StartPoint.y);
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_StartPointZ", config.StartPoint.z);

                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_StartNormalX", config.StartNormal.x);
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_StartNormalY", config.StartNormal.y);
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_StartNormalZ", config.StartNormal.z);
                }

                if (config.HasEndPoint)
                {
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_EndPointX", config.EndPoint.x);
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_EndPointY", config.EndPoint.y);
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_EndPointZ", config.EndPoint.z);

                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_EndNormalX", config.EndNormal.x);
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_EndNormalY", config.EndNormal.y);
                    EditorPrefs.SetFloat($"PipeListUI_Config_{i}_EndNormalZ", config.EndNormal.z);
                }

                // Save associated pipe indices
                EditorPrefs.SetInt($"PipeListUI_Config_{i}_PipeCount", config.AssociatedPipeIndices.Count);
                for (int j = 0; j < config.AssociatedPipeIndices.Count; j++)
                {
                    EditorPrefs.SetInt($"PipeListUI_Config_{i}_Pipe_{j}", config.AssociatedPipeIndices[j]);
                }
            }

            // Save active config index
            int activeIndex = _pipeConfigs.IndexOf(_activeConfig);
            EditorPrefs.SetInt("PipeListUI_ActiveConfigIndex", activeIndex);
        }

        private void OnGUI()
        {
            // Wrap the entire GUI in a top-level vertical layout
            EditorGUILayout.BeginVertical();

            // Check for PipeGenerator
            if (_generator == null)
            {
                _generator = FindObjectOfType<PipeGenerator>();
                if (_generator == null)
                {
                    EditorGUILayout.HelpBox("No PipeGenerator found in the scene. Please add a PipeGenerator component to an object.", MessageType.Warning);
                    if (GUILayout.Button("Create PipeGenerator"))
                    {
                        GameObject newObj = new GameObject("Pipe Generator");
                        _generator = newObj.AddComponent<PipeGenerator>();
                        Selection.activeGameObject = newObj;
                    }
                    EditorGUILayout.EndVertical(); // End the top vertical even in early return case
                    return;
                }
            }

            // 진행 중인 작업이 있으면 진행 상태와 취소 버튼 표시
            if (_isGeneratingPaths || _isRegeneratingPaths)
            {
                EditorGUILayout.Space(10);

                // 진행 중인 작업 종류에 따라 다른 제목 표시
                string operationTitle = _isGeneratingPaths ? "파이프 생성 중" : "파이프 경로 재생성 중";
                GUILayout.Label(operationTitle, EditorStyles.boldLabel);

                // 정보 박스 스타일 설정
                GUI.backgroundColor = new Color(0.8f, 0.9f, 1.0f);
                EditorGUILayout.HelpBox($"{_currentProgressText}", MessageType.Info);
                GUI.backgroundColor = Color.white;

                // 진행 상태 표시줄
                Rect progressRect = EditorGUILayout.GetControlRect(false, 24);
                EditorGUI.ProgressBar(progressRect,
                    _currentProgressValue,
                    $"{Mathf.FloorToInt(_currentProgressValue * 100)}%");

                EditorGUILayout.Space(10);

                // 취소 버튼
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("작업 취소", GUILayout.Height(40)))
                {
                    if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                    {
                        _cancellationTokenSource.Cancel();
                        _currentProgressText = "취소 요청 처리 중...";
                        UnityEngine.Debug.Log("사용자에 의해 작업 취소됨");
                    }

                    _isGeneratingPaths = false;
                    Repaint();
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.Space(10);

                // 진행 중일 때는 다른 UI 표시하지 않음
                EditorGUILayout.EndVertical();

                // 자동으로 창 다시 그리기 (진행 상태 업데이트)
                Repaint();
                return;
            }

            // Pipe list area
            GUILayout.Label("Pipe Configurations", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));

            for (int i = 0; i < _pipeConfigs.Count; i++)
            {
                DrawPipeConfigItem(i);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(5);

            // Buttons row
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Add Pipe Config"))
            {
                AddNewPipeConfig();
            }

            if (_activeConfig != null)
            {
                if (GUILayout.Button("Edit Selected"))
                {
                    // Reset toggles when edit is pressed
                    _startPointToggle = false;
                    _endPointToggle = false;
                }

                if (GUILayout.Button("Delete Selected"))
                {
                    DeleteSelectedPipeConfig();
                }

                // Add regenerate button
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("Regenerate Pipes") && _activeConfig.AssociatedPipeIndices.Count > 0)
                {
                    RegeneratePipesWithNewRadius();
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Path operations section
            GUILayout.Label("Path Operations", EditorStyles.boldLabel);

            // Show an explanatory message
            EditorGUILayout.HelpBox("Generate or regenerate paths for all existing pipe start/end points. This can fix issues with pipe geometry and overlapping paths.", MessageType.Info);

            // Combined path generation/regeneration button
            EditorGUILayout.BeginHorizontal();

            // Check if any configs have both start and end points set
            bool anyConfigHasPoints = _pipeConfigs.Any(c => c.HasStartPoint && c.HasEndPoint);

            // Generate All Paths button - enabled if any configs have points
            GUI.backgroundColor = new Color(0.5f, 0.8f, 1.0f);
            EditorGUI.BeginDisabledGroup(!anyConfigHasPoints);
            if (GUILayout.Button("Generate All Paths", GUILayout.Height(30)))
            {
                GenerateAllPaths();
            }
            EditorGUI.EndDisabledGroup();

            // ReGenerate All Paths button - enabled only if pipes exist
            GUI.backgroundColor = new Color(1.0f, 0.8f, 0.5f);
            EditorGUI.BeginDisabledGroup(_generator.Pipes.Count == 0);
            if (GUILayout.Button("ReGenerate All Paths", GUILayout.Height(30)))
            {
                ReGenerateAllPaths();
            }
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            // Add Clear All Pipes button - only shown if pipes exist
            if (_generator.Pipes.Count > 0)
            {
                GUI.backgroundColor = new Color(1.0f, 0.5f, 0.5f);
                if (GUILayout.Button("Clear All Pipes", GUILayout.Height(25)))
                {
                    if (EditorUtility.DisplayDialog("Clear All Pipes",
                        "This will delete ALL pipes in the scene. This operation cannot be undone. Continue?",
                        "Yes, delete all", "Cancel"))
                    {
                        ClearAllPipes();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                EditorGUILayout.HelpBox("No pipes exist yet. Use 'Generate All Paths' to create pipes for configurations with defined start and end points.", MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // Path creation area
            if (_activeConfig != null)
            {
                GUILayout.Label("Path Creation", EditorStyles.boldLabel);

                // Toggle buttons for start/end points
                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginChangeCheck();
                _startPointToggle = GUILayout.Toggle(_startPointToggle, _hasStartPoint ? "◆ Start Point" : "◇ Start Point", "Button", GUILayout.Height(30));
                if (EditorGUI.EndChangeCheck() && _startPointToggle)
                {
                    _endPointToggle = false;
                    SceneView.RepaintAll();
                }

                EditorGUI.BeginChangeCheck();
                _endPointToggle = GUILayout.Toggle(_endPointToggle, _hasEndPoint ? "◆ End Point" : "◇ End Point", "Button", GUILayout.Height(30));
                if (EditorGUI.EndChangeCheck() && _endPointToggle)
                {
                    _startPointToggle = false;
                    SceneView.RepaintAll();
                }

                EditorGUILayout.EndHorizontal();

                // Create path button
                EditorGUI.BeginDisabledGroup(!_hasStartPoint || !_hasEndPoint);
                if (GUILayout.Button("Create Path", GUILayout.Height(40)))
                {
                    CreatePath();
                }
                EditorGUI.EndDisabledGroup();
            }

            // Add debug button at the bottom
            EditorGUILayout.Space(10);

            if (_generator != null)
            {
                GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button("UnityEngine.Debug Log Pipe Info"))
                {
                    UnityEngine.Debug.Log($"======= PIPE CONFIGURATION DEBUG =======");
                    UnityEngine.Debug.Log($"Active Pipe Configs: {_pipeConfigs.Count}");
                    foreach (var config in _pipeConfigs)
                    {
                        UnityEngine.Debug.Log($"Config: {config.Name}, Associated Pipes: {config.AssociatedPipeIndices.Count}");
                        foreach (var index in config.AssociatedPipeIndices)
                        {
                            UnityEngine.Debug.Log($"  Pipe Index: {index}");
                        }
                    }

                    _generator.LogPipeInfo();
                }
                GUI.backgroundColor = Color.white;
            }

            // End top-level vertical
            EditorGUILayout.EndVertical();
        }

        private void DrawPipeConfigItem(int index)
        {
            PipeConfig config = _pipeConfigs[index];
            bool isActive = _activeConfig == config;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Selection toggle
            bool newIsActive = EditorGUILayout.Toggle(isActive, GUILayout.Width(20));
            if (newIsActive != isActive)
            {
                _activeConfig = newIsActive ? config : null;

                // Update the active points when changing selected config
                if (_activeConfig != null)
                {
                    _hasStartPoint = _activeConfig.HasStartPoint;
                    _hasEndPoint = _activeConfig.HasEndPoint;

                    if (_hasStartPoint)
                    {
                        _startPoint = _activeConfig.StartPoint;
                        _startNormal = _activeConfig.StartNormal;
                    }

                    if (_hasEndPoint)
                    {
                        _endPoint = _activeConfig.EndPoint;
                        _endNormal = _activeConfig.EndNormal;
                    }
                }
            }

            // Store original values for comparison
            Color originalColor = config.Color;
            float originalRadius = config.Radius;

            // Color field
            config.Color = EditorGUILayout.ColorField(GUIContent.none, config.Color, false, false, false, GUILayout.Width(40));

            // Name field
            config.Name = EditorGUILayout.TextField(config.Name);

            // Radius field
            config.Radius = EditorGUILayout.FloatField(config.Radius, GUILayout.Width(50));

            // If properties changed, update associated pipes
            if (config.Color != originalColor || config.Radius != originalRadius)
            {
                UpdateConfiguredPipes(config);
            }

            // Start point indicator
            GUIStyle startStyle = new GUIStyle(EditorStyles.miniButton);
            if (config.HasStartPoint)
            {
                startStyle.normal.textColor = Color.green;
                GUILayout.Label("S", startStyle, GUILayout.Width(15));
            }
            else
            {
                GUILayout.Label("S", startStyle, GUILayout.Width(15));
            }

            // End point indicator
            GUIStyle endStyle = new GUIStyle(EditorStyles.miniButton);
            if (config.HasEndPoint)
            {
                endStyle.normal.textColor = Color.green;
                GUILayout.Label("E", endStyle, GUILayout.Width(15));
            }
            else
            {
                GUILayout.Label("E", endStyle, GUILayout.Width(15));
            }

            // Show pipe count
            GUILayout.Label($"{config.AssociatedPipeIndices.Count}", GUILayout.Width(25));

            EditorGUILayout.EndHorizontal();
        }

        private void AddNewPipeConfig()
        {
            // Create a random color for the new pipe config
            Color randomColor = new Color(
                Random.Range(0.2f, 0.9f),
                Random.Range(0.2f, 0.9f),
                Random.Range(0.2f, 0.9f)
            );

            // Create a new pipe config with default values
            PipeConfig newConfig = new PipeConfig($"Pipe {_pipeConfigs.Count + 1}", randomColor, 1.0f);

            // Initialize with default values
            newConfig.HasStartPoint = false;
            newConfig.HasEndPoint = false;
            newConfig.StartPoint = Vector3.zero;
            newConfig.EndPoint = Vector3.zero;
            newConfig.StartNormal = Vector3.up;
            newConfig.EndNormal = Vector3.up;

            _pipeConfigs.Add(newConfig);
            _activeConfig = newConfig;

            // Reset active points
            _hasStartPoint = false;
            _hasEndPoint = false;
        }

        private void DeleteSelectedPipeConfig()
        {
            if (_activeConfig != null)
            {
                // First ask the user if they want to delete associated pipes
                bool deletePipes = EditorUtility.DisplayDialog("Delete Pipe Configuration",
                    $"Do you want to delete all pipes associated with '{_activeConfig.Name}'?",
                    "Yes, delete pipes", "No, keep pipes");

                if (deletePipes && _generator != null)
                {
                    Undo.RecordObject(_generator, "Delete Pipes");

                    // UnityEngine.Debug log before deletion
                    UnityEngine.Debug.Log($"Before deletion - Config: {_activeConfig.Name}, Pipe Indices: {string.Join(", ", _activeConfig.AssociatedPipeIndices)}");
                    _generator.LogPipeInfo();

                    // Sort indices in descending order to avoid index shifting when removing
                    _activeConfig.AssociatedPipeIndices.Sort();
                    _activeConfig.AssociatedPipeIndices.Reverse();


                    UnityEngine.Debug.Log($"Sorted indices for deletion: {string.Join(", ", _activeConfig.AssociatedPipeIndices)}");

                    // Remove all pipes associated with this config
                    foreach (int pipeIndex in _activeConfig.AssociatedPipeIndices.ToList())
                    {
                        UnityEngine.Debug.Log($"Attempting to delete pipe at index: {pipeIndex}");
                        if (pipeIndex >= 0 && pipeIndex < _generator.Pipes.Count)
                        {
                            _generator.RemovePipe(pipeIndex);

                            // Update all indices in all configs to account for the shift
                            UpdatePipeIndicesAfterDeletion(pipeIndex);
                        }
                        else
                        {
                            UnityEngine.Debug.LogWarning($"Skipping invalid pipe index: {pipeIndex}, Pipes count: {_generator.Pipes.Count}");
                        }
                    }

                    // Clear the associated indices as they've been removed
                    _activeConfig.AssociatedPipeIndices.Clear();

                    // Update the mesh after removing pipes
                    _generator.UpdateMesh();

                    // UnityEngine.Debug log after deletion
                    UnityEngine.Debug.Log($"After deletion - Remaining pipes: {_generator.Pipes.Count}");
                    _generator.LogPipeInfo();
                }

                // Remove the config
                _pipeConfigs.Remove(_activeConfig);
                _activeConfig = _pipeConfigs.Count > 0 ? _pipeConfigs[0] : null;
            }
        }

        // Add a new method to update pipe indices after deletion
        private void UpdatePipeIndicesAfterDeletion(int deletedIndex)
        {
            // For each config, adjust indices greater than the deleted index
            foreach (var config in _pipeConfigs)
            {
                for (int i = 0; i < config.AssociatedPipeIndices.Count; i++)
                {
                    int currentIndex = config.AssociatedPipeIndices[i];

                    if (currentIndex == deletedIndex)
                    {
                        // This should be removed from the list
                        config.AssociatedPipeIndices.RemoveAt(i);
                        i--; // Adjust index after removal
                    }
                    else if (currentIndex > deletedIndex)
                    {
                        // Decrement indices that are greater than the deleted index
                        config.AssociatedPipeIndices[i] = currentIndex - 1;
                    }
                    // Indices less than deletedIndex don't change
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_generator == null) return;

            Event evt = Event.current;

            // Handle keyboard escape to cancel placement mode
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                _startPointToggle = false;
                _endPointToggle = false;
                evt.Use();
                sceneView.Repaint();
                Repaint();
            }

            // Draw all configurations' start and end points
            foreach (var config in _pipeConfigs)
            {
                // Draw start point if it exists
                if (config.HasStartPoint)
                {
                    Handles.color = config.Color;
                    Handles.DrawWireDisc(config.StartPoint, config.StartNormal, config.Radius);
                    PipePointIcon.DrawStartPointIcon(config.StartPoint, 1.5f, config.Color);
                }

                // Draw end point if it exists
                if (config.HasEndPoint)
                {
                    Handles.color = config.Color;
                    Handles.DrawWireDisc(config.EndPoint, config.EndNormal, config.Radius);
                    PipePointIcon.DrawEndPointIcon(config.EndPoint, 1.5f, config.Color);
                }
            }

            // Only proceed if one of the toggle buttons is active and we have an active config
            if ((_startPointToggle || _endPointToggle) && _activeConfig != null)
            {
                // Raycast from camera to scene
                Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    // Create preview circle if it doesn't exist
                    if (_previewCircle == null)
                    {
                        _previewCircle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        _previewCircle.name = "PipePointPreview";

                        // Remove collider from preview
                        DestroyImmediate(_previewCircle.GetComponent<Collider>());

                        // Set material to be semi-transparent with configured color
                        Material previewMaterial = new Material(Shader.Find("Standard"));
                        Color previewColor = _activeConfig.Color;
                        previewColor.a = 0.5f; // Make it semi-transparent
                        previewMaterial.color = previewColor;
                        previewMaterial.SetFloat("_Mode", 3); // Transparent mode
                        previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        previewMaterial.SetInt("_ZWrite", 0);
                        previewMaterial.DisableKeyword("_ALPHATEST_ON");
                        previewMaterial.EnableKeyword("_ALPHABLEND_ON");
                        previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        previewMaterial.renderQueue = 3000;
                        _previewCircle.GetComponent<Renderer>().material = previewMaterial;
                    }

                    // Update preview position and scale - ensure radius is never zero
                    float previewRadius = Mathf.Max(0.1f, _activeConfig.Radius);
                    _previewCircle.transform.position = hit.point;
                    _previewCircle.transform.localScale = Vector3.one * previewRadius * 2;

                    // Handle mouse click to set points
                    if (evt.type == EventType.MouseDown && evt.button == 0)
                    {
                        if (_startPointToggle)
                        {
                            // 소수점을 제거한 위치 값으로 설정
                            Vector3 roundedPoint = new Vector3(
                                Mathf.Round(hit.point.x),
                                Mathf.Round(hit.point.y),
                                Mathf.Round(hit.point.z)
                            );

                            _startPoint = roundedPoint;
                            _startNormal = hit.normal;
                            _hasStartPoint = true;

                            // Update the active config with the start point
                            _activeConfig.HasStartPoint = true;
                            _activeConfig.StartPoint = _startPoint;
                            _activeConfig.StartNormal = _startNormal;

                            _startPointToggle = false;
                        }
                        else if (_endPointToggle)
                        {
                            // 소수점을 제거한 위치 값으로 설정
                            Vector3 roundedPoint = new Vector3(
                                Mathf.Round(hit.point.x),
                                Mathf.Round(hit.point.y),
                                Mathf.Round(hit.point.z)
                            );

                            _endPoint = roundedPoint;
                            _endNormal = hit.normal;
                            _hasEndPoint = true;

                            // Update the active config with the end point
                            _activeConfig.HasEndPoint = true;
                            _activeConfig.EndPoint = _endPoint;
                            _activeConfig.EndNormal = _endNormal;

                            _endPointToggle = false;
                        }

                        evt.Use();
                        Repaint();
                    }
                }
            }
            else
            {
                // Destroy preview object if it exists
                if (_previewCircle != null)
                {
                    DestroyImmediate(_previewCircle);
                    _previewCircle = null;
                }
            }

            // Force scene view to repaint
            if (evt.type == EventType.Layout) sceneView.Repaint();
        }

        private void CreatePath()
        {
            if (_activeConfig == null || _generator == null || !_activeConfig.HasStartPoint || !_activeConfig.HasEndPoint) return;

            // Ensure we have a valid radius
            float safeRadius = Mathf.Max(0.1f, _activeConfig.Radius);

            Undo.RecordObject(_generator, "Create Pipe Path");

            // Set default path creator settings
            _generator.GridSize = 1f;
            _generator.Height = 3f;

            // Create a new material instance for this pipe
            Material pipeMaterial = new Material(Shader.Find("Standard"));
            pipeMaterial.color = _activeConfig.Color;

            // Create temporary colliders at start and end points to help path finding
            GameObject startCollider = CreateTemporaryCollider(_activeConfig.StartPoint, _activeConfig.StartNormal);
            GameObject endCollider = CreateTemporaryCollider(_activeConfig.EndPoint, _activeConfig.EndNormal);

            try
            {
                // Before creating, check for overlap with existing pipes
                if (CheckForPipeOverlap(_activeConfig.StartPoint, _activeConfig.EndPoint))
                {
                    if (!EditorUtility.DisplayDialog("Pipe Overlap Detected",
                        "The new pipe may overlap with existing pipes. Would you like to continue?",
                        "Create Anyway", "Cancel"))
                    {
                        return;
                    }
                }

                // Remember the current pipe count to track which pipe we're adding
                int pipeIndexBefore = _generator.Pipes.Count;

                // Create the pipe with custom radius and material
                bool success = _generator.AddPipe(
                    _activeConfig.StartPoint,
                    _activeConfig.StartNormal,
                    _activeConfig.EndPoint,
                    _activeConfig.EndNormal,
                    safeRadius,
                    pipeMaterial
                );

                // If successful, track the new pipe's index in this config
                if (success)
                {
                    // Track the newly created pipe in this config
                    int newPipeIndex = _generator.Pipes.Count - 1;
                    _activeConfig.AssociatedPipeIndices.Add(newPipeIndex);

                    UnityEngine.Debug.Log($"Successfully created pipe: {_activeConfig.Name}");
                }
                else
                {
                    UnityEngine.Debug.LogWarning("Failed to create pipe path. Try adjusting settings or positions.");
                }
            }
            finally
            {
                // Clean up temporary colliders
                if (startCollider != null) DestroyImmediate(startCollider);
                if (endCollider != null) DestroyImmediate(endCollider);
            }
        }

        private void GenerateAllPaths()
        {
            if (_generator == null) return;

            if (!EditorUtility.DisplayDialog("Generate All Paths",
                "This will generate paths for all existing pipe start/end points. Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            // 경로 생성 상태 설정
            _isGeneratingPaths = true;
            _currentProgressValue = 0f;
            _currentProgressText = "파이프 설정 준비 중...";
            _cancellationTokenSource = new CancellationTokenSource();

            Undo.RecordObject(_generator, "Generate All Paths");

            // 백그라운드 스레드에서 실행할 작업 시작
            Task.Run(() => GenerateAllPathsAsync(_cancellationTokenSource.Token))
                .ContinueWith(task =>
                {
                    // 작업 완료 후 메인 스레드에서 실행할 코드
                    if (task.IsFaulted)
                    {
                        // 에러 발생 시 처리
                        EditorApplication.delayCall += () =>
                        {
                            _isGeneratingPaths = false;
                            UnityEngine.Debug.LogError($"경로 생성 중 오류 발생: {task.Exception?.InnerException?.Message}");
                            EditorUtility.DisplayDialog("오류 발생",
                                "경로 생성 중 오류가 발생했습니다. 자세한 내용은 콘솔을 확인해주세요.",
                                "확인");
                            Repaint();
                        };
                    }
                    // 취소된 경우는 GenerateAllPathsAsync 내부에서 처리
                });
        }

        // 백그라운드 스레드에서 실행할 비동기 파이프 생성 메서드
        private async Task GenerateAllPathsAsync(CancellationToken cancellationToken)
        {
            // 모든 파이프 구성 수집 및 전처리 (스레드 안전하게 데이터만 수집)
            List<PipeInfo> pipesToCreate = new List<PipeInfo>();
            List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Color color, string name)> pipeData =
                new List<(Vector3, Vector3, Vector3, Vector3, float, Color, string)>();

            // 메인 스레드에서 데이터 수집
            await Task.Delay(5); // 프레임 넘기기
            EditorApplication.delayCall += () =>
            {
                try
                {
                    // 모든 파이프 엔드포인트 정보 추출
                    foreach (var config in _pipeConfigs)
                    {
                        if (config.HasStartPoint && config.HasEndPoint)
                        {
                            pipesToCreate.Add(new PipeInfo
                            {
                                StartPoint = config.StartPoint,
                                StartNormal = config.StartNormal,
                                EndPoint = config.EndPoint,
                                EndNormal = config.EndNormal,
                                Color = config.Color,
                                Radius = config.Radius,
                                Config = config
                            });
                            pipeData.Add((
                                config.StartPoint,
                                config.StartNormal,
                                config.EndPoint,
                                config.EndNormal,
                                config.Radius,
                                config.Color,
                                config.Name
                            ));


                        }
                    }

                    // 진행 가능한 파이프가 없으면 종료
                    if (pipesToCreate.Count == 0)
                    {
                        _isGeneratingPaths = false;
                        EditorUtility.DisplayDialog("Generate All Paths",
                            "No valid pipe configurations found. Please set start and end points for at least one pipe configuration.",
                            "OK");
                        return;
                    }

                    // 기존 파이프 모두 제거
                    ClearAllPipes();

                    // 준비 단계 완료 진행률 표시
                    UpdateProgress(0.1f, "파이프 생성 준비 완료");
                }
                catch (System.Exception ex)
                {
                    _isGeneratingPaths = false;
                    UnityEngine.Debug.LogError($"데이터 수집 중 오류 발생: {ex.Message}");
                    EditorUtility.DisplayDialog("오류 발생",
                        "데이터 수집 중 오류가 발생했습니다.",
                        "확인");
                }
            };

            // 백그라운드에서 파이프 데이터 처리
            await Task.Delay(2000); // 메인 스레드 작업 완료 대기

            // 취소 요청 확인
            if (cancellationToken.IsCancellationRequested)
            {
                await SwitchToMainThread(() =>
                {
                    _isGeneratingPaths = false;
                    EditorUtility.DisplayDialog("작업 취소됨", "파이프 생성이 취소되었습니다.", "확인");
                });
                return;
            }

            // 파이프 생성 정보 처리 (무거운 계산 작업을 백그라운드에서 수행)
            List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius, Color color, string name)>
                processedPipes = new List<(Vector3, Vector3, Vector3, Vector3, float, Color, string)>();

            // 진행 상태 표시 변수
            int totalPaths = pipeData.Count;
            int processedPaths = 0;
            bool isCancelled = false;

            // 각 파이프 데이터 처리 (백그라운드에서 계산량이 많은 작업 처리)
            for (int i = 0; i < pipeData.Count; i++)
            {
                // 취소 요청 확인
                if (cancellationToken.IsCancellationRequested)
                {
                    isCancelled = true;
                    break;
                }

                var pipe = pipeData[i];
                processedPaths++;


                // 처리된 파이프 추가
                processedPipes.Add(pipe);

                // 진행 상태 메인 스레드에 업데이트
                await SwitchToMainThread(() =>
                {
                    float progress = 0.1f + (0.6f * (float)processedPaths / totalPaths); // 10%~70% 범위 내에서 진행
                    UpdateProgress(progress, $"파이프 처리 중 ({processedPaths}/{totalPaths}): {pipe.name}");
                });
            }

            // 취소된 경우 처리
            if (isCancelled)
            {
                await SwitchToMainThread(() =>
                {
                    if (processedPipes.Count > 0)
                    {
                        UpdateProgress(0.75f, "취소됨. 기존 처리된 파이프에 대해 경로를 생성하시겠습니까?");

                        if (!EditorUtility.DisplayDialog("작업 취소됨",
                            $"경로 생성이 취소되었습니다. 이미 처리된 {processedPipes.Count}개의 구성에 대해 경로를 생성하시겠습니까?",
                            "처리된 항목 생성", "모두 취소"))
                        {
                            // 모든 작업 취소
                            _isGeneratingPaths = false;
                            return;
                        }
                        // 계속 진행 - 이미 처리된 파이프만 생성
                    }
                    else
                    {
                        // 처리된 파이프가 없으면 종료
                        _isGeneratingPaths = false;
                        EditorUtility.DisplayDialog("작업 취소됨",
                            "경로 생성이 취소되었습니다. 생성된 경로가 없습니다.",
                            "확인");
                        return;
                    }
                });

                if (processedPipes.Count == 0)
                    return;
            }

            // 파이프 생성 전 최종 준비 (메인 스레드에서 수행)
            List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal, float radius)>
                pipeConfigs = new List<(Vector3, Vector3, Vector3, Vector3, float)>();
            int pipesBefore = 0;

            await SwitchToMainThread(() =>
            {
                try
                {
                    // 다중 파이프 생성 설정 준비
                    pipesBefore = _generator.Pipes.Count;

                    // 파이프 생성 시작 메시지
                    UpdateProgress(0.8f, $"경로 생성 중 ({processedPipes.Count}개 구성)...");

                    // 최종 취소 확인
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _isGeneratingPaths = false;
                        EditorUtility.DisplayDialog("작업 취소됨", "최종 경로 생성 단계에서 작업이 취소되었습니다.", "확인");
                        return;
                    }

                    // 재료 생성 (메인 스레드에서만 가능)
                    foreach (var pipe in processedPipes)
                    {
                        // 각 파이프의 설정 추가
                        Material pipeMaterial = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
                        pipeMaterial.name = $"Pipe_Material_{pipe.name}";

                        // 셰이더에 따라 색상 설정
                        if (pipeMaterial.HasProperty("_BaseColor"))
                        {
                            pipeMaterial.SetColor("_BaseColor", pipe.color);

                            if (pipeMaterial.HasProperty("_EmissionColor"))
                            {
                                pipeMaterial.EnableKeyword("_EMISSION");
                                pipeMaterial.SetColor("_EmissionColor", pipe.color * 0.5f);
                            }
                        }
                        else if (pipeMaterial.HasProperty("_Color"))
                        {
                            pipeMaterial.SetColor("_Color", pipe.color);

                            if (pipeMaterial.HasProperty("_EmissionColor"))
                            {
                                pipeMaterial.EnableKeyword("_EMISSION");
                                pipeMaterial.SetColor("_EmissionColor", pipe.color * 0.5f);
                            }
                        }

                        // 파이프 설정 추가
                        pipeConfigs.Add((
                            pipe.startPoint,
                            pipe.startNormal,
                            pipe.endPoint,
                            pipe.endNormal,
                            pipe.radius
                        ));
                    }

                    // 메시 업데이트 진행 중 메시지
                    UpdateProgress(0.85f, "파이프 경로 생성 중...");
                }
                catch (System.Exception ex)
                {
                    _isGeneratingPaths = false;
                    UnityEngine.Debug.LogError($"파이프 재료 생성 중 오류: {ex.Message}");
                    EditorUtility.DisplayDialog("오류 발생",
                        "파이프 재료 생성 중 오류가 발생했습니다.",
                        "확인");
                }
            });

            // 취소 확인
            if (cancellationToken.IsCancellationRequested)
            {
                await SwitchToMainThread(() =>
                {
                    _isGeneratingPaths = false;
                    EditorUtility.DisplayDialog("작업 취소됨", "메시 생성 직전에 작업이 취소되었습니다.", "확인");
                });
                return;
            }

            // 파이프 생성 실행 (메인 스레드에서만 가능)
            bool success = false;
            int newPipesCount = 0;
            List<float> Chaoses = new List<float> { 10f };
            List<int> miters = new List<int> { 5 };
            foreach (var miter in miters)
            {
                foreach (var chaos in Chaoses)
                {
                    _generator.Chaos = chaos;
                    _generator.miter = miter;
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
                    await SwitchToMainThread(() =>
                    {
                        try
                        {
                            // 다중 파이프 생성 실행
                            success = _generator.AddMultiplePipes(pipeConfigs);

                            // 파이프 연결 인덱스 추적
                            newPipesCount = _generator.Pipes.Count - pipesBefore;

                            // 메시 업데이트 진행 중 메시지
                            UpdateProgress(0.95f, "메시 업데이트 중...");
                        }
                        catch (System.Exception ex)
                        {
                            _isGeneratingPaths = false;
                            UnityEngine.Debug.LogError($"파이프 생성 중 오류: {ex.Message}");
                            EditorUtility.DisplayDialog("오류 발생",
                                "파이프 생성 중 오류가 발생했습니다.",
                                "확인");
                            return;
                        }
                    });
                    sw.Stop();
                    UnityEngine.Debug.Log("총 소요 시간(ms)" + sw.ElapsedMilliseconds);
                }
            }


            // 파이프 생성 결과 처리 (메인 스레드에서만 가능)
            await SwitchToMainThread(() =>
            {
                if (newPipesCount == pipeConfigs.Count)
                {
                    // 각 파이프 구성에 인덱스 할당
                    for (int i = 0; i < pipeConfigs.Count; i++)
                    {
                        int configIndex = pipesToCreate.FindIndex(p =>
                            p.StartPoint == pipeConfigs[i].startPoint &&
                            p.EndPoint == pipeConfigs[i].endPoint);

                        if (configIndex >= 0 && configIndex < pipesToCreate.Count)
                        {
                            int pipeIndex = pipesBefore + i;
                            pipesToCreate[configIndex].Config.AssociatedPipeIndices.Add(pipeIndex);
                        }
                    }

                    // 완료 메시지 표시
                    UpdateProgress(1.0f, "작업 완료!");

                    // 1초 후 진행 상태 표시 닫기
                    EditorApplication.delayCall += () =>
                    {
                        // 생성 완료 후 상태 초기화
                        _isGeneratingPaths = false;
                        Repaint();

                        if (success)
                        {
                            EditorUtility.DisplayDialog("경로 생성 완료",
                                $"{newPipesCount}개의 파이프가 성공적으로 생성되었습니다" +
                                (isCancelled ? " (작업이 일부 취소됨)." : "."),
                                "확인");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("경로 생성 경고",
                                "경로가 생성되었지만, 일부는 최적으로 생성되지 않았을 수 있습니다.",
                                "확인");
                        }
                    };
                }
                else
                {
                    _isGeneratingPaths = false;
                    EditorUtility.DisplayDialog("경로 생성 오류",
                        $"{pipeConfigs.Count}개의 파이프를 생성하려 했지만, {newPipesCount}개만 생성되었습니다.",
                        "확인");
                }

                // 씬 업데이트
                UpdateAllPipeMaterials();
                SceneView.RepaintAll();
            });
        }

        // 메인 스레드에서 작업을 실행하기 위한 헬퍼 메서드
        private async Task SwitchToMainThread(System.Action action)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (System.Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            await tcs.Task;
        }

        private void ReGenerateAllPaths()
        {
            if (_generator == null) return;

            // Check if pipes exist
            if (_generator.Pipes.Count == 0)
            {
                UnityEngine.Debug.LogWarning("No pipes to regenerate. Create a pipe first.");
                return;
            }

            // Confirm the regeneration
            if (!EditorUtility.DisplayDialog("Regenerate All Paths",
                "This will regenerate all paths for existing pipes. Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            // 상태 초기화 및 진행 설정
            _isRegeneratingPaths = true;
            _currentProgressValue = 0f;
            _currentProgressText = "재생성 준비 중...";
            _cancellationTokenSource = new CancellationTokenSource();

            Undo.RecordObject(_generator, "Regenerate Paths");

            // 백그라운드 스레드에서 작업 시작
            Task.Run(() => ReGenerateAllPathsAsync(_cancellationTokenSource.Token))
                .ContinueWith(task =>
                {
                    // 작업 완료 후 메인 스레드에서 실행할 코드
                    if (task.IsFaulted)
                    {
                        // 에러 발생 시 처리
                        EditorApplication.delayCall += () =>
                        {
                            _isRegeneratingPaths = false;
                            UnityEngine.Debug.LogError($"경로 재생성 중 오류 발생: {task.Exception?.InnerException?.Message}");
                            EditorUtility.DisplayDialog("오류 발생",
                                "경로 재생성 중 오류가 발생했습니다. 자세한 내용은 콘솔을 확인해주세요.",
                                "확인");
                            Repaint();
                        };
                    }
                    // 취소된 경우는 ReGenerateAllPathsAsync 내부에서 처리
                });
        }

        // 백그라운드 스레드에서 실행할 비동기 경로 재생성 메서드
        private async Task ReGenerateAllPathsAsync(CancellationToken cancellationToken)
        {
            // 스레드 안전하게 파이프 정보 저장할 데이터 구조
            List<(Vector3 startPoint, Vector3 startNormal, Vector3 endPoint, Vector3 endNormal)> pipeEndpoints =
                new List<(Vector3, Vector3, Vector3, Vector3)>();
            int totalPipes = 0;

            // 메인 스레드에서 파이프 정보 수집
            await SwitchToMainThread(() =>
            {
                try
                {
                    // 파이프 정보 수집
                    totalPipes = _generator.Pipes.Count;

                    // 진행 변수 초기화
                    UpdateProgress(0.05f, "파이프 정보 수집 중...");

                    // 각 파이프의 시작점과 끝점 정보를 수집
                    for (int i = 0; i < _generator.Pipes.Count; i++)
                    {
                        var pipe = _generator.Pipes[i];

                        if (pipe.Points.Count >= 2)
                        {
                            // 시작점과 방향 정보
                            Vector3 startPoint = pipe.Points[0];
                            Vector3 startDir = (pipe.Points[1] - pipe.Points[0]).normalized;

                            // 끝점과 방향 정보
                            int lastIndex = pipe.Points.Count - 1;
                            Vector3 endPoint = pipe.Points[lastIndex];
                            Vector3 endDir = (pipe.Points[lastIndex - 1] - pipe.Points[lastIndex]).normalized;

                            // 스레드 안전한 데이터 저장
                            pipeEndpoints.Add((startPoint, startDir, endPoint, endDir));
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    _isRegeneratingPaths = false;
                    UnityEngine.Debug.LogError($"파이프 정보 수집 중 오류: {ex.Message}");
                }
            });

            // 취소 요청 확인
            if (cancellationToken.IsCancellationRequested)
            {
                await SwitchToMainThread(() =>
                {
                    _isRegeneratingPaths = false;
                    EditorUtility.DisplayDialog("작업 취소됨", "파이프 정보 수집 단계에서 작업이 취소되었습니다.", "확인");
                });
                return;
            }

            // 임시 콜라이더 생성 (메인 스레드에서만 가능)
            List<GameObject> tempColliders = new List<GameObject>();

            await SwitchToMainThread(() =>
            {
                try
                {
                    // 준비 단계 완료 표시
                    UpdateProgress(0.1f, "콜라이더 설정 준비 중...");

                    // 각 파이프 엔드포인트에 임시 콜라이더 생성
                    for (int i = 0; i < pipeEndpoints.Count; i++)
                    {
                        // 취소 요청 확인
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        var endpoint = pipeEndpoints[i];

                        // 진행 상태 표시
                        float progress = 0.1f + (float)i / pipeEndpoints.Count * 0.3f; // 10%~40% 범위
                        UpdateProgress(progress, $"콜라이더 설정 중 ({i + 1}/{pipeEndpoints.Count})");

                        // 에디터 UI 반응성 유지
                        if (i % 5 == 0)
                        {
                            System.Threading.Thread.Sleep(10);
                            EditorApplication.QueuePlayerLoopUpdate();
                        }

                        // 시작점과 끝점에 임시 콜라이더 생성
                        tempColliders.Add(CreateTemporaryCollider(endpoint.startPoint, endpoint.startNormal));
                        tempColliders.Add(CreateTemporaryCollider(endpoint.endPoint, endpoint.endNormal));
                    }

                    // Ensure material is set before regenerating paths
                    if (_generator.Material == null)
                    {
                        _generator.Material = new Material(Shader.Find("Standard"));
                    }

                    // Set other parameters to ensure path finding works
                    _generator.GridSize = 3f;
                    _generator.Height = 5f;

                    // 경로 재생성 진행 메시지 표시
                    UpdateProgress(0.45f, "경로 재생성 준비 완료...");
                }
                catch (System.Exception ex)
                {
                    // 오류 처리
                    _isRegeneratingPaths = false;
                    UnityEngine.Debug.LogError($"콜라이더 설정 중 오류: {ex.Message}");
                    EditorUtility.DisplayDialog("오류 발생",
                        $"콜라이더 설정 중 오류가 발생했습니다: {ex.Message}",
                        "확인");

                    // 생성된 임시 콜라이더 정리
                    foreach (var collider in tempColliders)
                    {
                        if (collider != null) DestroyImmediate(collider);
                    }
                    tempColliders.Clear();
                    return;
                }
            });

            // 취소 요청 확인
            if (cancellationToken.IsCancellationRequested)
            {
                await SwitchToMainThread(() =>
                {
                    _isRegeneratingPaths = false;

                    // 생성된 임시 콜라이더 정리
                    foreach (var collider in tempColliders)
                    {
                        if (collider != null) DestroyImmediate(collider);
                    }

                    EditorUtility.DisplayDialog("작업 취소됨", "콜라이더 설정 단계에서 작업이 취소되었습니다.", "확인");
                });
                return;
            }


            // 경로 재생성 실행 (메인 스레드에서만 가능)
            bool success = false;

            await SwitchToMainThread(() =>
            {
                try
                {
                    // 경로 재생성 진행 메시지 표시
                    UpdateProgress(0.75f, "최종 경로 재생성 중...");

                    // 최종 취소 확인
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    // Regenerate all paths
                    success = _generator.RegeneratePaths();

                    // 메시 업데이트 메시지
                    UpdateProgress(0.9f, "메시 업데이트 완료 중...");

                    // Force an update to the mesh to ensure everything is applied
                    _generator.UpdateMesh();

                    // 완료 메시지
                    UpdateAllPipeMaterials();
                    UpdateProgress(1.0f, "작업 완료!");
                }
                catch (System.Exception ex)
                {
                    _isRegeneratingPaths = false;
                    UnityEngine.Debug.LogError($"경로 재생성 중 오류: {ex.Message}");
                    EditorUtility.DisplayDialog("오류 발생",
                        $"경로 재생성 중 오류가 발생했습니다: {ex.Message}",
                        "확인");
                }
                finally
                {
                    // Clean up all temporary colliders
                    foreach (var collider in tempColliders)
                    {
                        if (collider != null) DestroyImmediate(collider);
                    }
                }
            });

            // 취소 요청 확인
            if (cancellationToken.IsCancellationRequested)
            {
                await SwitchToMainThread(() =>
                {
                    _isRegeneratingPaths = false;

                    // 생성된 임시 콜라이더 정리 (아직 남아있을 경우)
                    foreach (var collider in tempColliders)
                    {
                        if (collider != null) DestroyImmediate(collider);
                    }

                    EditorUtility.DisplayDialog("작업 취소됨", "경로 재생성 중에 작업이 취소되었습니다.", "확인");
                });
                return;
            }

            // 결과 처리 (항상 메인 스레드에서)
            await SwitchToMainThread(() =>
            {
                // 1초 후 진행 상태 표시 닫기
                EditorApplication.delayCall += () =>
                {
                    // 상태 초기화
                    _isRegeneratingPaths = false;
                    Repaint();

                    // 결과 메시지 표시
                    if (success)
                    {
                        EditorUtility.DisplayDialog("경로 재생성 완료",
                            "모든 경로가 성공적으로 재생성되었습니다.",
                            "확인");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("경로 재생성 경고",
                            "일부 경로는 최적으로 재생성되지 않았을 수 있습니다.",
                            "확인");
                    }
                };

                // Force Unity to update the scene view
                SceneView.RepaintAll();
            });
        }

        // Helper method to create temporary colliders for path finding
        private GameObject CreateTemporaryCollider(Vector3 position, Vector3 normal)
        {
            if (_generator == null) return null;

            GameObject tempObj = new GameObject("TempPathCollider");
            tempObj.transform.position = position + (normal * 2.5f);
            tempObj.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            tempObj.transform.localScale = new Vector3(
                _generator.Radius * 2.5f,
                5f,
                _generator.Radius * 2.5f
            );

            // Add capsule collider
            CapsuleCollider collider = tempObj.AddComponent<CapsuleCollider>();
            collider.direction = 1; // Y-axis (up)

            return tempObj;
        }

        // Check if a new pipe would overlap with existing pipes
        private bool CheckForPipeOverlap(Vector3 startPoint, Vector3 endPoint)
        {
            if (_generator == null || _generator.Pipes.Count == 0) return false;

            // Simple check - see if either end point is very close to an existing pipe end point
            float overlapThreshold = 0.5f; // Adjust this threshold as needed

            foreach (var pipe in _generator.Pipes)
            {
                if (pipe.Points.Count < 2) continue;

                Vector3 pipeStart = pipe.Points[0];
                Vector3 pipeEnd = pipe.Points[pipe.Points.Count - 1];

                if (Vector3.Distance(startPoint, pipeStart) < overlapThreshold ||
                    Vector3.Distance(startPoint, pipeEnd) < overlapThreshold ||
                    Vector3.Distance(endPoint, pipeStart) < overlapThreshold ||
                    Vector3.Distance(endPoint, pipeEnd) < overlapThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        // Update all pipe materials to match their configurations
        private void UpdateAllPipeMaterials()
        {
            if (_generator == null) return;

            // Get renderer for material access
            Renderer renderer = _generator.GetComponent<Renderer>();
            if (renderer == null) return;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0) return;

            // Check if we need to create materials
            bool needsRecreatedMaterials = false;

            // Update each pipe's material based on its configuration
            foreach (var config in _pipeConfigs)
            {
                foreach (int pipeIndex in config.AssociatedPipeIndices)
                {
                    if (pipeIndex >= 0 && pipeIndex < _generator.Pipes.Count)
                    {
                        // Make sure we have enough materials
                        if (pipeIndex >= materials.Length)
                        {
                            needsRecreatedMaterials = true;
                            continue;
                        }

                        // Create a material if needed
                        if (materials[pipeIndex] == null)
                        {
                            materials[pipeIndex] = new Material(Shader.Find("Standard"));
                            materials[pipeIndex].name = $"Pipe_{pipeIndex}_Material";
                        }

                        // Update material color
                        materials[pipeIndex].color = config.Color;
                    }
                }
            }

            // Apply the updated materials
            renderer.sharedMaterials = materials;

            // If we need to recreate materials, update the mesh to ensure correct material count
            if (needsRecreatedMaterials)
            {
                _generator.UpdateMesh();
            }
        }

        // Method to regenerate pipes with new radius
        private void RegeneratePipesWithNewRadius()
        {
            if (_generator == null || _activeConfig == null) return;

            // If no pipes associated with this config, do nothing
            if (_activeConfig.AssociatedPipeIndices.Count == 0) return;

            // Confirm with the user
            if (!EditorUtility.DisplayDialog("Regenerate Pipes",
                $"This will recreate all {_activeConfig.AssociatedPipeIndices.Count} pipes with the current radius setting. Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            Undo.RecordObject(_generator, "Regenerate Pipes");

            // Store info about pipes to recreate
            List<PipeInfo> pipesToRecreate = new List<PipeInfo>();

            // Sort indices in descending order to avoid index shifting when removing
            _activeConfig.AssociatedPipeIndices.Sort();
            _activeConfig.AssociatedPipeIndices.Reverse();

            // For each pipe, store its info and remove it
            foreach (int pipeIndex in _activeConfig.AssociatedPipeIndices.ToList())
            {
                if (pipeIndex < 0 || pipeIndex >= _generator.Pipes.Count) continue;

                var pipe = _generator.Pipes[pipeIndex];
                if (pipe == null || pipe.Points.Count < 2) continue;

                // Store important points from the pipe
                Vector3 startPoint = pipe.Points[0];
                Vector3 startDir = (pipe.Points[1] - pipe.Points[0]).normalized;
                Vector3 endPoint = pipe.Points[pipe.Points.Count - 1];
                Vector3 endDir = (pipe.Points[pipe.Points.Count - 2] - pipe.Points[pipe.Points.Count - 1]).normalized;

                pipesToRecreate.Add(new PipeInfo
                {
                    StartPoint = startPoint,
                    StartNormal = startDir,
                    EndPoint = endPoint,
                    EndNormal = endDir,
                    Color = _activeConfig.Color,
                    Radius = _activeConfig.Radius,
                    Config = _activeConfig
                });

                // Remove the pipe and update other pipe indices
                _generator.RemovePipe(pipeIndex);
                UpdatePipeIndicesAfterDeletion(pipeIndex);
            }

            // Clear the associated pipe indices since we removed them all
            _activeConfig.AssociatedPipeIndices.Clear();

            // Now recreate each pipe with the new radius
            float safeRadius = Mathf.Max(0.1f, _activeConfig.Radius);

            // Set the path creator settings
            _generator.GridSize = 3f;
            _generator.Height = 5f;

            // Recreate each pipe
            foreach (var pipeInfo in pipesToRecreate)
            {
                // Create temporary colliders for path finding
                GameObject startCollider = CreateTemporaryCollider(pipeInfo.StartPoint, pipeInfo.StartNormal);
                GameObject endCollider = CreateTemporaryCollider(pipeInfo.EndPoint, pipeInfo.EndNormal);

                try
                {
                    // Get source shader to use
                    Shader shaderToUse = Shader.Find("Universal Render Pipeline/Simple Lit");
                    if (_generator.Material != null && _generator.Material.shader != null)
                    {
                        shaderToUse = _generator.Material.shader;
                    }

                    // Create a new material for this pipe
                    Material pipeMaterial = new Material(shaderToUse);
                    pipeMaterial.name = $"Pipe_Material_{_activeConfig.Name}_Regenerated";

                    // Set color based on shader property
                    if (pipeMaterial.HasProperty("_BaseColor"))
                    {
                        // URP properties
                        pipeMaterial.SetColor("_BaseColor", _activeConfig.Color);

                        // Enable emission if available
                        if (pipeMaterial.HasProperty("_EmissionColor"))
                        {
                            pipeMaterial.EnableKeyword("_EMISSION");
                            pipeMaterial.SetColor("_EmissionColor", _activeConfig.Color * 0.5f);
                        }
                    }
                    else if (pipeMaterial.HasProperty("_Color"))
                    {
                        // Standard shader properties
                        pipeMaterial.SetColor("_Color", _activeConfig.Color);

                        // Enable emission if available
                        if (pipeMaterial.HasProperty("_EmissionColor"))
                        {
                            pipeMaterial.EnableKeyword("_EMISSION");
                            pipeMaterial.SetColor("_EmissionColor", _activeConfig.Color * 0.5f);
                        }
                    }

                    // Create the pipe with custom radius and material
                    bool success = _generator.AddPipe(
                        pipeInfo.StartPoint,
                        pipeInfo.StartNormal,
                        pipeInfo.EndPoint,
                        pipeInfo.EndNormal,
                        safeRadius,
                        pipeMaterial
                    );

                    // If successful, track the new pipe
                    if (success)
                    {
                        int newPipeIndex = _generator.Pipes.Count - 1;
                        _activeConfig.AssociatedPipeIndices.Add(newPipeIndex);
                    }
                }
                finally
                {
                    // Clean up temporary colliders
                    if (startCollider != null) DestroyImmediate(startCollider);
                    if (endCollider != null) DestroyImmediate(endCollider);
                }
            }

            // Force scene update
            SceneView.RepaintAll();
        }
        void Update()
        {
            timer += Time.deltaTime;
        }

        // Update associated pipes when configuration changes
        private void UpdateConfiguredPipes(PipeConfig config)
        {
            if (_generator == null) return;

            Undo.RecordObject(_generator, "Update Pipes");

            UnityEngine.Debug.Log($"Updating pipes for config: {config.Name}, Color: {config.Color}, Associated pipes: {config.AssociatedPipeIndices.Count}");

            // Get source shader to use
            Shader shaderToUse = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (_generator.Material != null && _generator.Material.shader != null)
            {
                shaderToUse = _generator.Material.shader;
            }

            // Update each pipe associated with this config
            foreach (int pipeIndex in config.AssociatedPipeIndices)
            {
                if (pipeIndex < 0 || pipeIndex >= _generator.Pipes.Count) continue;

                // Create a new material with the updated color
                Material pipeMaterial = new Material(shaderToUse);
                pipeMaterial.name = $"Pipe_{pipeIndex}_Material_{config.Name}";

                // Set color based on shader property
                if (pipeMaterial.HasProperty("_BaseColor"))
                {
                    // URP properties
                    pipeMaterial.SetColor("_BaseColor", config.Color);

                    // Enable emission if available
                    if (pipeMaterial.HasProperty("_EmissionColor"))
                    {
                        pipeMaterial.EnableKeyword("_EMISSION");
                        pipeMaterial.SetColor("_EmissionColor", config.Color * 0.5f);
                    }
                }
                else if (pipeMaterial.HasProperty("_Color"))
                {
                    // Standard shader properties
                    pipeMaterial.SetColor("_Color", config.Color);

                    // Enable emission if available
                    if (pipeMaterial.HasProperty("_EmissionColor"))
                    {
                        pipeMaterial.EnableKeyword("_EMISSION");
                        pipeMaterial.SetColor("_EmissionColor", config.Color * 0.5f);
                    }
                }

                UnityEngine.Debug.Log($"Creating new material for pipe {pipeIndex}: {pipeMaterial.name}, Shader: {pipeMaterial.shader.name}");

                // Update the pipe properties
                _generator.SetPipeProperties(pipeIndex, config.Radius, pipeMaterial);
            }

            // Force scene update
            SceneView.RepaintAll();
        }

        // Method to clear all pipes
        private void ClearAllPipes()
        {
            if (_generator == null) return;

            Undo.RecordObject(_generator, "Clear All Pipes");

            // Log before clearing
            UnityEngine.Debug.Log("About to clear all pipes...");
            _generator.LogPipeInfo();

            // Use the new method to clear all pipes at once
            _generator.ClearAllPipes();

            // Clear all pipe indices in configurations
            foreach (var config in _pipeConfigs)
            {
                config.AssociatedPipeIndices.Clear();
            }

            // Log after clearing
            UnityEngine.Debug.Log("After clearing all pipes:");
            _generator.LogPipeInfo();

            // Force scene update
            SceneView.RepaintAll();
        }

        // 진행 상태 업데이트 메서드
        private void UpdateProgress(float progress, string text)
        {
            _currentProgressValue = progress;
            _currentProgressText = text;

            // UI 즉시 업데이트
            Repaint();

            // Unity 에디터 이벤트 처리 (UI 응답성 유지)
            if (Event.current != null)
                EditorApplication.QueuePlayerLoopUpdate();

            // 진행 상태 로그 (디버그용)
            if (progress == 0f || progress == 1f || Mathf.Approximately(progress % 0.25f, 0f))
            {
                UnityEngine.Debug.Log($"진행 상태: {Mathf.Floor(progress * 100)}% - {text}");
            }
        }

        // Class to hold pipe configuration data
        [System.Serializable]
        public class PipeConfig
        {
            public string Name;
            public Color Color;
            public float Radius;

            // Start and end point data
            public bool HasStartPoint = false;
            public bool HasEndPoint = false;
            public Vector3 StartPoint;
            public Vector3 EndPoint;
            public Vector3 StartNormal;
            public Vector3 EndNormal;

            // Track created pipes for this config
            public List<int> AssociatedPipeIndices = new List<int>();

            public PipeConfig(string name, Color color, float radius)
            {
                Name = name;
                Color = color;
                Radius = radius;
            }
        }

        // Update the PipeInfo class to include more pipe properties
        internal class PipeInfo
        {
            public Vector3 StartPoint;
            public Vector3 StartNormal;
            public Vector3 EndPoint;
            public Vector3 EndNormal;
            public Color Color;
            public float Radius;
            public PipeConfig Config;
        }
    }

}