using System;
using System.Collections;
using System.Collections.Generic;
using JadedBelles.Networking;
using Match3Game.Shop;
using UnityEngine;

/// <summary>
/// Loads the shop catalog. Remote-first: fetches shop items from the JadedBelles API
/// so new offers ship without a build, and falls back to the bundled copy in
/// Assets/Resources/Shop/shop.json when offline or the API is unreachable.
/// Mirrors LevelHandler intentionally so both catalogs behave the same way.
/// </summary>
public class ShopHandler : MonoBehaviour
{
    public const string BundledShopResource = "Shop/shop";

    public static ShopHandler instance { get; private set; }

    /// <summary>True once the catalog has been parsed.</summary>
    public bool ShopReady { get; private set; }

    /// <summary>Fired once when the catalog finishes loading.</summary>
    public event Action OnShopReady;

    private readonly List<ShopItemData> items = new List<ShopItemData>();
    private int catalogVersion;

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

        StartCoroutine(LoadCatalog());
    }

    private IEnumerator LoadCatalog()
    {
        bool remoteDone = false;
        string remoteJson = null;

        JadedBellesApiClient.Instance.GetShopCatalog(
            json => { remoteJson = json; remoteDone = true; },
            error =>
            {
                Debug.LogWarning($"Remote shop catalog unavailable ({error}). Using the bundled copy.");
                remoteDone = true;
            });

        while (!remoteDone)
            yield return null;

        if (string.IsNullOrEmpty(remoteJson) || !TryHydrate(remoteJson, "remote"))
        {
            var bundled = Resources.Load<TextAsset>(BundledShopResource);
            if (bundled == null || !TryHydrate(bundled.text, "bundled"))
            {
                Debug.LogError("No usable shop catalog found (remote and bundled both failed).");
                yield break;
            }
        }

        ShopReady = true;
        OnShopReady?.Invoke();
    }

    private bool TryHydrate(string json, string sourceLabel)
    {
        ShopCatalog catalog;
        try
        {
            catalog = JsonUtility.FromJson<ShopCatalog>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"Could not parse the {sourceLabel} shop catalog: {e.Message}");
            return false;
        }

        if (catalog == null || catalog.items == null || catalog.items.Count == 0)
        {
            Debug.LogError($"The {sourceLabel} shop catalog contains no items.");
            return false;
        }

        items.Clear();
        catalog.items.Sort((a, b) =>
        {
            int bySort = a.sortOrder.CompareTo(b.sortOrder);
            if (bySort != 0) return bySort;
            return a.priceValue.CompareTo(b.priceValue);
        });
        items.AddRange(catalog.items);
        catalogVersion = catalog.version;

        Debug.Log($"Loaded {items.Count} shop items from the {sourceLabel} catalog (version {catalogVersion}).");
        return true;
    }

    /// <summary>All items, in their sorted order.</summary>
    public List<ShopItemData> GetAllItems() => new List<ShopItemData>(items);

    /// <summary>
    /// Items for a single tab (e.g. "bundles"), preserving sort order.
    /// Case-insensitive tab match; missing/null tab returns an empty list.
    /// </summary>
    public List<ShopItemData> GetItemsForTab(string tab)
    {
        var results = new List<ShopItemData>();
        if (string.IsNullOrEmpty(tab)) return results;

        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.tab) &&
                string.Equals(item.tab.Trim(), tab.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                results.Add(item);
            }
        }
        return results;
    }

    public ShopItemData GetItemById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var item in items)
        {
            if (string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    public int Count => items.Count;
    public int Version => catalogVersion;
}
