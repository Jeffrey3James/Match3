using Match3Game;
using UnityEngine;

[CreateAssetMenu(fileName = "MissilePowerUp", menuName = "Match3/GemType/MissilePowerUp")]
public class MissilePowerUp : PowerUpGems 
{
    protected override void OnEnable()
    {
        base.OnEnable();
        powerUpType = PowerUpType.Missile; // Set the power-up type
    }

    public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
    {
        Debug.Log($"Activating Missile PowerUp at ({x}, {y}) for gem {gem.name}");

        ExplodeGem(gem, grid, x, y, powerUpAudio);
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
                    if (adjacentGem.GetGemType().IsObstacle())
                    {
                        base.ExplodeGem(adjacentGem, grid, nx, y, powerUpAudio);
                    }
                    ExplodeGem(adjacentGem, grid, nx, y, powerUpAudio);
                }
            }
        }
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
                    else
                    {
                        ExplodeGem(adjacentGem, grid, x, ny, powerUpAudio);
                    }

                }
            }
        }

    }
}



