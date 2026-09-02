using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Match3Game.Monetization
{
    /// <summary>
    /// The single authority on ads: owns the provider, eligibility rules, and
    /// per-attempt caps. Self-bootstraps like JadedBellesApiClient — no scene
    /// or prefab setup needed. UI asks <see cref="IsExtraMovesOfferAvailable"/>
    /// and calls <see cref="ShowRewardedExtraMoves"/>; everything else is internal.
    ///
    /// Fail-closed by design: on WebGL, in the editor, without the LevelPlay
    /// package/define, or with empty keys below, offers simply never appear
    /// and the game plays exactly as before.
    /// </summary>
    public sealed class AdManager : MonoBehaviour
    {
        // ── LevelPlay dashboard values ─────────────────────────────────────
        // Paste from platform.ironsrc.com after creating the app entries:
        //   app key:            LevelPlay > Apps > (your app)
        //   rewarded ad unit:   LevelPlay > Ad Units > Rewarded
        // Leave empty to keep ads disabled for that platform.
        private const string AndroidAppKey = "27e5b89cd";
        private const string AndroidRewardedAdUnitId = "q4qkd0m3uk3kh2mo";

        private const string IosAppKey = "27e5bc355";
        private const string IosRewardedAdUnitId = "466fs7ggxcnvhbg6";
        // ───────────────────────────────────────────────────────────────────

        /// <summary>Moves granted per completed rewarded view.</summary>
        public const int ExtraMovesPerAd = 5;

        // Release requirements from docs/AD-MONETIZATION-PLAN.md §4.
        private const int MaxExtraMovesGrantsPerAttempt = 2;
        private const int AdFreeDisplayedLevels = 3;

        public static AdManager Instance { get; private set; }

        private IAdProvider provider = new NullAdProvider();
        private int extraMovesGrantsThisAttempt;
        private bool showInFlight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("AdManager");
            go.AddComponent<AdManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
            CreateProvider();
            provider.Initialize(null);
        }

        private void OnDestroy()
        {
            if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void CreateProvider()
        {
#if LEVELPLAY_ENABLED && UNITY_ANDROID && !UNITY_EDITOR
            if (!string.IsNullOrEmpty(AndroidAppKey) && !string.IsNullOrEmpty(AndroidRewardedAdUnitId))
                provider = new LevelPlayProvider(this, AndroidAppKey, AndroidRewardedAdUnitId);
#elif LEVELPLAY_ENABLED && UNITY_IOS && !UNITY_EDITOR
            if (!string.IsNullOrEmpty(IosAppKey) && !string.IsNullOrEmpty(IosRewardedAdUnitId))
                provider = new LevelPlayProvider(this, IosAppKey, IosRewardedAdUnitId);
#endif
            // Otherwise: NullAdProvider — WebGL, editor, or unconfigured. Ads off.
        }

        // Every scene load is a fresh board attempt (retry reloads the scene),
        // so the per-attempt rewarded cap resets here.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) =>
            extraMovesGrantsThisAttempt = 0;

        /// <summary>
        /// True when the fail-panel "watch ad" button should be visible:
        /// past the ad-free levels, under the per-attempt cap, and an ad is
        /// actually loaded. UI must hide the offer when this is false — never
        /// show a button that can't pay out.
        /// </summary>
        public bool IsExtraMovesOfferAvailable() =>
            MonetizationConfig.REWARDED_ADS_ENABLED
            && !showInFlight
            && DisplayedPlayerLevel() > AdFreeDisplayedLevels
            && extraMovesGrantsThisAttempt < MaxExtraMovesGrantsPerAttempt
            && provider.IsRewardedReady();

        /// <summary>
        /// True when a "watch ad for +1 life" offer should be visible: rewarded
        /// ads are enabled, no other show is in flight, and an ad is loaded.
        /// The per-attempt cap deliberately does NOT apply — this is a menu
        /// offer, not an in-run rescue, so the cadence is naturally limited by
        /// the player deciding to press the button after losing a life.
        /// </summary>
        public bool IsRewardedLifeOfferAvailable() =>
            MonetizationConfig.REWARDED_ADS_ENABLED
            && !showInFlight
            && provider.IsRewardedReady();

        /// <summary>
        /// Shows the rewarded ad. onGranted fires only after the SDK verifies
        /// the reward (grant +<see cref="ExtraMovesPerAd"/> moves there);
        /// onNotGranted fires for decline/close/error and must grant nothing.
        /// </summary>
        public void ShowRewardedExtraMoves(Action onGranted, Action onNotGranted)
        {
            if (!IsExtraMovesOfferAvailable())
            {
                onNotGranted?.Invoke();
                return;
            }

            showInFlight = true;
            provider.ShowRewarded(
                onReward: () =>
                {
                    showInFlight = false;
                    extraMovesGrantsThisAttempt++;
                    Debug.Log($"[Ads] Extra-moves reward granted ({extraMovesGrantsThisAttempt}/{MaxExtraMovesGrantsPerAttempt} this attempt)");
                    onGranted?.Invoke();
                },
                onNoReward: reason =>
                {
                    showInFlight = false;
                    Debug.Log($"[Ads] No reward: {reason}");
                    onNotGranted?.Invoke();
                });
        }

        /// <summary>
        /// Rewarded-ad offer that grants +1 life on completion. Wired to the
        /// lives popup (item 16 in the gap doc). Grants the life via
        /// <see cref="PlayerHandler.AddALifeToPlayer"/> exactly once per view;
        /// declines/closes leave the player alone.
        /// </summary>
        public void ShowRewardedExtraLife(Action onGranted, Action onNotGranted)
        {
            if (!IsRewardedLifeOfferAvailable())
            {
                onNotGranted?.Invoke();
                return;
            }

            showInFlight = true;
            provider.ShowRewarded(
                onReward: () =>
                {
                    showInFlight = false;
                    if (PlayerHandler.instance != null)
                        PlayerHandler.instance.AddALifeToPlayer();
                    Debug.Log("[Ads] Extra-life reward granted");
                    onGranted?.Invoke();
                },
                onNoReward: reason =>
                {
                    showInFlight = false;
                    Debug.Log($"[Ads] No reward (life): {reason}");
                    onNotGranted?.Invoke();
                });
        }

        // ── Interstitials ──────────────────────────────────────────────────
        // No interstitial ad units are configured today, but keep the entry
        // point so future callers (level-load, main-menu return, etc.) route
        // through the same feature gate. Sep 11 playtest ships with
        // INTERSTITIALS_ENABLED=false, so these calls are no-ops.

        /// <summary>
        /// Show an interstitial ad if the feature flag allows it and the
        /// provider has one ready. Deliberately silent — callers must be
        /// tolerant of "no ad shown" (nothing depends on interstitials
        /// completing to progress gameplay).
        /// </summary>
        public void ShowInterstitial()
        {
            // Gate #1: master feature flag. Sep 11 playtest ships with this off.
            if (!MonetizationConfig.INTERSTITIALS_ENABLED) return;
            // Gate #2: don't stack on top of a rewarded ad.
            if (showInFlight) return;

            // No interstitial ad unit is configured on IAdProvider today — the
            // provider surface is rewarded-only. When the interstitial provider
            // is added, dispatch through it here. Deliberately not removed so
            // future call sites keep routing through the same feature gate.
            Debug.Log("[Ads] Interstitial requested; no provider wired.");
        }

        // playerLevel is a zero-based index; players see level 1 first.
        private static int DisplayedPlayerLevel()
        {
            var handler = PlayerHandler.instance;
            if (handler == null || handler.playerData == null) return 1;
            return handler.playerData.playerLevel + 1;
        }

        /// <summary>Coroutine scheduling for providers (they aren't MonoBehaviours).</summary>
        internal void RunDelayed(float seconds, Action action) =>
            StartCoroutine(DelayedRoutine(seconds, action));

        private static IEnumerator DelayedRoutine(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            action?.Invoke();
        }
    }
}
