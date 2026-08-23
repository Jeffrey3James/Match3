using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using StroTheGoat;

public class AuthManager : MonoBehaviour
{
    private CountdownTimer showLogoTimer;
    private float LogoDuration = 3f;

    /*[SerializeField] private GooglePlaySignInCode googlePlaySignInCode;*/

    private void Awake()
    {
        showLogoTimer = new CountdownTimer(LogoDuration);
        showLogoTimer.StartTimer();
    }

    private void Start()
    {
        showLogoTimer.OnTimerStop += async () =>
        {
            Debug.Log("Logo duration ended.");
            await Task.Delay(500);
            await SignInCachedUserAsync();
        };

        long currentTime = TimeUtils.UnixNow;
        Debug.Log(currentTime.ToString());
    }

    private void Update()
    {
        showLogoTimer.Tick(Time.deltaTime);
    }

    private async Task SignInCachedUserAsync()
    {
        if (showLogoTimer.isRunning)
            return;

        try
        {
            if (AuthenticationService.Instance.IsSignedIn)
            {
                Debug.Log("User is already signed in.");
                SceneManager.LoadScene("MainMenu");
            }
            else if (AuthenticationService.Instance.SessionTokenExists)
            {
                Debug.Log("Attempting to sign in with cached session...");
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                SceneManager.LoadScene("MainMenu");
            }
            else
            {
                Debug.Log("No session found. You may need to trigger Google Play Sign-In or a new anonymous sign-in.");
            }
        }
        catch (AuthenticationException ex)
        {
            Debug.LogError("Auth Exception: " + ex.Message);
        }
        catch (RequestFailedException ex)
        {
            Debug.LogError("Request Failed: " + ex.Message);
        }
    }
}
