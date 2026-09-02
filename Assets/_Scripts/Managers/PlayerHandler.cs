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

    private const float timeToRegainALife = 1200f; // Time in seconds to regenerate a life
    private const int maxLives = 5; // Maximum number of lives a player can have
    private float regenPollAccumulator;
    private const float regenPollInterval = 1f; // Check the countdown once per second

    // ---- Meta systems (stars, streak, boosters, decorate) ----
    // These are session-local shortcuts around playerData.*. The playerData fields are the
    // source of truth and are what gets serialized by PlayerDataManager.
    private bool _butlersGiftPending; // ephemeral; set when streak just hit exactly 3
    private const int ButlersGiftStreakThreshold = 3;

    // All meta mutations funnel through this so we never NRE against a not-yet-initialised
    // PlayerDataManager (which is possible early during scene bootstrap).
    private void PersistMetaChange()
    {
        if (PlayerDataManager.instance != null)
            _ = PlayerDataManager.instance.UpdatePlayerData();
    }

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
        GameEventsManager.instance.gameEvents.onLevelFailed += OnLevelFailed;
    }

    private void OnLevelCompleted()
    {
        playerData.playerLevel++;

        // Meta rewards for winning a level: +1 star and +1 streak.
        // Butler's Gift: exactly when the streak reaches the threshold, arm the flag so
        // Match3 can consume it on the next level start (free striped gem placement).
        AddStars(1);
        IncrementStreak();
        if (playerData != null && playerData.winStreak == ButlersGiftStreakThreshold)
        {
            _butlersGiftPending = true;
        }
    }

    private void OnLevelFailed()
    {
        // Losing a level always resets the streak. Stars persist.
        ResetStreak();
    }

    private void Update()
    {
        // Poll the life-regen countdown about once per second (no need for per-frame checks).
        regenPollAccumulator += Time.unscaledDeltaTime;
        if (regenPollAccumulator < regenPollInterval) return;
        regenPollAccumulator = 0f;
        TickLifeRegen();
    }

    /// <summary>
    /// Grants any lives whose countdown has elapsed. Handles offline catch-up:
    /// if the player was away for several regen periods, they get all of them
    /// (up to maxLives). Persists once per batch of granted lives.
    /// </summary>
    private void TickLifeRegen()
    {
        if (playerData == null) return;

        if (playerData.playerLives >= maxLives)
        {
            // Full: no countdown should be running.
            if (playerData.playerLifeCountdown != 0) playerData.playerLifeCountdown = 0;
            return;
        }

        // Below max but no countdown running (legacy saves / after ad-granted lives): start one.
        if (playerData.playerLifeCountdown == 0)
        {
            playerData.playerLifeCountdown = TimeUtils.UnixNow + (long)timeToRegainALife;
            return;
        }

        long now = TimeUtils.UnixNow;
        int granted = 0;

        // Catch-up loop: each elapsed interval grants one life and advances the deadline.
        while (playerData.playerLives < maxLives && now >= playerData.playerLifeCountdown)
        {
            playerData.playerLives++;
            granted++;
            playerData.playerLifeCountdown += (long)timeToRegainALife;
        }

        if (granted > 0)
        {
            if (playerData.playerLives >= maxLives) playerData.playerLifeCountdown = 0;
            Debug.Log($"Regenerated {granted} life/lives. Lives: {playerData.playerLives}/{maxLives}");
            _ = PlayerDataManager.instance.UpdatePlayerData();
        }
    }

    /// <summary>Seconds until the next life arrives, or 0 when full / no countdown. For UI timers.</summary>
    public long GetSecondsUntilNextLife()
    {
        if (playerData == null || playerData.playerLives >= maxLives || playerData.playerLifeCountdown == 0)
            return 0;
        return Math.Max(0, playerData.playerLifeCountdown - TimeUtils.UnixNow);
    }

    public int GetMaxLives() => maxLives;

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

    // ACT 2 ADDITION — minimal accessor only, no behavior change to Act 1.
    // playerData.playerLevel is the existing progress counter (incremented in
    // OnLevelCompleted above). Act2SpecialTileManager reads this through
    // GetPlayerLevel() to decide whether the standard combo table (striped /
    // wrapped / color-bomb tiles) is active for the current player. See
    // Assets/_Scripts/Act2/Act2SpecialTileManager.cs for the actual gate check.
    public int GetPlayerLevel()
    {
        return playerData != null ? playerData.playerLevel : 0;
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
        // Only start a countdown when none is already running. Spending a second
        // life while a regen is in progress must NOT reset the timer.
        if (playerData.playerLifeCountdown == 0)
        {
            playerData.playerLifeCountdown = TimeUtils.UnixNow + (long)timeToRegainALife;
        }
        _ = PlayerDataManager.instance.UpdatePlayerData();
    }

    public void AddALifeToPlayer()
    {
        if (playerData.playerLives < maxLives)
        {
            playerData.playerLives++;
            // Reaching max cancels any running countdown.
            if (playerData.playerLives >= maxLives) playerData.playerLifeCountdown = 0;
            Debug.Log($"Added a life. Lives: {playerData.playerLives}/{maxLives}");
            _ = PlayerDataManager.instance.UpdatePlayerData();
        }
        else
        {
            Debug.Log("Player is already at max lives; not adding another.");
        }
    }

    #endregion

    #region Stars

    public int GetStars()
    {
        return playerData != null ? playerData.stars : 0;
    }

    public void AddStars(int amount)
    {
        if (playerData == null || amount <= 0) return;
        playerData.stars += amount;
        Debug.Log($"Added {amount} star(s). Total stars: {playerData.stars}");
        PersistMetaChange();
    }

    /// <summary>Returns false when the player can't afford <paramref name="amount"/> and no stars are spent.</summary>
    public bool SpendStars(int amount)
    {
        if (playerData == null || amount <= 0) return false;
        if (playerData.stars < amount)
        {
            Debug.LogWarning($"Not enough stars to spend! Have {playerData.stars}, need {amount}.");
            return false;
        }
        playerData.stars -= amount;
        Debug.Log($"Spent {amount} star(s). Remaining stars: {playerData.stars}");
        PersistMetaChange();
        return true;
    }

    #endregion

    #region Win Streak

    public int GetWinStreak()
    {
        return playerData != null ? playerData.winStreak : 0;
    }

    public void IncrementStreak()
    {
        if (playerData == null) return;
        playerData.winStreak++;
        Debug.Log($"Win streak incremented to {playerData.winStreak}.");
    }

    public void ResetStreak()
    {
        if (playerData == null) return;
        if (playerData.winStreak == 0) return;
        Debug.Log($"Win streak reset from {playerData.winStreak} to 0.");
        playerData.winStreak = 0;
        PersistMetaChange();
    }

    /// <summary>
    /// Board start reads this to decide whether to place a free striped gem.
    /// Returns true exactly once per streak milestone; clears the flag on read.
    /// </summary>
    public bool ConsumeButlersGift()
    {
        if (!_butlersGiftPending) return false;
        _butlersGiftPending = false;
        Debug.Log("Butler's Gift consumed.");
        return true;
    }

    #endregion

    #region Boosters

    private static string NormalizeBoosterKey(string key)
    {
        return string.IsNullOrEmpty(key) ? string.Empty : key.Trim().ToLowerInvariant();
    }

    public int GetBoosterCount(string key)
    {
        if (playerData == null) return 0;
        switch (NormalizeBoosterKey(key))
        {
            case "rocket":    return playerData.rocketBoosters;
            case "tnt":       return playerData.tntBoosters;
            case "lightball": return playerData.lightballBoosters;
            case "hammer":    return playerData.hammerBoosters;
            default:
                Debug.LogWarning($"Unknown booster key '{key}'.");
                return 0;
        }
    }

    public void AddBooster(string key, int amount)
    {
        if (playerData == null || amount <= 0) return;
        switch (NormalizeBoosterKey(key))
        {
            case "rocket":    playerData.rocketBoosters    += amount; break;
            case "tnt":       playerData.tntBoosters       += amount; break;
            case "lightball": playerData.lightballBoosters += amount; break;
            case "hammer":    playerData.hammerBoosters    += amount; break;
            default:
                Debug.LogWarning($"Unknown booster key '{key}'.");
                return;
        }
        PersistMetaChange();
    }

    /// <summary>Returns false when the booster inventory is empty; nothing is consumed.</summary>
    public bool ConsumeBooster(string key)
    {
        if (playerData == null) return false;
        switch (NormalizeBoosterKey(key))
        {
            case "rocket":
                if (playerData.rocketBoosters <= 0) return false;
                playerData.rocketBoosters--;
                break;
            case "tnt":
                if (playerData.tntBoosters <= 0) return false;
                playerData.tntBoosters--;
                break;
            case "lightball":
                if (playerData.lightballBoosters <= 0) return false;
                playerData.lightballBoosters--;
                break;
            case "hammer":
                if (playerData.hammerBoosters <= 0) return false;
                playerData.hammerBoosters--;
                break;
            default:
                Debug.LogWarning($"Unknown booster key '{key}'.");
                return false;
        }
        PersistMetaChange();
        return true;
    }

    #endregion

    #region Decorate progress

    public int GetDecorateProgress()
    {
        return playerData != null ? playerData.decorateProgress : 0;
    }

    public bool IsDecorateTaskComplete(int taskIndex)
    {
        if (playerData == null || taskIndex < 0 || taskIndex >= 32) return false;
        return (playerData.decorateProgress & (1 << taskIndex)) != 0;
    }

    /// <summary>
    /// Spends 1 star, sets the task bit, and persists. Returns false if the task is already
    /// complete or the player can't afford the star cost. No partial state changes.
    /// </summary>
    public bool CompleteDecorateTask(int taskIndex)
    {
        if (playerData == null || taskIndex < 0 || taskIndex >= 32)
        {
            Debug.LogWarning($"CompleteDecorateTask: invalid taskIndex {taskIndex}.");
            return false;
        }
        int bit = 1 << taskIndex;
        if ((playerData.decorateProgress & bit) != 0)
        {
            Debug.Log($"Decorate task {taskIndex} already complete.");
            return false;
        }
        if (!SpendStars(1)) return false; // SpendStars already persists
        playerData.decorateProgress |= bit;
        Debug.Log($"Decorate task {taskIndex} completed. Progress bitmask now {playerData.decorateProgress}.");
        PersistMetaChange();
        return true;
    }

    #endregion

    #region Continue-offer counter (ephemeral)

    public int GetContinueAttemptCount()
    {
        return playerData != null ? playerData.continueAttemptCount : 0;
    }

    public void IncrementContinueAttemptCount()
    {
        if (playerData == null) return;
        playerData.continueAttemptCount++;
    }

    public void ResetContinueAttemptCount()
    {
        if (playerData == null) return;
        playerData.continueAttemptCount = 0;
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

    // ---- Meta systems (added by Agent E) ----
    // Additive only: existing saves that do not carry these fields deserialize with 0/default,
    // which is the correct starting value in every case.
    public int stars;
    public int winStreak;
    public int decorateProgress; // bitmask: bit i = decorate task i done

    // Ephemeral. It ships in the JSON payload because JsonUtility can't skip it, but it is
    // reset on level start by LevelResultPanel/ContinueOfferPanel (Agent D) so its value on
    // disk is only ever a stale in-flight value from a crashed session.
    [System.NonSerialized] public int continueAttemptCount;

    // Booster inventory (persistent). Keys map: rocket/tnt/lightball/hammer.
    public int rocketBoosters;
    public int tntBoosters;
    public int lightballBoosters;
    public int hammerBoosters;

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