using Match3Game;
using System;
using UnityEngine;
using System.Threading.Tasks;

public class GameEvents
{
    private GemTypes lastRequestedGemType;

    public event Action onMatchMade;
    public void MatchMade()
    {
        Debug.Log("Match Made Triggered");
        onMatchMade?.Invoke();
    }

    public event Action onSwapStarted;
    public void SwapStarted()
    {
        Debug.Log("Swap Started Triggered");
        onSwapStarted?.Invoke();
    }

    public Func<Level, Level> onLevelLoaded;
    public Level RequestLevel(Level level)
    {
        if (onLevelLoaded != null)
        {
            Level result = onLevelLoaded.Invoke(level);
            Debug.Log($"Received Level: {result.GetLevelName()}");
            return result;
        }
        Debug.LogWarning("No listeners for LevelLoaded.");
        return level; // Return the original if no listeners
    }

    public event Action onPlayerDataLoaded;
    public void PlayerDataLoaded()
    {
        Debug.Log("Player Data Loaded Triggered");
        onPlayerDataLoaded?.Invoke();
    }

    public event Action onPlayerDataSaved;
    public async Task PlayerDataSaved()
    {
        Debug.Log("Player Data Saved Triggered");
        await PlayerDataManager.instance.UpdatePlayerData();

        onPlayerDataSaved?.Invoke();
    }

    #region End Game Events

    public event Action onLevelCompleted;
    public void LevelCompleted()
    {
        Debug.Log("Level Completed Triggered");
        onLevelCompleted?.Invoke();
    }

    public event Action onLevelFailed;
    public void LevelFailed()
    {
        Debug.Log("Level Failed Triggered");
        onLevelFailed?.Invoke();
    }

    public event Action<int> onScoreChanged;
    public void ScoreChanged(int score)
    {
        Debug.Log($"Score Changed Triggered: {score}");
        onScoreChanged?.Invoke(score);
    }

    public event Action onScoreFinalized;
    public void ScoreFinalized()
    {
        Debug.Log("Score Finalized Triggered");
        onScoreFinalized?.Invoke();
    }

    #endregion

    #region Nuke Events
    public event Func<GemTypes, GemTypes> onGetGemType;

    public GemTypes RequestGemType(GemTypes type)
    {
        if (onGetGemType != null)
        {
            GemTypes result = onGetGemType.Invoke(type);
            //Debug.Log($"Received Gem Type: {result.name}");
            lastRequestedGemType = result; // Store it
            return result;
        }
        Debug.LogWarning("No listeners for GetGemType.");
        lastRequestedGemType = type; // Store the original
        return type;
    }

    public GemTypes GetLastRequestedGemType()
    {
        return lastRequestedGemType;
    }

    #endregion

    #region Obstacle Events


    public event Action onObstacleDamaged;
    public void ObstacleDamaged()
    {
        Debug.Log("Obstacle Damaged Triggered");
        onObstacleDamaged?.Invoke();
    }

    public event Action onObstacleCleared;
    public void ObstacleCleared()
    {
        onObstacleCleared?.Invoke();
    }

    #endregion

    #region Objective Events

    public event Action onObjectivesCreated;
    public void ObjectivesCreated()
    {
        Debug.Log("Objectives Created Triggered");
        onObjectivesCreated?.Invoke();
    }

    public event Action onObjectiveProgressionChanged;
    public void ObjectiveProgressionChanged()
    {
        Debug.Log("Objective Completed Triggered");
        onObjectiveProgressionChanged?.Invoke();
    }

    #endregion
}
