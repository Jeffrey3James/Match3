// -----------------------------------------------------------------------------
// PreLevelBoosterPanel
//
// Item 13 in the gap doc: the small panel players see BEFORE the board loads
// (or before the objective intro card if we choose to overlay in-scene). Lets
// them toggle up to three pre-level boosters — Rocket, TNT, Light Ball — and
// pay for them with inventory (free) or coins (300 / 500 / 900). Selections
// are handed to the board via MonetizationConfig.SelectedPreLevelBoosters
// (in-memory) and a PlayerPrefs mirror keyed by
// MonetizationConfig.PendingBoostersPrefsKey so the board can read them
// after the scene load.
//
// Ownership boundary: this panel does NOT modify Match3.cs (agent A's file).
// It just publishes the selection. The board is expected to read the bag on
// start; until A wires that read, the pre-level boosters land as no-ops on
// the board but the panel's UX is complete for playtest.
//
// SETUP:
//   Drop this component on the panel root. Wire three Buttons + Images +
//   TMP labels below. Toggle icons swap to greyscale automatically when the
//   player has neither inventory nor enough coins. Press "Start Level" to
//   commit the selection and load GameScene.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Match3Game.Monetization;

public class PreLevelBoosterPanel : MonoBehaviour
{
    // Booster ids are the same strings we publish to PlayerPrefs and expect
    // Match3 to read. Keep in one const block so a typo can't drift between
    // the toggle handler and the persistence step.
    private const string RocketId    = "rocket";
    private const string TntId       = "tnt";
    private const string LightBallId = "lightball";

    // Coin costs match the plan in the gap doc:
    //   Rocket 300, TNT 500, Light Ball 900.
    private const int RocketCost    = 300;
    private const int TntCost       = 500;
    private const int LightBallCost = 900;

    [Serializable]
    private class BoosterSlot
    {
        [Tooltip("The button the player taps to toggle this booster on/off.")]
        public Button toggleButton;

        [Tooltip("Icon image on the button. Greyed out when unaffordable.")]
        public Image  iconImage;

        [Tooltip("Optional label for the cost or inventory count.")]
        public TextMeshProUGUI costLabel;

        [Tooltip("Optional overlay showing a check when selected.")]
        public GameObject selectedOverlay;
    }

    [Header("Slots (Rocket / TNT / Light Ball, in order)")]
    [SerializeField] private BoosterSlot rocketSlot;
    [SerializeField] private BoosterSlot tntSlot;
    [SerializeField] private BoosterSlot lightBallSlot;

    [Header("Flow")]
    [Tooltip("Play button that closes the panel and loads the game scene.")]
    [SerializeField] private Button playButton;

