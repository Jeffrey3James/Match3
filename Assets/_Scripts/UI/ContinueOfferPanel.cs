// -----------------------------------------------------------------------------
// ContinueOfferPanel
//
// Item 15 in the gap doc: the "Out of moves! Get +5 more?" prompt shown
// BEFORE the loss LevelResultPanel. Three actions:
//
//   1. Pay coins for +5 moves. Cost escalates within the same level:
//        900 → 1800 → 3900 → 7800 (then stays at 7800).
//   2. Watch a rewarded ad for +5 moves (delegates to AdManager; the
//      rewarded flow already knows about the +5 moves reward).
//   3. Give up — hide this panel and fall through to LevelResultPanel.
//
// The escalating-cost counter resets when the player changes level. We do
// this by remembering the level index the counter was last measured against
// and zeroing it in OnEnable when the current player level differs.
//
// This panel doesn't call Match3 directly; it uses TryResumeWithExtraMoves
// (already public on the board) via a small helper. LevelResultPanel wires
// itself as the fallthrough listener via OnGiveUp/OnResumed events.
// -----------------------------------------------------------------------------

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Match3Game;
using Match3Game.Monetization;

public class ContinueOfferPanel : MonoBehaviour
{
    /// <summary>Moves granted per successful continue (coins or ad).</summary>
    public const int ExtraMovesPerContinue = 5;

    // Escalating price ladder per attempt. Fifth+ attempt sticks at 7800 —
    // pushing past that would be theatre; players who spent 14,400 already
    // exhausted the fantasy.
    private static readonly int[] CoinCostLadder = { 900, 1800, 3900, 7800 };

    // ── Static counter so the ladder survives instance rebuilds within the
    // same session but resets on level change. Comparing against
    // PlayerHandler.GetPlayerLevel() is enough: a win increments playerLevel,
    // a scene reload doesn't. Retry after loss leaves playerLevel unchanged.
    private static int s_continueAttempts;
    private static int s_lastMeasuredPlayerLevel = -1;

    [Header("Panel")]
    [Tooltip("Root object toggled on/off. Defaults to this GameObject.")]
    [SerializeField] private GameObject panelRoot;

    [SerializeField] private TextMeshProUGUI headerText;

    [Header("Buttons")]
    [Tooltip("Big gold button: '+5 Moves — <cost> coins'.")]
    [SerializeField] private Button coinButton;
    [SerializeField] private TextMeshProUGUI coinButtonLabel;

    [Tooltip("Secondary: 'Watch Ad for +5 Moves' (rewarded).")]
    [SerializeField] private Button adButton;

    [Tooltip("Tertiary: 'Give Up' — falls through to LevelResultPanel.")]
    [SerializeField] private Button giveUpButton;

    [Header("Board")]
    [Tooltip("Optional. Located automatically if empty.")]
    [SerializeField] private Match3 board;

    [Header("Labels")]
    [SerializeField] private string headerCopy = "Out of moves! Get +5 more?";

    /// <summary>Raised when the player takes the offer (coins or ad) and the
    /// board resumed with +5 moves. LevelResultPanel listens to stay hidden.</summary>
    public event Action OnContinued;

    /// <summary>Raised when the player declines. LevelResultPanel listens to
    /// take over and show the normal loss UI.</summary>
    public event Action OnGiveUp;

    private bool _busy;
    public bool IsShown => panelRoot != null && panelRoot.activeSelf;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        if (board == null) board = FindFirstObjectByType<Match3>();

        if (coinButton   != null) coinButton  .onClick.AddListener(OnCoinButtonClicked);
        if (adButton     != null) adButton    .onClick.AddListener(OnAdButtonClicked);
        if (giveUpButton != null) giveUpButton.onClick.AddListener(OnGiveUpClicked);

