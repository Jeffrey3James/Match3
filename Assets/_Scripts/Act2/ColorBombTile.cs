// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// Standard match-3 "color bomb" / rainbow special: created by a straight
// 5-in-a-row (or 5-in-a-column) match. Clears EVERY tile of one matched gem
// color currently on the board.
//
// NOTE on coexistence with Act 1: the existing 5-length match already spawns
// a random Act 1 Rocket (Horizontal/VerticalRocketPowerUp, Match3.cs
// FindMatches(), case 5). This class does not change that trigger. Below the
// level-251 gate, 5-matches keep spawning Act 1 Rockets exactly as today.
// At/above the gate, Act2SpecialTileManager routes 5-matches to THIS class
// instead of the Act 1 Rocket. See Act2SpecialTileManager.cs.
//
// This is intentionally NOT the same as NukePowerUp (Assets/_Scripts/GemTypes/
// PowerUpGemsStrategys/NukePowerUp.cs), even though both clear "all of one
// color": NukePowerUp is an Act 1 reward gated behind an 8-length match and
// is left completely untouched. ColorBombTile is the Act 2 standard-table
// equivalent, gated behind a 5-length straight match + the level-251 check.
using System.Collections.Generic;
using Match3Game;
using UnityEngine;

namespace Match3Game.Act2
{
    [CreateAssetMenu(fileName = "ColorBombTile", menuName = "Match3/Act2/ColorBombTile")]
    public class ColorBombTile : Act2SpecialTileGemType
    {
        [Header("Color Bomb Placeholder Sprite")]
        [Tooltip("Placeholder sprite name convention: colorbomb_gem (single sprite, " +
                 "no per-color variant — the color it clears is chosen at activation " +
                 "time from the match that spawned it, like NukeGem's target color).")]
        [SerializeField] private Sprite placeholderSprite;

        protected override void OnEnable()
        {
            base.OnEnable();
            act2SpecialType = Act2SpecialType.ColorBomb;
        }

        public Sprite GetPlaceholderSprite() => placeholderSprite;

        public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
        {
            var colorBombGem = gem as ColorBombGem;
            GemTypes targetType = colorBombGem != null ? colorBombGem.GetTargetGemType() : null;

            Debug.Log($"[Act2] Activating Color Bomb at ({x}, {y}) for gem {gem.name}, target={(targetType != null ? targetType.name : "null")}");

            ExplodeGem(gem, grid, x, y, act2SpecialAudio);

            if (targetType == null) return;

            for (int i = 0; i < grid.Width; i++)
            {
                for (int j = 0; j < grid.Height; j++)
                {
                    var gridObj = grid.GetValue(i, j);
                    var targetGem = gridObj?.GetGem();
                    if (targetGem == null) continue;

                    if (targetGem.GetGemType() == targetType)
                    {
                        ExplodeGem(targetGem, grid, i, j, act2SpecialAudio);
                    }
                }
            }
        }
    }
}
