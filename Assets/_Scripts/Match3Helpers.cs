using Match3Game;
using UnityEngine;

public static class PowerupUtils
{
    public static void TryExplodeOrTrigger(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
    {
        if (gem == null) return;

        var type = gem.GetGemType();
        if (type.CheckForObstacle(x, y, grid)) return;

        if (type.gemCategory != GemTypes.GemCategory.Normal)
        {
            gem.Activate(); // trigger chain reaction
        }

        Object.Destroy(gem.gameObject, 0.1f);
        grid.SetValue(x, y, null);
    }
}