using Match3Game;
using System.Collections.Generic;
using UnityEngine;

namespace Match3Game
{
    [CreateAssetMenu(fileName = "GemType", menuName = "Match3/GemType/NormalGem")]
    public class GemTypes : ScriptableObject
    {
        public Sprite sprite;

        public enum GemCategory { Normal, PowerUp, Obstacle }
        public GemCategory gemCategory;
        [SerializeField] private bool isObstacle;

        public bool IsObstacle() => isObstacle;
        public void SetIsObstacle() => isObstacle = true;

        public virtual void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
        {
            CheckForObstacle(x, y, grid); // Check for obstacles before activating
            Debug.Log($"Activating {gem.name} of type {name}");
        }

        public virtual bool CheckForObstacle(int x, int y, GridSystem2D<GridObj> grid)
        {
            var gridObj = grid.GetValue(x, y);
            var gem = gridObj?.GetGem();

            if (gem == null) return false;

            var gemType = gem.GetGemType();
            if (gemType is Obstacle obstacle)
            {
                obstacle.Activate(gem, x, y, grid);
                return true;
            }

            return false; // Not an obstacle
        }
    }
}

[System.Serializable]
public struct ObjectiveConfig
{
    public GemTypes typesToClear;
    public int amountToClear;

    public ObjectiveConfig(GemTypes gemTypes, int amountToClear)
    {
        this.typesToClear = gemTypes;
        this.amountToClear = amountToClear;
    }


    public GemTypes GetObjectiveConfigGemType() => typesToClear;
    public int GetObjectiveConfigAmountToClear() => amountToClear;
}
