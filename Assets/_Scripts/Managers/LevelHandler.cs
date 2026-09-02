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

    public static LevelHandler instance { get; private set; }

    /// <summary>True once the catalog has been parsed and hydrated.</summary>
    public bool LevelsReady { get; private set; }

    /// <summary>Fired once when the catalog finishes loading.</summary>
    public event Action OnLevelsReady;

    private readonly List<Level> levels = new List<Level>();
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
        collection.levels.Sort((a, b) => a.id.CompareTo(b.id));
        foreach (var data in collection.levels)
        {
            var level = ScriptableObject.CreateInstance<Level>();
            level.Hydrate(data, registry);
            levels.Add(level);
        }

        Debug.Log($"Loaded {levels.Count} levels from the {sourceLabel} catalog (version {collection.version}).");
        return true;
    }

    public List<Level> GetAllLevels()
    {
        return new List<Level>(levels);
    }

    /// <summary>Number of levels in the catalog, without copying the list.</summary>
    public int LevelCount => levels.Count;
}
