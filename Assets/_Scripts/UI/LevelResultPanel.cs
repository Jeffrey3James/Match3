using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Match3Game;
using Match3Game.Monetization;

/// <summary>
/// The end-of-level window. One panel, four buttons, shown or hidden per result:
///
///   WIN   -> Next Level, Main Menu
///   LOSS  -> Retry, Main Menu, Watch Ad (+5 moves, only when actually available)
///
/// Offering "+5 moves" after a win is nonsense, and offering "Next Level" after a loss
/// would skip progression, so neither is merely disabled — they're deactivated, and a
/// layout group on the button container reflows whatever is left.
///
/// SETUP:
///   - Put this on the game-over window root.
///   - Give the button container a Horizontal or Vertical Layout Group so hidden buttons
///     close their gap instead of leaving a hole.
///   - Drag in the four buttons. Any you leave empty are simply skipped.
///   - Do NOT wire the buttons' OnClick lists in the Inspector; this adds its own
///     listeners in Awake. Wiring both fires every action twice.
/// </summary>
public class LevelResultPanel : MonoBehaviour
{
    public enum LevelResult { Win, Loss }

    [Header("Panel")]
    [Tooltip("Root object toggled on/off. Defaults to this GameObject.")]
    [SerializeField] private GameObject panelRoot;

    [Tooltip("Optional. Container holding the buttons. Put a Layout Group on it so the " +
             "remaining buttons recentre when others are hidden.")]
    [SerializeField] private RectTransform buttonContainer;

    [Tooltip("Optional. Headline text, e.g. LEVEL COMPLETE / OUT OF MOVES.")]
    [SerializeField] private TextMeshProUGUI headerText;

    [Header("Buttons")]
    [Tooltip("Win only. Auto-hidden on the final level, since there's nothing to advance to.")]
    [SerializeField] private Button nextLevelButton;

    [Tooltip("Loss only.")]
    [SerializeField] private Button retryButton;

    [Tooltip("Shown for both results.")]
    [SerializeField] private Button mainMenuButton;

    [Tooltip("Loss only, and only when AdManager says an ad is loaded and the player is eligible.")]
    [SerializeField] private Button watchAdExtraMovesButton;

    [Header("Labels")]
    [SerializeField] private string winHeader = "LEVEL COMPLETE";
    [SerializeField] private string lossHeader = "OUT OF MOVES";
    [Tooltip("Header shown after winning the last level in the catalog.")]
    [SerializeField] private string finalLevelHeader = "ALL LEVELS COMPLETE";

    [Header("Lives")]
    [Tooltip("Retrying costs a life, same as starting a level from the main menu. " +
             "Turn off to make retries free.")]
    [SerializeField] private bool retryCostsALife = true;

    [Tooltip("Advancing to the next level costs a life, same as the main menu button.")]
    [SerializeField] private bool nextLevelCostsALife = true;

    [Header("Board")]
    [Tooltip("Optional. Used to resume play after a rewarded ad. Found automatically if empty.")]
    [SerializeField] private Match3 board;

    [Header("Continue offer (loss only)")]
    [Tooltip("Optional. Shown BEFORE the loss UI when the player runs out of moves. " +
             "If assigned, we hand the loss result to this panel first; on give-up we " +
             "take over and show the normal loss buttons. If null, behaves as before.")]
    [SerializeField] private ContinueOfferPanel continueOfferPanel;

    private const string MainMenuScene = "MainMenu";

    private LevelResult _result;
    private bool _shown;
    // True while the continue-offer panel is speaking for us. We keep this
    // flag so a stray Show() from Match3UI's re-entry doesn't double-open.
    private bool _offerInFlight;

    public bool IsShown => _shown;

    /// <summary>Raised when a rewarded ad puts the player back on the board.</summary>
    public event Action OnResumedWithExtraMoves;

    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        if (board == null) board = FindFirstObjectByType<Match3>();

        if (nextLevelButton != null) nextLevelButton.onClick.AddListener(OnNextLevelClicked);
        if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        if (watchAdExtraMovesButton != null) watchAdExtraMovesButton.onClick.AddListener(OnWatchAdClicked);

        // Wire the continue-offer panel's outcomes to our own show/hide logic.
        // Continued -> board resumed, we stay hidden (and forward the event so
        // Match3UI can hide any "level over" chrome it drew on onLevelFailed).
        // GaveUp    -> we take over and show the normal loss buttons.
        if (continueOfferPanel != null)
        {
            continueOfferPanel.OnContinued += OnContinueOfferContinued;
            continueOfferPanel.OnGiveUp    += OnContinueOfferGaveUp;
        }

