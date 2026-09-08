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
public class WorldMapUI : MonoBehaviour
{
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
    [SerializeField] private Color currentTint   = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color lockedTint    = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Scene routing")]
    [Tooltip("Scene loaded when the current-level node is tapped. Should match MainMenuUI's " +
             "levelButton scene so the map and the button behave the same.")]
    [SerializeField] private string gameSceneName = "GameScene";

    private readonly List<RectTransform> spawnedNodes = new();

  [Header("Scroll")]
  [Tooltip("The ScrollRect this Content object lives under. Used to auto-center the current node.")]
  [SerializeField] private ScrollRect scrollRect;

  private RectTransform currentNode;

  private void Start()
    {
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

    // at the end of Build(), after the for loop
    if (currentNode != null && scrollRect != null)
      StartCoroutine(CenterOnCurrentNodeNextFrame());
    }

  private IEnumerator CenterOnCurrentNodeNextFrame() {
    yield return new WaitForEndOfFrame();
    CenterOnNode(currentNode);
    }

  private void CenterOnNode(RectTransform node) {
    var content = scrollRect.content;
    var viewport = scrollRect.viewport;
    if (content == null || viewport == null || node == null) return;

    float viewportHeight = viewport.rect.height;
    float contentHeight = content.rect.height;
    float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);

    float nodeY = node.anchoredPosition.y;
    float clampedY = Mathf.Clamp(-nodeY - viewportHeight * 0.5f, 0f, maxScroll);

    content.anchoredPosition = new Vector2(content.anchoredPosition.x, clampedY);
    }

  private void ClearNodes()
    {
        foreach (var n in spawnedNodes)
        {
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

  private void WireNode(RectTransform node, int index, NodeState state)
    {
        var btn = node.GetComponent<Button>();
        if (btn == null) btn = node.GetComponentInChildren<Button>();
        if (btn == null) return;

        btn.onClick.RemoveAllListeners();

        switch (state)
        {
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

    private void OnCurrentTapped()
    {
        if (PlayerHandler.instance == null) { SceneManager.LoadScene(gameSceneName); return; }
        if (!PlayerHandler.instance.CheckPlayerLives()) return;
        PlayerHandler.instance.UseALifeFromPlayer();
        SceneManager.LoadScene(gameSceneName);
    }

    private void ShakeNode(RectTransform node)
    {
        if (node == null) return;
        node.DOKill(true);
    node.DOShakeAnchorPos(0.35f, new Vector2(18f, 0f), 18, 90f, false, true)
.SetLink(node.gameObject)
            .SetUpdate(true);
    }

  public void CenterOnLevel(RectTransform target) {
    Canvas.ForceUpdateCanvases(); // layout must be settled first

    Vector2 viewportSize = scrollRect.viewport.rect.size;
    Vector2 targetLocalPos = scrollRect.content.InverseTransformPoint(target.position);
    Vector2 desiredPos = -targetLocalPos + viewportSize * 0.5f;

    // Clamp so you don't scroll past the content edges
    Vector2 contentSize = scrollRect.content.rect.size;
    float minX = -(contentSize.x - viewportSize.x);
    float minY = -(contentSize.y - viewportSize.y);
    desiredPos.x = Mathf.Clamp(desiredPos.x, minX, 0);
    desiredPos.y = Mathf.Clamp(desiredPos.y, minY, 0);

    scrollRect.content.anchoredPosition = desiredPos;
    }
  }
