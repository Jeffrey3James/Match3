using JadedBelles.UI;
using StroTheGoat;
using System;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{

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
        // Check for a JadedBelles session token. Signed in → pull saves and continue.
        // Not signed in (and not previously a guest) → the gate spawns the AuthPanel modal.
        MainMenuAuthGate.Ensure();

        SetUpMainMenu();
        Debug.Log("Setting Up Main Menu UI");
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
            levelButton.GetComponentInChildren<TextMeshProUGUI>().text = PlayerHandler.instance.GetCurrentLevel().GetLevelName();
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