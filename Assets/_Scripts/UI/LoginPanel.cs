using System;
using JadedBelles.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plain uGUI login panel. No UI Toolkit, no UXML, no USS, no Resources.Load.
///
/// SETUP (all Inspector drag-and-drop):
///   1. Build a Canvas with a panel containing:
///        - TMP_InputField  "Username"   (this is the account email)
///        - TMP_InputField  "Password"   (Content Type = Password)
///        - Button          "Log In"
///        - Button          "Sign Up"
///        - (optional) TextMeshProUGUI   status label
///        - (optional) Button            "Play as Guest"
///   2. Put this component on the panel root GameObject.
///   3. Drag each of those into the matching slot below.
///
/// You do NOT need to wire the Buttons' OnClick lists in the Inspector — this
/// component adds its own listeners in Awake. Wiring them manually too would
/// fire each action twice.
/// </summary>
public class LoginPanel : MonoBehaviour
{
    [Header("Input Fields")]
    [Tooltip("Account email. Labeled 'Username' in the UI.")]
    [SerializeField] private TMP_InputField usernameField;
    [Tooltip("Password. Set Content Type = Password on this field.")]
    [SerializeField] private TMP_InputField passwordField;

    [Header("Buttons")]
    [SerializeField] private Button loginButton;
    [SerializeField] private Button signUpButton;
    [Tooltip("Optional. Skips auth entirely and closes the panel.")]
    [SerializeField] private Button guestButton;

