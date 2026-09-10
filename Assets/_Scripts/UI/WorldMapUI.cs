using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;

/// <summary>
/// Vertical scrolling level map. Reads the level catalog from LevelHandler and
/// spawns one node prefab per level, offset horizontally with a sine wobble so
/// the path snakes. Each node paints itself as locked, current, or completed
/// based on the player's progress.
///
/// Attach this to a ScrollView's Content object (or any RectTransform with a
/// vertical layout). Assign the node prefab and vertical spacing in the Inspector.
/// </summary>
public class WorldMapUI : MonoBehaviour {
  public enum NodeState { Completed, Current, Locked }

  [Header("Prefab & Layout")]
  [Tooltip("Node prefabs for each state — a TMP label for the level number and a " +
        "Button to receive clicks. See PR body for the exact wiring.")]
  [SerializeField] private RectTransform completedNodePrefab;
  [SerializeField] private RectTransform currentNodePrefab;
  [SerializeField] private RectTransform lockedNodePrefab;

  [Tooltip("Vertical spacing between nodes in the ScrollView Content.")]
  [SerializeField] private float verticalSpacing = 180f;

  [Tooltip("Peak horizontal wobble amplitude, in pixels. Formula: sin(i*0.6) * offset.")]
  [SerializeField] private float horizontalOffset = 220f;

  [Header("Fallback Tints")]
  [SerializeField] private Color completedTint = new Color(0.4f, 0.9f, 0.5f, 1f);
  [SerializeField] private Color currentTint = new Color(1f, 0.85f, 0.3f, 1f);
  [SerializeField] private Color lockedTint = new Color(0.5f, 0.5f, 0.5f, 1f);

  [Header("Scene routing")]
  [Tooltip("Scene loaded when the current-level node is tapped. Should match MainMenuUI's " +
           "levelButton scene so the map and the button behave the same.")]
  [SerializeField] private string gameSceneName = "GameScene";

  private readonly List<RectTransform> spawnedNodes = new();

  [Header("Scroll")]
  [Tooltip("The ScrollRect this Content object lives under. Used to auto-center the current node.")]
  [SerializeField] private ScrollRect scrollRect;

  private RectTransform currentNode;

  private void Start() {
    Build();
    }

  /// <summary>
  /// Rebuild the map. Safe to call after a level completes if you want the current-node
  /// halo to shift up without a full scene reload.
  /// </summary>
  public void Build() {
    if (completedNodePrefab == null || currentNodePrefab == null || lockedNodePrefab == null) {
      Debug.LogError("WorldMapUI: one or more node prefabs are unassigned. No nodes will spawn.");
      return;
      }
    if (LevelHandler.instance == null) {
      Debug.LogWarning("WorldMapUI: LevelHandler.instance is null. Map will populate later.");
      return;
      }

    ClearNodes();

    var levels = LevelHandler.instance.GetAllLevels();
    int count = levels != null ? levels.Count : 0;
    int currentPlayerLevel = PlayerHandler.instance != null
        ? PlayerHandler.instance.GetPlayerLevel()
        : 0;

    var contentRect = transform as RectTransform;
    if (contentRect != null) {
      Vector2 size = contentRect.sizeDelta;
      size.y = Mathf.Max(size.y, (count + 1) * verticalSpacing);
      contentRect.sizeDelta = size;
      }

    for (int i = 0; i < count; i++) {
      NodeState state = i < currentPlayerLevel ? NodeState.Completed
                      : i == currentPlayerLevel ? NodeState.Current
                      : NodeState.Locked;

      RectTransform prefabToUse = state switch {
        NodeState.Completed => completedNodePrefab,
        NodeState.Current => currentNodePrefab,
        _ => lockedNodePrefab
        };

      var node = Instantiate(prefabToUse, transform);
      spawnedNodes.Add(node);

      if (state == NodeState.Current)
        currentNode = node;

      float x = Mathf.Sin(i * 0.6f) * horizontalOffset;
      float y = i * verticalSpacing;
      node.anchoredPosition = new Vector2(x, y);

      PaintNode(node, i, state);
      WireNode(node, i, state);
      }

    if (currentNode != null && scrollRect != null)
      StartCoroutine(CenterOnCurrentNodeNextFrame());
    else if (currentNode == null)
      Debug.LogWarning("WorldMapUI: no Current-state node found (player may have completed all levels).");
    }

  private IEnumerator CenterOnCurrentNodeNextFrame() {
    yield return new WaitForEndOfFrame();
    CenterOnNode(currentNode);
    }

