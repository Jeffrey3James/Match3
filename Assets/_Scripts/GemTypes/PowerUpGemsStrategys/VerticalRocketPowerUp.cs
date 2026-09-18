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
        Debug.Log($"Activating Vertical Rocket PowerUp at ({x}, {y}) for gem {gem.name}");

        // Explode the rocket gem itself
        ExplodeGem(gem, grid, x, y, powerUpAudio);

        // Two seeking projectiles — one up, one down. Same rules as horizontal:
        // seek toward the nearest objective, max one column-hop per three
        // straight hops. See HorizontalRocketPowerUp for the full doc.
        FireSeekingProjectile(grid, x, y, 0, +1);
        FireSeekingProjectile(grid, x, y, 0, -1);
    }

    private void FireSeekingProjectile(GridSystem2D<GridObj> grid, int startX, int startY,
                                       int stepX, int stepY)
    {
        int cx = startX + stepX;
        int cy = startY + stepY;
        int straightSinceDiagonal = 0;
        int safety = grid.Width + grid.Height + 4;

        while (cx >= 0 && cx < grid.Width && cy >= 0 && cy < grid.Height && safety-- > 0)
        {
            List<Vector2Int> objectivePositions = null;
            if (Match3.Instance != null)
                objectivePositions = Match3.Instance.GetActiveObjectivePositions();

            bool isCurrentObjective = objectivePositions != null &&
                                      objectivePositions.Contains(new Vector2Int(cx, cy));

            bool canDiagonalHop = objectivePositions != null && objectivePositions.Count > 0
                                  && !isCurrentObjective
                                  && straightSinceDiagonal >= 3;

            if (canDiagonalHop && TryPickNearestObjectiveBias(objectivePositions,
                                                              cx, cy, stepX, stepY,
                                                              out int biasX, out int biasY))
            {
                ExplodeAt(grid, cx, cy);
                cx += biasX;
                cy += biasY;
                if (cx < 0 || cx >= grid.Width || cy < 0 || cy >= grid.Height) break;
                ExplodeAt(grid, cx, cy);
                straightSinceDiagonal = 0;
            }
            else
            {
                ExplodeAt(grid, cx, cy);
                straightSinceDiagonal++;
            }

            cx += stepX;
            cy += stepY;
        }
    }

    private static bool TryPickNearestObjectiveBias(List<Vector2Int> objectives,
                                                    int cx, int cy, int stepX, int stepY,
                                                    out int biasX, out int biasY)
    {
        biasX = 0; biasY = 0;
        Vector2Int? best = null;
        int bestDist = int.MaxValue;

        foreach (var o in objectives)
        {
            int dx = o.x - cx;
            int dy = o.y - cy;
            if (stepX != 0 && System.Math.Sign(dx) != System.Math.Sign(stepX) && dx != 0) continue;
            if (stepY != 0 && System.Math.Sign(dy) != System.Math.Sign(stepY) && dy != 0) continue;

            int d = System.Math.Abs(dx) + System.Math.Abs(dy);
            if (d < bestDist) { bestDist = d; best = o; }
        }

        if (!best.HasValue) return false;

        // Vertical rocket travels along Y; bias on X.
        if (stepY != 0)
        {
            int dx = best.Value.x - cx;
            if (dx == 0) return false;
            biasX = dx > 0 ? 1 : -1;
        }
        else
        {
            int dy = best.Value.y - cy;
            if (dy == 0) return false;
            biasY = dy > 0 ? 1 : -1;
        }
        return true;
    }

    private void ExplodeAt(GridSystem2D<GridObj> grid, int nx, int ny)
    {
        var gridObj = grid.GetValue(nx, ny);
        if (gridObj == null) return;
        var adjacentGem = gridObj.GetGem();
        if (adjacentGem == null) return;

        if (adjacentGem.GetGemType().gemCategory != GemCategory.Normal)
        {
            adjacentGem.Activate();
        }
        if (adjacentGem.GetGemType().IsObstacle())
        {
            adjacentGem.Activate();
        }
        else
        {
            ExplodeGem(adjacentGem, grid, nx, ny, powerUpAudio);
        }
    }
}
