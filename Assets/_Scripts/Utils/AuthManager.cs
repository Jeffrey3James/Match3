using JadedBelles.Networking;
using StroTheGoat;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Boot flow for the splash scene. Shows the logo, silently restores the JadedBelles
/// session when one is stored, then always continues to the main menu — guests can play
/// without an account, and signing in is optional.
/// </summary>
public class AuthManager : MonoBehaviour
{
    private CountdownTimer showLogoTimer;
    private const float LogoDuration = 3f;

    private bool sessionCheckDone;
    private bool menuLoaded;

    private void Awake()
    {
        showLogoTimer = new CountdownTimer(LogoDuration);
        showLogoTimer.StartTimer();
    }

    private void Start()
    {
        showLogoTimer.OnTimerStop += TryEnterGame;

        if (TokenStore.HasSession())
        {
            JadedBellesApiClient.Instance.RefreshToken(
                _ =>
                {
                    Debug.Log("JadedBelles session restored.");
                    sessionCheckDone = true;
                    TryEnterGame();
                },
                error =>
                {
                    Debug.Log($"Stored session could not be restored ({error}). Continuing as guest.");
                    TokenStore.Clear();
                    sessionCheckDone = true;
                    TryEnterGame();
                });
        }
        else
        {
            Debug.Log("No stored session. Continuing as guest.");
            sessionCheckDone = true;
        }
    }

    private void Update()
    {
        showLogoTimer.Tick(Time.deltaTime);
    }

    private void TryEnterGame()
    {
        if (menuLoaded || showLogoTimer.isRunning || !sessionCheckDone)
            return;

        menuLoaded = true;
        SceneManager.LoadScene("MainMenu");
    }
}
