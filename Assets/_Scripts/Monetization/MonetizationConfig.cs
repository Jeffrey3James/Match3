// -----------------------------------------------------------------------------
// MonetizationConfig
//
// Single, static source of truth for whether we run interstitial and rewarded
// ads. Deliberately const so the compiler can strip the guarded call sites in
// release builds — but the underlying ad code (AdManager, LevelPlayProvider)
// still exists. Flip the const, rebuild, ads come back.
//
// For the Sep 11 playtest we ship:
//   INTERSTITIALS_ENABLED = false  (no forced ads inside or between levels)
//   REWARDED_ADS_ENABLED  = true   (extra-life / extra-moves offers still work)
//
// Also holds the SelectedPreLevelBoosters bag written by PreLevelBoosterPanel
// and read by the board on GameScene start. A static bag avoids depending on
// Match3.cs (owned by agent A) and survives the scene load between the
// booster panel and the board.
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace Match3Game.Monetization
{
    /// <summary>
    /// Global monetization feature flags. Both fields are compile-time constants
    /// so `if (!MonetizationConfig.INTERSTITIALS_ENABLED) return;` collapses to
    /// dead code the JIT will elide entirely in the disabled configuration.
    /// </summary>
    public static class MonetizationConfig
    {
        /// <summary>
        /// Master switch for interstitial ads. Sep 11 playtest ships with this
        /// OFF so the run experience is uninterrupted; the code paths remain
        /// so we can flip it back after the playtest.
        /// </summary>
        public const bool INTERSTITIALS_ENABLED = false;

        /// <summary>
        /// Master switch for rewarded ads (extra moves, extra life). Stays ON
        /// during the playtest — those are opt-in offers, not forced.
        /// </summary>
        public const bool REWARDED_ADS_ENABLED = true;

        // ── PlayerPrefs key used to hand the pre-level booster selection to
        //    the board when it loads. Comma-separated ids: "rocket,tnt,lightball".
        //    Static-field-only agents (like us) can't wire the bag directly into
        //    Match3.cs, so the prefs key is the interop point.
        /// <summary>PlayerPrefs key: comma-separated booster ids to auto-apply on level start.</summary>
        public const string PendingBoostersPrefsKey = "PendingBoosters";

        /// <summary>
        /// In-memory mirror of the PlayerPrefs bag. Populated by
        /// PreLevelBoosterPanel just before it loads GameScene. Read by anyone
        /// on the game side (Match3, InRunBoosterBar) that wants the selection
        /// without touching PlayerPrefs.
        /// </summary>
        public static readonly HashSet<string> SelectedPreLevelBoosters = new HashSet<string>();
    }
}
