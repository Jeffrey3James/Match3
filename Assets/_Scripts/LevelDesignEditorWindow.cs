#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Match3Game;
using Match3Game.Levels;
using StroTheGoat;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Data-driven level editor. Reads and writes Assets/Resources/Levels/levels.json —
/// the same file the game bundles as a fallback and the JadedBelles API serves remotely.
/// Publishing a level = editing the JSON here, then uploading it to the API's App_Data.
/// </summary>
public class LevelDesignEditorWindow : EditorWindow
{
    private const string LevelsJsonPath = "Assets/Resources/Levels/levels.json";

    #region Fields
    private LevelEditorRuntime runtimeHost;
    private Vector2 scrollPos;

    private LevelCollection collection;
    private int selectedLevelIndex;

    private GemTypeRegistry registry;
    private string[] gemNames = new string[0];
    private string[] obstacleNames = new string[0];

    private float cellSize = .55f;
    private Vector3 originPosition = new Vector3(1, -1, 0);

    private bool showExcludedCells = true;
    private bool showObstacles = true;
    private bool showObjectives = true;
    #endregion

    [MenuItem("Tools/Level Design Editor")]
    public static void ShowWindow()
    {
        GetWindow<LevelDesignEditorWindow>("Level Design Editor");
    }

    private void OnEnable()
    {
        LoadRegistry();
        LoadCollection();
    }

    private void OnGUI()
    {
        scrollPos = GUILayout.BeginScrollView(scrollPos, false, true);

        DrawHeader();

        if (collection == null)
        {
            EditorGUILayout.HelpBox($"Could not load {LevelsJsonPath}. Click Reload to try again.", MessageType.Error);
            GUILayout.EndScrollView();
            return;
        }

        DrawLevelSelector();

        var level = GetSelectedLevel();
        if (level != null)
        {
            DrawLevelFields(level);
            DrawExcludedCells(level);
            DrawObstacles(level);
            DrawObjectives(level);
            DrawScenePreviewControls(level);
        }
        else
        {
            EditorGUILayout.HelpBox("Add a level to get started.", MessageType.Info);
        }

        EditorUtils.AddSpaceToGUI(15);
        DrawSaveBar();

        GUILayout.EndScrollView();
    }

    #region Loading / Saving
    private void LoadRegistry()
    {
        registry = GemTypeRegistry.Load();
        if (registry == null) return;

        gemNames = registry.GetGemTypes().Where(g => g != null).Select(g => g.name).ToArray();
        obstacleNames = registry.GetObstacles().Where(o => o != null).Select(o => o.name).ToArray();
    }

    private void LoadCollection()
    {
        if (!File.Exists(LevelsJsonPath))
        {
            collection = new LevelCollection { version = 1 };
            return;
        }

        try
        {
            collection = JsonUtility.FromJson<LevelCollection>(File.ReadAllText(LevelsJsonPath));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to parse {LevelsJsonPath}: {e.Message}");
            collection = null;
        }

        selectedLevelIndex = Mathf.Clamp(selectedLevelIndex, 0, (collection?.levels.Count ?? 1) - 1);
    }

    private void SaveCollection()
    {
        if (collection == null) return;

        // Keep ids sequential and unique so the game's ordering stays predictable.
        for (int i = 0; i < collection.levels.Count; i++)
            collection.levels[i].id = i + 1;

        File.WriteAllText(LevelsJsonPath, JsonUtility.ToJson(collection, true));
        AssetDatabase.Refresh();
        Debug.Log($"Saved {collection.levels.Count} levels to {LevelsJsonPath}. " +
                  "Remember to publish this file to the API (App_Data/match3-levels.json) to ship it remotely.");
    }
    #endregion

    #region Drawing
    private void DrawHeader()
    {
        GUIStyle centeredStyle = EditorUtils.CenteredStyle(18);
        GUILayout.Label("Level Editor (levels.json)", centeredStyle);

        runtimeHost = (LevelEditorRuntime)EditorGUILayout.ObjectField(
            "Runtime Host", runtimeHost, typeof(LevelEditorRuntime), true);

        if (registry == null)
        {
            EditorGUILayout.HelpBox("GemTypeRegistry.asset not found in Resources. Gem/obstacle popups will be empty.", MessageType.Warning);
        }
    }

