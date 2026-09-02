using TMPro;
using UnityEngine;
using Match3Game;
using UnityEngine.UI;
using System.Collections.Generic;
using System;

public class Match3UI : MonoBehaviour
{
    [Header("Objectives")]
    [SerializeField] private GameObject objectiveUIPrefab;

    [Header("Obstacles")]
    [SerializeField] private GameObject obstacleUIPrefab;

    [Header("End of Level")]
    [Tooltip("Owns the win/loss buttons. See LevelResultPanel for which buttons show when.")]
    [SerializeField] private LevelResultPanel levelResultPanel;
    [SerializeField] private Transform uiContainer;

    [Header("UI Background Container")]
    [SerializeField] private TextMeshProUGUI coinText;
    [SerializeField] private Transform uiBackgroundContainer;
    [SerializeField] private TextMeshProUGUI headerText;

    private TextMeshProUGUI coinTextInstance;

    private Match3 match3;
    private Level level;

    private System.Action onScoreFinalizedAction;

    // Which result we're showing. Set by onLevelCompleted / onLevelFailed, consumed by
    // onScoreFinalized. Without this the panel can't tell a win from a loss, because the
    // event that actually reveals it fires for both.
    private LevelResultPanel.LevelResult pendingResult = LevelResultPanel.LevelResult.Loss;
    private bool hasPendingResult;

    private void Awake()
    {
        match3 = FindFirstObjectByType<Match3>();
        if (match3 != null) level = match3.GetLevel();
    }

    private void Start()
    {
        // Match3 may have deferred its level resolution to Start; ask again now.
        if (level == null && match3 != null) level = match3.GetLevel();
        if (level == null)
        {
            Debug.LogWarning("Match3UI: no Level available yet; skipping UI init. Match3 should log the underlying reason.");
            enabled = false;
            return;
        }

        var events = GameEventsManager.instance.gameEvents;

        if (levelResultPanel == null)
        {
            levelResultPanel = FindFirstObjectByType<LevelResultPanel>(FindObjectsInactive.Include);
            if (levelResultPanel == null)
                Debug.LogError("Match3UI: no LevelResultPanel assigned or found. " +
                               "The end-of-level buttons will never appear.");
        }

        levelResultPanel?.Hide();
        CreateObstacleUI();
        CreateObjectiveUI();

        events.onLevelCompleted += LevelComplete;
        events.onLevelFailed += LevelFailed;
        events.onScoreChanged += UpdateScoreUI;

        onScoreFinalizedAction = ShowResultPanel;
        events.onScoreFinalized += onScoreFinalizedAction;
    }

    private void OnDestroy()
    {
        var events = GameEventsManager.instance.gameEvents;
        events.onLevelCompleted -= LevelComplete;
        events.onLevelFailed -= LevelFailed;
        events.onScoreChanged -= UpdateScoreUI;
        events.onScoreFinalized -= onScoreFinalizedAction;
        
    }

    private void UpdateScoreUI(int score)
    {
        Debug.Log(score);
        if (coinTextInstance != null)
        {
            coinTextInstance.text = score.ToString();
        }
    }

    private void LevelFailed()
    {
        pendingResult = LevelResultPanel.LevelResult.Loss;
        hasPendingResult = true;

        // A loss has no score to finalize, so show immediately rather than waiting on an
        // event that may never fire.
        ShowResultPanel();
    }

    /// <summary>
    /// Reveals the result panel with the correct button set. Safe to call twice — a loss
    /// calls it directly and onScoreFinalized may call it again.
    /// </summary>
    private void ShowResultPanel()
    {
        if (levelResultPanel == null) return;
        if (levelResultPanel.IsShown) return;

        if (!hasPendingResult)
        {
            Debug.LogWarning("Match3UI: score finalized without a win/loss signal. " +
                             "Defaulting to the loss layout.");
        }

        levelResultPanel.Show(pendingResult);
    }

    private void LevelComplete()
    {
        pendingResult = LevelResultPanel.LevelResult.Win;
        hasPendingResult = true;

        GridLayoutGroup uiGrid = uiBackgroundContainer.GetComponent<GridLayoutGroup>();
        headerText.text = "REWARD";
        foreach (Transform child in uiBackgroundContainer)
        {
            Destroy(child.gameObject);
        }

        uiGrid.cellSize = new Vector2(100, 100); 
        coinTextInstance = Instantiate(coinText, uiBackgroundContainer);   

        Debug.Log("Level Completed.. Updating UI");
        // The panel itself waits for onScoreFinalized so the reward tally finishes first.
    }

    public void CreateObstacleUI()
    {
        //TODO: Swap the TextMeshPro Number for a Check or something
        //That designates that the set of obstacle has been cleared

        if (obstacleUIPrefab == null || uiBackgroundContainer == null)
        {
            Debug.LogError("Match3UI: Obstacle UI Prefab or UI Background Container is unassigned. " +
                           "Nothing will appear in the top panel.");
            return;
        }

        List<ObstacleConfig> obstacleConfigsList = level.GetObtacleConfigs();
        if (obstacleConfigsList == null || obstacleConfigsList.Count == 0)
        {
            Debug.Log($"Match3UI: level '{level.GetLevelName()}' defines no obstacles, " +
                      "so no obstacle UI is spawned.");
            return;
        }

        HashSet<ObstacleConfig> obstacleConfigs = new HashSet<ObstacleConfig>(obstacleConfigsList);

        foreach (var obstacleConfig in obstacleConfigs)
        {
            // Hydrate skips obstacles whose name isn't in GemTypeRegistry, but a hand-authored
            // asset can still carry an empty slot. Instantiating against it would throw mid-loop
            // and take the remaining obstacles down with it.
            if (obstacleConfig.obstacle == null)
            {
                Debug.LogWarning("Match3UI: skipping an obstacle config with no Obstacle assigned.");
                continue;
            }

            var channel = level.GetOrCreateChannel(obstacleConfig.obstacle);
            var uiObj = Instantiate(obstacleUIPrefab, uiBackgroundContainer);
            var ui = uiObj.GetComponent<ObstacleUI>();
            ui.SetObstacleImage(obstacleConfig.obstacle.sprite);
            ui.SetChannel(channel);
            // Initialize the channel with the starting value
            channel.Invoke(obstacleConfig.locations.Count);
        }
    }

    public void CreateObjectiveUI()
    {
        List<ObjectiveConfig> objectiveConfigsList = level.GetObjectives();
        HashSet<ObjectiveConfig> objectiveConfigs = new HashSet<ObjectiveConfig>(objectiveConfigsList);

        foreach (var objectiveConfig in objectiveConfigs)
        {
            var channel = level.GetOrCreateChannelObjConfig(objectiveConfig.typesToClear);
            var uiObj = Instantiate(objectiveUIPrefab, uiBackgroundContainer);
            var ui = uiObj.GetComponent<ObjectiveUI>();
            ui.SetObjectiveImage(objectiveConfig.typesToClear.sprite);
            ui.SetChannel(channel);
            channel.Invoke(objectiveConfig.amountToClear);
        }
    }

}

