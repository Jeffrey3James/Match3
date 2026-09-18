using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Match3Game;

public class LevelOjectiveBanner : MonoBehaviour {
  [SerializeField] private Match3 match3;
  [SerializeField] private Transform container;
  [SerializeField] private RectTransform icon;

  private void Start() {
    if (container == null || icon == null) return;

    for (int i = container.childCount - 1; i >= 0; i--)
      Destroy(container.GetChild(i).gameObject);

    Level level = match3 != null ? match3.GetLevel() : null;
    if (level == null) {
      Debug.Log("No Match 3 level found");
      return;
      }

    SpawnObjectives(level);
    SpawnObstacles(level);
    }

  private void SpawnObjectives(Level level) {
    List<ObjectiveConfig> objectives = level.GetObjectives();
    if (objectives == null) {
      Debug.Log("No Objectives found");
      return;
      }

    var seen = new HashSet<ObjectiveConfig>();
    foreach (var cfg in objectives) {
      if (cfg.typesToClear == null) continue;
      if (!seen.Add(cfg)) continue;
      SpawnIcon(cfg.typesToClear.sprite, "x" + cfg.amountToClear);
      }
    }

  private void SpawnObstacles(Level level) {
    List<ObstacleConfig> obstacles = level.GetObstacles(); // confirm actual method name
    if (obstacles == null) {
      Debug.Log("No Obstacles found");
      return;
      }

    // De-dupe by the Obstacle asset reference, not the struct itself —
    // ObstacleConfig holds a List<Vector2Int>, which breaks default struct equality.
    var seen = new HashSet<Obstacle>();
    foreach (var cfg in obstacles) {
      if (cfg.obstacle == null) continue;
      if (!seen.Add(cfg.obstacle)) continue;
      int count = cfg.GetLocation() != null ? cfg.GetLocation().Count : 0;
      SpawnIcon(cfg.obstacle.sprite, "x" + count);
      }
    }

  private void SpawnIcon(Sprite sprite, string labelText) {
    var iconGO = Instantiate(icon.gameObject, container);
    var img = iconGO.GetComponentInChildren<Image>();
    if (img != null) img.sourceImage = sprite;
    var label = iconGO.GetComponentInChildren<TMPro.TextMeshProUGUI>();
    if (label != null) label.text = labelText;
    }
  }