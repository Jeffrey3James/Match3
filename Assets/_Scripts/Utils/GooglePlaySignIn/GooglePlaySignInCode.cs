using UnityEngine;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using TMPro;
public class GooglePlaySignInCode : MonoBehaviour
{
    public string GooglePlayToken;
    public string GooglePlayError;

    [SerializeField] TextMeshProUGUI testSignInTextUI;

    public TaskCompletionSource<bool> loginResultTCS;

    public async Task Authenticate()
    {
        PlayGamesPlatform.DebugLogEnabled = true;
        PlayGamesPlatform.Activate();

        var loginResultTCS = new TaskCompletionSource<string>();
        PlayGamesPlatform.Instance.Authenticate(status =>
        {
            Debug.Log("Google Play sign-in status: " + status);

            if (status == SignInStatus.Success)
            {
                PlayGamesPlatform.Instance.RequestServerSideAccess(true, code =>
                {
                    GooglePlayToken = code;
                    loginResultTCS.SetResult(code);
                });
            }
            else if (status == SignInStatus.Canceled)
            {
                Debug.LogWarning("Sign-in was canceled by the user or UI was closed.");
                testSignInTextUI.text = "Sign-in canceled by user.";
            }
            else if (status == SignInStatus.InternalError)
            {
                Debug.LogError("Google Play sign-in failed due to an internal error.");
                testSignInTextUI.text = "Internal sign-in error.";
            }
            else if (status == SignInStatus.DeveloperError)
            {
                Debug.LogError("Misconfigured credentials or wrong package name.");
                testSignInTextUI.text = "Developer error: Check OAuth Client ID and package name.";
            }
            else
            {
                Debug.LogError($"Unhandled sign-in status: {status}");
                testSignInTextUI.text = $"Unhandled sign-in status: {status}";
            }
        });

        // Wait for server auth code
        string serverAuthCode = await loginResultTCS.Task;

        // Now authenticate with Unity using the code
        await AuthenticateWithUnity(serverAuthCode);
    }


    private async Task AuthenticateWithUnity(string serverAuthCode)
    {
        try
        {
            await AuthenticationService.Instance.SignInWithGooglePlayGamesAsync(serverAuthCode);
            testSignInTextUI.text = "Unity Authentication succeeded.";
            Debug.Log("Unity Authentication succeeded.");
        }
        catch (AuthenticationException e)
        {
            Debug.LogError("Unity Authentication failed: " + e.Message);
            testSignInTextUI.text = "Unity Authentication failed: " + e.Message;
            throw;
        }
        catch (RequestFailedException e)
        {
            Debug.LogError("Request failed: " + e.Message);
            testSignInTextUI.text = "Request failed: " + e.Message;
            throw;
        }
    }
}
