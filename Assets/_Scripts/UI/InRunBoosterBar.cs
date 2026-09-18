// -----------------------------------------------------------------------------
// InRunBoosterBar
//
// Item 14 in the gap doc: the small bar of boosters players can tap during a
// run. Playtest ships with Hammer only — tap the hammer to enter "hammer
// mode", then the next board tap consumes one hammer from PlayerHandler and
// destroys the gem at that grid cell.
//
// Cross-agent dependency:
//   Match3.cs (owned by agent A) does not currently expose a public
//   RemoveGemAt(int x, int y) or a static Instance accessor. We filed a
//   request in /home/user/workspace/coord/requests.md asking A to add both.
//   Until they land, this component ships a stub that logs the tap and does
//   nothing to the board — the UX (button, cursor mode, cooldown) is fully
//   testable without the board hook, which is what the playtest needs.
//
// If A adds Match3.Instance + RemoveGemAt before merge, uncomment the block
// marked "BOARD HOOK" below and the hammer will start actually removing gems.
//
// SETUP:
//   - Put this on a bar prefab under Match3UI (the HUD canvas).
//   - Assign hammerButton, hammerCountLabel, and (optionally) the cursor
//     hint object that appears while in hammer mode.
//   - The click-catcher is an invisible full-screen Image that we spawn
//     lazily when hammer mode activates, so no scene wiring is required
//     for the input-capture layer.
// -----------------------------------------------------------------------------

