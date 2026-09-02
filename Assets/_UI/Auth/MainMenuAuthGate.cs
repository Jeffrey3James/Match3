using JadedBelles.Networking;
using UnityEngine;
using UnityEngine.UIElements;

// ------------------------------------------------------------------
// KNOWN ISSUE (JadedBelles ecosystem, 2026-09-01)
// ------------------------------------------------------------------
// Fully-runtime auth panel wiring (Resources.Load + AddComponent<UIDocument>
// + AddComponent<AuthPanelController>) has been flaky across the JadedBelles
// game ecosystem in this project: UIDocument's rootVisualElement does not
// consistently contain the cloned visual tree by the time our controller
// binds, even with a one-frame yield in OnEnable. The persistent workaround
// is to place ONE pre-configured MainMenuAuthGate GameObject in each scene
// that needs auth (MainMenu today) and DRAG the UXML/USS/PanelSettings
// assets onto its Inspector slots. Attach-at-runtime remains supported as
// a fallback, but Inspector references are the reliable path.
// ------------------------------------------------------------------

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

        [Header("Assets (drag in Inspector — reliable path)")]
        [Tooltip("AuthPanel.uxml. Drag Assets/Resources/UI/Auth/AuthPanel.uxml here.")]
        [SerializeField] private VisualTreeAsset _uxmlAsset;
        [Tooltip("AuthPanel.uss. Drag Assets/Resources/UI/Auth/AuthPanel.uss here.")]
        [SerializeField] private StyleSheet _ussAsset;
        [Tooltip("Optional Panel Settings asset. If empty, one is built in memory at runtime.")]
        [SerializeField] private PanelSettings _panelSettings;

        private static MainMenuAuthGate _instance;
        private AuthPanelController _panel;

        /// <summary>
        /// Idempotent bootstrap. Only creates a runtime gate if no scene-placed gate exists.
        /// If the MainMenu scene contains a pre-configured MainMenuAuthGate (with UXML/USS/
        /// PanelSettings dragged in the Inspector), <see cref="MainMenuUI"/> should NOT call this
        /// — or, if it does, this call becomes a no-op.
        /// </summary>
        public static void Ensure()
        {
            if (_instance != null) return;

            // Prefer a scene-placed gate if the developer dragged one in.
            var existing = FindObjectOfType<MainMenuAuthGate>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var go = new GameObject("MainMenuAuthGate");
            _instance = go.AddComponent<MainMenuAuthGate>();
        }

        private void Awake()
        {
            // If a scene-placed gate wakes up first, register it as the singleton.
            if (_instance == null) _instance = this;
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
            // Resolve UXML: Inspector reference first, then Resources fallback.
            var uxml = _uxmlAsset != null ? _uxmlAsset : Resources.Load<VisualTreeAsset>("UI/Auth/AuthPanel");
            if (uxml == null)
            {
                var raw = Resources.Load("UI/Auth/AuthPanel");
                if (raw != null)
                    Debug.LogError($"[MainMenuAuthGate] Resources/UI/Auth/AuthPanel exists but was imported as {raw.GetType().FullName}, not VisualTreeAsset. Drag the .uxml onto the Inspector or reimport it.");
                else
                    Debug.LogError("[MainMenuAuthGate] No UXML assigned in Inspector and Resources/UI/Auth/AuthPanel not found.");
                return;
            }

            // Reuse an existing UIDocument on this GameObject if present (scene-placed gate);
            // otherwise add one for the pure-runtime path.
            var doc = GetComponent<UIDocument>();
            bool addedDoc = false;
            if (doc == null)
            {
                gameObject.SetActive(false);
                doc = gameObject.AddComponent<UIDocument>();
                addedDoc = true;
            }

            // PanelSettings: Inspector first, otherwise build one in memory.
            var panelSettings = _panelSettings;
            if (panelSettings == null && doc.panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.name = "AuthPanel_RuntimePanelSettings";
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1080, 1920);
                panelSettings.match = 0.5f;
            }
            if (panelSettings != null) doc.panelSettings = panelSettings;

            if (doc.visualTreeAsset == null) doc.visualTreeAsset = uxml;
            doc.sortingOrder = 100; // Render above the rest of the MainMenu.

            _panel = GetComponent<AuthPanelController>();
            if (_panel == null) _panel = gameObject.AddComponent<AuthPanelController>();

            _panel.SetAssets(_uxmlAsset != null ? _uxmlAsset : uxml, _ussAsset);
            _panel.OnLoggedIn.AddListener(HandleLoggedIn);
            _panel.OnGuestChosen.AddListener(HandleGuestChosen);
            _panel.OnLoggedOut.AddListener(HandleLoggedOut);

            if (addedDoc) gameObject.SetActive(true);
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
