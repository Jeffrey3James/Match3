using System;
using System.Collections;
using System.Collections.Generic;
using JadedBelles.Networking;
using Match3Game.Levels;
using UnityEngine;

/// <summary>
/// Loads the level catalog. Remote-first: fetches levels from the JadedBelles API so new
/// levels ship without a build, and falls back to the bundled copy in
/// Assets/Resources/Levels/levels.json when offline or the API is unreachable.
/// </summary>
public class LevelHandler : MonoBehaviour
{
    public const string BundledLevelsResource = "Levels/levels";

    // ---- Bonus level configuration ----
    // Bonus levels are marked in levels.json by "type": "bonus" on the LevelData.
    // The board treats them as no-fail with a fixed 60s timer; each cleared gem is worth
    // +1 coin. Match3 reads BonusTimeSeconds via PlayerPrefs signaling (see PublishBonusSignal)
    // so no GameEvents change is required — the PlayerPrefs key is the cross-agent contract
    // documented in the Meta PR body.
    public const float BonusTimeSeconds = 60f;
    public const string CurrentLevelIsBonusPrefKey = "CurrentLevelIsBonus";
    public const string CurrentLevelBonusTimePrefKey = "CurrentLevelBonusTime";

    public static LevelHandler instance { get; private set; }

    /// <summary>True once the catalog has been parsed and hydrated.</summary>
    public bool LevelsReady { get; private set; }

    /// <summary>Fired once when the catalog finishes loading.</summary>
    public event Action OnLevelsReady;

    private readonly List<Level> levels = new List<Level>();
    // Set of level.id values whose LevelData.type == "bonus". Populated during hydration.
    // Level (owned by another agent) can't carry the field itself, so LevelHandler is the
    // one place that knows which levels are bonus. Match3/UI read via IsBonusLevel(index).
    private readonly HashSet<int> bonusLevelIds = new HashSet<int>();
    private GemTypeRegistry registry;

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
            return;
        }

        registry = GemTypeRegistry.Load();
        StartCoroutine(LoadCatalog());
    }

    private IEnumerator LoadCatalog()
    {
        bool remoteDone = false;
        string remoteJson = null;

        JadedBellesApiClient.Instance.GetLevelCatalog(
            json => { remoteJson = json; remoteDone = true; },
            error =>
            {
                Debug.LogWarning($"Remote level catalog unavailable ({error}). Using the bundled copy.");
                remoteDone = true;
            });

        while (!remoteDone)
            yield return null;

        if (string.IsNullOrEmpty(remoteJson) || !TryHydrate(remoteJson, "remote"))
        {
            var bundled = Resources.Load<TextAsset>(BundledLevelsResource);
            if (bundled == null || !TryHydrate(bundled.text, "bundled"))
            {
                Debug.LogError("No usable level catalog found (remote and bundled both failed).");
                yield break;
            }
        }

        LevelsReady = true;
        OnLevelsReady?.Invoke();
    }

    private bool TryHydrate(string json, string sourceLabel)
    {
        LevelCollection collection;
        try
        {
            collection = JsonUtility.FromJson<LevelCollection>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not parse the {sourceLabel} level catalog: {e.Message}");
            return false;
        }

        if (collection == null || collection.levels == null || collection.levels.Count == 0)
        {
            Debug.LogError($"The {sourceLabel} level catalog contains no levels.");
            return false;
        }

        levels.Clear();
        bonusLevelIds.Clear();
        levelIdsByIndex.Clear();
        collection.levels.Sort((a, b) => a.id.CompareTo(b.id));
        foreach (var data in collection.levels)
        {
            var level = ScriptableObject.CreateInstance<Level>();
            level.Hydrate(data, registry);
            levels.Add(level);
            levelIdsByIndex.Add(data.id);
            if (data.IsBonus) bonusLevelIds.Add(data.id);
        }

        Debug.Log($"Loaded {levels.Count} levels from the {sourceLabel} catalog (version {collection.version}). Bonus levels: {bonusLevelIds.Count}.");
        return true;
    }

    public List<Level> GetAllLevels()
    {
        return new List<Level>(levels);
    }

    /// <summary>Number of levels in the catalog, without copying the list.</summary>
    public int LevelCount => levels.Count;

    // ---- Bonus level API ----

    /// <summary>True if the level at the given catalog index is flagged "bonus".</summary>
    public bool IsBonusLevelAtIndex(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= levels.Count) return false;
        return IsBonusLevel(levels[levelIndex]);
    }

    /// <summary>True if the given hydrated Level is a bonus level.</summary>
    public bool IsBonusLevel(Level level)
    {
        if (level == null) return false;
        // Level's public API doesn't expose its source id; match by name against the
        // catalog we hydrated. Names collide only if the JSON is authored badly, in
        // which case both entries would be bonus/normal together — acceptable.
        for (int i = 0; i < levels.Count; i++)
        {
            if (levels[i] == level)
            {
                // Correlate index -> id via the parsed collection; we didn't retain LevelData
                // per level, but the ids in bonusLevelIds match catalog order. Rebuild by
                // walking the same sort — cheap because we only do this on level start.
                // Fallback path (id unknown here): assume not bonus.
                return bonusLevelIds.Contains(TryGetLevelIdAtIndex(i));
            }
        }
        return false;
    }

    // We drop the raw LevelData after hydration, so we reconstruct a stable id per index
    // during hydration and stash it on a parallel list. Keeps Level's public surface untouched.
    private readonly List<int> levelIdsByIndex = new List<int>();

    private int TryGetLevelIdAtIndex(int idx)
    {
        return (idx >= 0 && idx < levelIdsByIndex.Count) ? levelIdsByIndex[idx] : -1;
    }

    /// <summary>
    /// Publishes the current-level bonus signal via PlayerPrefs so Match3 (which A owns) can
    /// pick it up without a new event bus channel. Call before loading the game scene.
    /// </summary>
    public void PublishBonusSignal(int levelIndex)
    {
        bool isBonus = IsBonusLevelAtIndex(levelIndex);
        PlayerPrefs.SetInt(CurrentLevelIsBonusPrefKey, isBonus ? 1 : 0);
        PlayerPrefs.SetFloat(CurrentLevelBonusTimePrefKey, BonusTimeSeconds);
        PlayerPrefs.Save();
        Debug.Log($"Bonus signal for level index {levelIndex}: {(isBonus ? "BONUS" : "normal")}");
    }
}
