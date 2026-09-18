using System;
using JadedBelles.Networking;
using UnityEngine;

/// <summary>
/// Single source of truth for "is this player signed in?".
///
/// Owns session restoration so both the loading screen and the login panel read the same
/// answer instead of each doing their own token check. The loading screen runs
/// <see cref="Restore"/> as a load step; the login panel only ever reacts to the result.
///
/// Persistent login lives here: TokenStore keeps access + refresh tokens in PlayerPrefs
/// across launches, and JadedBellesApiClient transparently refreshes on a 401 and retries
/// once. Restore() therefore succeeds silently for a returning player whose access token
/// merely expired.
/// </summary>
public static class SessionService
{
    public enum SessionState
    {
        /// <summary>Restore hasn't run yet this launch.</summary>
        Unknown,
        /// <summary>A valid account session is active. Do not show the login panel.</summary>
        SignedIn,
        /// <summary>Player previously chose guest. Do not show the login panel.</summary>
        Guest,
        /// <summary>No usable session. The login panel should be shown.</summary>
        NeedsAuth,
        /// <summary>Tokens exist but the server was unreachable. Treated as signed in, offline.</summary>
        OfflineSignedIn
    }

    private const string GuestPrefKey = "jb_played_as_guest";

    /// <summary>Result of the most recent <see cref="Restore"/> call.</summary>
    public static SessionState State { get; private set; } = SessionState.Unknown;

    /// <summary>Display name or email of the restored user, when known.</summary>
    public static string CurrentUserName { get; private set; }

    /// <summary>True when the player should NOT be shown the login panel.</summary>
    public static bool IsResolved =>
        State == SessionState.SignedIn ||
        State == SessionState.Guest ||
        State == SessionState.OfflineSignedIn;

    /// <summary>True when a real account (not guest) is active.</summary>
    public static bool IsSignedIn =>
        State == SessionState.SignedIn || State == SessionState.OfflineSignedIn;

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public static event Action<SessionState> OnStateChanged;

    // ------------------------------------------------------------------
    // Restore
    // ------------------------------------------------------------------
    /// <summary>
    /// Resolves the player's session. Always invokes <paramref name="onResolved"/> exactly
    /// once, including on failure, so it's safe to use as a gated loading step.
    /// </summary>
    public static void Restore(Action<SessionState> onResolved)
    {
        // Guest choice short-circuits everything — no network call needed.
        if (!HasStoredSession() && HasChosenGuest())
        {
            Finish(SessionState.Guest, onResolved);
            return;
        }

        if (!HasStoredSession())
        {
            Finish(SessionState.NeedsAuth, onResolved);
            return;
        }

        // Tokens exist. HasSession() only proves the strings are present, not that they still
        // work, so verify against the API. A 401 triggers a silent refresh + retry inside the
        // client, meaning an expired access token resolves without bothering the player.
        JadedBellesApiClient.Instance.GetCurrentUser(
            onSuccess: response =>
            {
                if (response == null || !response.success)
                {
                    TokenStore.Clear();
                    Finish(SessionState.NeedsAuth, onResolved);
                    return;
                }

                CurrentUserName = response.data != null
                    ? (!string.IsNullOrEmpty(response.data.displayName)
                        ? response.data.displayName
                        : response.data.email)
                    : "player";

                // A live account supersedes any earlier guest choice.
                ClearGuestChoice();

                Debug.Log("[SessionService] Session restored for " + CurrentUserName);
                Finish(SessionState.SignedIn, onResolved);
            },
            onError: error =>
            {
                // Can't reach the server is NOT the same as being logged out. Keep the tokens
                // so a player without connectivity stays signed in and plays offline.
                if (IsLikelyNetworkFailure(error))
                {
                    Debug.LogWarning("[SessionService] API unreachable; keeping saved login. " + error);
                    Finish(SessionState.OfflineSignedIn, onResolved);
                    return;
                }

                Debug.Log("[SessionService] Stored session rejected: " + error);
                TokenStore.Clear();
                Finish(SessionState.NeedsAuth, onResolved);
            });
    }

    private static void Finish(SessionState state, Action<SessionState> onResolved)
    {
        State = state;
        OnStateChanged?.Invoke(state);
        onResolved?.Invoke(state);
    }

    // ------------------------------------------------------------------
    // State transitions driven by the login panel
    // ------------------------------------------------------------------
    /// <summary>Call after a successful login or sign up.</summary>
    public static void MarkSignedIn(string userName)
    {
        CurrentUserName = string.IsNullOrEmpty(userName) ? "player" : userName;
        ClearGuestChoice();
        State = SessionState.SignedIn;
        OnStateChanged?.Invoke(State);
    }

    /// <summary>Call when the player taps "Play as guest".</summary>
    public static void MarkGuest(bool remember = true)
    {
        if (remember)
        {
            PlayerPrefs.SetInt(GuestPrefKey, 1);
            PlayerPrefs.Save();
        }

        CurrentUserName = null;
        State = SessionState.Guest;
        OnStateChanged?.Invoke(State);
    }

    /// <summary>Call after logging out. Clears tokens and the guest flag.</summary>
    public static void MarkSignedOut()
    {
        TokenStore.Clear();
        ClearGuestChoice();
        CurrentUserName = null;
        State = SessionState.NeedsAuth;
        OnStateChanged?.Invoke(State);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    public static bool HasStoredSession() => TokenStore.HasSession();

    public static bool HasChosenGuest() => PlayerPrefs.GetInt(GuestPrefKey, 0) == 1;

    public static void ClearGuestChoice()
    {
        PlayerPrefs.DeleteKey(GuestPrefKey);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Distinguishes "couldn't reach the server" from "server said no". Only the latter
    /// should ever wipe a persistent login.
    /// </summary>
    public static bool IsLikelyNetworkFailure(string error)
    {
        if (string.IsNullOrEmpty(error)) return false;
        string e = error.ToLowerInvariant();
        return e.Contains("cannot connect")
            || e.Contains("connection")
            || e.Contains("timeout")
            || e.Contains("timed out")
            || e.Contains("unable to complete")
            || e.Contains("network")
            || e.Contains("dns")
            || e.Contains("host");
    }
}
