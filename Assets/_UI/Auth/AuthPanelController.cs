using System;
using JadedBelles.Networking;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace JadedBelles.UI
{
    /// <summary>
    /// Drop-in reusable UI Toolkit auth panel.
    ///
    /// Setup (once, per scene that needs auth):
    ///   1. Add a GameObject with a <see cref="UIDocument"/> component.
    ///   2. Assign AuthPanel.uxml as the Source Asset (the .uss is referenced from the UXML).
    ///   3. Add this <see cref="AuthPanelController"/> component to the same GameObject.
    ///
    /// Everything else is code — no per-scene button wiring, no prefab reconfiguration.
    /// Talks to <see cref="JadedBellesApiClient"/> for login / register / logout, then
    /// refreshes <see cref="PlayerDataManager"/> from the API so saves come down immediately.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AuthPanelController : MonoBehaviour
    {
        [Header("Behavior")]
        [Tooltip("Hide the whole panel automatically once the user is signed in. " +
                 "Leave off if you want a persistent account widget (menu, settings, etc.).")]
        [SerializeField] private bool _hideWhenSignedIn = false;

        [Tooltip("After a successful login, pull the player's remote save so lives / coins / progress reflect their account.")]
        [SerializeField] private bool _pullPlayerDataOnLogin = true;

        [Header("Events (optional, wire in Inspector if you want)")]
        public UnityEvent OnLoggedIn;
        public UnityEvent OnLoggedOut;
        public UnityEvent OnGuestChosen;

        // ---- Root
        private VisualElement _root;

        // ---- Signed-out controls
        private Button _tabLogin, _tabRegister;
        private VisualElement _panelLogin, _panelRegister;
        private TextField _loginEmail, _loginPassword;
        private TextField _regDisplay, _regEmail, _regPassword;
        private Button _btnLogin, _btnRegister, _btnGuest;
        private Label _status;
        private VisualElement _busyOverlay;

        // ---- Signed-in controls
        private Label _userDisplay, _userEmail;
        private Button _btnLogout;

        private bool _busy;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------
        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            var rootVisual = doc.rootVisualElement;
            if (rootVisual == null)
            {
                Debug.LogError("[AuthPanel] UIDocument has no rootVisualElement. Is a Panel Settings asset assigned?");
                return;
            }

            // Attach the stylesheet from Resources so callers don't have to wire it up per scene.
            var uss = Resources.Load<StyleSheet>("UI/Auth/AuthPanel");
            if (uss != null && !rootVisual.styleSheets.Contains(uss))
                rootVisual.styleSheets.Add(uss);

            _root = rootVisual.Q<VisualElement>("auth-root");
            if (_root == null)
            {
                Debug.LogError("[AuthPanel] Could not find #auth-root in the UXML. Is AuthPanel.uxml assigned to the UIDocument?");
                return;
            }

            CacheQueries();
            BindEvents();
            RefreshView();
        }

        /// <summary>
        /// Spawns a fully wired AuthPanel at runtime with no scene setup required.
        /// Loads the UXML from <c>Resources/UI/Auth/AuthPanel</c> and its USS from
        /// <c>Resources/UI/Auth/AuthPanel</c>. Returns the created controller so callers
        /// can subscribe to <see cref="OnLoggedIn"/>, <see cref="OnGuestChosen"/>, etc.
        /// </summary>
        public static AuthPanelController Spawn(PanelSettings panelSettings = null, bool hideWhenSignedIn = true, bool pullPlayerDataOnLogin = true)
        {
            var uxml = Resources.Load<VisualTreeAsset>("UI/Auth/AuthPanel");
            if (uxml == null)
            {
                Debug.LogError("[AuthPanel] Could not load Resources/UI/Auth/AuthPanel.uxml.");
                return null;
            }

            // If no PanelSettings was supplied, build a serviceable one in memory so the caller doesn't
            // have to author an asset in the Editor. Perfectly fine for a modal overlay.
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.name = "AuthPanel_RuntimePanelSettings";
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1080, 1920);
                panelSettings.match = 0.5f;
            }

            var go = new GameObject("AuthPanel (runtime)");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = uxml;
            doc.sortingOrder = 100; // Render above the rest of the MainMenu.

            var controller = go.AddComponent<AuthPanelController>();
            controller._hideWhenSignedIn = hideWhenSignedIn;
            controller._pullPlayerDataOnLogin = pullPlayerDataOnLogin;
            return controller;
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        // ------------------------------------------------------------------
        // Wiring
        // ------------------------------------------------------------------
        private void CacheQueries()
        {
            _tabLogin    = _root.Q<Button>("tab-login");
            _tabRegister = _root.Q<Button>("tab-register");
            _panelLogin    = _root.Q<VisualElement>("panel-login");
            _panelRegister = _root.Q<VisualElement>("panel-register");

            _loginEmail    = _root.Q<TextField>("login-email");
            _loginPassword = _root.Q<TextField>("login-password");
            _regDisplay    = _root.Q<TextField>("reg-display");
            _regEmail      = _root.Q<TextField>("reg-email");
            _regPassword   = _root.Q<TextField>("reg-password");

            _btnLogin    = _root.Q<Button>("btn-login");
            _btnRegister = _root.Q<Button>("btn-register");
            _btnGuest    = _root.Q<Button>("btn-guest");
            _btnLogout   = _root.Q<Button>("btn-logout");

            _status       = _root.Q<Label>("status");
            _busyOverlay  = _root.Q<VisualElement>("busy-overlay");

            _userDisplay = _root.Q<Label>("user-display");
            _userEmail   = _root.Q<Label>("user-email");
        }

        private void BindEvents()
        {
            _tabLogin.clicked    += ShowLoginTab;
            _tabRegister.clicked += ShowRegisterTab;
            _btnLogin.clicked    += HandleLogin;
            _btnRegister.clicked += HandleRegister;
            _btnGuest.clicked    += HandleGuest;
            _btnLogout.clicked   += HandleLogout;

            // Submit-on-enter
            _loginPassword.RegisterCallback<KeyDownEvent>(OnLoginEnter);
            _regPassword.RegisterCallback<KeyDownEvent>(OnRegisterEnter);
        }

        private void UnbindEvents()
        {
            if (_tabLogin != null)    _tabLogin.clicked    -= ShowLoginTab;
            if (_tabRegister != null) _tabRegister.clicked -= ShowRegisterTab;
            if (_btnLogin != null)    _btnLogin.clicked    -= HandleLogin;
            if (_btnRegister != null) _btnRegister.clicked -= HandleRegister;
            if (_btnGuest != null)    _btnGuest.clicked    -= HandleGuest;
            if (_btnLogout != null)   _btnLogout.clicked   -= HandleLogout;

            _loginPassword?.UnregisterCallback<KeyDownEvent>(OnLoginEnter);
            _regPassword?.UnregisterCallback<KeyDownEvent>(OnRegisterEnter);
        }

        private void OnLoginEnter(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) HandleLogin();
        }

        private void OnRegisterEnter(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) HandleRegister();
        }

        // ------------------------------------------------------------------
        // Tabs
        // ------------------------------------------------------------------
        private void ShowLoginTab()
        {
            _tabLogin.AddToClassList("tab-btn--active");
            _tabRegister.RemoveFromClassList("tab-btn--active");
            _panelLogin.AddToClassList("tab-panel--active");
            _panelRegister.RemoveFromClassList("tab-panel--active");
            ClearStatus();
        }

        private void ShowRegisterTab()
        {
            _tabRegister.AddToClassList("tab-btn--active");
            _tabLogin.RemoveFromClassList("tab-btn--active");
            _panelRegister.AddToClassList("tab-panel--active");
            _panelLogin.RemoveFromClassList("tab-panel--active");
            ClearStatus();
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------
        private void HandleLogin()
        {
            if (_busy) return;

            string email = (_loginEmail.value ?? string.Empty).Trim();
            string password = _loginPassword.value ?? string.Empty;
            if (!ValidateEmailPassword(email, password)) return;

            SetBusy(true, "Signing in...");
            JadedBellesApiClient.Instance.Login(email, password,
                _ =>
                {
                    // Tokens are persisted inside the client via TokenStore. Fetch the profile for the signed-in view.
                    FetchAndShowUser(afterLogin: true);
                },
                err => FinishError(err ?? "Login failed. Check your credentials."));
        }

        private void HandleRegister()
        {
            if (_busy) return;

            string display = (_regDisplay.value ?? string.Empty).Trim();
            string email   = (_regEmail.value ?? string.Empty).Trim();
            string password = _regPassword.value ?? string.Empty;

            if (string.IsNullOrEmpty(display))
            {
                ShowStatus("Please enter a display name.");
                return;
            }
            if (!ValidateEmailPassword(email, password)) return;

            SetBusy(true, "Creating account...");
            JadedBellesApiClient.Instance.Register(email, password, display,
                _ => FetchAndShowUser(afterLogin: true),
                err => FinishError(err ?? "Registration failed."));
        }

        private void HandleGuest()
        {
            ClearStatus();
            OnGuestChosen?.Invoke();
            if (_hideWhenSignedIn) gameObject.SetActive(false);
        }

        private void HandleLogout()
        {
            if (_busy) return;
            SetBusy(true, "Signing out...");
            JadedBellesApiClient.Instance.Logout(
                _ =>
                {
                    SetBusy(false);
                    RefreshView();
                    OnLoggedOut?.Invoke();
                },
                err =>
                {
                    // Even on error, treat the local session as gone — the client clears tokens on logout.
                    SetBusy(false);
                    RefreshView();
                    OnLoggedOut?.Invoke();
                    ShowStatus(err ?? "Logged out (local).", ok: true);
                });
        }

        // ------------------------------------------------------------------
        // Success path
        // ------------------------------------------------------------------
        private void FetchAndShowUser(bool afterLogin)
        {
            JadedBellesApiClient.Instance.GetCurrentUser(
                resp =>
                {
                    SetBusy(false);
                    string display = resp?.data?.displayName ?? "Player";
                    string email = resp?.data?.email ?? string.Empty;

                    if (_userDisplay != null) _userDisplay.text = display;
                    if (_userEmail   != null) _userEmail.text   = email;

                    if (afterLogin && _pullPlayerDataOnLogin)
                    {
                        // Fire-and-forget; PlayerDataManager handles remote-vs-local merge.
                        try
                        {
                            if (PlayerDataManager.instance != null)
                                PlayerDataManager.instance.PullRemotePlayerData();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("[AuthPanel] Could not trigger PullRemotePlayerData: " + ex.Message);
                        }
                    }

                    RefreshView();
                    OnLoggedIn?.Invoke();
                },
                err =>
                {
                    // Session exists but profile call failed — still switch to signed-in view.
                    SetBusy(false);
                    RefreshView();
                    ShowStatus(err ?? "Signed in, but couldn't load your profile.", ok: true);
                    OnLoggedIn?.Invoke();
                });
        }

        // ------------------------------------------------------------------
        // View state
        // ------------------------------------------------------------------
        private void RefreshView()
        {
            bool signedIn = JadedBellesApiClient.Instance != null && JadedBellesApiClient.Instance.HasSession;

            _root.RemoveFromClassList(signedIn ? "signed-out" : "signed-in");
            _root.AddToClassList(signedIn ? "signed-in" : "signed-out");

            if (signedIn && _hideWhenSignedIn)
            {
                gameObject.SetActive(false);
                return;
            }

            if (signedIn)
            {
                // Refresh cached user labels lazily; if empty, fetch now.
                if (_userDisplay != null && string.IsNullOrEmpty(_userDisplay.text))
                    FetchAndShowUser(afterLogin: false);
            }
            else
            {
                ClearStatus();
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private bool ValidateEmailPassword(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                ShowStatus("Please enter a valid email address.");
                return false;
            }
            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                ShowStatus("Password must be at least 6 characters.");
                return false;
            }
            return true;
        }

        private void FinishError(string message)
        {
            SetBusy(false);
            ShowStatus(message);
        }

        private void ShowStatus(string message, bool ok = false)
        {
            if (_status == null) return;
            _status.text = message ?? string.Empty;
            _status.RemoveFromClassList("auth-status--ok");
            if (ok) _status.AddToClassList("auth-status--ok");
        }

        private void ClearStatus() => ShowStatus(string.Empty);

        private void SetBusy(bool busy, string message = null)
        {
            _busy = busy;
            _btnLogin?.SetEnabled(!busy);
            _btnRegister?.SetEnabled(!busy);
            _btnGuest?.SetEnabled(!busy);
            _btnLogout?.SetEnabled(!busy);

            if (_busyOverlay != null)
            {
                if (busy) _busyOverlay.RemoveFromClassList("hidden");
                else      _busyOverlay.AddToClassList("hidden");
                var lbl = _busyOverlay.Q<Label>();
                if (lbl != null && !string.IsNullOrEmpty(message)) lbl.text = message;
            }
        }
    }
}