    [Tooltip("Optional. Panel root toggled on/off; defaults to this GameObject.")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Scene loaded when Play is pressed. Blank = don't switch scenes " +
             "(useful when this panel lives inside GameScene before objective intro).")]
    [SerializeField] private string gameSceneName = "GameScene";

    // Per-booster runtime state. Kept in a HashSet so publishing is trivial.
    private readonly HashSet<string> _selected = new HashSet<string>();

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        WireToggle(rocketSlot,    RocketId,    RocketCost);
        WireToggle(tntSlot,       TntId,       TntCost);
        WireToggle(lightBallSlot, LightBallId, LightBallCost);

        if (playButton != null)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(OnPlayClicked);
        }
    }

    private void OnEnable()
    {
        // Selection is a per-level thing — the panel opens fresh each time.
        _selected.Clear();
        // Also clear any prior publish so a previous level's picks don't leak.
        MonetizationConfig.SelectedPreLevelBoosters.Clear();
        PlayerPrefs.DeleteKey(MonetizationConfig.PendingBoostersPrefsKey);
        RefreshAll();
    }

    private void WireToggle(BoosterSlot slot, string id, int cost)
    {
        if (slot == null || slot.toggleButton == null) return;
        slot.toggleButton.onClick.RemoveAllListeners();
        slot.toggleButton.onClick.AddListener(() => ToggleBooster(slot, id, cost));
    }

    // ------------------------------------------------------------------
    // Toggle handling
    // ------------------------------------------------------------------

    private void ToggleBooster(BoosterSlot slot, string id, int cost)
    {
        // Deselect if already on — pre-level toggles are non-destructive; the
        // spend only happens on Play.
        if (_selected.Contains(id))
        {
            _selected.Remove(id);
            RefreshSlot(slot, id, cost);
            return;
        }

        // Selecting requires either inventory or enough coins.
        if (!CanAfford(id, cost))
        {
            Debug.Log($"[PreLevelBoosterPanel] Cannot afford {id}: no inventory and no coins.");
            return;
        }

        _selected.Add(id);
        RefreshSlot(slot, id, cost);
    }

    private bool CanAfford(string id, int cost)
    {
        if (HasInventory(id)) return true;
        var handler = PlayerHandler.instance;
        if (handler == null || handler.playerData == null) return false;
        return handler.playerData.playerCoins >= cost;
    }

    // ------------------------------------------------------------------
    // Play button — commit spend and publish selection
    // ------------------------------------------------------------------

    private void OnPlayClicked()
    {
        // Commit the spend now: consume inventory first, then coins.
        // If PlayerHandler exposes a consume-inventory method we'll use it;
        // otherwise we still charge coins so the economy stays balanced.
        foreach (var id in _selected)
        {
            int cost = CostFor(id);
            if (TryConsumeInventory(id))
            {
                Debug.Log($"[PreLevelBoosterPanel] Consumed inventory for {id}.");
                continue;
            }
            if (PlayerHandler.instance != null)
                PlayerHandler.instance.SpendCoins(cost);
        }

        // Publish selection for the board — both channels so whoever reads
        // first has a source of truth.
        MonetizationConfig.SelectedPreLevelBoosters.Clear();
        foreach (var id in _selected) MonetizationConfig.SelectedPreLevelBoosters.Add(id);

        var joined = new StringBuilder();
        bool first = true;
        foreach (var id in _selected)
        {
            if (!first) joined.Append(',');
            joined.Append(id);
            first = false;
        }
        PlayerPrefs.SetString(MonetizationConfig.PendingBoostersPrefsKey, joined.ToString());
        PlayerPrefs.Save();

        if (panelRoot != null) panelRoot.SetActive(false);

        if (!string.IsNullOrEmpty(gameSceneName) &&
            SceneManager.GetActiveScene().name != gameSceneName)
        {
            SceneManager.LoadScene(gameSceneName);
        }
    }

    // ------------------------------------------------------------------
    // Inventory hooks — PlayerHandler doesn't ship a booster inventory yet.
    // We try common method shapes via reflection so this component doesn't
    // block on E adding the API. If nothing is found, the coin path pays.
    // ------------------------------------------------------------------

    private static bool HasInventory(string id)
    {
        try
        {
            int count = GetBoosterCountReflected(id);
            return count > 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PreLevelBoosterPanel] Booster inventory read failed: {e.Message}");
            return false;
        }
    }

    private static bool TryConsumeInventory(string id)
    {
        try
        {
            var handler = PlayerHandler.instance;
            if (handler == null) return false;

            // Try methods in order: ConsumeBooster(string), UseBooster(string).
            var type = handler.GetType();
            var consume = type.GetMethod("ConsumeBooster",
                BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(string) }, null)
                ?? type.GetMethod("UseBooster",
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(string) }, null);

            if (consume != null && GetBoosterCountReflected(id) > 0)
            {
                consume.Invoke(handler, new object[] { id });
                return true;
            }
            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PreLevelBoosterPanel] Booster inventory consume failed: {e.Message}");
            return false;
        }
    }

    private static int GetBoosterCountReflected(string id)
    {
        var handler = PlayerHandler.instance;
        if (handler == null) return 0;

        var type = handler.GetType();
        var m = type.GetMethod("GetBoosterCount",
            BindingFlags.Instance | BindingFlags.Public,
            null, new[] { typeof(string) }, null);
        if (m != null)
        {
            object result = m.Invoke(handler, new object[] { id });
            if (result is int i) return i;
        }
        return 0;
    }

    // ------------------------------------------------------------------
    // Visual refresh
    // ------------------------------------------------------------------

    private void RefreshAll()
    {
        RefreshSlot(rocketSlot,    RocketId,    RocketCost);
        RefreshSlot(tntSlot,       TntId,       TntCost);
        RefreshSlot(lightBallSlot, LightBallId, LightBallCost);
    }

    private void RefreshSlot(BoosterSlot slot, string id, int cost)
    {
        if (slot == null) return;

        bool owned    = HasInventory(id);
        bool afford   = owned || CanAfford(id, cost);
        bool selected = _selected.Contains(id);

        if (slot.toggleButton != null)
            slot.toggleButton.interactable = afford;

        if (slot.iconImage != null)
        {
            // Greyscale-ish: full colour when affordable, dimmed grey when not.
            slot.iconImage.color = afford
                ? Color.white
                : new Color(0.5f, 0.5f, 0.5f, 0.6f);
        }

        if (slot.selectedOverlay != null)
            slot.selectedOverlay.SetActive(selected);

        if (slot.costLabel != null)
            slot.costLabel.text = owned ? "OWNED" : cost.ToString();
    }

    private static int CostFor(string id)
    {
        switch (id)
        {
            case RocketId:    return RocketCost;
            case TntId:       return TntCost;
            case LightBallId: return LightBallCost;
            default:          return 0;
        }
    }
}
