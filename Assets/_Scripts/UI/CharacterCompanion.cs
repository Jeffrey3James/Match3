using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Match3Game.Juice;

/// <summary>
/// Xandria + Armadillo portrait in the bottom-left of the main menu with a
/// rotating speech bubble. Rotates through 8 idle lines every 6 seconds. On
/// level win, shows a cheer + punch scale; on loss, shows sympathy.
///
/// Win subscription: subscribes to GameEvents.onLevelCompleted if the manager
/// is present. External callers (LevelResultPanel, D's code) can also poke
/// PlayCheerReaction / PlaySympathyReaction directly — safe on either path.
/// </summary>
public class CharacterCompanion : MonoBehaviour
{
    [Header("Portrait")]
    [Tooltip("Assign Assets/_Characters/XandriaAndArmadillo.png here.")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Sprite portraitSprite;

    [Header("Speech Bubble")]
    [SerializeField] private GameObject speechBubbleRoot;
    [SerializeField] private TextMeshProUGUI speechLabel;

    [Header("Timing")]
    [Tooltip("Seconds between idle line changes.")]
    [SerializeField] private float lineRotationSeconds = 6f;

    [Tooltip("How long win / loss reaction lines stay up before idle lines resume.")]
    [SerializeField] private float reactionSeconds = 3f;

    // Idle lines — kept as private fields so the wording lives in one place and
    // isn't accidentally overridden by an Inspector drag.
    private static readonly string[] IdleLines = new[]
    {
        "Ready to sparkle up some gems?",
        "Xandria's counting on you!",
        "One more match... you got this!",
        "The armadillo's cheering!",
        "Watch that combo build up!",
        "You look like a natural.",
        "Bet you can get 3 stars.",
        "Every gem you clear helps restore the Halls."
    };

    private static readonly string[] WinLines = new[]
    {
        "Yesss! Well played!",
        "Xandria approves. ✨",
        "That was gorgeous."
    };

    private static readonly string[] LossLines = new[]
    {
        "Aw, so close!",
        "Try again — the halls need you.",
        "You had it, one more move."
    };

    private Coroutine idleRoutine;
    private Coroutine reactionRoutine;
    private int idleIndex;
    private bool subscribedToEvents;

    private void OnEnable()
    {
        if (portraitImage != null && portraitSprite != null)
            portraitImage.sprite = portraitSprite;

        TrySubscribe();
        StartIdle();
    }

    private void OnDisable()
    {
        TryUnsubscribe();
        StopAllReactions();
    }

    private void TrySubscribe()
    {
        if (subscribedToEvents) return;
        if (GameEventsManager.instance == null || GameEventsManager.instance.gameEvents == null) return;

        // The plan mentions GameEvents.OnLevelWon; the actual event on this repo is
        // onLevelCompleted. Subscribe to what exists. LevelResultPanel already knows
        // win-vs-loss, so it may call PlayCheerReaction / PlaySympathyReaction directly
        // for the loss path.
        GameEventsManager.instance.gameEvents.onLevelCompleted += HandleLevelWon;
        GameEventsManager.instance.gameEvents.onLevelFailed += HandleLevelLost;
        subscribedToEvents = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribedToEvents) return;
        if (GameEventsManager.instance == null || GameEventsManager.instance.gameEvents == null) return;

        GameEventsManager.instance.gameEvents.onLevelCompleted -= HandleLevelWon;
        GameEventsManager.instance.gameEvents.onLevelFailed -= HandleLevelLost;
        subscribedToEvents = false;
    }

    private void HandleLevelWon() => PlayCheerReaction();
    private void HandleLevelLost() => PlaySympathyReaction();

    // ------------------------------------------------------------------
    // Public reaction hooks (called by LevelResultPanel / D's code)
    // ------------------------------------------------------------------

    public void PlayCheerReaction()
    {
        StopAllReactions();
        string line = WinLines[Random.Range(0, WinLines.Length)];
        SetBubble(line, show: true);
        MatchJuice.PunchScale(transform);
        reactionRoutine = StartCoroutine(ResumeIdleAfter(reactionSeconds));
    }

    public void PlaySympathyReaction()
    {
        StopAllReactions();
        string line = LossLines[Random.Range(0, LossLines.Length)];
        SetBubble(line, show: true);
        reactionRoutine = StartCoroutine(ResumeIdleAfter(reactionSeconds));
    }

    // ------------------------------------------------------------------
    // Idle rotation
    // ------------------------------------------------------------------

    private void StartIdle()
    {
        if (idleRoutine != null) StopCoroutine(idleRoutine);
        idleRoutine = StartCoroutine(IdleRoutine());
    }

    private IEnumerator IdleRoutine()
    {
        // Small initial delay so the bubble doesn't slam up on scene load — feels less
        // like an ad, more like a companion noticing you.
        SetBubble(IdleLines[idleIndex], show: true);
        yield return new WaitForSeconds(lineRotationSeconds);

        while (true)
        {
            idleIndex = (idleIndex + 1) % IdleLines.Length;
            SetBubble(IdleLines[idleIndex], show: true);
            yield return new WaitForSeconds(lineRotationSeconds);
        }
    }

    private IEnumerator ResumeIdleAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        // Fresh line — advance past whatever we were on so the reaction doesn't
        // ghost back into the same text.
        idleIndex = (idleIndex + 1) % IdleLines.Length;
        StartIdle();
    }

    private void StopAllReactions()
    {
        if (idleRoutine != null) { StopCoroutine(idleRoutine); idleRoutine = null; }
        if (reactionRoutine != null) { StopCoroutine(reactionRoutine); reactionRoutine = null; }
    }

    private void SetBubble(string text, bool show)
    {
        if (speechLabel != null) speechLabel.text = text;
        if (speechBubbleRoot != null) speechBubbleRoot.SetActive(show);
    }
}
