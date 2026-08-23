using UnityEditor;
using UnityEngine;
using Match3Game;
using System.Collections.Generic;
using StroTheGoat;

public class LevelDesignEditorWindow : EditorWindow
{
    #region Fields
    private LevelEditorRuntime runtimeHost;
    private Vector2 scrollPos;

    private Level currentLevel;
    private LevelShaperSO currentShaper;
    private LevelShaperSO lastShaper; // Track changes
    private Vector2Int gridSize;
    private int maxMoves;
    private float cellSize = .55f;
    private Vector3 originPosition = new Vector3(1, -1, 0);
    private string newLevelShaperName = "NewLevelShaper";

    private SerializedObject serializedLevel;
    private SerializedProperty obstaclesProperty;
    private SerializedProperty objectivesProperty;
    private SerializedProperty levelShaperProperty;

    // Level Shaper Groups
    private List<LevelShaperSOGroups> groups = new List<LevelShaperSOGroups>();
    private List<SerializedObject> serializedGroups = new List<SerializedObject>();
    private List<SerializedProperty> positionsProperties = new List<SerializedProperty>();

    // Button timing
    private double lastButtonClickTime = 0;
    private const double BUTTON_COOLDOWN = 0.3;
    #endregion

    [MenuItem("Tools/Level Design Editor")]
    public static void ShowWindow()
    {
        GetWindow<LevelDesignEditorWindow>("Level Design Editor");
    }

    private void OnGUI()
    {
        scrollPos = GUILayout.BeginScrollView(scrollPos, true, true);

        DrawHeader();
        DrawLevelConfiguration();
        DrawGridControls();

        if (currentLevel != null)
        {
            DrawLevelContent();
        }
        else
        {
            EditorGUILayout.HelpBox("Select a Level asset to edit.", MessageType.Info);
        }

        GUILayout.EndScrollView();
    }

    #region Main Drawing Methods
    private void DrawHeader()
    {
        GUIStyle centeredStyle = EditorUtils.CenteredStyle(18);
        GUILayout.Label("Level Editor", centeredStyle);

        runtimeHost = (LevelEditorRuntime)EditorGUILayout.ObjectField(
            "Runtime Host", runtimeHost, typeof(LevelEditorRuntime), true);
    }

    private void DrawLevelConfiguration()
    {
        currentLevel = (Level)EditorGUILayout.ObjectField(
            "Level", currentLevel, typeof(Level), false);

        EditorUtils.AddSpaceToGUI(10);

        maxMoves = EditorGUILayout.IntField("Max Moves", maxMoves);
        gridSize = EditorGUILayout.Vector2IntField("Grid Size", gridSize);

        if (currentLevel != null)
        {
            SyncSerializedLevel();
        }
    }

    private void DrawGridControls()
    {
        EditorUtils.AddSpaceToGUI(10);

        if (GUILayout.Button("Draw Level in Scene"))
        {
            if (ValidateRuntimeHost())
            {
                ClearGridEditorWindow();
                DrawLevelGridEditorWindow();
            }
        }
    }

    private void DrawLevelContent()
    {
        GUIStyle centeredStyle = EditorUtils.CenteredStyle(18);

        DrawLevelShaperGroups(centeredStyle);
        DrawObstacleConfig(centeredStyle);
        DrawObjectives(centeredStyle);
        DrawLevelShaperSO(centeredStyle);

        EditorUtils.AddSpaceToGUI(10);
        if (GUILayout.Button("Save Changes"))
        {
            SaveAllChanges();
        }
    }
    #endregion

    #region Level Shaper Groups
    private void DrawLevelShaperGroups(GUIStyle guiStyle)
    {
        EditorUtils.AddSpaceToGUI(10);
        EditorUtils.CreateLabelAndConfigure("Level Shaper Groups", guiStyle, Color.cyan);

        // Shaper name input
        newLevelShaperName = EditorGUILayout.TextField("New Shaper Name", newLevelShaperName);

        // Current shaper selection
        EditorGUI.BeginChangeCheck();
        currentShaper = (LevelShaperSO)EditorGUILayout.ObjectField(
            "Current Level Shaper", currentShaper, typeof(LevelShaperSO), false);

        if (EditorGUI.EndChangeCheck() || currentShaper != lastShaper)
        {
            RefreshGroupsFromShaper();
            lastShaper = currentShaper;
        }

        DrawExistingGroups();
        DrawShaperGroupButtons();
    }

