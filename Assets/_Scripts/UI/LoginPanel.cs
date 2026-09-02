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
    [Tooltip("OFF (recommended): SessionBootstrap drives this panel via the loading screen and " +
             "only shows it when auth is actually needed. ON: this panel resolves the session " +
             "itself on Start — use only if there's no SessionBootstrap in the scene.")]
    [SerializeField] private bool resolveSessionOnStart = false;
    [Tooltip("After a successful login or sign up, pull the player's remote save.")]
    [SerializeField] private bool pullPlayerDataOnLogin = true;
    [Tooltip("Remember a guest choice so the panel doesn't reappear on later loads.")]
    [SerializeField] private bool rememberGuestChoice = true;

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

        // Default path: SessionBootstrap already resolved the session behind the loading
        // screen and calls Show() only if auth is genuinely required. Stay hidden.
        if (!resolveSessionOnStart)
        {
            if (SessionService.IsResolved) Hide();
            return;
        }

        // Standalone fallback for scenes with no SessionBootstrap.
        SetBusy(true, "Checking session...");
        SessionService.Restore(state =>
        {
            SetBusy(false, null);

            if (SessionService.IsResolved)
            {
                if (SessionService.IsSignedIn) PullPlayerData();
                Hide();
                return;
            }

            Show();
            if (state == SessionService.SessionState.NeedsAuth && SessionService.HasStoredSession() == false)
                SetStatus("");
        });
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

        SessionService.MarkGuest(rememberGuestChoice);
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

        // Single source of truth — also clears any earlier guest choice.
        SessionService.MarkSignedIn(who);

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
        SessionService.MarkSignedOut();
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
