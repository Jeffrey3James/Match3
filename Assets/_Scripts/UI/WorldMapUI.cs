using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

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
    [Tooltip("Node prefab — should have an Image called nodeBg, a TMP label for the level number, " +
             "and a Button to receive clicks. See PR body for the exact wiring.")]
    [SerializeField] private RectTransform _nodePrefab;

    [Tooltip("Vertical spacing between nodes in the ScrollView Content.")]
    [SerializeField] private float verticalSpacing = 180f;

    [Tooltip("Peak horizontal wobble amplitude, in pixels. Formula: sin(i*0.6) * offset.")]
    [SerializeField] private float horizontalOffset = 220f;

    [Header("State Sprites (optional)")]
    [Tooltip("Sprite used for a completed level's chip. Falls back to a tint if left empty.")]
    [SerializeField] private Sprite completedSprite;
    [SerializeField] private Sprite currentSprite;
    [SerializeField] private Sprite lockedSprite;

    [Header("Fallback Tints")]
    [SerializeField] private Color completedTint = new Color(0.4f, 0.9f, 0.5f, 1f);
    [SerializeField] private Color currentTint   = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private Color lockedTint    = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Scene routing")]
    [Tooltip("Scene loaded when the current-level node is tapped. Should match MainMenuUI's " +
             "levelButton scene so the map and the button behave the same.")]
    [SerializeField] private string gameSceneName = "GameScene";

    private readonly List<RectTransform> spawnedNodes = new();

    private void Start()
    {
        Build();
    }

    /// <summary>
    /// Rebuild the map. Safe to call after a level completes if you want the current-node
    /// halo to shift up without a full scene reload.
    /// </summary>
    public void Build()
    {
        if (_nodePrefab == null)
        {
            Debug.LogError("WorldMapUI: _nodePrefab is unassigned. No nodes will spawn.");
            return;
        }
        if (LevelHandler.instance == null)
        {
            Debug.LogWarning("WorldMapUI: LevelHandler.instance is null. Map will populate later.");
            return;
        }

        ClearNodes();

        var levels = LevelHandler.instance.GetAllLevels();
        int count = levels != null ? levels.Count : 0;
        int currentPlayerLevel = PlayerHandler.instance != null
            ? PlayerHandler.instance.GetPlayerLevel()
            : 0;

        // The RectTransform we live on has to be tall enough to actually scroll all nodes.
        var contentRect = transform as RectTransform;
        if (contentRect != null)
        {
            Vector2 size = contentRect.sizeDelta;
            size.y = Mathf.Max(size.y, (count + 1) * verticalSpacing);
            contentRect.sizeDelta = size;
        }

        for (int i = 0; i < count; i++)
        {
            var node = Instantiate(_nodePrefab, transform);
            spawnedNodes.Add(node);

            // Spiral / sine wobble. i grows bottom-up so higher levels sit up-screen.
            float x = Mathf.Sin(i * 0.6f) * horizontalOffset;
            float y = i * verticalSpacing;
            node.anchoredPosition = new Vector2(x, y);

            NodeState state = i < currentPlayerLevel ? NodeState.Completed
                            : i == currentPlayerLevel ? NodeState.Current
                            : NodeState.Locked;

            PaintNode(node, i, state);
            WireNode(node, i, state);
        }
    }

    private void ClearNodes()
    {
        foreach (var n in spawnedNodes)
        {
            if (n != null) Destroy(n.gameObject);
        }
        spawnedNodes.Clear();
    }

    private void PaintNode(RectTransform node, int index, NodeState state)
    {
        // Try to find a background Image on the prefab — first one found gets tinted / swapped.
        var img = node.GetComponentInChildren<Image>();
        if (img != null)
        {
            Sprite s = state == NodeState.Completed ? completedSprite
                     : state == NodeState.Current ? currentSprite
                     : lockedSprite;
            if (s != null) img.sprite = s;
            else img.color = state == NodeState.Completed ? completedTint
                            : state == NodeState.Current ? currentTint
                            : lockedTint;
        }

        // Label — level number (1-based for the player).
        var label = node.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.text = (index + 1).ToString();

        // Current node pulses so the player can find it after scrolling.
        if (state == NodeState.Current)
        {
            node.DOKill();
            node.localScale = Vector3.one;
            node.DOScale(1.12f, 0.6f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
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
            .SetUpdate(true);
    }
}
