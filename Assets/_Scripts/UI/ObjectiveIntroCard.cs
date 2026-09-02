using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using Match3Game;

/// <summary>
/// Full-screen intro card that reads the current level's objectives and shows them
/// as a punchy scale-in, brief hold, then dissolve. Skips entirely on Retry so the
/// player isn't waiting on the same card three losses in a row.
///
/// Wiring:
///  - dimPanel        — full-screen semi-transparent Image, blocks raycasts while up.
///  - cardRoot        — the scaled/dissolved container; sits above dimPanel.
///  - iconContainer   — parent with a HorizontalLayoutGroup; icons are instantiated here.
///  - iconPrefab      — RectTransform with an Image child; big enough to read at a glance.
///  - iconCountLabel  — optional. If set, gets stamped with the objective count next to each icon.
///
/// D's LevelResultPanel Retry button should call PlayerPrefs.SetInt("SkipObjectiveIntro", 1)
/// before reloading GameScene; we consume the pref on the next Show().
/// </summary>
public class ObjectiveIntroCard : MonoBehaviour
{
    private const string SkipPrefKey = "SkipObjectiveIntro";

    [Header("Panel")]
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private RectTransform cardRoot;
    [SerializeField] private CanvasGroup cardGroup;
    [SerializeField] private Transform iconContainer;
    [SerializeField] private RectTransform iconPrefab;

    [Header("Timing")]
    [SerializeField] private float punchInDuration = 0.25f;
    [SerializeField] private float holdDuration = 0.8f;
    [SerializeField] private float dissolveDuration = 0.25f;

    [Header("Options")]
    [Tooltip("Set from D's LevelResultPanel Retry: skips the intro once. Also skips if the " +
             "SkipObjectiveIntro PlayerPref is set.")]
    public bool SkipOnRetry;

    private void Start()
    {
        // Auto-run on level start. If someone wants manual control they can disable
        // the component and call Show() themselves.
        Show();
    }

    /// <summary>
    /// Show the intro card unless retry / pref says skip.
    /// </summary>
    public void Show()
    {
        if (SkipOnRetry || PlayerPrefs.GetInt(SkipPrefKey, 0) == 1)
        {
            // Consume the flag so a non-retry replay isn't also skipped.
            PlayerPrefs.SetInt(SkipPrefKey, 0);
            PlayerPrefs.Save();
            SkipOnRetry = false;

            HideImmediate();
            return;
        }

        PopulateIcons();
        StartCoroutine(PlayRoutine());
    }

    private void HideImmediate()
    {
        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            panelGroup.blocksRaycasts = false;
            panelGroup.interactable = false;
        }
        gameObject.SetActive(false);
    }

    private void PopulateIcons()
    {
        if (iconContainer == null || iconPrefab == null) return;

        // Nuke any children from a previous show — the intro card lives across levels.
        for (int i = iconContainer.childCount - 1; i >= 0; i--)
            Destroy(iconContainer.GetChild(i).gameObject);

        Match3 match3 = FindFirstObjectByType<Match3>();
        Level level = match3 != null ? match3.GetLevel() : null;
        if (level == null) return;

        List<ObjectiveConfig> objectives = level.GetObjectives();
        if (objectives == null) return;

        // De-dupe: level authoring sometimes has the same objective config referenced twice.
        var seen = new HashSet<ObjectiveConfig>();
        foreach (var cfg in objectives)
        {
            if (cfg == null || cfg.typesToClear == null) continue;
            if (!seen.Add(cfg)) continue;

            var iconGO = Instantiate(iconPrefab.gameObject, iconContainer);
            var img = iconGO.GetComponentInChildren<Image>();
            if (img != null) img.sprite = cfg.typesToClear.sprite;

            var label = iconGO.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (label != null) label.text = "x" + cfg.amountToClear;
        }
    }

    private IEnumerator PlayRoutine()
    {
        if (panelGroup != null)
        {
            panelGroup.alpha = 1f;
            panelGroup.blocksRaycasts = true;
            panelGroup.interactable = true;
        }

        // Punch scale in.
        if (cardRoot != null)
        {
            cardRoot.DOKill();
            cardRoot.localScale = Vector3.zero;
            cardRoot.DOScale(1f, punchInDuration).SetEase(Ease.OutBack).SetUpdate(true);
        }
        if (cardGroup != null)
        {
            cardGroup.alpha = 1f;
        }

        // Timers use unscaled so a paused board can't strand the card.
        float t = 0f;
        while (t < punchInDuration) { t += Time.unscaledDeltaTime; yield return null; }

        // Hold.
        t = 0f;
        while (t < holdDuration) { t += Time.unscaledDeltaTime; yield return null; }

        // Dissolve out (alpha only — leaving scale alone reads calmer than a shrink).
        if (cardGroup != null)
            cardGroup.DOFade(0f, dissolveDuration).SetUpdate(true);
        if (panelGroup != null)
            panelGroup.DOFade(0f, dissolveDuration).SetUpdate(true);

        t = 0f;
        while (t < dissolveDuration) { t += Time.unscaledDeltaTime; yield return null; }

        HideImmediate();
    }
}
