using System;
using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Restore the Gem Halls decorate panel MVP.
///
/// Header: "Restore the Gem Halls". Body: five task cards, each with an icon (before/after
/// sprite pair), a name, a star cost, and a Restore button. When the player taps Restore we
/// call <see cref="PlayerHandler.CompleteDecorateTask"/> which atomically spends one star
/// and flips the task's bit in the persisted <c>decorateProgress</c> bitmask.
///
/// Task index is the card's position in the arrays (0..4). We only walk the first
/// <see cref="MaxTasks"/> entries even if the SerializeFields carry more, so an artist can
/// extend the arrays for future halls without breaking the panel today.
///
/// This script is deliberately independent of MatchJuice / DOTween so it compiles cleanly on
/// a fresh checkout. Sparkle bursts from B's <c>MatchJuice.BurstAt</c> are invoked reflectively
/// if the type is present at runtime.
/// </summary>
public class DecoratePanel : MonoBehaviour
{
    /// <summary>Number of decorate task cards this MVP shows.</summary>
    public const int MaxTasks = 5;

    [Header("Header")]
    [Tooltip("The panel title. Defaults to 'Restore the Gem Halls' if left empty.")]
    [SerializeField] private TMP_Text headerLabel;
    [SerializeField] private string headerText = "Restore the Gem Halls";

    [Tooltip("Star balance readout at the top of the panel. Optional; leave empty to hide.")]
    [SerializeField] private TMP_Text starBalanceLabel;

    [Header("Task cards (index-aligned with the arrays below)")]
    [Tooltip("Root GameObject for each task card. Must have MaxTasks entries.")]
    [SerializeField] private GameObject[] taskCardRoots;

    [Tooltip("Image component on each card whose sprite we swap between before/after.")]
    [SerializeField] private Image[] taskIcons;

    [Tooltip("Optional label per card that shows the task name.")]
    [SerializeField] private TMP_Text[] taskNameLabels;

    [Tooltip("Optional label per card that shows the star cost (all 1 for MVP).")]
    [SerializeField] private TMP_Text[] taskCostLabels;

    [Tooltip("Restore button on each card. Hidden and swapped for the checkmark when done.")]
    [SerializeField] private Button[] taskRestoreButtons;

    [Tooltip("Optional checkmark GameObject shown once the task is complete.")]
    [SerializeField] private GameObject[] taskDoneCheckmarks;

    [Header("Sprites (parallel arrays; index i = task i)")]
    [SerializeField] private Sprite[] taskIconsBefore;
    [SerializeField] private Sprite[] taskIconsAfter;

    [Header("Text")]
    [Tooltip("Human-readable task names, parallel to the sprite arrays.")]
    [SerializeField] private string[] taskNames = new[]
    {
        "Restore the Foyer Chandelier",
        "Polish the Grand Piano",
        "Repair the Rose Window",
        "Refresh the Fountain",
        "Rehang the Tapestries",
    };

    [Header("Reveal animation")]
    [Tooltip("Scale-punch amount when a task is completed. 0 disables the animation.")]
    [SerializeField] private float revealPunchScale = 0.25f;

    [Tooltip("Duration of the scale-punch animation in seconds.")]
    [SerializeField] private float revealPunchDuration = 0.35f;

    [Tooltip("Sparkle burst color forwarded to MatchJuice.BurstAt if available at runtime.")]
    [SerializeField] private Color sparkleColor = new Color(1f, 0.85f, 0.3f, 1f);

    private void OnEnable()
    {
        if (headerLabel != null) headerLabel.text = headerText;
        RefreshAllCards();
    }

    /// <summary>
    /// Redraws every card from the current PlayerHandler state. Safe to call whenever
    /// anything meta-related might have changed (e.g. after winning a level and awarding a star).
    /// </summary>
    public void RefreshAllCards()
    {
        UpdateStarBalance();
        for (int i = 0; i < MaxTasks; i++)
        {
            RefreshCard(i);
        }
    }

    private void UpdateStarBalance()
    {
        if (starBalanceLabel == null) return;
        int stars = PlayerHandler.instance != null ? PlayerHandler.instance.GetStars() : 0;
        starBalanceLabel.text = stars.ToString();
    }

