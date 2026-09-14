using UnityEngine;

/// <summary>
/// Static haptic feedback façade. Gameplay code fires <see cref="Light"/>,
/// <see cref="Medium"/>, or <see cref="Heavy"/> at match / powerup / gameover moments
/// without caring what platform is underneath.
///
/// Every call is a no-op when the player has turned haptics OFF in Settings
/// (<see cref="SettingsPanel.HapticEnabled"/> == false) or the platform doesn't
/// support vibration. Never throws — haptics must not be able to crash gameplay.
///
/// Platform mapping today:
///   iOS Editor / Standalone  : no-op (Handheld.Vibrate would beep-buzz the Mac).
///   Android device           : Handheld.Vibrate() — coarse system buzz.
///   iOS device               : Handheld.Vibrate() — coarse system buzz.
///
/// TODO: swap to iOS Core Haptics (Light/Medium/Heavy Impact) via a native
/// plugin when we add one. The intensity distinction is preserved in the API
/// today so callers don't have to change when that upgrade lands.
/// </summary>
public static class HapticService
{
    public static void Light()  => Fire(HapticStrength.Light);
    public static void Medium() => Fire(HapticStrength.Medium);
    public static void Heavy()  => Fire(HapticStrength.Heavy);

    private enum HapticStrength { Light, Medium, Heavy }

    private static void Fire(HapticStrength strength)
    {
        if (!SettingsPanel.HapticEnabled) return; // player disabled haptics in Settings

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        try
        {
            // Coarse Handheld.Vibrate is the only cross-platform primitive Unity
            // ships out of the box. Strength is ignored on both platforms today —
            // native plugins can differentiate later.
            Handheld.Vibrate();
        }
        catch (System.Exception e)
        {
            // A native binding failing must never propagate into a gameplay
            // coroutine (see AudioManager.PlayRandomPitch comment). Log once,
            // swallow forever.
            Debug.LogWarning($"[HapticService] Vibrate failed ({strength}): {e.Message}");
        }
#else
        // Editor / desktop: silent no-op. Uncomment the next line if you want
        // to eyeball that gameplay is calling into the service:
        // Debug.Log($"[HapticService] (editor no-op) {strength}");
        _ = strength;
#endif
    }
}
