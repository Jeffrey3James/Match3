// ACT 2 ADDITION — gated behind player level 251+. This file IS the activation gate.
//
// ============================================================================
// ACT 1 / ACT 2 COEXISTENCE — READ THIS FIRST
// ============================================================================
// The existing Act 1 system (Assets/_Scripts/Match3.cs FindMatches(), plus
// BombPowerUp/Horizontal+VerticalRocketPowerUp/HammerPowerUp/MissilePowerUp/
// NukePowerUp under Assets/_Scripts/GemTypes/PowerUpGemsStrategys/) is left
// COMPLETELY UNTOUCHED. Its switch(group.Count) ladder (4->Bomb, 5->Rocket,
// 6->Hammer, 7->Missile, 8->Nuke) still runs on every match, exactly as before.
//
// Decision for how both systems coexist (per task instructions):
//   - Below player level 251: Act 2 is fully inactive. Matches behave EXACTLY
//     as they do today — Act 1 only. This class's gate (IsActive()) returns
//     false and every Act2 hook becomes a no-op.
//   - At/above player level 251: for the SPECIFIC match lengths where the
//     standard combo table and the Act 1 ladder overlap (4-length and
//     5-length straight matches), Act 2 REPLACES the Act 1 outcome for that
//     match — i.e. a 4-match spawns a Striped tile INSTEAD OF a Bomb, and a
//     5-length STRAIGHT match spawns a Color Bomb INSTEAD OF a Rocket. This
//     is implemented by having Match3.FindMatches() ask
//     Act2SpecialTileManager.TryOverridePowerupSpawn(...) before it commits
//     an Act 1 powerupSpawns.Add(...) call for case 4 / case 5; if Act 2 is
//     active, the Act 1 add is skipped and the Act 2 tile is queued instead.
//     Cases 6/7/8 (Hammer/Missile/Nuke) are NEVER touched by Act 2 — they
//     don't correspond to anything in the standard combo table, so Act 1
//     keeps spawning them unconditionally at any player level.
//   - The L/T-shape (5-tile intersection) trigger has NO Act 1 equivalent at
//     all (Act 1 has no shape detection), so it only ever produces a Wrapped
//     tile, and only when the gate is active; below level 251 it produces
//     nothing extra (the two straight 3-runs that make up the shape still
//     clear normally as ordinary 3-matches, just with no bonus tile).
//
// This file is the single source of truth for the level-251 gate. Search for
// "Act2SpecialTileManager" in Match3.cs to find the exact (small) hook.
// ============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using Match3Game;
using UnityEngine;

namespace Match3Game.Act2
{
    public class Act2SpecialTileManager : MonoBehaviour
    {
        /// <summary>
        /// The level-gate constant. "The implementation of the Act 2 stuff
        /// should be automatically activated once the player level 251" —
        /// exact wording from the task. Change this single constant if the
        /// gate level ever needs to move.
        /// </summary>
        public const int Act2UnlockLevel = 251;

        public static Act2SpecialTileManager instance { get; private set; }

        [Header("Act 2 Prefabs (mirrors Match3's powerUpPrefab/nukePrefab fields)")]
        [SerializeField] private StripedGem stripedGemPrefab;
        [SerializeField] private WrappedGem wrappedGemPrefab;
        [SerializeField] private ColorBombGem colorBombGemPrefab;

        [Header("Act 2 GemType Assets (ScriptableObjects, assign in Inspector)")]
        [SerializeField] private StripedTile stripedTileAsset;
        [SerializeField] private WrappedTile wrappedTileAsset;
        [SerializeField] private ColorBombTile colorBombTileAsset;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>
        /// THE level gate. Reads the same player-progress field Act 1 uses
        /// for everything else (PlayerHandler.playerData.playerLevel, exposed
        /// via the new PlayerHandler.GetPlayerLevel() accessor added alongside
        /// this file) — no separate/duplicate progress tracker was introduced.
        /// </summary>
        public static bool IsActive()
        {
            if (PlayerHandler.instance == null) return false;
            return PlayerHandler.instance.GetPlayerLevel() >= Act2UnlockLevel;
        }

        /// <summary>
        /// Called from Match3.FindMatches() for match lengths 4 and 5 (the two
        /// lengths where Act 1 and the standard combo table overlap) BEFORE the
        /// Act 1 powerupSpawns.Add(...) call for that case. Returns true if Act 2
        /// handled (queued) this match and the caller should skip its Act 1 add;
        /// returns false (gate inactive, or an unsupported length) to leave the
        /// existing Act 1 behavior completely alone.
        /// </summary>
        public bool TryOverridePowerupSpawn(List<Vector2Int> group, GemTypes matchedGemType)
        {
            if (!IsActive() || instance == null) return false;
            if (group == null || group.Count == 0) return false;

            switch (group.Count)
            {
                case 4:
                    QueueStriped(group, matchedGemType);
                    return true;
                case 5:
                    QueueColorBomb(group, matchedGemType);
                    return true;
                default:
                    return false; // 6/7/8 stay Act 1 (Hammer/Missile/Nuke) unconditionally.
            }
        }