    private void DrawLevelSelector()
    {
        EditorUtils.AddSpaceToGUI(10);

        EditorGUILayout.BeginHorizontal();

        string[] levelNames = collection.levels
            .Select((l, i) => $"{i + 1}: {(string.IsNullOrEmpty(l.name) ? "(unnamed)" : l.name)}")
            .ToArray();

        if (levelNames.Length > 0)
        {
            selectedLevelIndex = EditorGUILayout.Popup("Level", Mathf.Clamp(selectedLevelIndex, 0, levelNames.Length - 1), levelNames);
        }
        else
        {
            EditorGUILayout.LabelField("Level", "(none)");
        }

        if (GUILayout.Button("Add", GUILayout.Width(50)))
        {
            var newLevel = new LevelData
            {
                name = $"Level {collection.levels.Count + 1}",
                maxMoves = 15,
                width = 9,
                height = 13
            };
            collection.levels.Add(newLevel);
            selectedLevelIndex = collection.levels.Count - 1;
        }

        using (new EditorGUI.DisabledScope(collection.levels.Count == 0))
        {
            if (GUILayout.Button("Duplicate", GUILayout.Width(75)))
            {
                var source = collection.levels[selectedLevelIndex];
                var copy = JsonUtility.FromJson<LevelData>(JsonUtility.ToJson(source));
                copy.name = source.name + " Copy";
                collection.levels.Insert(selectedLevelIndex + 1, copy);
                selectedLevelIndex++;
            }

            if (GUILayout.Button("Delete", GUILayout.Width(60)) &&
                EditorUtility.DisplayDialog("Delete Level",
                    $"Delete '{collection.levels[selectedLevelIndex].name}'?", "Delete", "Cancel"))
            {
                collection.levels.RemoveAt(selectedLevelIndex);
                selectedLevelIndex = Mathf.Clamp(selectedLevelIndex, 0, collection.levels.Count - 1);
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawLevelFields(LevelData level)
    {
        EditorUtils.AddSpaceToGUI(10);

        level.name = EditorGUILayout.TextField("Name", level.name);
        level.maxMoves = Mathf.Max(1, EditorGUILayout.IntField("Max Moves", level.maxMoves));

        Vector2Int size = EditorGUILayout.Vector2IntField("Grid Size", new Vector2Int(level.width, level.height));
        level.width = Mathf.Max(1, size.x);
        level.height = Mathf.Max(1, size.y);
    }

    private void DrawExcludedCells(LevelData level)
    {
        EditorUtils.AddSpaceToGUI(10);
        showExcludedCells = EditorGUILayout.Foldout(showExcludedCells, $"Excluded Cells (Level Shape) — {level.excludedCells.Count}", true);
        if (!showExcludedCells) return;

        DrawCellList(level.excludedCells, level);
    }

    private void DrawObstacles(LevelData level)
    {
        EditorUtils.AddSpaceToGUI(10);
        showObstacles = EditorGUILayout.Foldout(showObstacles, $"Obstacles — {level.obstacles.Count}", true);
        if (!showObstacles) return;

        for (int i = 0; i < level.obstacles.Count; i++)
        {
            var obstacle = level.obstacles[i];

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            obstacle.type = DrawNamePopup("Type", obstacle.type, obstacleNames);
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                level.obstacles.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            obstacle.health = Mathf.Max(1, EditorGUILayout.IntField("Health", obstacle.health));

            EditorGUILayout.LabelField($"Cells ({obstacle.cells.Count})");
            DrawCellList(obstacle.cells, level);
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("Add Obstacle"))
        {
            level.obstacles.Add(new ObstacleData
            {
                type = obstacleNames.Length > 0 ? obstacleNames[0] : "",
                health = 1
            });
        }
    }

    private void DrawObjectives(LevelData level)
    {
        EditorUtils.AddSpaceToGUI(10);
        showObjectives = EditorGUILayout.Foldout(showObjectives, $"Objectives — {level.objectives.Count}", true);
        if (!showObjectives) return;

        string[] allTargetNames = gemNames.Concat(obstacleNames).ToArray();

        for (int i = 0; i < level.objectives.Count; i++)
        {
            var objective = level.objectives[i];

            EditorGUILayout.BeginHorizontal("box");
            objective.gemType = DrawNamePopup("Clear", objective.gemType, allTargetNames);
            objective.amount = Mathf.Max(1, EditorGUILayout.IntField("Amount", objective.amount));
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                level.objectives.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                return;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("Add Objective"))
        {
            level.objectives.Add(new ObjectiveData
            {
                gemType = allTargetNames.Length > 0 ? allTargetNames[0] : "",
                amount = 10
            });
        }
    }

    private void DrawCellList(List<CellData> cells, LevelData level)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            var current = cells[i];
            Vector2Int edited = EditorGUILayout.Vector2IntField(GUIContent.none, new Vector2Int(current.x, current.y));
            current.x = Mathf.Clamp(edited.x, 0, level.width - 1);
            current.y = Mathf.Clamp(edited.y, 0, level.height - 1);
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                cells.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                return;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (GUILayout.Button("Add Cell"))
        {
            cells.Add(new CellData(0, 0));
        }
    }

    private string DrawNamePopup(string label, string currentValue, string[] options)
    {
        if (options.Length == 0)
        {
            return EditorGUILayout.TextField(label, currentValue);
        }

        int currentIndex = System.Array.IndexOf(options, currentValue);
        if (currentIndex < 0) currentIndex = 0;
        int newIndex = EditorGUILayout.Popup(label, currentIndex, options);
        return options[newIndex];
    }

    private void DrawSaveBar()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Save levels.json", GUILayout.Height(30)))
        {
            SaveCollection();
        }

        if (GUILayout.Button("Reload", GUILayout.Width(70), GUILayout.Height(30)))
        {
            LoadRegistry();
            LoadCollection();
        }

        EditorGUILayout.EndHorizontal();
    }
    #endregion

    #region Scene Preview
    private void DrawScenePreviewControls(LevelData level)
    {
        EditorUtils.AddSpaceToGUI(15);
        GUIStyle centeredStyle = EditorUtils.CenteredStyle(14);
        EditorUtils.CreateLabelAndConfigure("Scene Preview (LevelConfigScene)", centeredStyle, Color.cyan);

        if (GUILayout.Button("Draw Level in Scene"))
        {
            if (ValidateRuntimeHost())
            {
                ClearGridEditorWindow(level);
                DrawLevelGridEditorWindow(level);
                ApplyLevelShapePreview(level);
                ApplyObstaclesPreview(level);
            }
        }
    }

    private void DrawLevelGridEditorWindow(LevelData level)
    {
        bool shouldDrawDebug = GameObject.Find("Debugging") == null;

        runtimeHost.gridToConfigure = GridSystem2D<GridObj>.VerticalGrid(
            level.width, level.height, cellSize, originPosition, shouldDrawDebug);

        for (int x = 0; x < level.width; x++)
        {
            for (int y = 0; y < level.height; y++)
            {
                runtimeHost.CreateGem(x, y);
            }
        }
    }

    private void ClearGridEditorWindow(LevelData level)
    {
        if (runtimeHost.gridToConfigure == null) return;

        for (int x = 0; x < level.width; x++)
        {
            for (int y = 0; y < level.height; y++)
            {
                runtimeHost.ClearGem(x, y);
            }
        }

        runtimeHost.spawnedObstacles.Clear();
    }

    private void ApplyLevelShapePreview(LevelData level)
    {
        var positions = level.excludedCells.Select(c => c.ToVector()).ToList();
        if (positions.Count > 0)
        {
            runtimeHost.InstantiateLevelShaperEditor(positions);
        }
    }

    private void ApplyObstaclesPreview(LevelData level)
    {
        if (registry == null) return;

        for (int i = 0; i < level.obstacles.Count; i++)
        {
            var data = level.obstacles[i];
            var obstacleType = registry.FindObstacle(data.type);
            if (obstacleType == null) continue;

            var config = new ObstacleConfig
            {
                obstacle = obstacleType,
                locations = data.cells.Select(c => c.ToVector()).ToList(),
                health = data.health
            };

            if (!runtimeHost.spawnedObstacles.ContainsKey(i))
                runtimeHost.spawnedObstacles[i] = new List<GameObject>();

            runtimeHost.InstantiateObstacleConfigEditor(config, i);
        }
    }

    private bool ValidateRuntimeHost()
    {
        if (runtimeHost == null)
        {
            Debug.LogWarning("Assign a Runtime Host in the Scene first (open LevelConfigScene).");
            return false;
        }
        return true;
    }
    #endregion
}
#endif