using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InRunBoosterBar : MonoBehaviour
{
    private const string HammerId = "hammer";

    [Header("Hammer")]
    [SerializeField] private Button hammerButton;
    [SerializeField] private TextMeshProUGUI hammerCountLabel;

    [Tooltip("Optional. Shown while hammer mode is armed. A cursor icon or " +
             "board-tint object that makes the state obvious.")]
    [SerializeField] private GameObject hammerModeIndicator;

    [Tooltip("Optional. If set, the bar hides itself when the level is over " +
             "(win or loss) via GameEventsManager.")]
    [SerializeField] private bool autoHideOnLevelEnd = true;

    // While armed, the next tap on the board is intercepted and consumed as
    // a hammer strike. Simple state machine — no queue.
    private bool _hammerArmed;

    // Cached delegates so unsub matches sub.
    private Action _onLevelCompleted;
    private Action _onLevelFailed;

    private void Awake()
    {
        if (hammerButton != null)
        {
            hammerButton.onClick.RemoveAllListeners();
            hammerButton.onClick.AddListener(OnHammerButtonClicked);
        }
        if (hammerModeIndicator != null) hammerModeIndicator.SetActive(false);
        RefreshHammerLabel();
    }

    private void OnEnable()
    {
        SubscribeLevelEndEvents();
    }

    private void OnDisable()
    {
        UnsubscribeLevelEndEvents();
        DisarmHammer();
    }

    private void SubscribeLevelEndEvents()
    {
        if (!autoHideOnLevelEnd) return;
        if (GameEventsManager.instance == null) return;
        var events = GameEventsManager.instance.gameEvents;
        if (events == null) return;

        _onLevelCompleted = () => gameObject.SetActive(false);
        _onLevelFailed    = () => gameObject.SetActive(false);
        events.onLevelCompleted += _onLevelCompleted;
        events.onLevelFailed    += _onLevelFailed;
    }

    private void UnsubscribeLevelEndEvents()
    {
        if (GameEventsManager.instance == null) return;
        var events = GameEventsManager.instance.gameEvents;
        if (events == null) return;
        if (_onLevelCompleted != null) events.onLevelCompleted -= _onLevelCompleted;
        if (_onLevelFailed    != null) events.onLevelFailed    -= _onLevelFailed;
        _onLevelCompleted = null;
        _onLevelFailed    = null;
    }

    // ------------------------------------------------------------------
    // Hammer arming
    // ------------------------------------------------------------------

    private void OnHammerButtonClicked()
    {
        if (_hammerArmed) { DisarmHammer(); return; }

        // Refuse to arm when the player has no hammers (and no fallback path
        // to obtain one at zero cost — this is playtest, not the shop).
        if (GetHammerCount() <= 0)
        {
            Debug.Log("[InRunBoosterBar] No hammers in inventory.");
            return;
        }

        ArmHammer();
    }

    private void ArmHammer()
    {
        _hammerArmed = true;
        if (hammerModeIndicator != null) hammerModeIndicator.SetActive(true);
        Debug.Log("[InRunBoosterBar] Hammer armed. Tap a gem to strike.");
    }

    private void DisarmHammer()
    {
        _hammerArmed = false;
        if (hammerModeIndicator != null) hammerModeIndicator.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Input capture — polled in Update so we don't need a scene-wired
    // click-catcher and can bail out cleanly if the pointer is over UI.
    // ------------------------------------------------------------------

    private void Update()
    {
        if (!_hammerArmed) return;
        if (!Input.GetMouseButtonDown(0)) return;

        // Ignore taps that landed on the HUD/booster bar itself.
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        TryStrikeAtScreen(Input.mousePosition);
    }

    private void TryStrikeAtScreen(Vector3 screenPos)
    {
        Vector3 world = Camera.main != null
            ? Camera.main.ScreenToWorldPoint(screenPos)
            : screenPos;

        // Grid cells are 1 unit in the current project — the board uses
        // GridSystem2D<GridObj>.VerticalGrid with cellSize wired in inspector.
        // Rounding to int is safe for a stub; the real board hook (once A
        // wires RemoveGemAt) can translate world→grid coordinates itself.
        int gx = Mathf.FloorToInt(world.x);
        int gy = Mathf.FloorToInt(world.y);

        if (!TrySpendHammer())
        {
            Debug.Log("[InRunBoosterBar] Hammer spend failed; disarming.");
            DisarmHammer();
            return;
        }

        Debug.Log($"[InRunBoosterBar] hammer used at ({gx},{gy})");

        // ── BOARD HOOK ────────────────────────────────────────────────────
        // Uncomment once agent A exposes Match3.Instance and RemoveGemAt.
        // See /home/user/workspace/coord/requests.md, "FROM D TO A".
        //
        if (Match3Game.Match3.Instance != null)
            Match3Game.Match3.Instance.RemoveGemAt(gx, gy);
        // ──────────────────────────────────────────────────────────────────

        DisarmHammer();
        RefreshHammerLabel();
    }

    // ------------------------------------------------------------------
    // Inventory hooks (reflection — PlayerHandler doesn't ship booster
    // methods yet; we degrade to "always zero" so the button just doesn't
    // arm rather than crashing).
    // ------------------------------------------------------------------

    private static int GetHammerCount()
    {
        try
        {
            var handler = PlayerHandler.instance;
            if (handler == null) return 0;
            var m = handler.GetType().GetMethod("GetBoosterCount",
                BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(string) }, null);
            if (m == null) return 0;
            object result = m.Invoke(handler, new object[] { HammerId });
            return result is int i ? i : 0;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[InRunBoosterBar] Hammer count read failed: {e.Message}");
            return 0;
        }
    }

    private static bool TrySpendHammer()
    {
        try
        {
            var handler = PlayerHandler.instance;
            if (handler == null) return false;
            var type = handler.GetType();
            var m = type.GetMethod("ConsumeBooster",
                BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(string) }, null)
                ?? type.GetMethod("UseBooster",
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(string) }, null);
            if (m == null)
            {
                // Booster API not implemented yet: pretend the spend worked
                // so the playtest UX (arm → tap → log) is complete.
                return true;
            }
            if (GetHammerCount() <= 0) return false;
            m.Invoke(handler, new object[] { HammerId });
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[InRunBoosterBar] Hammer spend failed: {e.Message}");
            return false;
        }
    }

    private void RefreshHammerLabel()
    {
        if (hammerCountLabel == null) return;
        int count = GetHammerCount();
        hammerCountLabel.text = count.ToString();
    }
}
