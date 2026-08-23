using Match3Game;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Obstacle", menuName = "Match3/GemType/Obstacle")]
public class Obstacle : GemTypes
{
    private void OnEnable()
    {
        gemCategory = GemCategory.Obstacle; // Set the gem category to Obstacle
        SetIsObstacle(); // Mark this type as an obstacle
    }

    private readonly List<Vector2Int> obstacleChecks = new()
    {
        new Vector2Int(-1, 0),  // Left        
        new Vector2Int(1, 0),   // Right        
        new Vector2Int(0, -1),  // Down        
        new Vector2Int(0, 1),   // Up
    };

    [SerializeField] public List<Sprite> damagedObstacles;

    public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
    {
        ObstacleGem obstacleGem = gem as ObstacleGem;
        obstacleGem.DamageObstacle(); // Call the base activation method
        GameEventsManager.instance.gameEvents.ObstacleDamaged();
    }

    public Obstacle GetObstacleType()
    {
        return this;
    }

    public List<Sprite> GetGemTypeSprites()
    {
        return damagedObstacles;
    }
}

    [System.Serializable]
    public struct ObstacleConfig
    {
        public Obstacle obstacle;
        public List<Vector2Int> locations;
        public int health;

        public ObstacleConfig(Obstacle obstacle, List<Vector2Int> locations, int health)
        {
            this.obstacle = obstacle;
            this.locations = locations;
            this.health = health;
        }

    public List<Vector2Int> GetLocation() => locations;
    public int GetHealth() => health;
}

    public class ObstacleConfigBuilder
    {
        private Obstacle obstacle;
        private List<Vector2Int> locations;
        private int health;

        public ObstacleConfigBuilder SetObstacle(Obstacle obstacle)
        {
            this.obstacle = obstacle;
            return this;
        }

        public ObstacleConfigBuilder SetLocation(List<Vector2Int> locations)
        {
            this.locations = locations;
            return this;
        }

        public ObstacleConfigBuilder SetAmountToSpawn(int amount)
        {
            this.health = amount;
            return this;
        }

        public ObstacleConfig Build()
        {
            return new ObstacleConfig(obstacle, locations, health);
        }
    }