        Hide();
    }

    // ------------------------------------------------------------------
    // Show / hide
    // ------------------------------------------------------------------

    /// <summary>Shows the panel with the button set appropriate to the result.
    /// If a ContinueOfferPanel is wired and this is a Loss, defers to that
    /// panel first; only after Give Up does the normal loss UI appear.</summary>
    public void Show(LevelResult result)
    {
        _result = result;

        // Loss route: hand off to the continue-offer panel if we have one.
        // We deliberately do NOT set _shown here — the panel isn't actually
        // on-screen yet, and Match3UI polls IsShown to know whether to draw
        // additional chrome. That reads correctly the moment we take over.
        if (result == LevelResult.Loss
            && continueOfferPanel != null
            && !_offerInFlight)
        {
            _offerInFlight = true;
            panelRoot.SetActive(false);
            continueOfferPanel.Show();
            return;
        }

        ShowResultDirectly(result);
    }

    // The old Show() body, split out so both the direct-loss path and the
    // give-up-from-continue path can call it without recursion tricks.
    private void ShowResultDirectly(LevelResult result)
    {
        _offerInFlight = false;
        _shown = true;

        bool isWin = result == LevelResult.Win;
        bool hasNext = HasNextLevel();

        SetActive(nextLevelButton, isWin && hasNext);
        SetActive(retryButton, !isWin);
        SetActive(mainMenuButton, true);
        SetActive(watchAdExtraMovesButton, !isWin && IsAdOfferAvailable());

        if (headerText != null)
            headerText.text = isWin ? (hasNext ? winHeader : finalLevelHeader) : lossHeader;

        panelRoot.SetActive(true);

        // Buttons were toggled before the container became active, so force one rebuild
        // rather than waiting a frame and letting the player see the layout pop.
        if (buttonContainer != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(buttonContainer);
    }

    public void Hide()
    {
        _shown = false;
        _offerInFlight = false;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Continue-offer callbacks
    // ------------------------------------------------------------------

    private void OnContinueOfferContinued()
    {
        // Board is running again; stay hidden. Match3UI treats us as "not
        // shown" and its own onLevelFailed cleanup already happened when the
        // loss fired — the resume path re-enables input in Match3 itself.
        _offerInFlight = false;
        _shown = false;
        OnResumedWithExtraMoves?.Invoke();
    }

    private void OnContinueOfferGaveUp()
    {
        // Player declined the offer. Fall through to the normal loss UI.
        ShowResultDirectly(LevelResult.Loss);
    }

    private static void SetActive(Button button, bool visible)
    {
        if (button == null) return;
        button.gameObject.SetActive(visible);
        if (visible) button.interactable = true;
    }

    // ------------------------------------------------------------------
    // State checks
    // ------------------------------------------------------------------

    /// <summary>
    /// True when the catalog has a level beyond the one just finished. PlayerHandler already
    /// incremented playerLevel on the win, so playerLevel is the index of the next level.
    /// </summary>
    private bool HasNextLevel()
    {
        if (LevelHandler.instance == null || PlayerHandler.instance == null) return false;
        if (PlayerHandler.instance.playerData == null) return false;

        return PlayerHandler.instance.playerData.playerLevel < LevelHandler.instance.LevelCount;
    }

    // Fail closed: no AdManager, no offer.
    private bool IsAdOfferAvailable()
    {
        return AdManager.Instance != null && AdManager.Instance.IsExtraMovesOfferAvailable();
    }

    // ------------------------------------------------------------------
    // Button handlers
    // ------------------------------------------------------------------

    private void OnNextLevelClicked()
    {
        if (nextLevelCostsALife && !TrySpendALife()) return;

        // PlayerHandler re-reads playerLevel from the catalog on every scene load, so simply
        // reloading the game scene starts the next level.
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnRetryClicked()
    {
        if (retryCostsALife && !TrySpendALife()) return;

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnMainMenuClicked()
    {
        SceneManager.LoadScene(MainMenuScene);
    }

    /// <summary>
    /// Charges a life, or refuses and sends the player back to the menu when they're out.
    /// Mirrors the main menu's gate so playing from here can't dodge the life economy.
    /// </summary>
    private bool TrySpendALife()
    {
        var handler = PlayerHandler.instance;
        if (handler == null) return true; // no life system available; don't block play

        if (!handler.CheckPlayerLives())
        {
            Debug.Log("[LevelResultPanel] No lives left. Returning to the main menu.");
            if (headerText != null) headerText.text = "OUT OF LIVES";
            SetActive(nextLevelButton, false);
            SetActive(retryButton, false);
            return false;
        }

        handler.UseALifeFromPlayer();
        return true;
    }

    private void OnWatchAdClicked()
    {
        if (AdManager.Instance == null) return;

        // No double-taps while the ad is on screen.
        if (watchAdExtraMovesButton != null) watchAdExtraMovesButton.interactable = false;

        AdManager.Instance.ShowRewardedExtraMoves(
            onGranted: () =>
            {
                var target = board != null ? board : FindFirstObjectByType<Match3>();
                if (target != null && target.TryResumeWithExtraMoves(AdManager.ExtraMovesPerAd))
                {
                    Hide();
                    OnResumedWithExtraMoves?.Invoke();
                    return;
                }

                // Board refused to resume — re-evaluate the offer rather than leaving a
                // dead button sitting there.
                RefreshAdButton();
            },
            onNotGranted: RefreshAdButton);
    }

    private void RefreshAdButton()
    {
        if (watchAdExtraMovesButton == null) return;

        bool available = _result == LevelResult.Loss && IsAdOfferAvailable();
        watchAdExtraMovesButton.gameObject.SetActive(available);
        watchAdExtraMovesButton.interactable = available;

        if (buttonContainer != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(buttonContainer);
    }
}
