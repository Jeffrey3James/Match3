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

    private void Start()
    {    
        SetUpMainMenu();
        Debug.Log("Setting Up Main Menu UI");   
    }

    private void Update()
    {
    }

    private void SetUpMainMenu()
    {
        var playerData = PlayerHandler.instance;
        playerLivesText.text = PlayerHandler.instance.playerData.playerLives.ToString();
        playerCoinsText.text = PlayerHandler.instance.playerData.playerCoins.ToString();
        playerLivesText.text = PlayerHandler.instance.playerData.playerLives.ToString();

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
                    PlayerHandler.instance.UseALifeFromPlayer();

                });
        }

    }
}