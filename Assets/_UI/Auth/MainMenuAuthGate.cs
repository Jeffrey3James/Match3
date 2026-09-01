using JadedBelles.Networking;
using UnityEngine;

namespace JadedBelles.UI
{
    /// <summary>
    /// Runs once when MainMenu loads.
    ///
    /// Flow:
    ///  - If a JadedBelles session token exists (<see cref="TokenStore.HasSession"/>) →
    ///    pull the player's saves and let the menu render normally. No overlay.
    ///  - Otherwise → spawn the reusable <see cref="AuthPanelController"/> as a modal
    ///    overlay. The panel stays up until the player either signs in / registers
    ///    (session token gets stored + saves pull) OR taps "Play as guest".
    ///  - A "guest" choice is remembered in PlayerPrefs so we don't re-prompt on every
    ///    scene load. Clearing the token via logout resets the flag so the panel comes
    ///    back next time.
    ///
    /// Zero scene wiring: <see cref="MainMenuUI"/> calls <see cref="Ensure"/> once in
    /// <c>Start</c>, this creates its own GameObject.
    /// </summary>
    public sealed class MainMenuAuthGate : MonoBehaviour
    {
        private const string GuestPrefKey = "jb_played_as_guest";

        private static MainMenuAuthGate _instance;
        private AuthPanelController _panel;

        /// <summary>Idempotent bootstrap. Safe to call from <c>MainMenuUI.Start()</c>.</summary>
        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("MainMenuAuthGate");
            _instance = go.AddComponent<MainMenuAuthGate>();
        }

        private void Start()
        {
            // Case 1: signed in already. Pull remote saves and get out of the way.
            if (TokenStore.HasSession())
            {
                TryPullPlayerData();
                return;
            }

            // Case 2: previously chose guest. Respect that; don't nag.
            if (PlayerPrefs.GetInt(GuestPrefKey, 0) == 1)
            {
                Debug.Log("[MainMenuAuthGate] Continuing as guest (remembered choice).");
                return;
            }

            // Case 3: no session, no guest choice yet → show the modal auth panel.
            ShowAuthPanel();
        }

        private void ShowAuthPanel()
        {
            _panel = AuthPanelController.Spawn(
                panelSettings: null,          // Spawn will build one in memory.
                hideWhenSignedIn: true,       // Vanish on successful login/register.
                pullPlayerDataOnLogin: true); // Controller triggers PullRemotePlayerData.

            if (_panel == null)
            {
                Debug.LogError("[MainMenuAuthGate] Could not spawn AuthPanel. Falling through as guest.");
                return;
            }

            _panel.OnLoggedIn.AddListener(HandleLoggedIn);
            _panel.OnGuestChosen.AddListener(HandleGuestChosen);
            _panel.OnLoggedOut.AddListener(HandleLoggedOut);
        }

        private void HandleLoggedIn()
        {
            // Successful login clears the guest flag so the user is treated as a real account from here on.
            PlayerPrefs.DeleteKey(GuestPrefKey);
            PlayerPrefs.Save();
            // AuthPanelController already called PullRemotePlayerData; nothing more to do.
        }

        private void HandleGuestChosen()
        {
            PlayerPrefs.SetInt(GuestPrefKey, 1);
            PlayerPrefs.Save();
            // Hide the panel — controller doesn't auto-hide on guest since some scenes may want it to stay.
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        private void HandleLoggedOut()
        {
            // If the user logs out from inside MainMenu, forget the guest flag so the panel re-prompts next time.
            PlayerPrefs.DeleteKey(GuestPrefKey);
            PlayerPrefs.Save();
        }

        private void TryPullPlayerData()
        {
            try
            {
                if (PlayerDataManager.instance != null)
                    PlayerDataManager.instance.PullRemotePlayerData();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[MainMenuAuthGate] PullRemotePlayerData failed: " + ex.Message);
            }
        }
    }
}
