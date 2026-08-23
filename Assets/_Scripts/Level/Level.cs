using Match3Game;
using NUnit.Framework;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;

[CreateAssetMenu(fileName = "Level", menuName = "Match3/Level")]
public class Level : ScriptableObject
{
    [SerializeField] private string levelName;
    [SerializeField] private int maxMoves;
    private int scoreForThisLevel;

    [Header("Grid Settings")]
    [SerializeField] private int width;
    [SerializeField] private int height;

    [Header("Level Shaper")]
    [SerializeField] private GameObject levelShaperPrefab;
    [SerializeField] private List<LevelShaperSO> positionsToExclude;

    [Header("Level Obstacles")]
    private Dictionary<Obstacle, IntEventChannel> obstacleChannels;

    [Header("Level Objectives")]
    [SerializeField] private List<ObjectiveConfig> gemClearObjectives;
    private Dictionary<GemTypes, IntEventChannel> objectiveChannels;

    [Header("Level Obstacles")]
    [SerializeField] private List<ObstacleConfig> obstaclesConfigs;

    [Header("Gem Dictionary")]
    private Dictionary<GemTypes, IntEventChannel> regularGemChannels;

    public int GetMaxMoves() => maxMoves;
    public int GetWidth() => width;
    public int GetHeight() => height;

    public void SetMaxMoves(int moves) => maxMoves = moves;
    public void SetWidth(int w) => width = w;
    public void SetHeight(int h) => height = h;
    private void OnEnable()
    {
        levelName = this.name;
    }

    public List<ObstacleConfig> GetObstacles() => obstaclesConfigs;
    public List<ObjectiveConfig> GetObjectives() => gemClearObjectives;
    public string GetLevelName() => levelName;
    public int GetScoreForThisLevel() => scoreForThisLevel;

    public void AddScoreForLevel(int score)
    {
        scoreForThisLevel += score;
        Debug.Log(scoreForThisLevel);
    }

    #region LevelShaper
    public List<Vector2Int> GetExcludedPositions()
    {
        var allToBeExcluded = new List<Vector2Int>();
        foreach (var levelShaper in positionsToExclude)
        {
            allToBeExcluded.AddRange(levelShaper.GetPositionsToExclude());
        }
        return allToBeExcluded;
    }

    public GameObject GetLevelShaperPrefab() => levelShaperPrefab;
    #endregion

    #region Level Obstacles
   
    public List<ObstacleConfig> GetObtacleConfigs()
    {
        return obstaclesConfigs;
    }

    public IntEventChannel GetOrCreateChannel(Obstacle obstacle)
    {
        if (obstacleChannels == null)
        {
            obstacleChannels = new Dictionary<Obstacle, IntEventChannel>();
        }

        if (!obstacleChannels.TryGetValue(obstacle, out IntEventChannel channel))
        {
            channel = ScriptableObject.CreateInstance<IntEventChannel>();
            channel.name = obstacle.name + " Channel";
            obstacleChannels[obstacle] = channel;
        }

        return channel;
    }

    public IntEventChannel GetOrCreateChannelObjConfig(GemTypes objective)
    {
        if (objectiveChannels == null)
        {
            objectiveChannels = new Dictionary<GemTypes, IntEventChannel>();
        }
        if (!objectiveChannels.TryGetValue(objective, out IntEventChannel channel))
        {
            channel = ScriptableObject.CreateInstance<IntEventChannel>();
            channel.name = objective.name + " Channel";
            objectiveChannels[objective] = channel;
        }
        return channel;
    }
/*
    public IntEventChannel GetOrCreateGemChannel(GemTypes gemType)
    {
        if (regularGemChannels == null)
        {
            regularGemChannels = new Dictionary<GemTypes, IntEventChannel>();
        }
        if (!regularGemChannels.TryGetValue(gemType, out IntEventChannel channel))
        {
            channel = ScriptableObject.CreateInstance<IntEventChannel>();
            channel.name = gemType.name + " Channel";
            regularGemChannels[gemType] = channel;
        }
        return channel;
    }*/

    public void ClearChannels()
    {
        obstacleChannels?.Clear();
    }

    #endregion
}
   

