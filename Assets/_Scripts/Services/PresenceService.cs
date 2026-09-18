using UnityEngine;

/// <summary>
/// Static presence façade. Broadcasts the player's "last seen" timestamp to
/// the JadedBelles backend when they foreground the app or leave a scene,
/// so friends / leaderboard rows can show recent activity.
///
/// Today this is a stub — no presence endpoint is wired in yet. When one
/// lands, swap the body of <see cref="MaybeBroadcastLastSeen"/> for the real
/// POST. The gating rules stay identical.
///
/// Gating rule:
///   * <see cref="SettingsPanel.LastSeenEnabled"/> false → do NOT broadcast.
///     (Player has opted out of showing last-seen to others.)
///   * Guest session (not signed in)                     → do NOT broadcast.
///     (There is no account to attach the timestamp to.)
/// </summary>
public static class PresenceService
{
    /// <summary>
    /// Fire-and-forget. Silent no-op unless the player is signed in AND has
    /// last-seen ON in Settings. Safe to call from OnApplicationPause,
    /// OnEnable, scene loads — anywhere the game wants to nudge the server.
    /// </summary>
    public static void MaybeBroadcastLastSeen()
    {
        if (!SettingsPanel.LastSeenEnabled) return;
        if (!SessionService.IsSignedIn) return;

        // TODO: POST /api/presence/lastSeen with the current timestamp.
        // Never surface failures — presence is best-effort telemetry, not
        // gameplay-critical. Log once at most on error.
        Debug.Log("[PresenceService] (stub) Would broadcast last-seen timestamp.");
    }
}
