using TMPro;
using UnityEngine;
using Match3Game;
using UnityEngine.UI;
using System.Dynamic;
using System.Collections.Generic;
using System;
using UnityEngine.SceneManagement;
using UnityEditor.PackageManager;

public class Match3UI : MonoBehaviour
{
    [Header("Objectives")]
    [SerializeField] private GameObject objectiveUIPrefab;

    [Header("Obstacles")]
    [SerializeField] private GameObject obstacleUIPrefab;

    [Header("Game Over")]
    [SerializeField] private GameObject gameOverWindow;
    [SerializeField] private Button retry;
    [SerializeField] private Button mainMenu;
    [SerializeField] private Transform uiContainer;

    [Header("UI Background Container")]
    [SerializeField] private TextMeshProUGUI coinText;
    [SerializeField] private Transform uiBackgroundContainer;
    [SerializeField] private TextMeshProUGUI headerText;

    private TextMeshProUGUI coinTextInstance;

    private Match3 match3;
    private Level level;

    private System.Action onScoreFinalizedAction;

    private void Awake()
    {
        match3 = FindFirstObjectByType<Match3>();
        level = match3.GetLevel();
    }

    private void Start()
    {
        var events = GameEventsManager.instance.gameEvents;
        SetupGameOverScreen();
        gameOverWindow.SetActive(false);
        CreateObstacleUI();
        CreateObjectiveUI();

        events.onLevelCompleted += LevelComplete;
        events.onLevelFailed += LevelFailed;
        events.onScoreChanged += UpdateScoreUI;

        onScoreFinalizedAction = () => gameOverWindow.SetActive(true);
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
        
        gameOverWindow.SetActive(true);
    }

    private void LevelComplete()
    {
        GridLayoutGroup uiGrid = uiBackgroundContainer.GetComponent<GridLayoutGroup>();
        headerText.text = "REWARD";
        foreach (Transform child in uiBackgroundContainer)
        {
            Destroy(child.gameObject);
        }

        uiGrid.cellSize = new Vector2(100, 100); 
        coinTextInstance = Instantiate(coinText, uiBackgroundContainer);   

        Debug.Log("Level Completed.. Updating UI");
    }

    public void CreateObstacleUI()
    {
        //TODO: Swap the TextMeshPro Number for a Check or something
        //That designates that the set of obstacle has been cleared

        List<ObstacleConfig> obstacleConfigsList = level.GetObtacleConfigs();
        HashSet<ObstacleConfig> obstacleConfigs = new HashSet<ObstacleConfig>(obstacleConfigsList);

        foreach (var obstacleConfig in obstacleConfigs)
        {
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

    private void SetupGameOverScreen()
    {
        retry.onClick.AddListener(() =>
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                Debug.Log("Retry button clicked");
            });

        mainMenu.onClick.AddListener(() =>
            {
                SceneManager.LoadScene("MainMenu");
                Debug.Log("Main Menu button clicked");
            });
    }
}

