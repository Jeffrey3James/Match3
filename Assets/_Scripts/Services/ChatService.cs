using UnityEngine;

/// <summary>
/// Static chat façade. Today there is no chat backend wired into Xandria Gem
/// Adventure — this service exists so UI code (Team panel, Leaderboard bubble,
/// etc.) can check <see cref="IsEnabled"/> before spawning chat widgets, and
/// so that the moment a real chat backend lands there is one place to plug it
/// in without touching every caller.
///
/// Gating rule:
///   * <see cref="SettingsPanel.ChatEnabled"/> false  → chat features hidden.
///   * Guest session (not signed in)                  → chat features hidden.
///     (Server-side chat requires an account.)
///
/// Both conditions are ORed into <see cref="IsEnabled"/> so callers don't
/// have to remember either rule.
/// </summary>
public static class ChatService
{
    /// <summary>
    /// True when the player has chat ON in Settings AND is signed in.
    /// UI should hide the chat dock / composer when this is false.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            if (!SettingsPanel.ChatEnabled) return false;
            if (!SessionService.IsSignedIn) return false;
            return true;
        }
    }

    /// <summary>
    /// Guarded send. Silent no-op unless <see cref="IsEnabled"/> is true.
    /// TODO: replace body with a real POST to the chat backend once one exists.
    /// </summary>
    public static void SendMessage(string channelId, string body)
    {
        if (!IsEnabled) return;
        if (string.IsNullOrWhiteSpace(body)) return;

        Debug.Log($"[ChatService] (stub) Would send to '{channelId}': {body}");
    }
}
