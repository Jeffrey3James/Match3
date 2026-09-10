using System.Collections.Generic;
using JadedBelles.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toon Blast–style Settings modal.
///
/// Owns the five toggle rows that live in the Settings sheet plus a
/// "Save Your Progress" section that hosts sign-in for guests and sign-out
/// for signed-in players. All state persists in PlayerPrefs so the rest of
/// the game can read it with the static <see cref="SettingsPanel"/>
/// accessors (e.g. <c>SettingsPanel.MusicEnabled</c>) without holding a
/// reference to the panel itself.
///
/// SETUP (all Inspector drag-and-drop, same conventions as LoginPanel /
/// AccountSettingsPanel):
///
///   1. Build a Canvas with a panel containing:
///        - 5 Toggle rows, each with:
///             - Toggle          "Music"          + Image "Icon"  (icon_music_on)
///             - Toggle          "Haptic"         + Image "Icon"  (icon_haptic_on)
///             - Toggle          "Chat"           + Image "Icon"  (icon_chat_on)
///             - Toggle          "Last Seen"      + Image "Icon"  (icon_lastseen_on)
///             - Toggle          "Notifications"  + Image "Icon"  (icon_notifications_on)
///        - "Save Your Progress" section with:
///             - GameObject      "Signed Out Group"
///                  - Button     "Sign In"        (opens LoginPanel)
///                  - Button     "Sign Up"        (opens LoginPanel, focus sign-up)
///             - GameObject      "Signed In Group"
///                  - TMP_Text   "Signed in as {email}"
///                  - Button     "Sign Out"
///        - (optional) Button    "Close"
///        - (optional) Button    "Delete Account" (opens AccountSettingsPanel)
///   2. Put this component on the panel root GameObject.
///   3. Drag every reference below into its matching slot.
///
/// You do NOT need to wire the buttons' OnClick lists in the Inspector — this
/// component adds its own listeners in Awake. Wiring them manually too would
/// fire each action twice (same footgun as LoginPanel).
///
/// The five glyph sprites (icon_music_on, icon_haptic_on, icon_chat_on,
/// icon_lastseen_on, icon_notifications_on) live in
/// <c>Assets/Images/UI_Icons/</c>. Drop each one on the matching row's
/// Image "Icon" child.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Static accessors — read these from anywhere in the game.
    // Reflect the user's persisted choice; default ON for a fresh install.
    // ------------------------------------------------------------------
    private const string MusicPrefKey         = "settings.music.on";
    private const string HapticPrefKey        = "settings.haptic.on";
    private const string ChatPrefKey          = "settings.chat.on";
    private const string LastSeenPrefKey      = "settings.lastseen.on";
    private const string NotificationsPrefKey = "settings.notifications.on";

    public static bool MusicEnabled         => GetBool(MusicPrefKey);
    public static bool HapticEnabled        => GetBool(HapticPrefKey);
    public static bool ChatEnabled          => GetBool(ChatPrefKey);
    public static bool LastSeenEnabled      => GetBool(LastSeenPrefKey);
    public static bool NotificationsEnabled => GetBool(NotificationsPrefKey);

    private static bool GetBool(string key) => PlayerPrefs.GetInt(key, 1) == 1;
    private static void SetBool(string key, bool value)
    {
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------
    // Toggle rows
    // ------------------------------------------------------------------
    [Header("Toggles")]
    [Tooltip("Music on/off. Persists to PlayerPrefs; audio code should read SettingsPanel.MusicEnabled.")]
    [SerializeField] private Toggle musicToggle;
    [Tooltip("Haptic feedback on/off. Persists to PlayerPrefs; input code should read SettingsPanel.HapticEnabled.")]
    [SerializeField] private Toggle hapticToggle;
    [Tooltip("Chat on/off. Persists to PlayerPrefs; social features should read SettingsPanel.ChatEnabled.")]
    [SerializeField] private Toggle chatToggle;
    [Tooltip("Broadcast last-seen on/off. Persists to PlayerPrefs; presence code should read SettingsPanel.LastSeenEnabled.")]
    [SerializeField] private Toggle lastSeenToggle;
    [Tooltip("Push notifications on/off. Persists to PlayerPrefs; push registration should read SettingsPanel.NotificationsEnabled.")]
    [SerializeField] private Toggle notificationsToggle;

    // ------------------------------------------------------------------
    // Save-your-progress section (host for sign-in / sign-out)
    // ------------------------------------------------------------------
    [Header("Save Your Progress")]
    [Tooltip("Group shown when the user is a guest / signed out. Contains 'Sign In' and 'Sign Up' buttons.")]
    [SerializeField] private GameObject signedOutGroup;
    [Tooltip("Group shown when the user is signed in. Contains the email label and 'Sign Out'.")]
    [SerializeField] private GameObject signedInGroup;

    [Tooltip("Opens the existing LoginPanel. Handles Sign In.")]
    [SerializeField] private Button signInButton;
    [Tooltip("Opens the existing LoginPanel. Handles Sign Up (LoginPanel already exposes both).")]
    [SerializeField] private Button signUpButton;
    [Tooltip("Shown when signed in. Signs the user out via SessionService.MarkSignedOut() and swaps groups.")]
    [SerializeField] private Button signOutButton;
    [Tooltip("Optional. Displays 'Signed in as {email}' when signed in.")]
    [SerializeField] private TextMeshProUGUI signedInLabel;

    [Header("Panel References")]
    [Tooltip("The LoginPanel to show when Sign In / Sign Up is pressed. Reuses the same auth UI as first-run.")]
    [SerializeField] private LoginPanel loginPanel;
    [Tooltip("Optional. AccountSettingsPanel opened by 'Delete Account'. Only meaningful when signed in.")]
    [SerializeField] private AccountSettingsPanel accountSettingsPanel;
    [Tooltip("Optional. Opens the account-deletion panel. Only enabled when signed in.")]
    [SerializeField] private Button deleteAccountButton;

    [Header("Optional")]
    [Tooltip("Optional. Closes this panel.")]
    [SerializeField] private Button closeButton;
    [Tooltip("Optional. The GameObject hidden when Close is pressed. Defaults to this GameObject.")]
    [SerializeField] private GameObject panelRoot;

    // ------------------------------------------------------------------
    // Bookkeeping so OnDestroy can cleanly unhook every listener.
    // ------------------------------------------------------------------
    private readonly List<(Toggle t, UnityEngine.Events.UnityAction<bool> h)> _toggleHooks = new();

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------
    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;

        // ---- Toggles ----
        HookToggle(musicToggle,         MusicPrefKey);
        HookToggle(hapticToggle,        HapticPrefKey);
        HookToggle(chatToggle,          ChatPrefKey);
        HookToggle(lastSeenToggle,      LastSeenPrefKey);
        HookToggle(notificationsToggle, NotificationsPrefKey);

        // ---- Sign-in section ----
        if (signInButton  != null) signInButton.onClick.AddListener(OnSignInClicked);
        if (signUpButton  != null) signUpButton.onClick.AddListener(OnSignUpClicked);
        if (signOutButton != null) signOutButton.onClick.AddListener(OnSignOutClicked);

        // ---- Misc ----
        if (closeButton         != null) closeButton.onClick.AddListener(OnCloseClicked);
        if (deleteAccountButton != null) deleteAccountButton.onClick.AddListener(OnDeleteAccountClicked);
    }

    private void OnEnable()
    {
        SessionService.OnStateChanged += HandleSessionStateChanged;
        RefreshSignInSection();
    }

    private void OnDisable()
    {
        SessionService.OnStateChanged -= HandleSessionStateChanged;
    }

    private void OnDestroy()
    {
        foreach (var (t, h) in _toggleHooks)
            if (t != null) t.onValueChanged.RemoveListener(h);
        _toggleHooks.Clear();

        if (signInButton         != null) signInButton.onClick.RemoveListener(OnSignInClicked);
        if (signUpButton         != null) signUpButton.onClick.RemoveListener(OnSignUpClicked);
        if (signOutButton        != null) signOutButton.onClick.RemoveListener(OnSignOutClicked);
        if (closeButton          != null) closeButton.onClick.RemoveListener(OnCloseClicked);
        if (deleteAccountButton  != null) deleteAccountButton.onClick.RemoveListener(OnDeleteAccountClicked);
    }

    // ------------------------------------------------------------------
    // Toggle wiring
    // ------------------------------------------------------------------
    private void HookToggle(Toggle toggle, string prefKey)
    {
        if (toggle == null) return;

        // Initialize UI from saved value WITHOUT firing the listener.
        toggle.SetIsOnWithoutNotify(GetBool(prefKey));

        UnityEngine.Events.UnityAction<bool> handler = value =>
        {
            SetBool(prefKey, value);
            OnPrefChanged(prefKey);
        };
        toggle.onValueChanged.AddListener(handler);
        _toggleHooks.Add((toggle, handler));
    }

    /// <summary>
    /// Fires any side effects a toggle should have on OS-level state
    /// (push registration today; more later). Called AFTER the pref is
    /// persisted, so services that read <see cref="NotificationsEnabled"/>
    /// see the new value.
    /// </summary>
    private void OnPrefChanged(string prefKey)
    {
        if (prefKey == NotificationsPrefKey)
        {
            // Align the OS's push-registration state with the new toggle value.
            PushNotificationService.Sync();
        }
        // Music / Haptic / Chat / LastSeen are polled on demand by the systems
        // that read them (AudioManager, MatchJuiceRuntime, HapticService,
        // ChatService, PresenceService), so no proactive push is needed here.
    }

    // ------------------------------------------------------------------
    // Sign-in section
    // ------------------------------------------------------------------
    private void HandleSessionStateChanged(SessionService.SessionState _) => RefreshSignInSection();

    private void RefreshSignInSection()
    {
        bool signedIn = SessionService.IsSignedIn;

        if (signedOutGroup != null) signedOutGroup.SetActive(!signedIn);
        if (signedInGroup  != null) signedInGroup.SetActive(signedIn);

        if (signedInLabel != null)
        {
            signedInLabel.text = signedIn
                ? $"Signed in as {SessionService.CurrentUserName ?? "player"}"
                : string.Empty;
        }

        // Account-deletion path is only meaningful when there IS an account.
        if (deleteAccountButton != null)
            deleteAccountButton.gameObject.SetActive(signedIn);
    }

    private void OnSignInClicked()  => ShowLoginPanel();
    private void OnSignUpClicked()  => ShowLoginPanel();

    private void ShowLoginPanel()
    {
        if (loginPanel == null)
        {
            Debug.LogWarning("[SettingsPanel] Sign In pressed but no LoginPanel is wired.");
            return;
        }

        // Reveal the LoginPanel and hide this Settings sheet so the two don't stack.
        loginPanel.gameObject.SetActive(true);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void OnSignOutClicked()
    {
        SessionService.MarkSignedOut();
        RefreshSignInSection();
    }

    private void OnDeleteAccountClicked()
    {
        if (accountSettingsPanel == null)
        {
            Debug.LogWarning("[SettingsPanel] Delete Account pressed but no AccountSettingsPanel is wired.");
            return;
        }

        accountSettingsPanel.gameObject.SetActive(true);
    }

    // ------------------------------------------------------------------
    // Close
    // ------------------------------------------------------------------
    private void OnCloseClicked()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }
}
