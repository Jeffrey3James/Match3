// Unity LevelPlay (ironSource) adapter — compiled ONLY when the Ads Mediation
// package is installed AND the LEVELPLAY_ENABLED scripting define is set for
// the Android/iOS player settings. Without the define this file compiles to
// nothing, so the project builds cleanly before the SDK is installed.
//
// Setup (once, in the editor):
//   1. Window > Package Manager > search "Ads Mediation" > Install
//      (package name: com.unity.services.levelplay, 9.5.x at time of writing)
//   2. Project Settings > Player > Android AND iOS > Scripting Define Symbols:
//      add LEVELPLAY_ENABLED
//   3. Paste the app keys + rewarded ad unit ids from the LevelPlay dashboard
//      into AdManager.cs.
#if LEVELPLAY_ENABLED && (UNITY_ANDROID || UNITY_IOS)
using System;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace Match3Game.Monetization
{
    public sealed class LevelPlayProvider : IAdProvider
    {
        // OnAdRewarded and OnAdClosed are asynchronous and can arrive in either
        // order, so after a rewardless close we wait briefly before declaring
        // failure (LevelPlay docs require granting late rewards).
        private const float LateRewardGraceSeconds = 2f;

        private readonly string appKey;
        private readonly string rewardedAdUnitId;
        private readonly AdManager host; // for coroutine scheduling only

        private LevelPlayRewardedAd rewardedAd;
        private bool initialized;

        private Action pendingReward;
        private Action<string> pendingNoReward;
        private bool rewardGranted;

        public LevelPlayProvider(AdManager host, string appKey, string rewardedAdUnitId)
        {
            this.host = host;
            this.appKey = appKey;
            this.rewardedAdUnitId = rewardedAdUnitId;
        }

        public bool IsInitialized => initialized;

        public void Initialize(string userId)
        {
            LevelPlay.OnInitSuccess += OnInitSuccess;
            LevelPlay.OnInitFailed += error =>
                Debug.LogWarning($"[Ads] LevelPlay init failed: {error}");

            // userId lets LevelPlay attribute rewards per player; null is fine.
            LevelPlay.Init(appKey, userId);
        }

        private void OnInitSuccess(LevelPlayConfiguration configuration)
        {
            initialized = true;

            // Ad objects must only be created after OnInitSuccess (SDK rule).
            rewardedAd = new LevelPlayRewardedAd(rewardedAdUnitId);

            rewardedAd.OnAdLoadFailed += error =>
            {
                Debug.LogWarning($"[Ads] Rewarded load failed: {error}");
                // Back off, then retry so an offer can appear later in the session.
                host.RunDelayed(30f, LoadRewarded);
            };

            rewardedAd.OnAdDisplayFailed += (info, error) => FinishNoReward($"display_failed: {error}");

            rewardedAd.OnAdRewarded += (info, reward) =>
            {
                rewardGranted = true;
                var callback = pendingReward;
                pendingReward = null;
                pendingNoReward = null;
                callback?.Invoke();
            };

            rewardedAd.OnAdClosed += info =>
            {
                if (!rewardGranted && pendingNoReward != null)
                {
                    // Give a late OnAdRewarded a moment to arrive before failing.
                    host.RunDelayed(LateRewardGraceSeconds, () =>
                    {
                        if (!rewardGranted) FinishNoReward("closed_without_reward");
                    });
                }
                LoadRewarded(); // always preload the next one
            };

            LoadRewarded();
        }

        public bool IsRewardedReady() => initialized && rewardedAd != null && rewardedAd.IsAdReady();

        public void LoadRewarded()
        {
            if (initialized && rewardedAd != null && !rewardedAd.IsAdReady())
                rewardedAd.LoadAd();
        }

        public void ShowRewarded(Action onReward, Action<string> onNoReward)
        {
            // Rewarded ads are opt-in offers, gated by the same master switch
            // as interstitials so a full ad shutoff is one flag flip.
            if (!MonetizationConfig.REWARDED_ADS_ENABLED)
            {
                onNoReward?.Invoke("rewarded_disabled");
                return;
            }

            if (!IsRewardedReady())
            {
                onNoReward?.Invoke("not_ready");
                return;
            }

            rewardGranted = false;
            pendingReward = onReward;
            pendingNoReward = onNoReward;
            rewardedAd.ShowAd();
        }

        // ── Interstitials (disabled for Sep 11 playtest) ─────────────────────
        // Interstitial support is not wired to a LevelPlay ad unit yet, but the
        // gated hook lives here so when it lands, the feature flag guards it.
        // Do NOT remove: the whole point of the playtest gating is that we can
        // flip MonetizationConfig.INTERSTITIALS_ENABLED back on later.
        internal void ShowInterstitialGated()
        {
            if (!MonetizationConfig.INTERSTITIALS_ENABLED) return;
            // TODO: create LevelPlayInterstitialAd, load in OnInitSuccess, show here.
            Debug.Log("[Ads] LevelPlayProvider.ShowInterstitialGated: not implemented");
        }

        private void FinishNoReward(string reason)
        {
            var callback = pendingNoReward;
            pendingReward = null;
            pendingNoReward = null;
            callback?.Invoke(reason);
        }
    }
}
#endif