  /// <summary>
  /// Scrolls Content so that the given node is centered in the viewport.
  /// Uses actual RectTransform space conversions rather than hand-derived
  /// coordinates, so it works regardless of Content's pivot/anchor setup.
  /// </summary>
  private void CenterOnNode(RectTransform node) {
    if (scrollRect == null || node == null) return;

    RectTransform content = scrollRect.content;
    RectTransform viewport = scrollRect.viewport;

    // A lot of ScrollRect setups leave the Viewport field unassigned in the
    // Inspector. Dragging still works because ScrollRect falls back internally,
    // but reading scrollRect.viewport from script returns null in that case —
    // so we fall back to the ScrollRect's own RectTransform, which is a close
    // enough stand-in for a typical single-viewport hierarchy.
    if (viewport == null) {
      viewport = scrollRect.transform as RectTransform;
      Debug.LogWarning("WorldMapUI: ScrollRect.viewport is unassigned in the Inspector. " +
                        "Falling back to the ScrollRect's own RectTransform for centering math. " +
                        "Assign Viewport explicitly for accurate results.");
      }
    if (content == null || viewport == null) return;

    Canvas.ForceUpdateCanvases(); // make sure layout/content size is current
    scrollRect.StopMovement();    // kill any in-flight drag/inertia first

    // Where does the node currently sit within the viewport's own local rect?
    Vector2 nodeInViewport = viewport.InverseTransformPoint(node.position);

    // How far (in viewport-local units) the node is from dead center vertically.
    float deltaY = viewport.rect.center.y - nodeInViewport.y;

    // Content and viewport share the same scale/rotation in a standard ScrollRect
    // hierarchy, so shifting Content by this same delta brings the node to center.
    // Only Y is touched — X is left alone so horizontal position never drifts.
    float newY = content.anchoredPosition.y + deltaY;

    float viewportHeight = viewport.rect.height;
    float contentHeight = content.rect.height;
    float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
    float minY = Mathf.Min(0f, -maxScroll);
    float maxY = Mathf.Max(0f, maxScroll);
    newY = Mathf.Clamp(newY, minY, maxY);

    content.anchoredPosition = new Vector2(content.anchoredPosition.x, newY);
    }

  private void ClearNodes() {
    foreach (var n in spawnedNodes) {
      if (n != null) Destroy(n.gameObject);
      }
    spawnedNodes.Clear();
    }

  private void PaintNode(RectTransform node, int index, NodeState state) {
    // Label — level number (1-based for the player).
    var label = node.GetComponentInChildren<TextMeshProUGUI>();
    if (label != null) label.text = (index + 1).ToString();

    // Current node pulses so the player can find it after scrolling.
    if (state == NodeState.Current) {
      node.DOKill();
      node.localScale = Vector3.one;
      node.DOScale(1.12f, 0.6f)
          .SetEase(Ease.InOutSine)
          .SetLoops(-1, LoopType.Yoyo)
          .SetUpdate(true)
          .SetLink(node.gameObject);
      }
    }

  private void WireNode(RectTransform node, int index, NodeState state) {
    var btn = node.GetComponent<Button>();
    if (btn == null) btn = node.GetComponentInChildren<Button>();
    if (btn == null) return;

    btn.onClick.RemoveAllListeners();

    switch (state) {
      case NodeState.Current:
        btn.onClick.AddListener(() => OnCurrentTapped());
        break;
      case NodeState.Completed:
        // Optional replay affordance — for now, same as current. Cheap wins.
        btn.onClick.AddListener(() => OnCurrentTapped());
        break;
      case NodeState.Locked:
        btn.onClick.AddListener(() => ShakeNode(node));
        break;
      }
    }

  private void OnCurrentTapped() {
    if (PlayerHandler.instance == null) { SceneManager.LoadScene(gameSceneName); return; }
    if (!PlayerHandler.instance.CheckPlayerLives()) return;
    PlayerHandler.instance.UseALifeFromPlayer();
    SceneManager.LoadScene(gameSceneName);
    }

  private void ShakeNode(RectTransform node) {
    if (node == null) return;
    node.DOKill(true);
    node.DOShakeAnchorPos(0.35f, new Vector2(18f, 0f), 18, 90f, false, true)
.SetLink(node.gameObject)
            .SetUpdate(true);
    }

  /// <summary>
  /// Public entry point for centering on an arbitrary node (e.g. called from
  /// outside after a level completes). Delegates to the same centering logic
  /// used internally so both paths stay consistent.
  /// </summary>
  public void CenterOnLevel(RectTransform target) {
    CenterOnNode(target);
    }
  }