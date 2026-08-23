using Match3Game;
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "VerticalRocketPowerUp", menuName = "Match3/GemType/VerticallRocketPowerUp")]
public class VerticalRocketPowerUp : PowerUpGems
{
    protected override void OnEnable()
    {
        powerUpType = PowerUpType.Rocket; // Set the power-up type
    }

    public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
    {
        Debug.Log($"Activating Vertrical Rocket PowerUp at ({x}, {y}) for gem {gem.name}");

        // Explode the rocket gem itself
        ExplodeGem(gem, grid, x, y, powerUpAudio);

        // Iterate over the entire row (all columns, same y)
        for (int ny = 0; ny < grid.Height; ny++)
        {
            if (ny == y) continue; // Skip the origin gem, already exploded

            var gridObj = grid.GetValue(x, ny);
            if (gridObj != null)
            {
                var adjacentGem = gridObj.GetGem();
                if (adjacentGem != null)
                {
                    if (adjacentGem.GetGemType().gemCategory != GemCategory.Normal)
                    {
                        adjacentGem.Activate(); // Trigger chain reaction for powerups
                    }
                    if (adjacentGem.GetGemType().IsObstacle())
                    {
                        adjacentGem.Activate(); // Handle obstacle activation
                    }
                    else
                    {
                        ExplodeGem(adjacentGem, grid, x, ny, powerUpAudio);
                    }
                }
            }
        }
    }
}
