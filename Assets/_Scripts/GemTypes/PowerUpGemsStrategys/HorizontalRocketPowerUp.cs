using Match3Game;
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "HorizontalRocketPowerUp", menuName = "Match3/GemType/HorizontalRocketPowerUp")]
public class HorizontalRocketPowerUp : PowerUpGems
{ 

    protected override void OnEnable()
    {
        base.OnEnable();
        powerUpType = PowerUpType.Rocket; // Set the power-up type
    }

    public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
    {
        Debug.Log($"Activating Horizontal Rocket PowerUp at ({x}, {y}) for gem {gem.name}");

        // Explode the rocket gem itself
        ExplodeGem(gem, grid, x, y, powerUpAudio);

        // Iterate over the entire row (all columns, same y)
        for (int nx = 0; nx < grid.Width; nx++)
        {
            if (nx == x) continue; // Skip the origin gem, already exploded

            var gridObj = grid.GetValue(nx, y);
            if (gridObj != null)
            {
                var adjacentGem = gridObj.GetGem();
                if (adjacentGem != null)
                {
                    if (adjacentGem.GetGemType().gemCategory != GemCategory.Normal)
                    {
                        adjacentGem.Activate(); // Trigger chain reaction for powerups
                    }
                     if(adjacentGem.GetGemType().IsObstacle())
                    {
                        adjacentGem.Activate();
                    }
                     else
                    { 
                        ExplodeGem(adjacentGem, grid, nx, y, powerUpAudio); 
                    }
                }
            }
        }
    }
}

