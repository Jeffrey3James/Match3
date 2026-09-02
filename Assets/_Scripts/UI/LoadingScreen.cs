using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loading screen controller: a breathing image, a gated upward-filling bar, and a
/// step tracker that decides what "real progress" actually means.
///
/// The gating contract, end to end:
///   - You register the steps that must finish before loading is done.
///   - Each CompleteStep() raises real progress by 1/stepCount.
///   - LoadingBar crawls toward that ceiling at a constant speed and stops there.
///   - The screen only dismisses once BOTH the data is done AND the bar has visually
///     reached 100% AND the minimum display time has elapsed.
///
/// Net effect: instant loads still show a full, smooth, constant-speed fill. Slow loads
/// stall the bar partway instead of faking progress. The bar never lies.
///
/// SETUP:
///   1. Canvas → full-screen panel. Put this component on the panel root.
///   2. Drag in the LoadingBar and (optionally) the BreathingImage and a CanvasGroup.
///   3. From your loader:
///        LoadingScreen.Instance.Show();
///        LoadingScreen.Instance.RegisterStep("levels");
///        LoadingScreen.Instance.RegisterStep("player");
///        ... later ...
///        LoadingScreen.Instance.CompleteStep("levels");
///        LoadingScreen.Instance.CompleteStep("player");
///      The screen dismisses itself.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance { get; private set; }

    [Header("Pieces")]
    [Tooltip("The gated fill bar.")]
    [SerializeField] private LoadingBar loadingBar;
    [Tooltip("Optional. The breathing logo/art. Reset to rest before fade-out.")]
    [SerializeField] private BreathingImage breathingImage;
    [Tooltip("Optional. Used to fade the whole screen out. Auto-added if missing.")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Optional. Root to deactivate when hidden. Defaults to this GameObject.")]
    [SerializeField] private GameObject screenRoot;

    [Header("Optional Status Text")]
    [SerializeField] private TMPro.TextMeshProUGUI statusLabel;

    [Header("Timing")]
    [Tooltip("Minimum seconds the screen stays up, even if everything loads instantly. " +
             "Stops the screen from flashing on and off.")]
    [SerializeField, Min(0f)] private float minimumDisplaySeconds = 1.25f;

    [Tooltip("Seconds to fade out once loading is finished. 0 = snap off.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.35f;

    [Header("Lifetime")]
    [Tooltip("Survive scene loads. Turn on if this screen covers a scene transition.")]
    [SerializeField] private bool persistAcrossScenes = false;

    /// <summary>Fires once the screen has fully dismissed.</summary>
    public event Action OnLoadingComplete;

    private readonly HashSet<string> _registeredSteps = new HashSet<string>();
    private readonly HashSet<string> _completedSteps = new HashSet<string>();

    private float _shownAtUnscaledTime;
    private bool _isShowing;
    private bool _dismissing;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (screenRoot == null) screenRoot = gameObject;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        if (loadingBar == null) loadingBar = GetComponentInChildren<LoadingBar>(true);
        if (breathingImage == null) breathingImage = GetComponentInChildren<BreathingImage>(true);

        if (loadingBar == null)
            Debug.LogError("[LoadingScreen] No LoadingBar assigned or found in children.", this);

        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // Show / hide
    // ------------------------------------------------------------------
    /// <summary>Shows the screen and resets all progress.</summary>
    public void Show(string status = null)
    {
        _registeredSteps.Clear();
        _completedSteps.Clear();
        _dismissing = false;
        _isShowing = true;
        _shownAtUnscaledTime = Time.unscaledTime;

        if (screenRoot != null) screenRoot.SetActive(true);
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        if (loadingBar != null) loadingBar.ResetBar();
        SetStatus(status);
    }

    /// <summary>
    /// Registers a unit of work that must finish before loading is considered done.
    /// Call this for every step BEFORE you start them, so the denominator is stable.
    /// </summary>
    public void RegisterStep(string stepId)
    {
        if (string.IsNullOrEmpty(stepId)) return;
        _registeredSteps.Add(stepId);
        PushProgress();
    }

    /// <summary>Registers several steps at once.</summary>
    public void RegisterSteps(params string[] stepIds)
    {
        if (stepIds == null) return;
        foreach (string id in stepIds) RegisterStep(id);
    }

    /// <summary>
    /// Marks a registered step finished. Real progress rises; the bar starts crawling
    /// toward the new ceiling at its constant speed.
    /// </summary>
    public void CompleteStep(string stepId, string status = null)
    {
        if (string.IsNullOrEmpty(stepId)) return;

        if (!_registeredSteps.Contains(stepId))
        {
            Debug.LogWarning($"[LoadingScreen] CompleteStep('{stepId}') for a step that was " +
                             "never registered. Registering it now so progress stays consistent.");
            _registeredSteps.Add(stepId);
        }

        _completedSteps.Add(stepId);
        SetStatus(status);
        PushProgress();

        if (AllStepsComplete && !_dismissing) StartCoroutine(DismissWhenReady());
    }

    /// <summary>
    /// Reports fractional progress inside a single step (e.g. a download at 60%).
    /// Optional — use it when a step is long enough that binary done/not-done feels dead.
    /// </summary>
    public void ReportPartialProgress(float fraction01)
    {
        if (loadingBar == null || _registeredSteps.Count == 0) return;

        float perStep = 1f / _registeredSteps.Count;
        float baseline = _completedSteps.Count * perStep;
        loadingBar.ReportProgress(baseline + (Mathf.Clamp01(fraction01) * perStep));
    }

    /// <summary>Skips step tracking and drives the bar directly, 0..1.</summary>
    public void ReportProgressDirect(float progress01)
    {
        if (loadingBar != null) loadingBar.ReportProgress(progress01);
        if (progress01 >= 1f && !_dismissing) StartCoroutine(DismissWhenReady());
    }

    /// <summary>Forces loading to finish regardless of outstanding steps.</summary>
    public void ForceComplete()
    {
        foreach (string id in _registeredSteps) _completedSteps.Add(id);
        if (loadingBar != null) loadingBar.ReportComplete();
        if (!_dismissing) StartCoroutine(DismissWhenReady());
    }

    private bool AllStepsComplete =>
        _registeredSteps.Count > 0 && _completedSteps.Count >= _registeredSteps.Count;

    private void PushProgress()
    {
        if (loadingBar == null) return;

        if (_registeredSteps.Count == 0)
        {
            loadingBar.ReportProgress(0f);
            return;
        }

        loadingBar.ReportProgress((float)_completedSteps.Count / _registeredSteps.Count);
    }

    // ------------------------------------------------------------------
    // Dismissal
    // ------------------------------------------------------------------
    /// <summary>
    /// Waits for all three gates before hiding: the data is done, the bar has visually
    /// finished its constant-speed fill, and the minimum display time has elapsed.
    /// </summary>
    private IEnumerator DismissWhenReady()
    {
        if (_dismissing) yield break;
        _dismissing = true;

        // Gate 1: minimum on-screen time.
        float elapsed = Time.unscaledTime - _shownAtUnscaledTime;
        if (elapsed < minimumDisplaySeconds)
            yield return new WaitForSecondsRealtime(minimumDisplaySeconds - elapsed);

        // Gate 2: the bar has to actually reach the top at its own constant speed.
        while (loadingBar != null && !loadingBar.IsVisuallyComplete)
            yield return null;

        // Gate 3: fade out.
        if (breathingImage != null) breathingImage.ResetToRest();

        if (fadeOutSeconds > 0f && canvasGroup != null)
        {
            float t = 0f;
            while (t < fadeOutSeconds)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / fadeOutSeconds);
                yield return null;
            }
        }

        Hide();
        OnLoadingComplete?.Invoke();
    }

    /// <summary>Hides the screen immediately.</summary>
    public void Hide()
    {
        _isShowing = false;
        _dismissing = false;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        if (screenRoot != null) screenRoot.SetActive(false);
    }

    /// <summary>True while the screen is up and not yet dismissed.</summary>
    public bool IsShowing => _isShowing;

    private void SetStatus(string status)
    {
        if (statusLabel != null && !string.IsNullOrEmpty(status))
            statusLabel.text = status;
    }
}
