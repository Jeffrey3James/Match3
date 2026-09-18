using Match3Game;
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ObjectiveTracker {
  public event Action OnProgressChanged;
  public event Action OnAllObjectivesCompleted;

  private readonly Dictionary<GemTypes, int> remainingByGemType =
      new Dictionary<GemTypes, int>();

  public bool IsComplete {
    get {
      foreach (int remaining in remainingByGemType.Values) {
        if (remaining > 0)
          return false;
        }

      return remainingByGemType.Count > 0;
      }
    }

  public void Initialize(IReadOnlyList<ObjectiveConfig> objectives) {
    remainingByGemType.Clear();

    if (objectives == null) {
      Debug.LogWarning("ObjectiveTracker: Level has no objectives.");
      return;
      }

    foreach (ObjectiveConfig objective in objectives) {
      if (objective.typesToClear == null) {
        Debug.LogWarning("ObjectiveTracker: Objective has no gem type.");
        continue;
        }

      int startingAmount = Mathf.Max(0, objective.amountToClear);

      if (remainingByGemType.ContainsKey(objective.typesToClear)) {
        remainingByGemType[objective.typesToClear] += startingAmount;
        } else {
        remainingByGemType.Add(objective.typesToClear, startingAmount);
        }
      }

    Debug.Log($"ObjectiveTracker initialized with {remainingByGemType.Count} objective type(s).");
    OnProgressChanged?.Invoke();

    if (IsComplete)
      OnAllObjectivesCompleted?.Invoke();
    }

  public void RegisterGemCleared(GemTypes clearedGemType, int amount = 1) {
    if (clearedGemType == null || amount <= 0)
      return;

    if (!remainingByGemType.TryGetValue(clearedGemType, out int remaining))
      return;

    int updatedRemaining = Mathf.Max(0, remaining - amount);

    if (updatedRemaining == remaining)
      return;

    remainingByGemType[clearedGemType] = updatedRemaining;

    Debug.Log(
        $"Objective progress: {clearedGemType.name} " +
        $"{remaining} → {updatedRemaining}");

    OnProgressChanged?.Invoke();

    if (IsComplete) {
      Debug.Log("ALL OBJECTIVES COMPLETE.");
      OnAllObjectivesCompleted?.Invoke();
      }
    }

  public int GetRemaining(GemTypes gemType) {
    if (gemType == null)
      return 0;

    return remainingByGemType.TryGetValue(gemType, out int remaining)
        ? remaining
        : 0;
    }

  public bool IsObjectiveGem(GemTypes gemType) {
    return gemType != null && remainingByGemType.ContainsKey(gemType);
    }
  }