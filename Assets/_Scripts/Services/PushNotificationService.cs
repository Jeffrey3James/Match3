using UnityEngine;

/// <summary>
/// Static push-notification façade. Boot code and the Settings toggle both
/// call <see cref="Sync"/>: it registers with the OS for push if the player
/// has notifications ON, and unregisters (or skips registration entirely)
/// if the player has them OFF.
///
/// Today this is a stub — no third-party push SDK (Firebase / OneSignal / APNS
/// via UnityEngine.iOS.NotificationServices) is wired in. Behavior is
/// intentionally observable via Debug.Log so QA can see the toggle taking
/// effect end-to-end. When the real SDK lands, replace the marked TODOs and
/// this file's public API stays the same.
///
/// Callers:
///   SettingsPanel  — should call HapticService/Push equivalents on toggle
///                    changes so the OS state matches the UI in real time.
///   Boot code      — call Sync() from a bootstrap MonoBehaviour Start() so
///                    a device that's been in the app before is properly
///                    (un)registered on relaunch.
/// </summary>
public static class PushNotificationService
{
    private static bool _registered;

    /// <summary>
    /// Idempotent. Aligns the OS's push-registration state with the current
    /// value of <see cref="SettingsPanel.NotificationsEnabled"/>.
    /// </summary>
    public static void Sync()
    {
        if (SettingsPanel.NotificationsEnabled)
        {
            if (_registered) return;
            Register();
        }
        else
        {
            if (!_registered) return;
            Unregister();
        }
    }

    private static void Register()
    {
        _registered = true;

#if UNITY_IOS && !UNITY_EDITOR
        // TODO: register with APNS via UnityEngine.iOS.NotificationServices
        // or a bundled provider (Firebase Messaging / OneSignal). Prompt the
        // user for permission on first call. Never throw out of here.
        Debug.Log("[PushNotificationService] (iOS stub) Would register for push notifications.");
#elif UNITY_ANDROID && !UNITY_EDITOR
        // TODO: register with FCM via a bundled provider. Android 13+ needs the
        // POST_NOTIFICATIONS runtime permission; request it before registering.
        Debug.Log("[PushNotificationService] (Android stub) Would register for push notifications.");
#else
        Debug.Log("[PushNotificationService] (editor stub) Register.");
#endif
    }

    private static void Unregister()
    {
        _registered = false;

#if UNITY_IOS && !UNITY_EDITOR
        // TODO: unregister with APNS. Some providers only need a server-side
        // token invalidation; do whichever the shipping provider recommends.
        Debug.Log("[PushNotificationService] (iOS stub) Would unregister from push notifications.");
#elif UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("[PushNotificationService] (Android stub) Would unregister from push notifications.");
#else
        Debug.Log("[PushNotificationService] (editor stub) Unregister.");
#endif
    }
}
