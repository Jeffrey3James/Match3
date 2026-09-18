// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// Standard match-3 "wrapped" special: created by a 5-tile L-shape or T-shape
// match (an intersection of a horizontal run and a vertical run sharing a
// corner/junction cell — see ShapeMatchDetector.cs, which is the ONLY place
// in the codebase that detects this shape; Match3.FindMatches() itself still
// only does straight-line runs, untouched).
//
// Effect: explodes a 3x3 area centered on the tile, twice (double detonation),
// matching the standard convention of wrapped candy exploding again after the
// board settles from the first blast.
using System.Collections;
using System.Collections.Generic;
using Match3Game;
using UnityEngine;

namespace Match3Game.Act2
{
    [CreateAssetMenu(fileName = "WrappedTile", menuName = "Match3/Act2/WrappedTile")]
    public class WrappedTile : Act2SpecialTileGemType
    {
        [Header("Wrapped Placeholder Sprite")]
        [Tooltip("Placeholder sprite name convention: wrapped_gem_[color]. " +
                 "Assign per-color variants here once art exists.")]
        [SerializeField] private Sprite placeholderSprite;

        [Tooltip("Radius of the square blast, in tiles from center. 1 = 3x3.")]
        [SerializeField] private int blastRadius = 1;

        [Tooltip("Seconds to wait between the first and second detonation.")]
        [SerializeField] private float secondDetonationDelay = 0.35f;

        protected override void OnEnable()
        {
            base.OnEnable();
            act2SpecialType = Act2SpecialType.Wrapped;
        }

        public Sprite GetPlaceholderSprite() => placeholderSprite;

        public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
        {
            Debug.Log($"[Act2] Activating Wrapped tile at ({x}, {y}) for gem {gem.name} (double detonation)");

            // First detonation: explode the tile itself plus the surrounding
            // 3x3 area (blastRadius = 1 by default).
            ExplodeSquare(gem, grid, x, y, includeCenter: true);

            // Second detonation happens after a short delay so falling gems /
            // VFX from the first blast have a beat to read before the area
            // clears again — this needs a live MonoBehaviour to run a
            // coroutine, so we route it through the Act2SpecialTileManager
            // singleton rather than adding coroutine support to this
            // ScriptableObject (ScriptableObjects can't run coroutines).
            Act2SpecialTileManager.RunDelayedAction(secondDetonationDelay, () => ExplodeSquare(null, grid, x, y, includeCenter: false, secondPass: true));
        }

        private void ExplodeSquare(Gem originGem, GridSystem2D<GridObj> grid, int cx, int cy, bool includeCenter, bool secondPass = false)
        {
            if (includeCenter && originGem != null)
            {
                ExplodeGem(originGem, grid, cx, cy, act2SpecialAudio);
            }

            for (int dx = -blastRadius; dx <= blastRadius; dx++)
            {
                for (int dy = -blastRadius; dy <= blastRadius; dy++)
                {
                    if (dx == 0 && dy == 0) continue; // center handled above (first pass) or already empty (second pass)

                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;

                    var gridObj = grid.GetValue(nx, ny);
                    var target = gridObj?.GetGem();
                    if (target == null) continue;

                    var targetType = target.GetGemType();
                    if (targetType.gemCategory != GemTypes.GemCategory.Normal || targetType.IsObstacle())
                    {
                        target.Activate(); // Chain into other powerups/obstacles, same convention as Act 1 Bomb/Hammer.
                    }
                    else
                    {
                        ExplodeGem(target, grid, nx, ny, act2SpecialAudio);
                    }
                }
            }
        }
    }
}
