using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires session validation into the loading screen.
///
/// Boot order:
///   1. Loading screen appears, breathing, bar at zero.
///   2. Session validation runs as a gated load step. Player data and level catalog
///      load as their own steps.
///   3. The bar crawls up at its constant speed as each step confirms.
///   4. Screen dismisses only when every step is done AND the bar visually reached 100%.
///   5. THEN, and only then, the login panel is shown — and only if the session
///      actually needs auth.
///
/// A returning player with a valid (or merely expired-but-refreshable) token never sees
/// the login panel at all. It stays deactivated the whole time, so nothing else in the
/// UI has to know or care whether auth happened.
///
/// This replaces the old AuthManager, which used a fixed 3-second logo timer and cleared
/// the player's tokens whenever a refresh failed — which logged people out just for being
/// offline. SessionService now distinguishes "server said no" from "couldn't reach the
/// server" and only clears tokens for the former.
///
/// SETUP:
///   - Put this on a boot GameObject in the first scene (AuthSplashScreen).
///   - Drag in the LoadingScreen and the LoginPanel.
///   - Make sure the LoginPanel GameObject starts DISABLED in the scene.
///   - Set Next Scene to the scene to load once the player is resolved.
/// </summary>
public class SessionBootstrap : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The loading screen to drive. Falls back to LoadingScreen.Instance.")]
    [SerializeField] private LoadingScreen loadingScreen;

    [Tooltip("The login panel. Leave its GameObject DISABLED in the scene — this " +
             "component activates it only when auth is actually required.")]
    [SerializeField] private LoginPanel loginPanel;

    [Header("Load Steps")]
    [Tooltip("Wait for the level catalog before dismissing the loading screen.")]
    [SerializeField] private bool waitForLevelCatalog = true;

    [Tooltip("Wait for remote player data before dismissing the loading screen. " +
             "Skipped automatically when the player is a guest.")]
    [SerializeField] private bool waitForPlayerData = true;

    [Tooltip("Seconds to wait for a step before giving up and letting the game continue.")]
    [SerializeField, Min(1f)] private float stepTimeoutSeconds = 15f;

    [Header("Scene Flow")]
    [Tooltip("Scene to load once the player is signed in or has chosen guest. Leave empty " +
             "to stay in the current scene (e.g. if this lives on the MainMenu).")]
    [SerializeField] private string nextScene = "MainMenu";

    private const string StepSession = "session";
    private const string StepPlayerData = "playerData";
    private const string StepLevels = "levels";

    private bool _sessionResolved;
    private bool _sceneLoadStarted;

    private void Start()
    {
        if (loadingScreen == null) loadingScreen = LoadingScreen.Instance;

        // Login panel must not be visible during loading.
        if (loginPanel != null) loginPanel.Hide();

        if (loadingScreen == null)
        {
            Debug.LogWarning("[SessionBootstrap] No LoadingScreen found. Running auth without a loading screen.");
            StartCoroutine(BootWithoutLoadingScreen());
            return;
        }

        StartCoroutine(BootRoutine());
    }

    private IEnumerator BootRoutine()
    {
        loadingScreen.Show("Connecting...");

        // Register everything up front so the denominator is stable and the bar
        // doesn't recalculate its scale mid-fill.
        loadingScreen.RegisterStep(StepSession);
        if (waitForLevelCatalog) loadingScreen.RegisterStep(StepLevels);
        if (waitForPlayerData) loadingScreen.RegisterStep(StepPlayerData);

        // --- Step 1: session ---
        _sessionResolved = false;
        SessionService.Restore(state =>
        {
            _sessionResolved = true;
            Debug.Log("[SessionBootstrap] Session resolved: " + state);
        });

        yield return WaitForFlagOrTimeout(() => _sessionResolved, "session");

        string status = SessionService.IsSignedIn
            ? "Welcome back" + (string.IsNullOrEmpty(SessionService.CurrentUserName)
                ? "." : ", " + SessionService.CurrentUserName + ".")
            : "Ready.";
        loadingScreen.CompleteStep(StepSession, status);

        // --- Step 2: level catalog ---
        if (waitForLevelCatalog)
        {
            yield return LoadLevelCatalog();
            loadingScreen.CompleteStep(StepLevels, "Levels loaded.");
        }

        // --- Step 3: player data ---
        if (waitForPlayerData)
        {
            if (SessionService.IsSignedIn)
            {
                yield return LoadPlayerData();
                loadingScreen.CompleteStep(StepPlayerData, "Progress synced.");
            }
            else
            {
                // Guests have no remote save. Complete immediately so the bar still fills,
                // but the constant-speed gate keeps it from snapping to the top.
                loadingScreen.CompleteStep(StepPlayerData, "Playing offline.");
            }
        }

        // The loading screen dismisses itself once the bar visually catches up.
        // Only after that do we decide whether the login panel is needed.
        bool dismissed = false;
        loadingScreen.OnLoadingComplete += () => dismissed = true;

        float waited = 0f;
        while (!dismissed && waited < stepTimeoutSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        // --- Resolved players go straight through; everyone else stops at the login panel ---
        if (SessionService.IsResolved)
        {
            Debug.Log("[SessionBootstrap] Session resolved (" + SessionService.State +
                      "). Login panel stays hidden.");
            if (loginPanel != null) loginPanel.Hide();
            GoToNextScene();
            yield break;
        }

        if (loginPanel == null)
        {
            Debug.LogWarning("[SessionBootstrap] Auth required but no LoginPanel is assigned. " +
                             "Continuing as an unauthenticated player.");
            GoToNextScene();
            yield break;
        }

        Debug.Log("[SessionBootstrap] Auth required. Showing login panel.");
        loginPanel.Show();

        // Hold here until the player signs in, signs up, or picks guest. LoginPanel routes all
        // three through SessionService, so we just watch the state rather than wiring callbacks
        // into the panel.
        while (!SessionService.IsResolved)
            yield return null;

        Debug.Log("[SessionBootstrap] Player resolved as " + SessionService.State + ". Continuing.");
        loginPanel.Hide();
        GoToNextScene();
    }

    /// <summary>
    /// Degraded path for a scene with no LoadingScreen assigned. Same auth outcome, just no
    /// visuals — so a missing reference can't hard-block the boot.
    /// </summary>
    private IEnumerator BootWithoutLoadingScreen()
    {
        _sessionResolved = false;
        SessionService.Restore(_ => _sessionResolved = true);
        yield return WaitForFlagOrTimeout(() => _sessionResolved, "session");

        if (SessionService.IsResolved || loginPanel == null)
        {
            if (loginPanel != null) loginPanel.Hide();
            GoToNextScene();
            yield break;
        }

        loginPanel.Show();
        while (!SessionService.IsResolved)
            yield return null;

        loginPanel.Hide();
        GoToNextScene();
    }

    private void GoToNextScene()
    {
        if (string.IsNullOrWhiteSpace(nextScene))
        {
            // No scene change wanted — this bootstrap is running in the scene it belongs to.
            return;
        }

        if (_sceneLoadStarted) return;
        _sceneLoadStarted = true;

        Debug.Log("[SessionBootstrap] Loading scene: " + nextScene);
        SceneManager.LoadScene(nextScene);
    }

    // ------------------------------------------------------------------
    // Individual load steps
    // ------------------------------------------------------------------
    private IEnumerator LoadLevelCatalog()
    {
        LevelHandler handler = FindObjectOfType<LevelHandler>();
        if (handler == null)
        {
            Debug.LogWarning("[SessionBootstrap] No LevelHandler in scene; skipping level step.");
            yield break;
        }

        yield return WaitForFlagOrTimeout(() => handler.LevelsReady, "level catalog");
    }

    private IEnumerator LoadPlayerData()
    {
        if (PlayerDataManager.instance == null)
        {
            Debug.LogWarning("[SessionBootstrap] No PlayerDataManager; skipping player data step.");
            yield break;
        }

        bool started = false;
        try
        {
            PlayerDataManager.instance.PullRemotePlayerData();
            started = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[SessionBootstrap] PullRemotePlayerData threw: " + ex.Message);
        }

        if (!started) yield break;

        // PullRemotePlayerData is fire-and-forget, so give it a brief, bounded window rather
        // than blocking the whole boot on it.
        float budget = Mathf.Min(stepTimeoutSeconds, 5f);
        float waited = 0f;
        while (waited < budget)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>Waits for a condition with a timeout, logging if it expires.</summary>
    private IEnumerator WaitForFlagOrTimeout(System.Func<bool> condition, string label)
    {
        float waited = 0f;
        while (!condition() && waited < stepTimeoutSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!condition())
            Debug.LogWarning($"[SessionBootstrap] Timed out after {stepTimeoutSeconds}s waiting for {label}. Continuing.");
    }
}
