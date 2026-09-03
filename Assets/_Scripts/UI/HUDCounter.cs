using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Match3Game.Juice;

/// <summary>
/// Drop-in counter for HUD chips (coins, lives, stars). SetValue snaps.
/// Tick(delta) animates a 1-by-1 count over 0.4s and punches the transform
/// so the value change reads. Works with either TMP_Text or legacy Text —
/// only one needs to be assigned.
/// </summary>
[DisallowMultipleComponent]
public class HUDCounter : MonoBehaviour
{
    [Tooltip("Preferred label target. If both are set, TMP wins.")]
    [SerializeField] private TextMeshProUGUI tmpLabel;
    [SerializeField] private Text uguiLabel;

    [Tooltip("Seconds spent counting up from old value to new value on Tick(). " +
             "Kept short so back-to-back ticks don't queue forever.")]
    [SerializeField] private float countDuration = 0.4f;

    private int currentValue;
    private Coroutine activeTween;

    public int CurrentValue => currentValue;

    private void Awake()
    {
        if (tmpLabel == null) tmpLabel = GetComponent<TextMeshProUGUI>();
        if (tmpLabel == null && uguiLabel == null) uguiLabel = GetComponent<Text>();
    }

    /// <summary>Snap to a value. No animation. Use for first paint or hard resets.</summary>
    public void SetValue(int value)
    {
        if (activeTween != null) { StopCoroutine(activeTween); activeTween = null; }
        currentValue = value;
        WriteLabel(value);
    }

    /// <summary>
    /// Add delta (positive or negative). The label counts one integer per frame
    /// until it reaches the new value, capped by countDuration. Also punches scale
    /// so it reads even when the counter is off-screen the user's eye.
    /// </summary>
    public void Tick(int delta)
    {
        if (delta == 0) return;

        int startVal = currentValue;
        int endVal = currentValue + delta;
        currentValue = endVal;

        if (activeTween != null) StopCoroutine(activeTween);
        if (isActiveAndEnabled)
            activeTween = StartCoroutine(CountRoutine(startVal, endVal));
        else
            WriteLabel(endVal);

        MatchJuice.PunchScale(transform);
    }

    private IEnumerator CountRoutine(int from, int to)
    {
        int diff = Mathf.Abs(to - from);
        // A zero-diff tick would divide by zero; a very small diff shouldn't waste 0.4s.
        float duration = Mathf.Min(countDuration, Mathf.Max(0.05f, diff * 0.03f));
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            int shown = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
            WriteLabel(shown);
            yield return null;
        }

        WriteLabel(to);
        activeTween = null;
    }

    private void WriteLabel(int value)
    {
        string s = value.ToString();
        if (tmpLabel != null) tmpLabel.text = s;
        else if (uguiLabel != null) uguiLabel.text = s;
    }
}
