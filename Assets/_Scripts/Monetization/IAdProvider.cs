using System;

namespace Match3Game.Monetization
{
    /// <summary>
    /// Thin boundary between the game and whatever ad SDK is installed.
    /// AdManager owns eligibility rules (level gates, per-attempt caps);
    /// providers own SDK details. Keep SDK types out of game code.
    /// </summary>
    public interface IAdProvider
    {
        bool IsInitialized { get; }

        /// <param name="userId">Optional JadedBelles user id for the ad network's user segmentation. May be null for guests.</param>
        void Initialize(string userId);

        bool IsRewardedReady();
        void LoadRewarded();

        /// <summary>
        /// Shows a rewarded ad. Exactly one of the callbacks fires, exactly once:
        /// onReward only after the SDK's verified reward callback, onNoReward for
        /// close-without-reward, display failure, or SDK error.
        /// </summary>
        void ShowRewarded(Action onReward, Action<string> onNoReward);
    }

    /// <summary>
    /// Fail-closed provider used on WebGL, in the editor, when the LevelPlay
    /// package/define is absent, or when app keys are not configured.
    /// Never ready, never shows anything — the game simply hides ad offers.
    /// </summary>
    public sealed class NullAdProvider : IAdProvider
    {
        public bool IsInitialized => false;
        public void Initialize(string userId) { }
        public bool IsRewardedReady() => false;
        public void LoadRewarded() { }
        public void ShowRewarded(Action onReward, Action<string> onNoReward) =>
            onNoReward?.Invoke("ads_unavailable");
    }
}