        Hide();
    }

    private void OnEnable()
    {
        // Reset the ladder when the player moved on to a new level.
        int currentLevel = PlayerHandler.instance != null
            ? PlayerHandler.instance.GetPlayerLevel()
            : 0;
        if (currentLevel != s_lastMeasuredPlayerLevel)
        {
            s_continueAttempts = 0;
            s_lastMeasuredPlayerLevel = currentLevel;
        }
    }

    // ------------------------------------------------------------------
    // Show / hide
    // ------------------------------------------------------------------

    public void Show()
    {
        _busy = false;
        if (headerText != null) headerText.text = headerCopy;

        RefreshButtons();
        panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void RefreshButtons()
    {
        int cost = CurrentCoinCost();
        if (coinButtonLabel != null)
            coinButtonLabel.text = $"+{ExtraMovesPerContinue} Moves — {cost}";

        bool canAfford = PlayerHandler.instance != null
                         && PlayerHandler.instance.playerData != null
                         && PlayerHandler.instance.playerData.playerCoins >= cost;
        if (coinButton != null)
            coinButton.interactable = canAfford;

        bool adAvailable = AdManager.Instance != null
                           && AdManager.Instance.IsExtraMovesOfferAvailable();
        if (adButton != null)
        {
            adButton.gameObject.SetActive(adAvailable);
            adButton.interactable = adAvailable;
        }

        if (giveUpButton != null) giveUpButton.interactable = true;
    }

    // ------------------------------------------------------------------
    // Cost ladder
    // ------------------------------------------------------------------

    private static int CurrentCoinCost()
    {
        int idx = Mathf.Clamp(s_continueAttempts, 0, CoinCostLadder.Length - 1);
        return CoinCostLadder[idx];
    }

    // Test hook: force the ladder to start fresh (used by unit tests
    // and by ContinueOfferPanel.OnEnable when a new level begins).
    public static void ResetLadder()
    {
        s_continueAttempts = 0;
        s_lastMeasuredPlayerLevel = -1;
    }

    // ------------------------------------------------------------------
    // Button handlers
    // ------------------------------------------------------------------

    private void OnCoinButtonClicked()
    {
        if (_busy) return;
        var handler = PlayerHandler.instance;
        if (handler == null || handler.playerData == null) return;

        int cost = CurrentCoinCost();
        if (handler.playerData.playerCoins < cost)
        {
            // Shouldn't happen — button was refreshed to non-interactable —
            // but defend against a stale click racing a purchase elsewhere.
            RefreshButtons();
            return;
        }

        _busy = true;
        handler.SpendCoins(cost);
        s_continueAttempts++; // ladder advances only after a successful spend

        if (TryResumeBoard())
        {
            OnContinued?.Invoke();
            Hide();
            return;
        }

        // Board refused to resume (already won, or bailed for some other
        // reason). Refund and fall through to the loss panel; we don't want
        // to charge coins for nothing.
        handler.AddCoins(cost);
        s_continueAttempts = Mathf.Max(0, s_continueAttempts - 1);
        _busy = false;
        OnGiveUp?.Invoke();
        Hide();
    }

    private void OnAdButtonClicked()
    {
        if (_busy) return;
        if (AdManager.Instance == null) { OnGiveUp?.Invoke(); Hide(); return; }

        _busy = true;
        if (adButton != null) adButton.interactable = false;

        AdManager.Instance.ShowRewardedExtraMoves(
            onGranted: () =>
            {
                if (TryResumeBoard())
                {
                    OnContinued?.Invoke();
                    Hide();
                    return;
                }
                _busy = false;
                RefreshButtons();
            },
            onNotGranted: () =>
            {
                _busy = false;
                RefreshButtons();
            });
    }

    private void OnGiveUpClicked()
    {
        if (_busy) return;
        OnGiveUp?.Invoke();
        Hide();
    }

    // ------------------------------------------------------------------
    // Board bridge
    // ------------------------------------------------------------------

    private bool TryResumeBoard()
    {
        var target = board != null ? board : FindFirstObjectByType<Match3>();
        if (target == null) return false;
        return target.TryResumeWithExtraMoves(ExtraMovesPerContinue);
    }
}
