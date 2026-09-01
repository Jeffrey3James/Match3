using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using Unity.Advertisement.IosSupport;
#endif

namespace Match3Game.Monetization {
  /// <summary>
  /// Requests the iOS App Tracking Transparency prompt once at process
  /// start, BEFORE AdManager bootstraps (which is BeforeSplashScreen vs
  /// AdManager's AfterSceneLoad). This is required for LevelPlay to
  /// receive an IDFA on iOS 14+ and for ads to serve non-personalized
  /// fallbacks cleanly when the user declines.
  ///
  /// Fail-closed like the rest of Monetization/: on Android, WebGL, in
  /// the editor, or if the ios-support package isn't installed, this
  /// file is a no-op and nothing in the build changes.
  ///
  /// Requires (iOS builds only):
  ///   • Package "iOS 14 Advertising Support" (com.unity.ads.ios-support)
  ///     installed via Package Manager.
  ///   • Player Settings > iOS > Other Settings > "User Tracking Usage
  ///     Description" filled in (writes NSUserTrackingUsageDescription
  ///     to Info.plist). Without that string iOS silently skips the
  ///     prompt and IDFA stays zeroed.
  /// </summary>
  internal static class AttPrompt {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void Request() {
#if UNITY_IOS && !UNITY_EDITOR
            var status = ATTrackingStatusBinding.GetAuthorizationTrackingStatus();
            Debug.Log($"[Ads] ATT status at launch: {status}");

            if (status == ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED)
            {
                // Fire-and-forget: iOS surfaces the system dialog. LevelPlay's
                // Init runs immediately after AdManager.Bootstrap AfterSceneLoad;
                // even if the user hasn't tapped yet, LevelPlay handles late
                // authorization updates internally on subsequent ad requests.
                ATTrackingStatusBinding.RequestAuthorizationTracking();
            }
#endif
      }
    }
  }