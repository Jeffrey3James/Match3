using Match3Game;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "HammerPowerUp", menuName = "Match3/GemType/HammerPowerUp")]
public class HammerPowerUp : PowerUpGems
{
    private readonly List<Vector2Int> adjacentOffsets = new()
    {
        new Vector2Int(-1, 0),  // Left
        new Vector2Int(-2, 0),  // Left 2
        new Vector2Int(1, 0),   // Right
        new Vector2Int(2, 0),   // Right 2
        new Vector2Int(0, -1),  // Down
        new Vector2Int(0, -2),  // Down 2
        new Vector2Int(0, 1),   // Up
        new Vector2Int(0, 2),   // Up 2
        new Vector2Int(-1, -1), // Down-Left
        new Vector2Int(-2, -1), // Down-Left 2
        new Vector2Int(1, -1),  // Down-Right
        new Vector2Int(2, -1),  // Down-Right 2
        new Vector2Int(-1, 1),  // Up-Left
        new Vector2Int(-2, 1),  // Up-Left 2
        new Vector2Int(1, 1),    // Up-Right
        new Vector2Int(2, 1)     // Up-Right 2
    };

    protected override void OnEnable()
    {
        powerUpType = PowerUpType.Hammer; // Set the power-up type
    }

    public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
    {
        Debug.Log($"Activating Bomb PowerUp at ({x}, {y}) for gem {gem.name}");
        ExplodeGem(gem, grid, x, y, powerUpAudio);
        foreach (var offset in adjacentOffsets)
        {
            int nx = x + offset.x;
            int ny = y + offset.y;

            if (nx >= 0 && nx < grid.Width && ny >= 0 && ny < grid.Height)
            {
                var gridObj = grid.GetValue(nx, ny);
                if (gridObj != null)
                {
                    var adjacentGem = gridObj.GetGem();
                    if (adjacentGem != null)
                    {
                        if (adjacentGem.GetGemType().gemCategory != GemTypes.GemCategory.Normal)
                        {
                            adjacentGem.Activate();
                        }
                        else
                        {
                            ExplodeGem(adjacentGem, grid, nx, ny, powerUpAudio);
                        }
                    }
                }
            }
        }
    }
}   

