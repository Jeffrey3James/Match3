using StroTheGoat;
using System;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{

    [Header("Auth")]
    [Tooltip("Shown only when the player arrives with no usable session. Leave its " +
             "GameObject DISABLED in the scene — a returning player never sees it.")]
    [SerializeField] private LoginPanel loginPanel;

    [Tooltip("Menu controls to hide behind the login panel. Leave empty to let the player " +
             "see the menu underneath.")]
    [SerializeField] private GameObject menuContentRoot;

    [Header("UI Elements")]
    [SerializeField] private Button levelButton;
    [SerializeField] private Button AddLifeTestButton;
    [SerializeField] private TextMeshProUGUI playerLivesText;
    [SerializeField] private TextMeshProUGUI playerCoinsText;
    [SerializeField] private TextMeshProUGUI timeUntilNewLifeText;

    [Header("HUD Counters (new)")]
    [Tooltip("Optional. If set, coins are shown here with count-up + punch-scale, and the raw TMP field is ignored for coin writes.")]
    [SerializeField] private HUDCounter coinCounter;
    [Tooltip("Optional. If set, lives are shown here with count-up + punch-scale.")]
    [SerializeField] private HUDCounter livesCounter;
    [Tooltip("Optional. Depends on E's PlayerHandler.GetStars(); if E hasn't shipped, this stays at 0.")]
    [SerializeField] private HUDCounter starsCounter;

    private float uiRefreshAccumulator;
    private const float uiRefreshInterval = 0.25f; // Refresh lives/timer labels 4x per second

    // Snapshots used so HUDCounter only Tick()s when the value actually changes.
    private int lastCoins;
    private int lastLives;
    private int lastStars;
    private bool hudBootstrapped;

    private const string HasSeenMainMenuPref = "HasSeenMainMenu";

    private void Start()
    {
        SetUpMainMenu();
        ApplySessionState();
        SessionService.OnStateChanged += OnSessionStateChanged;
        Debug.Log("Setting Up Main Menu UI");

        // Auto-play on very first launch: if the player has never seen the menu
        // AND is at level 0, dive straight into level 0. Sets the pref so it never
        // happens twice for the same install.
        TryAutoPlayFirstLaunch();
    }

    private void TryAutoPlayFirstLaunch()
    {
        if (PlayerHandler.instance == null) return;
        if (levelButton == null) return;
        if (PlayerHandler.instance.GetPlayerLevel() != 0) return;
        if (PlayerPrefs.GetInt(HasSeenMainMenuPref, 0) != 0) return;

        PlayerPrefs.SetInt(HasSeenMainMenuPref, 1);
        PlayerPrefs.Save();

        Debug.Log("MainMenuUI: first launch — auto-loading Level 0.");
        levelButton.onClick.Invoke();
    }

    private void OnDestroy()
    {
        SessionService.OnStateChanged -= OnSessionStateChanged;
    }

    // ------------------------------------------------------------------
    // Auth gate
    // ------------------------------------------------------------------

    /// <summary>
    /// The splash screen already tried to restore the session, so by the time the menu
    /// exists the answer is known. A signed-in, offline-signed-in, or guest player sees
    /// the menu and nothing else. Only an unresolved session gets the login panel.
    /// </summary>
    private void ApplySessionState()
    {
        if (loginPanel == null)
        {
            if (!SessionService.IsResolved)
                Debug.LogWarning("MainMenuUI: no session and no LoginPanel assigned. " +
                                 "The player can't sign in from here.");
            return;
        }

        if (SessionService.IsResolved)
        {
            loginPanel.Hide();
            SetMenuContentVisible(true);
            return;
        }

        Debug.Log("MainMenuUI: no usable session. Showing the login panel.");
        SetMenuContentVisible(false);
        loginPanel.Show();
    }

    private void OnSessionStateChanged(SessionService.SessionState state)
    {
        if (!SessionService.IsResolved)
        {
            // Signed out from the menu. Re-gate rather than leaving the menu live behind
            // the panel with the previous player's lives and level still on it.
            SetMenuContentVisible(false);
            if (loginPanel != null) loginPanel.Show();
            return;
        }

        // Signed in or chose guest from the panel — put the menu back and refresh it, since
        // signing in may have pulled a different player's progress.
        if (loginPanel != null) loginPanel.Hide();
        SetMenuContentVisible(true);
        SetUpMainMenu();
    }

    private void SetMenuContentVisible(bool visible)
    {
        if (menuContentRoot != null) menuContentRoot.SetActive(visible);
    }

    private void Update()
    {
        uiRefreshAccumulator += Time.unscaledDeltaTime;
        if (uiRefreshAccumulator < uiRefreshInterval) return;
        uiRefreshAccumulator = 0f;
        RefreshLivesUI();
    }

    private void RefreshLivesUI()
    {
        var handler = PlayerHandler.instance;
        if (handler == null || handler.playerData == null) return;

        int coins = handler.playerData.playerCoins;
        int lives = handler.playerData.playerLives;
        int stars = TryGetStars(handler);

        // Prefer HUDCounter widgets when assigned — they animate deltas + punch scale.
        // Fall back to the raw TMP text fields so legacy scenes keep working.
        if (!hudBootstrapped)
        {
            if (coinCounter != null) coinCounter.SetValue(coins);
            if (livesCounter != null) livesCounter.SetValue(lives);
            if (starsCounter != null) starsCounter.SetValue(stars);
            lastCoins = coins; lastLives = lives; lastStars = stars;
            hudBootstrapped = true;
        }
        else
        {
            if (coinCounter != null && coins != lastCoins) coinCounter.Tick(coins - lastCoins);
            if (livesCounter != null && lives != lastLives) livesCounter.Tick(lives - lastLives);
            if (starsCounter != null && stars != lastStars) starsCounter.Tick(stars - lastStars);
            lastCoins = coins; lastLives = lives; lastStars = stars;
        }

        if (playerLivesText != null && livesCounter == null)
            playerLivesText.text = lives.ToString();

        if (playerCoinsText != null && coinCounter == null)
            playerCoinsText.text = coins.ToString();

        if (timeUntilNewLifeText != null)
        {
            long seconds = handler.GetSecondsUntilNextLife();
            timeUntilNewLifeText.text = seconds > 0
                ? TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss")
                : "FULL";
        }
    }

    // depends on E — PlayerHandler.GetStars() is E's promised API. Swallow anything
    // (missing method, null data) so C's PR never depends on E's PR merging first.
    private static int TryGetStars(PlayerHandler handler)
    {
        try
        {
            var mi = handler.GetType().GetMethod("GetStars");
            if (mi != null)
            {
                object result = mi.Invoke(handler, null);
                if (result is int i) return i;
            }
        }
        catch { /* depends on E */ }
        return 0;
    }

    private void SetUpMainMenu()
    {
        RefreshLivesUI();

        if (levelButton != null)
        {
            levelButton.onClick.RemoveAllListeners();

            // The catalog may still be loading, or have failed entirely. Don't take the menu
            // down over it — just leave the button's existing label alone.
            var currentLevel = PlayerHandler.instance != null
                ? PlayerHandler.instance.GetCurrentLevel()
                : null;
            var levelLabel = levelButton.GetComponentInChildren<TextMeshProUGUI>();
            if (currentLevel != null && levelLabel != null)
                levelLabel.text = currentLevel.GetLevelName();

            levelButton.onClick.AddListener(() =>
                {
                    Debug.Log("Level Button Clicked");
                    if (!PlayerHandler.instance.CheckPlayerLives()) { return; }
                    PlayerHandler.instance.UseALifeFromPlayer();
                    SceneManager.LoadScene("GameScene");
                });
        }

        if (AddLifeTestButton != null)
        {
            AddLifeTestButton.onClick.RemoveAllListeners();
            AddLifeTestButton.onClick.AddListener(() =>
                {
                    Debug.Log("Add Life Test Button Clicked");
                    PlayerHandler.instance.AddALifeToPlayer();

                });
        }

    }
}