    private void RefreshCard(int i)
    {
        // Root visibility: if the artist skipped a slot in the Inspector, we simply don't draw it.
        GameObject root = SafeGet(taskCardRoots, i);
        if (root == null) return;
        root.SetActive(true);

        bool done = PlayerHandler.instance != null && PlayerHandler.instance.IsDecorateTaskComplete(i);

        // Sprite: before or after.
        Image icon = SafeGet(taskIcons, i);
        if (icon != null)
        {
            Sprite chosen = done ? SafeGet(taskIconsAfter, i) : SafeGet(taskIconsBefore, i);
            if (chosen != null) icon.sprite = chosen;
        }

        // Name and cost labels are pure display.
        TMP_Text nameLabel = SafeGet(taskNameLabels, i);
        if (nameLabel != null)
        {
            string n = SafeGet(taskNames, i);
            nameLabel.text = string.IsNullOrEmpty(n) ? $"Task {i + 1}" : n;
        }

        TMP_Text costLabel = SafeGet(taskCostLabels, i);
        if (costLabel != null) costLabel.text = "1";

        // Button vs checkmark: only one of them is active at a time.
        Button restoreBtn = SafeGet(taskRestoreButtons, i);
        if (restoreBtn != null)
        {
            restoreBtn.gameObject.SetActive(!done);
            // Rebind to guarantee exactly one listener with the current index captured.
            int captured = i;
            restoreBtn.onClick.RemoveAllListeners();
            restoreBtn.onClick.AddListener(() => OnRestoreClicked(captured));
        }

        GameObject check = SafeGet(taskDoneCheckmarks, i);
        if (check != null) check.SetActive(done);
    }

    private void OnRestoreClicked(int taskIndex)
    {
        if (PlayerHandler.instance == null)
        {
            Debug.LogWarning("DecoratePanel: PlayerHandler is not available.");
            return;
        }

        bool ok = PlayerHandler.instance.CompleteDecorateTask(taskIndex);
        if (!ok)
        {
            // Not enough stars, or already complete. Refresh so the button state matches truth.
            RefreshCard(taskIndex);
            UpdateStarBalance();
            return;
        }

        // Success. Swap sprite and celebrate.
        Image icon = SafeGet(taskIcons, taskIndex);
        Sprite after = SafeGet(taskIconsAfter, taskIndex);
        if (icon != null && after != null) icon.sprite = after;

        Button restoreBtn = SafeGet(taskRestoreButtons, taskIndex);
        if (restoreBtn != null) restoreBtn.gameObject.SetActive(false);

        GameObject check = SafeGet(taskDoneCheckmarks, taskIndex);
        if (check != null) check.SetActive(true);

        UpdateStarBalance();
        StartCoroutine(PlayRevealAnimation(taskIndex));
        InvokeMatchJuiceBurstIfAvailable(taskIndex);
    }

    /// <summary>
    /// Cheap manual scale-punch. Uses unscaled time so it plays even if the game is paused
    /// on the main menu. Guarded against zero-duration configs.
    /// </summary>
    private IEnumerator PlayRevealAnimation(int taskIndex)
    {
        if (revealPunchScale <= 0f || revealPunchDuration <= 0f) yield break;

        GameObject root = SafeGet(taskCardRoots, taskIndex);
        if (root == null) yield break;
        Transform t = root.transform;

        Vector3 baseScale = t.localScale;
        Vector3 peakScale = baseScale * (1f + revealPunchScale);

        float half = revealPunchDuration * 0.5f;
        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / half);
            t.localScale = Vector3.Lerp(baseScale, peakScale, k);
            yield return null;
        }
        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / half);
            t.localScale = Vector3.Lerp(peakScale, baseScale, k);
            yield return null;
        }
        t.localScale = baseScale;
    }

    /// <summary>
    /// Best-effort call into B's <c>MatchJuice.BurstAt(Vector3, Color)</c>. We do not take a
    /// compile-time dependency because that file lives on Agent B's branch; reflection lets us
    /// merge in either order without a rebuild dance.
    /// </summary>
    private void InvokeMatchJuiceBurstIfAvailable(int taskIndex)
    {
        GameObject root = SafeGet(taskCardRoots, taskIndex);
        if (root == null) return;

        Type juice = Type.GetType("MatchJuice");
        if (juice == null) return; // MatchJuice hasn't been merged yet; silently skip.

        // Try signatures in order of preference: (Vector3, Color), (Vector3), ().
        MethodInfo m = juice.GetMethod("BurstAt", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(Vector3), typeof(Color) }, null);
        if (m != null) { m.Invoke(null, new object[] { root.transform.position, sparkleColor }); return; }

        m = juice.GetMethod("BurstAt", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(Vector3) }, null);
        if (m != null) { m.Invoke(null, new object[] { root.transform.position }); return; }

        m = juice.GetMethod("BurstAt", BindingFlags.Public | BindingFlags.Static, Type.DefaultBinder,
            Type.EmptyTypes, null);
        if (m != null) { m.Invoke(null, null); }
    }

    private static T SafeGet<T>(T[] arr, int i)
    {
        return (arr != null && i >= 0 && i < arr.Length) ? arr[i] : default;
    }
}
