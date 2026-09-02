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

        // Fire two seeking projectiles — one to the left, one to the right —
        // along y. Each projectile walks cell-by-cell and may bias one row
        // up or down toward the nearest remaining objective tile (seeking).
        FireSeekingProjectile(grid, x, y, +1, 0);
        FireSeekingProjectile(grid, x, y, -1, 0);
    }

    /// <summary>
    /// Advance one cell at a time along (stepX, stepY). At each step, if the
    /// current cell is not itself an objective tile but an objective tile
    /// still exists somewhere on the remaining path, the projectile may take
    /// a single diagonal hop toward the nearest objective. Rate-limited to
    /// one diagonal per three straight hops so the motion still reads as
    /// "seeking" and not "teleporting". Every visited cell (straight or
    /// biased) is exploded via the standard PowerUpGems.ExplodeGem path.
    /// </summary>
    private void FireSeekingProjectile(GridSystem2D<GridObj> grid, int startX, int startY,
                                       int stepX, int stepY)
    {
        int cx = startX + stepX;
        int cy = startY + stepY;
        int straightSinceDiagonal = 0;
        int safety = grid.Width + grid.Height + 4; // hard cap in case of grid weirdness

        while (cx >= 0 && cx < grid.Width && cy >= 0 && cy < grid.Height && safety-- > 0)
        {
            // Consult the board for still-alive objective tiles. Match3.Instance
            // may be null in isolated unit tests or during teardown — degrade to
            // straight-line behavior in that case.
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
                // Diagonal step — explode this cell, then hop one cell toward
                // the objective on the perpendicular axis before continuing.
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

    /// <summary>
    /// Chooses the perpendicular hop (±1 on the axis not being travelled) that
    /// moves closer to the nearest objective. Returns false when no
    /// objectives are ahead on the remaining path (in which case the caller
    /// stays straight). Only "ahead" objectives count so the projectile
    /// doesn't spin backwards.
    /// </summary>
    private static bool TryPickNearestObjectiveBias(List<Vector2Int> objectives,
                                                    int cx, int cy, int stepX, int stepY,
                                                    out int biasX, out int biasY)
    {
        biasX = 0; biasY = 0;
        Vector2Int? best = null;
        int bestDist = int.MaxValue;

        foreach (var o in objectives)
        {
            // "Ahead" test: signed delta along the travel axis must be >= 0.
            int dx = o.x - cx;
            int dy = o.y - cy;
            if (stepX != 0 && System.Math.Sign(dx) != System.Math.Sign(stepX) && dx != 0) continue;
            if (stepY != 0 && System.Math.Sign(dy) != System.Math.Sign(stepY) && dy != 0) continue;

            int d = System.Math.Abs(dx) + System.Math.Abs(dy);
            if (d < bestDist) { bestDist = d; best = o; }
        }

        if (!best.HasValue) return false;

        // Horizontal rocket travels along X; bias on Y.
        if (stepX != 0)
        {
            int dy = best.Value.y - cy;
            if (dy == 0) return false; // already aligned; no bias needed
            biasY = dy > 0 ? 1 : -1;
        }
        else
        {
            int dx = best.Value.x - cx;
            if (dx == 0) return false;
            biasX = dx > 0 ? 1 : -1;
        }
        return true;
    }

    /// <summary>
    /// Apply the standard rocket destruction rules at (nx, ny): power-up
    /// chain-reacts, obstacle takes damage, normal gem explodes. Identical
    /// to the pre-seek loop body, factored out so straight and diagonal
    /// steps go through one path.
    /// </summary>
    private void ExplodeAt(GridSystem2D<GridObj> grid, int nx, int ny)
    {
        var gridObj = grid.GetValue(nx, ny);
        if (gridObj == null) return;
        var adjacentGem = gridObj.GetGem();
        if (adjacentGem == null) return;

        if (adjacentGem.GetGemType().gemCategory != GemCategory.Normal)
        {
            adjacentGem.Activate(); // Trigger chain reaction for powerups
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
