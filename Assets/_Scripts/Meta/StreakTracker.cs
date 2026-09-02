using UnityEngine;

namespace JadedBelles.Meta
{
    /// <summary>
    /// Optional read-only observer over <see cref="PlayerHandler"/>'s streak state.
    /// PlayerHandler itself subscribes to onLevelCompleted / onLevelFailed and mutates the
    /// streak; this class exists so UI (character companion, HUD, decorate panel) can pull a
    /// single number without knowing about PlayerHandler's larger surface area, and so that
    /// tests or debug tooling can be pointed at a small dedicated object.
    ///
    /// Attach nowhere in the shipped scenes unless you want to expose it in the Inspector for
    /// debugging. All useful members are static.
    /// </summary>
    public sealed class StreakTracker : MonoBehaviour
    {
        /// <summary>Current win streak. Zero when no PlayerHandler is available.</summary>
        public static int Current
        {
            get
            {
                var ph = PlayerHandler.instance;
                return ph != null ? ph.GetWinStreak() : 0;
            }
        }

        /// <summary>True when the next level start should place a free striped gem (Butler's Gift).</summary>
        public static bool ButlersGiftIsPending
        {
            get
            {
                // Non-consuming peek would require another PlayerHandler method; instead we
                // simply report on threshold match so external UIs can preview the banner.
                // The real one-shot consumption lives on PlayerHandler.ConsumeButlersGift().
                var ph = PlayerHandler.instance;
                return ph != null && ph.GetWinStreak() >= StarEconomy.ButlersGiftStreakThreshold;
            }
        }

        /// <summary>Convenience: forwards to <see cref="PlayerHandler.ConsumeButlersGift"/>.</summary>
        public static bool ConsumeButlersGift()
        {
            var ph = PlayerHandler.instance;
            return ph != null && ph.ConsumeButlersGift();
        }
    }
}
