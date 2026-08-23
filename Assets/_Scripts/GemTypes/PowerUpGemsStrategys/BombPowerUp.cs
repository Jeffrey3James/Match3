using Match3Game;
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "BombPowerUp", menuName = "Match3/GemType/BombPowerUp")]
public class BombPowerUp : PowerUpGems
{
    private readonly List<Vector2Int> adjacentOffsets = new()
    {
        new Vector2Int(-1, 0),  // Left
        new Vector2Int(1, 0),   // Right
        new Vector2Int(0, -1),  // Down
        new Vector2Int(0, 1),   // Up
        new Vector2Int(-1, -1), // Down-Left
        new Vector2Int(1, -1),  // Down-Right
        new Vector2Int(-1, 1),  // Up-Left
        new Vector2Int(1, 1)    // Up-Right
    };

    protected override void OnEnable()
    {
        powerUpType = PowerUpType.Bomb; // Set the power-up type
    }

    public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
    {
        Debug.Log($"Activating Bomb PowerUp at ({x}, {y}) for gem {gem.name}");
        ExplodeGem(gem, grid, x, y,powerUpAudio);
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

