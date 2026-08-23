using Match3Game;
using Match3Game.Levels;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime representation of one level. No longer authored as individual assets:
/// instances are created by LevelHandler and hydrated from levels.json data.
/// </summary>
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
    [SerializeField] private List<Vector2Int> excludedCells = new List<Vector2Int>();

    [Header("Level Objectives")]
    [SerializeField] private List<ObjectiveConfig> gemClearObjectives = new List<ObjectiveConfig>();
    private Dictionary<GemTypes, IntEventChannel> objectiveChannels;

    [Header("Level Obstacles")]
    [SerializeField] private List<ObstacleConfig> obstaclesConfigs = new List<ObstacleConfig>();
    private Dictionary<Obstacle, IntEventChannel> obstacleChannels;

    /// <summary>Populates this instance from data-driven level JSON.</summary>
    public void Hydrate(LevelData data, GemTypeRegistry registry)
    {
        levelName = string.IsNullOrEmpty(data.name) ? $"Level {data.id}" : data.name;
        name = levelName;
        maxMoves = data.maxMoves;
        width = data.width;
        height = data.height;
        levelShaperPrefab = registry != null ? registry.GetLevelShaperPrefab() : null;

        excludedCells = new List<Vector2Int>();
        if (data.excludedCells != null)
        {
            foreach (var cell in data.excludedCells)
            {
                if (cell != null)
                    excludedCells.Add(cell.ToVector());
            }
        }

        gemClearObjectives = new List<ObjectiveConfig>();
        if (data.objectives != null && registry != null)
        {
            foreach (var objective in data.objectives)
            {
                var gemType = registry.FindGemType(objective.gemType);
                if (gemType != null)
                    gemClearObjectives.Add(new ObjectiveConfig(gemType, objective.amount));
            }
        }

        obstaclesConfigs = new List<ObstacleConfig>();
        if (data.obstacles != null && registry != null)
        {
            foreach (var obstacleData in data.obstacles)
            {
                var obstacleType = registry.FindObstacle(obstacleData.type);
                if (obstacleType == null)
                    continue;

                var locations = new List<Vector2Int>();
                if (obstacleData.cells != null)
                {
                    foreach (var cell in obstacleData.cells)
                    {
                        if (cell != null)
                            locations.Add(cell.ToVector());
                    }
                }

                obstaclesConfigs.Add(new ObstacleConfig(obstacleType, locations, obstacleData.health));
            }
        }
    }

    public int GetMaxMoves() => maxMoves;
    public int GetWidth() => width;
    public int GetHeight() => height;

    public void SetMaxMoves(int moves) => maxMoves = moves;
    public void SetWidth(int w) => width = w;
    public void SetHeight(int h) => height = h;

    public List<ObstacleConfig> GetObstacles() => obstaclesConfigs;
    public List<ObjectiveConfig> GetObjectives() => gemClearObjectives;
    public string GetLevelName() => levelName;
    public int GetScoreForThisLevel() => scoreForThisLevel;

    public void AddScoreForLevel(int score)
    {
        scoreForThisLevel += score;
    }

    #region LevelShaper
    public List<Vector2Int> GetExcludedPositions()
    {
        return new List<Vector2Int>(excludedCells);
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

    public void ClearChannels()
    {
        obstacleChannels?.Clear();
    }

    #endregion
}