    private void RefreshGroupsFromShaper()
    {
        ClearGroupLists();

        if (currentShaper != null)
        {
            var positionGroups = currentShaper.GetPositionGroups();
            foreach (var group in positionGroups)
            {
                if (group != null) // Only add valid groups
                {
                    AddGroupToEditor(group);
                }
            }
        }
    }

    private void DrawExistingGroups()
    {
        EditorUtils.AddSpaceToGUI(10);

        for (int i = groups.Count - 1; i >= 0; i--) // Iterate backwards for safe removal
        {
            if (DrawSingleGroup(i))
            {
                // Group was removed, continue to next
                continue;
            }
        }
    }

    private bool DrawSingleGroup(int index)
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label($"Level Shaper Group {index + 1}", EditorStyles.boldLabel);

        // Group asset field
        EditorGUI.BeginChangeCheck();
        LevelShaperSOGroups newGroup = (LevelShaperSOGroups)EditorGUILayout.ObjectField(
            "Group Asset", groups[index], typeof(LevelShaperSOGroups), false);

        if (EditorGUI.EndChangeCheck())
        {
            UpdateGroupAtIndex(index, newGroup);
        }

        bool groupRemoved = false;

        if (groups[index] != null && IsGroupValid(index))
        {
            DrawGroupPositions(index);
            groupRemoved = DrawGroupActionButtons(index);
        }
        else if (groups[index] == null)
        {
            EditorGUILayout.HelpBox("Drag a LevelShaperSOGroups asset here to configure its positions", MessageType.Info);
            groupRemoved = DrawEmptySlotButtons(index);
        }

        GUILayout.EndVertical();
        EditorUtils.AddSpaceToGUI(5);