        private readonly List<PendingSpawn> pendingSpawns = new();

        private struct PendingSpawn
        {
            public Vector2Int position;
            public Act2SpecialType type;
            public StripedOrientation orientation;
            public GemTypes colorToTarget;
        }

        private void QueueStriped(List<Vector2Int> group, GemTypes matchedGemType)
        {
            // Orientation is derived the same way Match3.FindMatches() already
            // distinguishes horizontal vs vertical runs: if every cell in the
            // group shares the same y, it was a horizontal run.
            bool isHorizontal = true;
            int firstY = group[0].y;
            for (int i = 1; i < group.Count; i++)
            {
                if (group[i].y != firstY) { isHorizontal = false; break; }
            }

            pendingSpawns.Add(new PendingSpawn
            {
                position = group[0],
                type = Act2SpecialType.Striped,
                orientation = isHorizontal ? StripedOrientation.Horizontal : StripedOrientation.Vertical,
                colorToTarget = null
            });
        }

        private void QueueColorBomb(List<Vector2Int> group, GemTypes matchedGemType)
        {
            pendingSpawns.Add(new PendingSpawn
            {
                position = group[0],
                type = Act2SpecialType.ColorBomb,
                orientation = default,
                colorToTarget = matchedGemType
            });
        }

        /// <summary>
        /// Queues a Wrapped tile for an L/T-shape match found by
        /// ShapeMatchDetector. Only ever called when IsActive() is true (the
        /// Match3.cs hook checks the gate before calling ShapeMatchDetector at
        /// all), so no redundant gate check is needed here.
        /// </summary>
        public void QueueWrapped(Vector2Int spawnPosition)
        {
            pendingSpawns.Add(new PendingSpawn
            {
                position = spawnPosition,
                type = Act2SpecialType.Wrapped,
                orientation = default,
                colorToTarget = null
            });
        }

        /// <summary>
        /// Mirrors Match3.SpawnPowerups()/CreatePowerUpGem(): instantiates the
        /// queued Act 2 specials into the grid. Called from the same place in
        /// Match3's game loop, right alongside SpawnPowerups().
        /// </summary>
        public void SpawnQueuedSpecials(GridSystem2D<GridObj> grid, Transform parent)
        {
            foreach (var pending in pendingSpawns)
            {
                if (grid.GetValue(pending.position.x, pending.position.y) != null) continue; // Cell already filled (e.g. by an Act 1 spawn this frame).

                Gem gemInstance = null;
                Vector3 worldPos = grid.GetWorldPositionCenter(pending.position.x, pending.position.y);

                switch (pending.type)
                {
                    case Act2SpecialType.Striped:
                        var striped = Instantiate(stripedGemPrefab, worldPos, Quaternion.identity, parent);
                        striped.SetType(stripedTileAsset);
                        striped.SetOrientation(pending.orientation);
                        gemInstance = striped;
                        break;

                    case Act2SpecialType.Wrapped:
                        var wrapped = Instantiate(wrappedGemPrefab, worldPos, Quaternion.identity, parent);
                        wrapped.SetType(wrappedTileAsset);
                        gemInstance = wrapped;
                        break;

                    case Act2SpecialType.ColorBomb:
                        var colorBomb = Instantiate(colorBombGemPrefab, worldPos, Quaternion.identity, parent);
                        colorBomb.SetType(colorBombTileAsset);
                        colorBomb.SetTargetGemType(pending.colorToTarget);
                        gemInstance = colorBomb;
                        break;
                }

                if (gemInstance != null)
                {
                    var gridObj = new GridObj(grid, pending.position.x, pending.position.y);
                    gridObj.SetGem(gemInstance);
                    grid.SetValue(pending.position.x, pending.position.y, gridObj);
                }
            }

            pendingSpawns.Clear();
        }

        public int PendingSpawnCount => pendingSpawns.Count;

        /// <summary>
        /// Small coroutine-runner helper so WrappedTile (a ScriptableObject,
        /// which cannot run coroutines itself) can schedule its second
        /// detonation. No-op (no-throw) if no manager instance exists yet,
        /// e.g. in editor/unit-test contexts.
        /// </summary>
        public static void RunDelayedAction(float delaySeconds, Action action)
        {
            if (instance == null || action == null) return;
            instance.StartCoroutine(DelayedActionCoroutine(delaySeconds, action));
        }

        private static IEnumerator DelayedActionCoroutine(float delaySeconds, Action action)
        {
            yield return new WaitForSeconds(delaySeconds);
            action();
        }
    }
}
