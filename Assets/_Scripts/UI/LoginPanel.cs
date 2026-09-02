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
///        - TMP_InputField  "Username"   (a username OR an email address)
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
    [Tooltip("Username or email address. Either is accepted at login.")]
    [SerializeField] private TMP_InputField usernameField;
    [Tooltip("Password. Set Content Type = Password on this field.")]
    [SerializeField] private TMP_InputField passwordField;

    [Header("Auto-size")]
    [Tooltip("Shrink the text in the username/password fields so long values stay inside the box.")]
    [SerializeField] private bool autoSizeInputText = true;
    [Tooltip("Smallest font size the input text can shrink to.")]
    [SerializeField] private float autoSizeMin = 12f;
    [Tooltip("Largest font size the input text can grow to. Also the starting size.")]
    [SerializeField] private float autoSizeMax = 32f;

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

        if (autoSizeInputText)
        {
            ApplyAutoSize(usernameField);
            ApplyAutoSize(passwordField);
        }
    }

    // Enables TMP auto-sizing on the field's text component so long usernames,
    // emails, or password masks shrink to fit inside the input box instead of
    // clipping or overflowing.
    private void ApplyAutoSize(TMP_InputField field)
    {
        if (field == null) return;

        TMP_Text text = field.textComponent;
        if (text != null)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = autoSizeMin;
            text.fontSizeMax = autoSizeMax;
            text.fontSize = autoSizeMax;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        TMP_Text placeholder = field.placeholder as TMP_Text;
        if (placeholder != null)
        {
            placeholder.enableAutoSizing = true;
            placeholder.fontSizeMin = autoSizeMin;
            placeholder.fontSizeMax = autoSizeMax;
            placeholder.fontSize = autoSizeMax;
            placeholder.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Keep the input on a single visible line — auto-size handles the fit.
        field.lineType = TMP_InputField.LineType.SingleLine;
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

        string identifier = (usernameField != null ? usernameField.text : "").Trim();
        string password = passwordField != null ? passwordField.text : "";

        if (!ValidateCredentials(identifier, password)) return;

        SetBusy(true, "Signing in...");

        JadedBellesApiClient.Instance.Login(
            identifier,
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

        string identifier = (usernameField != null ? usernameField.text : "").Trim();
        string password = passwordField != null ? passwordField.text : "";

        if (!ValidateCredentials(identifier, password)) return;

        // Only two fields exist, so derive a starting display name from the
        // identifier (the email local part, or the username itself). The player
        // can rename later from account settings.
        string displayName = DeriveDisplayName(identifier);

        SetBusy(true, "Creating account...");

        JadedBellesApiClient.Instance.Register(
            identifier,
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
    private bool ValidateCredentials(string identifier, string password)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            SetStatus("Enter your username or email.", isError: true);
            return false;
        }

        // Either a username or an email is accepted. If it looks like an email
        // (contains '@'), require a '.' too so a stray '@' isn't accepted as a
        // half-typed address. Bare usernames pass through without that check.
        if (identifier.Contains("@") && !identifier.Contains("."))
        {
            SetStatus("That doesn't look like a valid email address.", isError: true);
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

    private static string DeriveDisplayName(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return "Player";
        int at = identifier.IndexOf('@');
        string local = at > 0 ? identifier.Substring(0, at) : identifier;
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