        return groupRemoved;
    }

    private void DrawGroupPositions(int index)
    {
        if (!IsGroupValid(index)) return;

        serializedGroups[index].Update();

        EditorGUILayout.LabelField("Excluded Positions:", EditorStyles.boldLabel);

        var positionsProperty = positionsProperties[index];

        // Array size control
        int newSize = EditorGUILayout.IntField("Size", positionsProperty.arraySize);
        if (newSize != positionsProperty.arraySize && newSize >= 0)
        {
            positionsProperty.arraySize = newSize;
        }

        // Draw individual positions
        EditorGUI.indentLevel++;
        for (int j = positionsProperty.arraySize - 1; j >= 0; j--)
        {
            DrawSinglePosition(positionsProperty, j);
        }
        EditorGUI.indentLevel--;

        // Add new position button
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Add New Position"))
        {
            AddNewPosition(positionsProperty);
        }
        GUI.backgroundColor = Color.white;

        // Show positions info
        var positions = groups[index].GetPositions();
        EditorGUILayout.HelpBox($"This group excludes {positions.Count} position(s)", MessageType.Info);

        serializedGroups[index].ApplyModifiedProperties();
    }

    private void DrawSinglePosition(SerializedProperty positionsProperty, int positionIndex)
    {
        GUILayout.BeginHorizontal();

        SerializedProperty positionElement = positionsProperty.GetArrayElementAtIndex(positionIndex);

        GUILayout.Label($"Pos {positionIndex}:", GUILayout.Width(50));

        Vector2Int currentPos = positionElement.vector2IntValue;
        Vector2Int newPos = EditorGUILayout.Vector2IntField("", currentPos);
        if (newPos != currentPos)
        {
            positionElement.vector2IntValue = newPos;
        }

        // Delete button
        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("×", GUILayout.Width(25)))
        {
            positionsProperty.DeleteArrayElementAtIndex(positionIndex);
        }
        GUI.backgroundColor = Color.white;

        GUILayout.EndHorizontal();
    }

    private bool DrawGroupActionButtons(int index)
    {
        EditorUtils.AddSpaceToGUI(5);
        GUILayout.BeginHorizontal();

        // Post to scene
        GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button("Post to Scene"))
        {
            PostSingleGroupToScene(index);
        }

        // Add to shaper
        GUI.backgroundColor = Color.yellow;
        if (GUILayout.Button("Add to Shaper"))
        {
            AddGroupToCurrentShaper(index);
        }

        // Remove from editor
        GUI.backgroundColor = Color.red;
        bool removed = false;
        if (GUILayout.Button("Remove"))
        {
            RemoveGroupFromEditor(index);
            removed = true;
        }

        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        return removed;
    }

    private bool DrawEmptySlotButtons(int index)
    {
        GUILayout.BeginHorizontal();

        GUI.backgroundColor = Color.red;
        bool removed = false;
        if (GUILayout.Button("Remove Empty Slot"))
        {
            RemoveGroupFromEditor(index);
            removed = true;
        }

        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        return removed;
    }

    private void DrawShaperGroupButtons()
    {
        EditorUtils.AddSpaceToGUI(10);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Create New Group"))
        {
            CreateAndAddNewGroup();
        }
        if (GUILayout.Button("Add Empty Slot") && CanClickButton())
        {
            AddEmptySlot();
        }
        GUILayout.EndHorizontal();

        EditorUtils.AddSpaceToGUI(5);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Post All Groups"))
        {
            PostAllGroupsToScene();
        }
        if (GUILayout.Button("Clear All Groups"))
        {
            ClearGroupLists();
        }
        if (GUILayout.Button("Create New Shaper"))
        {
            CreateNewShaper();
        }
        GUILayout.EndHorizontal();
    }
    #endregion

    #region Group Management
    private void AddGroupToEditor(LevelShaperSOGroups group)
    {
        groups.Add(group);

        if (group != null)
        {
            SerializedObject so = new SerializedObject(group);
            serializedGroups.Add(so);
            positionsProperties.Add(so.FindProperty("levelShaperSOs"));
        }
        else
        {
            serializedGroups.Add(null);
            positionsProperties.Add(null);
        }
    }

    private void UpdateGroupAtIndex(int index, LevelShaperSOGroups newGroup)
    {
        groups[index] = newGroup;

        EnsureListCapacity(index);

        if (newGroup != null)
        {
            serializedGroups[index] = new SerializedObject(newGroup);
            positionsProperties[index] = serializedGroups[index].FindProperty("levelShaperSOs");
        }
        else
        {
            serializedGroups[index] = null;
            positionsProperties[index] = null;
        }
    }

    private void RemoveGroupFromEditor(int index)
    {
        if (index >= 0 && index < groups.Count)
        {
            groups.RemoveAt(index);

            if (index < serializedGroups.Count)
                serializedGroups.RemoveAt(index);

            if (index < positionsProperties.Count)
                positionsProperties.RemoveAt(index);
        }
    }

    private void AddGroupToCurrentShaper(int index)
    {
        if (!IsValidGroupIndex(index)) return;

        if (currentShaper == null)
        {
            currentShaper = CreateNewLevelShaper();
            if (currentShaper == null) return;
        }

        var group = groups[index];
        var shaperGroups = currentShaper.GetPositionGroups();
        if (!shaperGroups.Contains(group))
        {
            shaperGroups.Add(group);
            EditorUtility.SetDirty(currentShaper);
            AssetDatabase.SaveAssets();
            Debug.Log($"Added group {group.name} to shaper {currentShaper.name}");
        }
        else
        {
            Debug.LogWarning("Group is already in the shaper");
        }
    }

    private void CreateAndAddNewGroup()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Level Shaper Group",
            "NewLevelShaperGroup",
            "asset",
            "Choose location to save Level Shaper SO Group"
        );

        if (!string.IsNullOrEmpty(path))
        {
            LevelShaperSOGroups newGroup = CreateInstance<LevelShaperSOGroups>();
            AssetDatabase.CreateAsset(newGroup, path);
            AssetDatabase.SaveAssets();
            AddGroupToEditor(newGroup);
        }
    }

    private void AddEmptySlot()
    {
        AddGroupToEditor(null);
        Debug.Log($"Added empty slot. Total slots: {groups.Count}");
    }

    private void ClearGroupLists()
    {
        groups.Clear();
        serializedGroups.Clear();
        positionsProperties.Clear();
    }
    #endregion

    #region Scene Operations
    private void PostSingleGroupToScene(int index)
    {
        if (ValidateRuntimeHost() && IsValidGroupIndex(index))
        {
            var group = groups[index];
            runtimeHost.InstantiateLevelShaperEditor(group);
            Debug.Log($"Posted group {group.name} to scene");
        }
    }

    private void PostAllGroupsToScene()
    {
        if (!ValidateRuntimeHost()) return;

        // Clear existing
        foreach (var activeShaper in runtimeHost.activeLevelShaperGroups)
        {
            if (activeShaper != null)
            {
                runtimeHost.ClearGem(activeShaper.x, activeShaper.y);
                runtimeHost.CreateGem(activeShaper.x, activeShaper.y);
            }
        }

        // Post all groups
        foreach (var group in groups)
        {
            if (group != null)
            {
                runtimeHost.InstantiateLevelShaperEditor(group);
            }
        }
    }
    #endregion

    #region Level Shaper SO Management
    private void DrawLevelShaperSO(GUIStyle guiStyle)
    {
        EditorUtils.AddSpaceToGUI(10);
        EditorUtils.CreateLabelAndConfigure("Level Shapers", guiStyle, Color.cyan);

        if (currentLevel != null && levelShaperProperty != null)
        {
            EditorGUILayout.PropertyField(levelShaperProperty, new GUIContent("Level Shapers"), true);
            serializedLevel.ApplyModifiedProperties();
        }
        else
        {
            EditorGUILayout.HelpBox("No Level Shaper assigned to this level.", MessageType.Warning);
        }
    }

    private LevelShaperSO CreateNewLevelShaper()
    {
        if (string.IsNullOrEmpty(newLevelShaperName))
        {
            Debug.LogWarning("Please enter a name for the new shaper");
            return null;
        }

        LevelShaperSO newSO = ScriptableObject.CreateInstance<LevelShaperSO>();
        string path = $"Assets/_ScriptableObjects/LevelShapers/{newLevelShaperName}.asset";
        path = AssetDatabase.GenerateUniqueAssetPath(path);

        AssetDatabase.CreateAsset(newSO, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Created new Level Shaper: {path}");
        return newSO;
    }

    private void CreateNewShaper()
    {
        var newShaper = CreateNewLevelShaper();
        if (newShaper != null)
        {
            currentShaper = newShaper;
        }
    }
    #endregion

    #region Other Configurations
    private void DrawObstacleConfig(GUIStyle guiStyle)
    {
        EditorUtils.AddSpaceToGUI(10);
        EditorUtils.CreateLabelAndConfigure("Obstacle Config", guiStyle, Color.cyan);

        if (currentLevel != null && obstaclesProperty != null)
        {
            EditorGUILayout.PropertyField(obstaclesProperty, new GUIContent("Obstacles"), true);
            UpdateObstacles();
            serializedLevel.ApplyModifiedProperties();
        }
    }

    private void DrawObjectives(GUIStyle guiStyle)
    {
        EditorUtils.AddSpaceToGUI(10);
        EditorUtils.CreateLabelAndConfigure("Objectives Config", guiStyle, Color.cyan);

        if (currentLevel != null && objectivesProperty != null)
        {
            EditorGUILayout.PropertyField(objectivesProperty, new GUIContent("Objectives"), true);
            serializedLevel.ApplyModifiedProperties();
        }
    }
    #endregion

    #region Grid Operations
    private void DrawLevelGridEditorWindow()
    {
        bool shouldDrawDebug = GameObject.Find("Debugging") == null;

        runtimeHost.gridToConfigure = GridSystem2D<GridObj>.VerticalGrid(
            gridSize.x, gridSize.y, cellSize, originPosition, shouldDrawDebug);

        for (int x = 0; x < gridSize.x; x++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                runtimeHost.CreateGem(x, y);
            }
        }
    }

    private void ClearGridEditorWindow()
    {
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int y = 0; y < gridSize.y; y++)
            {
                runtimeHost.ClearGem(x, y);
            }
        }
    }
    #endregion

    #region Utility Methods
    private void SyncSerializedLevel()
    {
        if (serializedLevel == null || serializedLevel.targetObject != currentLevel)
        {
            serializedLevel = new SerializedObject(currentLevel);
            obstaclesProperty = serializedLevel.FindProperty("obstaclesConfigs");
            objectivesProperty = serializedLevel.FindProperty("gemClearObjectives");
            levelShaperProperty = serializedLevel.FindProperty("positionsToExclude");
        }
        serializedLevel.Update();
    }

    private void SaveAllChanges()
    {
        if (currentLevel != null)
        {
            EditorUtility.SetDirty(currentLevel);
            currentLevel.SetMaxMoves(maxMoves);
            currentLevel.SetWidth(gridSize.x);
            currentLevel.SetHeight(gridSize.y);
        }

        if (currentShaper != null)
        {
            EditorUtility.SetDirty(currentShaper);
        }

        foreach (var group in groups)
        {
            if (group != null)
            {
                EditorUtility.SetDirty(group);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("All changes saved");
    }

    private void AddNewPosition(SerializedProperty positionsProperty)
    {
        positionsProperty.arraySize++;
        var newElement = positionsProperty.GetArrayElementAtIndex(positionsProperty.arraySize - 1);
        newElement.vector2IntValue = Vector2Int.zero;
    }

    private bool ValidateRuntimeHost()
    {
        if (runtimeHost == null)
        {
            Debug.LogWarning("Assign a Runtime Host in the Scene first.");
            return false;
        }
        return true;
    }

    private bool CanClickButton()
    {
        return EditorApplication.timeSinceStartup - lastButtonClickTime > BUTTON_COOLDOWN;
    }

    private bool IsValidGroupIndex(int index)
    {
        return index >= 0 && index < groups.Count && groups[index] != null;
    }

    private bool IsGroupValid(int index)
    {
        return index < serializedGroups.Count &&
               index < positionsProperties.Count &&
               serializedGroups[index] != null &&
               positionsProperties[index] != null;
    }

    private void EnsureListCapacity(int index)
    {
        while (serializedGroups.Count <= index) serializedGroups.Add(null);
        while (positionsProperties.Count <= index) positionsProperties.Add(null);
    }
    #endregion

    #region Obstacle Management
    private void UpdateObstacles()
    {
        if (GUILayout.Button("Update Obstacles"))
        {
            if (!ValidateRuntimeHost()) return;

            HashSet<int> currentIds = new HashSet<int>();

            for (int i = 0; i < obstaclesProperty.arraySize; i++)
            {
                ProcessObstacleAtIndex(i, currentIds);
            }

            CleanupRemovedObstacles(currentIds);
        }
    }

    private void ProcessObstacleAtIndex(int index, HashSet<int> currentIds)
    {
        SerializedProperty element = obstaclesProperty.GetArrayElementAtIndex(index);
        var obstacleRef = element.FindPropertyRelative("obstacle").objectReferenceValue as Obstacle;
        var locationsRef = element.FindPropertyRelative("locations");
        var healthValue = element.FindPropertyRelative("health").intValue;

        List<Vector2Int> locations = new List<Vector2Int>();
        for (int c = 0; c < locationsRef.arraySize; c++)
        {
            SerializedProperty coord = locationsRef.GetArrayElementAtIndex(c);
            locations.Add(new Vector2Int(
                coord.FindPropertyRelative("x").intValue,
                coord.FindPropertyRelative("y").intValue
            ));
        }

        ObstacleConfig config = new ObstacleConfig
        {
            obstacle = obstacleRef,
            locations = locations,
            health = healthValue
        };

        currentIds.Add(index);
        ClearOldObstacles(index);
        runtimeHost.InstantiateObstacleConfigEditor(config, index);
    }

    private void ClearOldObstacles(int id)
    {
        if (runtimeHost.spawnedObstacles.TryGetValue(id, out var oldList))
        {
            foreach (var go in oldList)
            {
                if (go != null)
                {
                    Vector2Int gridPos = runtimeHost.gridToConfigure.GetXY(go.transform.position);
                    runtimeHost.ClearGem(gridPos.x, gridPos.y);
                    runtimeHost.CreateGem(gridPos.x, gridPos.y);
                }
            }
            oldList.Clear();
        }
        else
        {
            runtimeHost.spawnedObstacles[id] = new List<GameObject>();
        }
    }

    private void CleanupRemovedObstacles(HashSet<int> currentIds)
    {
        List<int> keysToRemove = new List<int>();

        foreach (var kvp in runtimeHost.spawnedObstacles)
        {
            if (!currentIds.Contains(kvp.Key))
            {
                foreach (var go in kvp.Value)
                {
                    if (go != null)
                    {
                        Vector2Int gridPos = runtimeHost.gridToConfigure.GetXY(go.transform.position);
                        runtimeHost.ClearGem(gridPos.x, gridPos.y);
                        runtimeHost.CreateGem(gridPos.x, gridPos.y);
                    }
                }
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            runtimeHost.spawnedObstacles.Remove(key);
        }
    }
    #endregion
}