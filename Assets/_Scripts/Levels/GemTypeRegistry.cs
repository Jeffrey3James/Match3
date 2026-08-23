using System.Collections.Generic;
using Match3Game;
using UnityEngine;

/// <summary>
/// Maps the string keys used in levels.json to the visual/behavioral ScriptableObjects.
/// Lives at Assets/Resources/GemTypeRegistry.asset so the level loader can find it
/// without any scene wiring.
/// </summary>
[CreateAssetMenu(fileName = "GemTypeRegistry", menuName = "Match3/GemTypeRegistry")]
public class GemTypeRegistry : ScriptableObject
{
    public const string ResourcePath = "GemTypeRegistry";

    [SerializeField] private List<GemTypes> gemTypes = new List<GemTypes>();
    [SerializeField] private List<Obstacle> obstacles = new List<Obstacle>();
    [SerializeField] private GameObject levelShaperPrefab;

    private Dictionary<string, GemTypes> gemLookup;
    private Dictionary<string, Obstacle> obstacleLookup;

    public static GemTypeRegistry Load()
    {
        var registry = Resources.Load<GemTypeRegistry>(ResourcePath);
        if (registry == null)
            Debug.LogError($"GemTypeRegistry not found at Resources/{ResourcePath}. Level loading will fail.");
        return registry;
    }

    public GameObject GetLevelShaperPrefab() => levelShaperPrefab;

    public IReadOnlyList<GemTypes> GetGemTypes() => gemTypes;
    public IReadOnlyList<Obstacle> GetObstacles() => obstacles;

    public GemTypes FindGemType(string key)
    {
        BuildLookupsIfNeeded();
        if (string.IsNullOrEmpty(key)) return null;

        if (gemLookup.TryGetValue(key, out var gem))
            return gem;
        if (obstacleLookup.TryGetValue(key, out var obstacle))
            return obstacle;

        Debug.LogWarning($"GemTypeRegistry: no gem type named '{key}'.");
        return null;
    }

    public Obstacle FindObstacle(string key)
    {
        BuildLookupsIfNeeded();
        if (string.IsNullOrEmpty(key)) return null;

        if (obstacleLookup.TryGetValue(key, out var obstacle))
            return obstacle;

        Debug.LogWarning($"GemTypeRegistry: no obstacle named '{key}'.");
        return null;
    }

    private void BuildLookupsIfNeeded()
    {
        if (gemLookup != null && obstacleLookup != null)
            return;

        gemLookup = new Dictionary<string, GemTypes>();
        foreach (var gem in gemTypes)
        {
            if (gem != null && !gemLookup.ContainsKey(gem.name))
                gemLookup.Add(gem.name, gem);
        }

        obstacleLookup = new Dictionary<string, Obstacle>();
        foreach (var obstacle in obstacles)
        {
            if (obstacle != null && !obstacleLookup.ContainsKey(obstacle.name))
                obstacleLookup.Add(obstacle.name, obstacle);
        }
    }
}