    [Header("Optional")]
    [Tooltip("Optional. Shows errors and progress messages.")]
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("Optional. The GameObject to hide once the player is signed in or chooses guest. Defaults to this GameObject.")]
    [SerializeField] private GameObject panelRoot;

    [Header("Behavior")]
    [Tooltip("On start, restore a saved session and hide this panel if it's still valid.")]
    [SerializeField] private bool skipIfAlreadySignedIn = true;
    [Tooltip("Keep the panel hidden while the saved session is being validated, so returning " +
             "players never see a login flash. Turn off to always show the panel until verified.")]
    [SerializeField] private bool hideWhileRestoringSession = true;
    [Tooltip("After a successful login or sign up, pull the player's remote save.")]
    [SerializeField] private bool pullPlayerDataOnLogin = true;
    [Tooltip("Remember a guest choice so the panel doesn't reappear on later MainMenu loads.")]
    [SerializeField] private bool rememberGuestChoice = true;

    private const string GuestPrefKey = "jb_played_as_guest";

    private bool _busy;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------
    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;

        if (loginButton != null) loginButton.onClick.AddListener(OnLoginClicked);
        if (signUpButton != null) signUpButton.onClick.AddListener(OnSignUpClicked);
        if (guestButton != null) guestButton.onClick.AddListener(OnGuestClicked);

        // Pressing Enter in the password field submits a login.
        if (passwordField != null)
            passwordField.onSubmit.AddListener(_ => OnLoginClicked());
    }

    private void OnDestroy()
    {
        if (loginButton != null) loginButton.onClick.RemoveListener(OnLoginClicked);
        if (signUpButton != null) signUpButton.onClick.RemoveListener(OnSignUpClicked);
        if (guestButton != null) guestButton.onClick.RemoveListener(OnGuestClicked);
    }

    private void Start()
    {
        WarnAboutMissingSlots();

        // Persistent login: tokens live in PlayerPrefs (TokenStore) and survive app restarts.
        // HasSession() only proves the strings exist, not that they still work, so we verify
        // against the API. JadedBellesApiClient auto-refreshes on a 401 and retries once, so a
        // merely-expired access token is renewed silently and the player stays signed in.
        if (skipIfAlreadySignedIn && TokenStore.HasSession())
        {
            RestoreSession();
            return;
        }

        // Previously chose guest → don't nag.
        if (rememberGuestChoice && PlayerPrefs.GetInt(GuestPrefKey, 0) == 1)
        {
            Hide();
            return;
        }

        Show();
    }

    /// <summary>
    /// Validates the stored session against the API. Succeeds silently for returning players
    /// (including when the access token had expired and was refreshed), and only falls back to
    /// the login panel when the refresh token is genuinely dead.
    /// </summary>
    private void RestoreSession()
    {
        // Avoid a login-panel flash for the common case where the session is fine.
        if (hideWhileRestoringSession) Hide();
        else SetBusy(true, "Restoring session...");

        JadedBellesApiClient.Instance.GetCurrentUser(
            onSuccess: response =>
            {
                if (!hideWhileRestoringSession) SetBusy(false, null);

                if (response == null || !response.success)
                {
                    // Server answered but rejected the session.
                    FallBackToSignIn("Please sign in again.");
                    return;
                }

                string who = response.data != null
                    ? (!string.IsNullOrEmpty(response.data.displayName)
                        ? response.data.displayName
                        : response.data.email)
                    : "player";

                Debug.Log("[LoginPanel] Session restored for " + who);
                SetStatus("Welcome back, " + who + ".");

                // A live account supersedes any earlier guest choice.
                PlayerPrefs.DeleteKey(GuestPrefKey);
                PlayerPrefs.Save();

                PullPlayerData();
                Hide();
            },
            onError: error =>
            {
                if (!hideWhileRestoringSession) SetBusy(false, null);

                // Distinguish "the network is down" from "this session is dead". On a transport
                // failure we keep the tokens so the player stays logged in and can retry offline;
                // only a real auth rejection clears them.
                if (IsLikelyNetworkFailure(error))
                {
                    Debug.LogWarning("[LoginPanel] Could not reach the API to verify the session; " +
                                     "keeping the saved login. " + error);
                    PullPlayerData();
                    Hide();
                    return;
                }

                Debug.Log("[LoginPanel] Stored session is no longer valid: " + error);
                FallBackToSignIn("Your session expired. Please sign in again.");
            });
    }

    /// <summary>Clears the dead session and puts the player back on the login panel.</summary>
    private void FallBackToSignIn(string message)
    {
        TokenStore.Clear();
        Show();
        SetStatus(message, isError: true);
    }

    /// <summary>
    /// Heuristic for "couldn't reach the server" vs "server said no". We only want to wipe a
    /// persistent login for the latter — a player on a plane shouldn't get logged out.
    /// </summary>
    private static bool IsLikelyNetworkFailure(string error)
    {
        if (string.IsNullOrEmpty(error)) return false;
        string e = error.ToLowerInvariant();
        return e.Contains("cannot connect")
            || e.Contains("connection")
            || e.Contains("timeout")
            || e.Contains("timed out")
            || e.Contains("unable to complete")
            || e.Contains("network")
            || e.Contains("dns")
            || e.Contains("host");
    }

    // ------------------------------------------------------------------
    // Button handlers
    // ------------------------------------------------------------------
    private void OnLoginClicked()
    {
        if (_busy) return;

        string email = (usernameField != null ? usernameField.text : "").Trim();
        string password = passwordField != null ? passwordField.text : "";

        if (!ValidateCredentials(email, password)) return;

        SetBusy(true, "Signing in...");

        JadedBellesApiClient.Instance.Login(
            email,
            password,
            onSuccess: response =>
            {
                // The client already persisted tokens via TokenStore on success.
                SetBusy(false, null);
                HandleAuthSuccess(response);
            },
            onError: error =>
            {
                SetBusy(false, null);
                SetStatus(string.IsNullOrEmpty(error) ? "Login failed." : error, isError: true);
                Debug.LogWarning("[LoginPanel] Login failed: " + error);
            });
    }

    private void OnSignUpClicked()
    {
        if (_busy) return;

        string email = (usernameField != null ? usernameField.text : "").Trim();
        string password = passwordField != null ? passwordField.text : "";

        if (!ValidateCredentials(email, password)) return;

        // Only two fields exist, so derive a starting display name from the email
        // local part. The player can rename later from account settings.
        string displayName = DeriveDisplayName(email);

        SetBusy(true, "Creating account...");

        JadedBellesApiClient.Instance.Register(
            email,
            password,
            displayName,
            onSuccess: response =>
            {
                SetBusy(false, null);
                HandleAuthSuccess(response);
            },
            onError: error =>
            {
                SetBusy(false, null);
                SetStatus(string.IsNullOrEmpty(error) ? "Sign up failed." : error, isError: true);
                Debug.LogWarning("[LoginPanel] Register failed: " + error);
            });
    }

    private void OnGuestClicked()
    {
        if (_busy) return;

        if (rememberGuestChoice)
        {
            PlayerPrefs.SetInt(GuestPrefKey, 1);
            PlayerPrefs.Save();
        }

        Debug.Log("[LoginPanel] Continuing as guest.");
        Hide();
    }

    // ------------------------------------------------------------------
    // Shared auth handling
    // ------------------------------------------------------------------
    private void HandleAuthSuccess(ApiResponseAuth response)
    {
        if (response == null || !response.success)
        {
            string msg = response != null && !string.IsNullOrEmpty(response.message)
                ? response.message
                : "Authentication failed.";
            SetStatus(msg, isError: true);
            return;
        }

        string who = response.data != null && response.data.user != null
            ? (!string.IsNullOrEmpty(response.data.user.displayName)
                ? response.data.user.displayName
                : response.data.user.email)
            : "player";

        SetStatus("Welcome, " + who + ".");
        Debug.Log("[LoginPanel] Authenticated as " + who);

        // A real account supersedes any earlier guest choice.
        PlayerPrefs.DeleteKey(GuestPrefKey);
        PlayerPrefs.Save();

        // Clear the password field so it isn't sitting in memory / on screen.
        if (passwordField != null) passwordField.text = "";

        PullPlayerData();
        Hide();
    }

    private void PullPlayerData()
    {
        if (!pullPlayerDataOnLogin) return;

        try
        {
            if (PlayerDataManager.instance != null)
                PlayerDataManager.instance.PullRemotePlayerData();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[LoginPanel] PullRemotePlayerData failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Validation + helpers
    // ------------------------------------------------------------------
    private bool ValidateCredentials(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            SetStatus("Enter your username.", isError: true);
            return false;
        }

        // The API treats the username as an email address.
        if (!email.Contains("@") || !email.Contains("."))
        {
            SetStatus("Username must be a valid email address.", isError: true);
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            SetStatus("Enter your password.", isError: true);
            return false;
        }

        if (password.Length < 6)
        {
            SetStatus("Password must be at least 6 characters.", isError: true);
            return false;
        }

        return true;
    }

    private static string DeriveDisplayName(string email)
    {
        if (string.IsNullOrEmpty(email)) return "Player";
        int at = email.IndexOf('@');
        string local = at > 0 ? email.Substring(0, at) : email;
        return string.IsNullOrWhiteSpace(local) ? "Player" : local;
    }

    private void SetBusy(bool busy, string message)
    {
        _busy = busy;

        if (loginButton != null) loginButton.interactable = !busy;
        if (signUpButton != null) signUpButton.interactable = !busy;
        if (guestButton != null) guestButton.interactable = !busy;
        if (usernameField != null) usernameField.interactable = !busy;
        if (passwordField != null) passwordField.interactable = !busy;

        if (!string.IsNullOrEmpty(message)) SetStatus(message);
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (statusText == null)
        {
            if (isError) Debug.LogWarning("[LoginPanel] " + message);
            return;
        }

        statusText.text = message;
        statusText.color = isError ? new Color(1f, 0.42f, 0.42f) : new Color(0.75f, 0.78f, 0.85f);
    }

    /// <summary>Shows the panel.</summary>
    public void Show()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
        SetStatus("");
    }

    /// <summary>Hides the panel.</summary>
    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    /// <summary>
    /// Signs the player out, clears the remembered guest choice, and shows the panel again.
    /// Hook this to a "Log Out" button anywhere in your menus.
    /// </summary>
    public void Logout()
    {
        if (_busy) return;
        SetBusy(true, "Signing out...");

        JadedBellesApiClient.Instance.Logout(
            onSuccess: _ =>
            {
                SetBusy(false, null);
                FinishLogout();
            },
            onError: error =>
            {
                // Even if the server call fails, the client clears local tokens.
                Debug.LogWarning("[LoginPanel] Logout call failed (local session cleared anyway): " + error);
                SetBusy(false, null);
                FinishLogout();
            });
    }

    private void FinishLogout()
    {
        PlayerPrefs.DeleteKey(GuestPrefKey);
        PlayerPrefs.Save();
        if (usernameField != null) usernameField.text = "";
        if (passwordField != null) passwordField.text = "";
        Show();
        SetStatus("Signed out.");
    }

    private void WarnAboutMissingSlots()
    {
        if (usernameField == null) Debug.LogError("[LoginPanel] Username field is not assigned in the Inspector.", this);
        if (passwordField == null) Debug.LogError("[LoginPanel] Password field is not assigned in the Inspector.", this);
        if (loginButton == null) Debug.LogError("[LoginPanel] Log In button is not assigned in the Inspector.", this);
        if (signUpButton == null) Debug.LogError("[LoginPanel] Sign Up button is not assigned in the Inspector.", this);
    }
}
