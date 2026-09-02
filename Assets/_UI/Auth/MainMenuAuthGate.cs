using JadedBelles.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace JadedBelles.UI
{
    /// <summary>
    /// Runs once when MainMenu loads.
    ///
    /// Flow:
    ///  - If a JadedBelles session token exists (<see cref="TokenStore.HasSession"/>) →
    ///    pull the player's saves and let the menu render normally. No overlay.
    ///  - Otherwise → attach a <see cref="UIDocument"/> + <see cref="AuthPanelController"/>
    ///    to this same GameObject. The panel stays up until the player either signs in /
    ///    registers OR taps "Play as guest".
    ///  - A "guest" choice is remembered in PlayerPrefs so we don't re-prompt on every
    ///    scene load. Clearing the token via logout resets the flag so the panel comes
    ///    back next time.
    ///
    /// Zero scene wiring: <see cref="MainMenuUI"/> calls <see cref="Ensure"/> once in
    /// <c>Start</c>. Everything ends up on one runtime GameObject.
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

            // Case 3: no session, no guest choice yet → mount the AuthPanel on this GameObject.
            AttachAuthPanel();
        }

        /// <summary>
        /// Adds UIDocument + AuthPanelController to THIS gate GameObject so we don't end up
        /// with a second "AuthPanel (runtime)" object that duplicates the controller.
        /// </summary>
        private void AttachAuthPanel()
        {
            var uxml = Resources.Load<VisualTreeAsset>("UI/Auth/AuthPanel");
            if (uxml == null)
            {
                var raw = Resources.Load("UI/Auth/AuthPanel");
                if (raw != null)
                    Debug.LogError($"[MainMenuAuthGate] Resources/UI/Auth/AuthPanel exists but was imported as {raw.GetType().FullName}, not VisualTreeAsset. Reimport the .uxml in Unity.");
                else
                    Debug.LogError("[MainMenuAuthGate] Resources/UI/Auth/AuthPanel not found. Expected Assets/Resources/UI/Auth/AuthPanel.uxml.");
                return;
            }

            // Build inactive so the components' OnEnable doesn't run mid-configuration.
            gameObject.SetActive(false);

            var doc = gameObject.AddComponent<UIDocument>();

            // In-memory PanelSettings so no asset authoring is needed. Fine for a modal overlay.
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "AuthPanel_RuntimePanelSettings";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1080, 1920);
            panelSettings.match = 0.5f;

            doc.panelSettings = panelSettings;
            doc.visualTreeAsset = uxml;
            doc.sortingOrder = 100; // Render above the rest of the MainMenu.

            _panel = gameObject.AddComponent<AuthPanelController>();
            _panel.OnLoggedIn.AddListener(HandleLoggedIn);
            _panel.OnGuestChosen.AddListener(HandleGuestChosen);
            _panel.OnLoggedOut.AddListener(HandleLoggedOut);

            gameObject.SetActive(true);
        }

        private void HandleLoggedIn()
        {
            // Successful login clears the guest flag so the user is treated as a real account from here on.
            PlayerPrefs.DeleteKey(GuestPrefKey);
            PlayerPrefs.Save();
            // AuthPanelController already triggers PullRemotePlayerData; nothing more to do.
        }

        private void HandleGuestChosen()
        {
            PlayerPrefs.SetInt(GuestPrefKey, 1);
            PlayerPrefs.Save();
            HideOverlay();
        }

        private void HandleLoggedOut()
        {
            // If the user logs out from inside MainMenu, forget the guest flag so the panel re-prompts next time.
            PlayerPrefs.DeleteKey(GuestPrefKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Hides the modal without destroying the gate. We disable the UIDocument so the visual
        /// tree is removed from the panel; the gate itself stays alive to remember state.
        /// </summary>
        private void HideOverlay()
        {
            var doc = GetComponent<UIDocument>();
            if (doc != null) doc.enabled = false;
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
