using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes any Image (or any RectTransform) look like it's breathing — a slow, organic
/// scale pulse with optional alpha and rotation sway. Pure code, no animation clips,
/// no Animator, no timeline.
///
/// SETUP: drop this on the GameObject holding your loading-screen Image. That's it.
/// Everything else is optional tuning in the Inspector.
///
/// Runs on unscaled time so it keeps breathing even when Time.timeScale is 0
/// (which is common while a loading screen is up).
/// </summary>
[DisallowMultipleComponent]
public class BreathingImage : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Optional. The Image to pulse alpha on. Leave empty to auto-find one on this GameObject.")]
    [SerializeField] private Image targetImage;

    [Header("Breath Timing")]
    [Tooltip("Seconds for one full breath (in + out). Human resting breath is roughly 4-6s.")]
    [SerializeField, Min(0.1f)] private float breathDuration = 4f;

    [Tooltip("Real breathing isn't symmetrical — the exhale is longer than the inhale. " +
             "0.5 = even. 0.4 means 40% of the cycle inhaling, 60% exhaling.")]
    [SerializeField, Range(0.15f, 0.85f)] private float inhaleRatio = 0.4f;

    [Tooltip("Randomizes the starting point in the cycle so multiple breathing objects " +
             "don't pulse in lockstep.")]
    [SerializeField] private bool randomizeStartPhase = true;

    [Header("Scale Pulse")]
    [SerializeField] private bool pulseScale = true;
    [Tooltip("How much it grows at peak inhale. 0.05 = 5% larger. Keep it subtle.")]
    [SerializeField, Range(0f, 0.5f)] private float scaleAmplitude = 0.05f;

    [Header("Alpha Pulse")]
    [SerializeField] private bool pulseAlpha = false;
    [Tooltip("Alpha at full exhale (the dimmest point).")]
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.75f;
    [Tooltip("Alpha at full inhale (the brightest point).")]
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 1f;

    [Header("Rotation Sway")]
    [Tooltip("Adds a gentle tilt. Reads as 'alive' rather than 'looping animation'.")]
    [SerializeField] private bool swayRotation = false;
    [Tooltip("Peak tilt in degrees, each direction.")]
    [SerializeField, Range(0f, 15f)] private float swayDegrees = 2f;

    private Vector3 _baseScale;
    private Quaternion _baseRotation;
    private float _phase;          // 0..1 position within the breath cycle
    private bool _captured;

    private void Awake()
    {
        if (targetImage == null) targetImage = GetComponent<Image>();
        CaptureBaseTransform();

        if (randomizeStartPhase) _phase = Random.value;
    }

    private void OnEnable()
    {
        // Re-capture in case something moved us while disabled, but only if we
        // never captured (avoids baking a mid-breath scale in as the new base).
        if (!_captured) CaptureBaseTransform();
    }

    private void OnDisable()
    {
        // Leave the object at rest rather than frozen mid-breath.
        ApplyBreath(0f);
    }

    private void CaptureBaseTransform()
    {
        _baseScale = transform.localScale;
        _baseRotation = transform.localRotation;
        _captured = true;
    }

    private void Update()
    {
        // Unscaled: loading screens usually run with timeScale pinned at 0.
        _phase += Time.unscaledDeltaTime / breathDuration;
        if (_phase >= 1f) _phase -= 1f;

        ApplyBreath(BreathCurve(_phase));
    }

    /// <summary>
    /// Maps a 0..1 phase to a 0..1 breath amount, where 0 is full exhale and 1 is peak inhale.
    /// Uses a smoothstep on each half so there's a natural pause at the top and bottom of the
    /// breath instead of the constant-velocity feel a raw sine gives.
    /// </summary>
    private float BreathCurve(float phase)
    {
        float t;
        if (phase < inhaleRatio)
        {
            // Inhale: 0 -> 1
            t = phase / inhaleRatio;
        }
        else
        {
            // Exhale: 1 -> 0
            t = 1f - ((phase - inhaleRatio) / (1f - inhaleRatio));
        }

        return Mathf.SmoothStep(0f, 1f, t);
    }

    private void ApplyBreath(float breath)
    {
        if (pulseScale)
            transform.localScale = _baseScale * (1f + scaleAmplitude * breath);

        if (swayRotation)
        {
            // Remap 0..1 to -1..1 so it tilts both ways across the cycle.
            float signed = (breath * 2f) - 1f;
            transform.localRotation = _baseRotation * Quaternion.Euler(0f, 0f, signed * swayDegrees);
        }

        if (pulseAlpha && targetImage != null)
        {
            Color c = targetImage.color;
            c.a = Mathf.Lerp(minAlpha, maxAlpha, breath);
            targetImage.color = c;
        }
    }

    /// <summary>Snaps back to the captured resting transform. Useful before a fade-out.</summary>
    public void ResetToRest()
    {
        transform.localScale = _baseScale;
        transform.localRotation = _baseRotation;
        if (targetImage != null)
        {
            Color c = targetImage.color;
            c.a = maxAlpha;
            targetImage.color = c;
        }
    }
}
