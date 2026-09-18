using System.Collections.Generic;
using JadedBelles.Networking;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Settings modal containing five preference toggles, an optional Close
/// button, and an optional shortcut to AccountSettingsPanel.
///
/// Sign-in and sign-out belong to LoginPanel, not this component.
///
/// SETUP:
///   1. Attach this component to the settings panel.
///   2. Assign the five Toggle references.
///   3. Optionally assign Close, panelRoot, Delete Account,
///      and AccountSettingsPanel.
///   4. Leave these controls' Inspector event lists empty.
///      This component registers its own listeners.
///
/// Preferences persist in PlayerPrefs and default to ON.
/// </summary>
public class SettingsPanel : MonoBehaviour {
  // ------------------------------------------------------------------
  // Persisted preferences
  // ------------------------------------------------------------------
  private const string MusicPrefKey = "settings.music.on";
  private const string HapticPrefKey = "settings.haptic.on";
  private const string ChatPrefKey = "settings.chat.on";
  private const string LastSeenPrefKey = "settings.lastseen.on";
  private const string NotificationsPrefKey = "settings.notifications.on";

  public static bool MusicEnabled => GetBool(MusicPrefKey);
  public static bool HapticEnabled => GetBool(HapticPrefKey);
  public static bool ChatEnabled => GetBool(ChatPrefKey);
  public static bool LastSeenEnabled => GetBool(LastSeenPrefKey);
  public static bool NotificationsEnabled => GetBool(NotificationsPrefKey);

  private static bool GetBool ( string key ) {
    return PlayerPrefs.GetInt(key, 1) == 1;
  }

  private static void SetBool ( string key, bool value ) {
    PlayerPrefs.SetInt(key, value ? 1 : 0);
    PlayerPrefs.Save();
  }

  // ------------------------------------------------------------------
  // Inspector references
  // ------------------------------------------------------------------
  [Header("Toggles")]
  [SerializeField] private Toggle musicToggle;
  [SerializeField] private Toggle hapticToggle;
  [SerializeField] private Toggle chatToggle;
  [SerializeField] private Toggle lastSeenToggle;
  [SerializeField] private Toggle notificationsToggle;

  [Header("Account Settings")]
  [Tooltip("Optional. The panel responsible for account deletion.")]
  [SerializeField] private AccountSettingsPanel accountSettingsPanel;

  [Tooltip("Optional. Opens account settings. Visible only when signed in.")]
  [SerializeField] private Button deleteAccountButton;

  [Header("Optional")]
  [SerializeField] private Button closeButton;

  [Tooltip("The object hidden by Close. Defaults to this GameObject.")]
  [SerializeField] private GameObject panelRoot;

  private readonly List<(Toggle toggle, UnityAction<bool> handler)>
      _toggleHooks = new();

  // ------------------------------------------------------------------
  // Lifecycle
  // ------------------------------------------------------------------
  private void Awake () {
    if(panelRoot == null)
      panelRoot = gameObject;

    HookToggle(musicToggle, MusicPrefKey);
    HookToggle(hapticToggle, HapticPrefKey);
    HookToggle(chatToggle, ChatPrefKey);
    HookToggle(lastSeenToggle, LastSeenPrefKey);
    HookToggle(notificationsToggle, NotificationsPrefKey);

    if(closeButton != null)
      closeButton.onClick.AddListener(OnCloseClicked);

    if(deleteAccountButton != null)
      deleteAccountButton.onClick.AddListener(OnDeleteAccountClicked);
  }

  private void OnEnable () {
    SessionService.OnStateChanged += HandleSessionStateChanged;

    RefreshToggles();
    RefreshAccountShortcut();
  }

  private void OnDisable () {
    SessionService.OnStateChanged -= HandleSessionStateChanged;
  }

  private void OnDestroy () {
    foreach(var (toggle, handler) in _toggleHooks) {
      if(toggle != null)
        toggle.onValueChanged.RemoveListener(handler);
    }

    _toggleHooks.Clear();

    if(closeButton != null)
      closeButton.onClick.RemoveListener(OnCloseClicked);

    if(deleteAccountButton != null)
      deleteAccountButton.onClick.RemoveListener(OnDeleteAccountClicked);
  }

  // ------------------------------------------------------------------
  // Preferences
  // ------------------------------------------------------------------
  private void HookToggle ( Toggle toggle, string prefKey ) {
    if(toggle == null)
      return;

    toggle.SetIsOnWithoutNotify(GetBool(prefKey));

    UnityAction<bool> handler = value =>
    {
      SetBool(prefKey, value);

      if(prefKey == NotificationsPrefKey)
        PushNotificationService.Sync();
    };

    toggle.onValueChanged.AddListener(handler);
    _toggleHooks.Add((toggle, handler));
  }

  private void RefreshToggles () {
    if(musicToggle != null)
      musicToggle.SetIsOnWithoutNotify(MusicEnabled);

    if(hapticToggle != null)
      hapticToggle.SetIsOnWithoutNotify(HapticEnabled);

    if(chatToggle != null)
      chatToggle.SetIsOnWithoutNotify(ChatEnabled);

    if(lastSeenToggle != null)
      lastSeenToggle.SetIsOnWithoutNotify(LastSeenEnabled);

    if(notificationsToggle != null)
      notificationsToggle.SetIsOnWithoutNotify(NotificationsEnabled);
  }

  // ------------------------------------------------------------------
  // Account-deletion shortcut only. No authentication actions.
  // ------------------------------------------------------------------
  private void HandleSessionStateChanged ( SessionService.SessionState _ ) {
    RefreshAccountShortcut();
  }

  private void RefreshAccountShortcut () {
    if(deleteAccountButton != null)
      deleteAccountButton.gameObject.SetActive(SessionService.IsSignedIn);
  }

  private void OnDeleteAccountClicked () {
    if(!SessionService.IsSignedIn)
      return;

    if(accountSettingsPanel == null) {
      Debug.LogWarning(
          "[SettingsPanel] No AccountSettingsPanel is assigned.",
          this);
      return;
    }

    accountSettingsPanel.gameObject.SetActive(true);
  }

  // ------------------------------------------------------------------
  // Close
  // ------------------------------------------------------------------
  private void OnCloseClicked () {
    if(panelRoot != null)
      panelRoot.SetActive(false);
  }
}