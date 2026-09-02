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

    private float uiRefreshAccumulator;
    private const float uiRefreshInterval = 0.25f; // Refresh lives/timer labels 4x per second

    private void Start()
    {
        SetUpMainMenu();
        ApplySessionState();
        SessionService.OnStateChanged += OnSessionStateChanged;
        Debug.Log("Setting Up Main Menu UI");
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

        if (playerLivesText != null)
            playerLivesText.text = handler.playerData.playerLives.ToString();

        if (playerCoinsText != null)
            playerCoinsText.text = handler.playerData.playerCoins.ToString();

        if (timeUntilNewLifeText != null)
        {
            long seconds = handler.GetSecondsUntilNextLife();
            timeUntilNewLifeText.text = seconds > 0
                ? TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss")
                : "FULL";
        }
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