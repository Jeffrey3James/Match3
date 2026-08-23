using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using StroTheGoat;
using System;

public class PlayerHandler : MonoBehaviour
{
    public static PlayerHandler instance { get; private set; }

    public PlayerData playerData;

    [SerializeField] Level playerCurrentLevel;
    private float TimeUntilNewLife;

     private const float timeToRegainALife = 1200f; // Time in seconds to regenerate a life
    private const int maxLives = 5; // Maximum number of lives a player can have
    private CountdownTimer lifeTimer;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        GameEventsManager.instance.gameEvents.onLevelCompleted += OnLevelCompleted;
    }

    private void OnLevelCompleted()
    {
        playerData.playerLevel++;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"Scene Loaded: {scene.name}");

        if (LevelHandler.instance == null)
        {
            Debug.LogWarning("LevelHandler is not available yet; the current level will be set once levels load.");
            return;
        }

        if (LevelHandler.instance.LevelsReady)
        {
            SetCurrentLevelFromCatalog();
        }
        else
        {
            LevelHandler.instance.OnLevelsReady += SetCurrentLevelFromCatalog;
        }
    }

    private void SetCurrentLevelFromCatalog()
    {
        LevelHandler.instance.OnLevelsReady -= SetCurrentLevelFromCatalog;

        List<Level> allLevels = LevelHandler.instance.GetAllLevels();
        if (allLevels.Count == 0)
        {
            Debug.LogError("The level catalog is empty; cannot set the current level.");
            return;
        }

        int levelIndex = playerData != null ? playerData.playerLevel : 0;
        levelIndex = Mathf.Clamp(levelIndex, 0, allLevels.Count - 1);
        playerCurrentLevel = allLevels[levelIndex];
    }

    public Level GetCurrentLevel()
    {
        if (playerCurrentLevel == null)
        {
            Debug.LogError("Player current level is null. Please ensure it is set correctly.");
            return null;
        }
        return playerCurrentLevel;
    }

    public void RecievePlayerDataFromCloud(PlayerData data)
    {
        playerData = data;
    }

    public PlayerData SendPlayerDataToCloud(PlayerData data)
    {
        if (playerData == null)
        {
            Debug.LogError("Player data is null. Cannot send to cloud.");
            return null;
        }
        Debug.Log($"Sending Player Data to Cloud: {playerData.playerName}, Level: {playerData.playerLevel}, Lives: {playerData.playerLives}, Coins: {playerData.playerCoins}");

        return playerData;
    }

    #region Coins

    public void AddCoins(int amount)
    {
        playerData.playerCoins += amount;
        Debug.Log($"Added {amount} coins. Total coins: {playerData.playerCoins}");
    }

    public void SpendCoins(int amount)
    {
        if (playerData.playerCoins >= amount)
        {
            playerData.playerCoins -= amount;
            Debug.Log($"Spent {amount} coins. Remaining coins: {playerData.playerCoins}");
        }
        else
        {
            Debug.LogWarning("Not enough coins to spend!");
        }
    }

    public int GetPlayerCoins()
    {
        return playerData.playerCoins;
    }

    #endregion

    #region Lives
    public bool CheckPlayerLives()
    {
        if (playerData.playerLives == 0)
        {
            Debug.Log("Player has no lives left. Cannot start another level.");
            return false;
        }
        Debug.Log($"Player has {playerData.playerLives} lives left. Proceeding to start level: {playerCurrentLevel.GetLevelName()}");
        return true;
    }

    public void UseALifeFromPlayer()
    {
        if (playerData.playerLives > 0)
        {
            playerData.playerLives--;
            CalculateNewLife();
            Debug.Log($"Used a life. Remaining lives: {playerData.playerLives}");
        }
        else
        {
            
            Debug.LogWarning("No lives left to use!");
        }
    }
    
    private void CalculateNewLife()
    {
        long newLifeTime = TimeUtils.UnixNow + (long)timeToRegainALife;
        playerData.playerLifeCountdown = newLifeTime;
        _ = PlayerDataManager.instance.UpdatePlayerData();
    }

    public void AddALifeToPlayer()
    {
        if (playerData.playerLives < maxLives)
        {
            playerData.playerLives++;
            Debug.Log($"Added a life. Lives: {playerData.playerLives}/{maxLives}");
            _ = PlayerDataManager.instance.UpdatePlayerData();
        }
        else
        {
            Debug.Log("Player is already at max lives; not adding another.");
        }
    }

    #endregion
}

[System.Serializable]
public class PlayerData
{
    public string playerName;
    public int playerLevel;
    public int playerLives;
    public int playerCoins;

    public long playerLifeCountdown;

    public PlayerData() { }

    public PlayerData(string name, int level, int lives, int coins, long lifeCountdown)
    {
        playerName = name;
        playerLevel = level;
        playerLives = lives;
        playerCoins = coins;
        playerLifeCountdown = lifeCountdown;

    }
}