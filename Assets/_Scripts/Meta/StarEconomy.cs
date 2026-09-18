using UnityEngine;

namespace JadedBelles.Meta
{
    /// <summary>
    /// Static convenience facade over the star currency stored on <see cref="PlayerHandler"/>.
    /// Non-authoritative: every call goes through PlayerHandler so persistence and cloud sync
    /// stay in one place. UI code and gameplay code call these helpers instead of poking
    /// PlayerHandler.playerData directly so we can move the storage later without a rewrite.
    /// </summary>
    public static class StarEconomy
    {
        /// <summary>Star cost of every decorate task in the MVP. All slots share one price.</summary>
        public const int DecorateTaskCost = 1;

        /// <summary>Streak length at which the Butler's Gift signal fires exactly once.</summary>
        public const int ButlersGiftStreakThreshold = 3;

        public static int Balance
        {
            get
            {
                var ph = PlayerHandler.instance;
                return ph != null ? ph.GetStars() : 0;
            }
        }

        public static bool CanAfford(int cost)
        {
            return Balance >= cost;
        }

        public static void Award(int amount)
        {
            var ph = PlayerHandler.instance;
            if (ph == null)
            {
                Debug.LogWarning("StarEconomy.Award called before PlayerHandler is available.");
                return;
            }
            ph.AddStars(amount);
        }

        public static bool Spend(int amount)
        {
            var ph = PlayerHandler.instance;
            if (ph == null)
            {
                Debug.LogWarning("StarEconomy.Spend called before PlayerHandler is available.");
                return false;
            }
            return ph.SpendStars(amount);
        }
    }
}
