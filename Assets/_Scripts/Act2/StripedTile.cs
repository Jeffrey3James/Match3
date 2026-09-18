// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// Standard match-3 "striped" special: created by a straight 4-in-a-row (or
// 4-in-a-column) match. Clears the entire row when it came from a horizontal
// match, or the entire column when it came from a vertical match.
//
// NOTE on coexistence with Act 1: the existing 4-length match already spawns
// the Act 1 Bomb/Flower power-up (Match3.cs FindMatches(), case 4). This class
// does not change that. Act2SpecialTileManager decides — based on the level
// gate — whether a 4-match spawns the Act 1 Bomb (level < 251) or this Act 2
// Striped tile (level >= 251) INSTEAD of the Act 1 result. See
// Act2SpecialTileManager.cs for exactly how that decision is made.
//
// Follows the same authoring pattern as BombPowerUp.cs etc.:
// [CreateAssetMenu] ScriptableObject holding the activation logic, referenced
// by an on-board Act2SpecialGem instance.
using System.Collections.Generic;
using Match3Game;
using UnityEngine;

namespace Match3Game.Act2
{
    [CreateAssetMenu(fileName = "StripedTile", menuName = "Match3/Act2/StripedTile")]
    public class StripedTile : Act2SpecialTileGemType
    {
        [Header("Striped Placeholder Sprites")]
        [Tooltip("Placeholder sprite name convention: striped_gem_horizontal_[color]. " +
                 "Assign per-color variants here once art exists; art can drop sprites " +
                 "into these fields without any further code changes.")]
        [SerializeField] private Sprite placeholderHorizontalSprite;

        [Tooltip("Placeholder sprite name convention: striped_gem_vertical_[color].")]
        [SerializeField] private Sprite placeholderVerticalSprite;

        protected override void OnEnable()
        {
            base.OnEnable();
            act2SpecialType = Act2SpecialType.Striped;
        }

        /// <summary>Returns the placeholder sprite matching the requested orientation.</summary>
        public Sprite GetSpriteFor(StripedOrientation orientation) =>
            orientation == StripedOrientation.Horizontal ? placeholderHorizontalSprite : placeholderVerticalSprite;

        public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
        {
            var stripedGem = gem as StripedGem;
            StripedOrientation orientation = stripedGem != null ? stripedGem.GetOrientation() : StripedOrientation.Horizontal;

            Debug.Log($"[Act2] Activating Striped tile ({orientation}) at ({x}, {y}) for gem {gem.name}");

            ExplodeGem(gem, grid, x, y, act2SpecialAudio);

            if (orientation == StripedOrientation.Horizontal)
            {
                for (int nx = 0; nx < grid.Width; nx++)
                {
                    if (nx == x) continue;
                    ClearOrChain(grid, nx, y);
                }
            }
            else
            {
                for (int ny = 0; ny < grid.Height; ny++)
                {
                    if (ny == y) continue;
                    ClearOrChain(grid, x, ny);
                }
            }
        }

        private void ClearOrChain(GridSystem2D<GridObj> grid, int nx, int ny)
        {
            var gridObj = grid.GetValue(nx, ny);
            var adjacentGem = gridObj?.GetGem();
            if (adjacentGem == null) return;

            var adjacentType = adjacentGem.GetGemType();
            if (adjacentType.gemCategory != GemTypes.GemCategory.Normal || adjacentType.IsObstacle())
            {
                adjacentGem.Activate(); // Chain into powerups/obstacles, same convention as Act 1 rockets.
            }
            else
            {
                ExplodeGem(adjacentGem, grid, nx, ny, act2SpecialAudio);
            }
        }
    }
}
