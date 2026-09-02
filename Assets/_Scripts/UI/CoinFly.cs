using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot arcing coin/star burst from a board position toward a HUD target.
/// Pure UI space — spawns RectTransform sprites onto the target's Canvas so we
/// can render above the game and still fire per-arrival callbacks to increment
/// a HUDCounter one coin at a time.
///
/// Note: this component is a helper — call CoinFly.Fly / CoinFly.FlyStars from
/// gameplay code. It creates its own runner GameObject when needed.
/// </summary>
public class CoinFly : MonoBehaviour
{
    private static CoinFly _runner;

    private static CoinFly Runner
    {
        get
        {
            if (_runner == null)
            {
                var go = new GameObject("[CoinFlyRunner]");
                DontDestroyOnLoad(go);
                _runner = go.AddComponent<CoinFly>();
            }
            return _runner;
        }
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Spawn <paramref name="count"/> instances of <paramref name="coinIconPrefab"/> at
    /// <paramref name="startWorldPos"/>, each arcing (bezier) to <paramref name="target"/>.
    /// <paramref name="staggerSeconds"/> spaces them out so the arrivals feel like a stream.
    /// <paramref name="onEachArrive"/> is invoked with (1) as each icon reaches the target.
    /// </summary>
    public static void Fly(RectTransform coinIconPrefab,
                           Vector3 startWorldPos,
                           RectTransform target,
                           int count,
                           float staggerSeconds,
                           Action<int> onEachArrive)
    {
        Runner.StartCoroutine(FlyRoutine(coinIconPrefab, startWorldPos, target,
                                         count, staggerSeconds, onEachArrive, isStar: false));
    }

    /// <summary>Same shape as Fly, but plays the star arrival cue instead of the coin one.</summary>
    public static void FlyStars(RectTransform coinIconPrefab,
                                Vector3 startWorldPos,
                                RectTransform target,
                                int count,
                                float staggerSeconds,
                                Action<int> onEachArrive)
    {
        Runner.StartCoroutine(FlyRoutine(coinIconPrefab, startWorldPos, target,
                                         count, staggerSeconds, onEachArrive, isStar: true));
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private static IEnumerator FlyRoutine(RectTransform prefab,
                                          Vector3 startWorldPos,
                                          RectTransform target,
                                          int count,
                                          float staggerSeconds,
                                          Action<int> onEachArrive,
                                          bool isStar)
    {
        if (prefab == null || target == null || count <= 0) yield break;

        // Parent under the target's canvas so we're at the right z / render order,
        // and use the target canvas for its scale factor when we convert world→screen.
        Canvas canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("CoinFly: target has no parent Canvas; nothing to fly onto.");
            yield break;
        }

        RectTransform canvasRect = canvas.transform as RectTransform;
        Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        // Convert the board-space start position into the same local space as the target.
        Vector2 startLocal = WorldToLocalOnCanvas(startWorldPos, canvasRect, uiCam);

        for (int i = 0; i < count; i++)
        {
            var iconGO = Instantiate(prefab.gameObject, canvasRect);
            var iconRect = (RectTransform)iconGO.transform;
            iconRect.anchoredPosition = startLocal;
            iconRect.localScale = Vector3.one;

            Runner.StartCoroutine(SingleArc(iconRect, startLocal, target, canvasRect, uiCam,
                                            onEachArrive, isStar));

            if (staggerSeconds > 0f) yield return new WaitForSeconds(staggerSeconds);
        }
    }

    private static IEnumerator SingleArc(RectTransform icon,
                                         Vector2 startLocal,
                                         RectTransform target,
                                         RectTransform canvasRect,
                                         Camera uiCam,
                                         Action<int> onArrive,
                                         bool isStar)
    {
        // Bezier control point randomized above the straight line — a symmetric arc
        // reads like a lob toward the HUD chip. Recompute the target every frame so
        // the icons still land correctly if the HUD gets reflowed mid-flight.
        Vector2 controlOffset = new Vector2(
            UnityEngine.Random.Range(-120f, 120f),
            UnityEngine.Random.Range(140f, 260f));

        // Flight time. Fixed duration + a tiny random jitter keeps a burst feeling organic.
        float duration = 0.55f + UnityEngine.Random.Range(-0.05f, 0.1f);
        float elapsed = 0f;

        while (elapsed < duration && icon != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Ease-in cubic feels like acceleration toward the HUD.
            float te = t * t * t;

            Vector2 endLocal = WorldToLocalOnCanvas(target.position, canvasRect, uiCam);
            Vector2 control = Vector2.Lerp(startLocal, endLocal, 0.5f) + controlOffset;

            Vector2 a = Vector2.Lerp(startLocal, control, te);
            Vector2 b = Vector2.Lerp(control, endLocal, te);
            icon.anchoredPosition = Vector2.Lerp(a, b, te);

            // Small spin makes the coins feel like coins, not decals.
            icon.localRotation = Quaternion.Euler(0f, 0f, elapsed * 540f);

            yield return null;
        }

        if (icon != null)
        {
            if (isStar) MatchJuice.StarFlyCue();
            else MatchJuice.CoinFlyCue();

            onArrive?.Invoke(1);
            Destroy(icon.gameObject);
        }
    }

    /// <summary>
    /// Board pieces live in world space; the HUD lives on a Canvas. This is the
    /// dance to place a world point at the right anchored position under the Canvas
    /// rect, respecting overlay vs. camera-space canvases.
    /// </summary>
    private static Vector2 WorldToLocalOnCanvas(Vector3 worldPos, RectTransform canvasRect, Camera uiCam)
    {
        Camera worldCam = Camera.main;
        Vector2 screen = worldCam != null
            ? (Vector2)worldCam.WorldToScreenPoint(worldPos)
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, uiCam, out Vector2 local);
        return local;
    }
}
