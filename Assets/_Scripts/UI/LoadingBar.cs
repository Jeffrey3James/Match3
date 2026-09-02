using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A filled-Image loading bar that fills upward at a constant speed, gated by real progress.
///
/// The two rules, both enforced here:
///   1. CONSTANT SPEED  — the bar never jumps. However fast the data actually arrives, the
///      fill crawls toward its target at a fixed units-per-second rate, so a 20ms load and a
///      3s load look identical to the player until the target moves.
///   2. CANNOT OUTRUN THE DATA — the displayed fill is clamped to the reported progress. If
///      only 40% of the work is confirmed done, the bar stops dead at 40% no matter how much
///      time passes. It physically cannot show 100% before the data says 100%.
///
/// So the displayed value is: min(constant-speed ramp, actual reported progress).
///
/// SETUP:
///   1. Create an Image for the fill.
///   2. Set Image Type = Filled, Fill Method = Vertical, Fill Origin = Bottom.
///      (This component will force those settings in Awake if you forget.)
///   3. Drop this on any GameObject and drag that Image into Fill Image.
///
/// Drive it from your loader with ReportProgress(0..1), or let LoadingScreen do it.
/// </summary>
public class LoadingBar : MonoBehaviour
{
    [Header("Fill")]
    [Tooltip("Image with Type = Filled. Fill Method/Origin are forced to Vertical/Bottom in Awake.")]
    [SerializeField] private Image fillImage;

    [Tooltip("Force Fill Method = Vertical and Fill Origin = Bottom so the bar always moves up.")]
    [SerializeField] private bool forceVerticalFill = true;

    [Header("Speed Gate")]
    [Tooltip("Fill units per second (1 = empty to full in one second). This is the CONSTANT " +
             "speed cap. Lower it to make loading feel more deliberate.")]
    [SerializeField, Min(0.01f)] private float fillSpeed = 0.35f;

    [Tooltip("If on, the bar also cannot move faster than this even when progress jumps to 1 " +
             "at the very end — guarantees the final sprint is still visible.")]
    [SerializeField] private bool applySpeedCapToFinalFill = true;

    [Header("Optional Label")]
    [Tooltip("Optional. Shows the displayed percentage, not the real one.")]
    [SerializeField] private TMPro.TextMeshProUGUI percentLabel;
    [SerializeField] private string percentFormat = "{0}%";

    /// <summary>Real progress reported by the loader, 0..1. The ceiling the bar can reach.</summary>
    private float _targetProgress;

    /// <summary>What the player actually sees, 0..1. Always &lt;= _targetProgress.</summary>
    private float _displayedProgress;

    /// <summary>True once the bar has visually caught up to a target of 1.</summary>
    public bool IsVisuallyComplete => _displayedProgress >= 0.999f;

    /// <summary>What the player currently sees, 0..1.</summary>
    public float DisplayedProgress => _displayedProgress;

    /// <summary>The real reported progress, 0..1.</summary>
    public float TargetProgress => _targetProgress;

    private void Awake()
    {
        if (fillImage == null)
        {
            Debug.LogError("[LoadingBar] Fill Image is not assigned in the Inspector.", this);
            enabled = false;
            return;
        }

        if (forceVerticalFill)
        {
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Vertical;
            fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
        }

        SetImmediate(0f);
    }

    private void Update()
    {
        if (fillImage == null) return;

        // Unscaled so the bar keeps moving with timeScale at 0.
        float maxStep = fillSpeed * Time.unscaledDeltaTime;

        float ceiling = _targetProgress;

        // The bar approaches the ceiling at a constant rate and stops there. It can never
        // pass it, so it cannot display progress the data hasn't confirmed.
        if (_displayedProgress < ceiling)
        {
            float step = applySpeedCapToFinalFill
                ? maxStep
                : (Mathf.Approximately(ceiling, 1f) ? ceiling - _displayedProgress : maxStep);

            _displayedProgress = Mathf.Min(ceiling, _displayedProgress + step);
            Apply();
        }
        else if (_displayedProgress > ceiling)
        {
            // Defensive: a loader that reports a lower value shouldn't rubber-band backwards.
            _displayedProgress = ceiling;
            Apply();
        }
    }

    /// <summary>
    /// Reports how much of the real work is done, 0..1. The bar will crawl toward this at the
    /// configured constant speed and stop here until the value increases.
    /// </summary>
    public void ReportProgress(float progress01)
    {
        float clamped = Mathf.Clamp01(progress01);

        // Progress should only move forward. Ignore regressions so a slow step finishing after
        // a fast one can't yank the bar backwards.
        if (clamped > _targetProgress) _targetProgress = clamped;
    }

    /// <summary>Marks the real work finished. The bar still takes its time getting there.</summary>
    public void ReportComplete() => ReportProgress(1f);

    /// <summary>Jumps the bar straight to a value with no animation. Use for resets.</summary>
    public void SetImmediate(float progress01)
    {
        _targetProgress = Mathf.Clamp01(progress01);
        _displayedProgress = _targetProgress;
        Apply();
    }

    /// <summary>Resets both real and displayed progress to zero.</summary>
    public void ResetBar() => SetImmediate(0f);

    private void Apply()
    {
        if (fillImage != null) fillImage.fillAmount = _displayedProgress;

        if (percentLabel != null)
            percentLabel.text = string.Format(percentFormat, Mathf.RoundToInt(_displayedProgress * 100f));
    }
}
