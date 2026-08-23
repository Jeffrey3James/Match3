using System.Collections.Generic;
using UnityEngine;
using Match3Game;

using Unity.VisualScripting;

[CreateAssetMenu(fileName = "NukePowerUp", menuName = "Match3/GemType/NukePowerUp")]
public class NukePowerUp : PowerUpGems
{
    [System.Serializable]
    public class NukeSpriteMapping
    {
        public GemTypes gemType;
        public Sprite sprite;
    }

    [SerializeField] private List<NukeSpriteMapping> nukeSprites;
    [SerializeField] private NukeGem nukeGem;

    public Sprite GetNukeSprite(GemTypes targetType)
    {
        foreach (var mapping in nukeSprites)
        {
            if (mapping.gemType == targetType)
                return mapping.sprite;
        }
        return null;
    }

 

    public override void Activate(Gem gem, int x, int y, GridSystem2D<GridObj> grid)
    {
    Debug.Log($"Activating Nuke PowerUp at ({x}, {y}) for gem {gem.name}");

    Vector2Int[] cardinalOffsets = new Vector2Int[]
    {
            new Vector2Int(0, 1),   // Up
            new Vector2Int(0, -1),  // Down
            new Vector2Int(1, 0),   // Right
            new Vector2Int(-1, 0)   // Left
    };
    HashSet<Vector2Int> affectedPositions = new HashSet<Vector2Int>();
    List<Gem> gemsToBeDestroyed = new List<Gem>();
    List<Vector2Int> gridObjectsToBeDestroyed = new List<Vector2Int>();

    gridObjectsToBeDestroyed.Clear();
    var powerUp = gem as PowerUp;
    if (powerUp == null)
    {
        Debug.LogError("This gem is not a PowerUpGem!");
        return;
    }

    ExplodeGem(gem, grid, x, y, powerUpAudio);

    for (int i = 0; i < grid.Width; i++)
    {
        for (int j = 0; j < grid.Height; j++)
        {
            var gridObjAtPosition = grid.GetValue(i, j);
            if (gridObjAtPosition == null) continue;

            var targetGem = gridObjAtPosition.GetGem();
            if (targetGem == null) continue;

            var someTargetType = targetGem.GetGemType();
            if (powerUp.GetTypeToDestroy() == someTargetType)
            {
                ExplodeGem(targetGem, grid, i, j, powerUpAudio);
                gemsToBeDestroyed.Add(targetGem);

                Vector2Int gridPosition = new Vector2Int(i, j);
                gridObjectsToBeDestroyed.Add(gridPosition);
            }
            foreach (var g in gridObjectsToBeDestroyed)
            {
                foreach (var offset in cardinalOffsets)
                    {
                      Vector2Int obstaclePos = g + offset;
                        // Skip if already affected
                        if (affectedPositions.Contains(obstaclePos)) continue;
                        affectedPositions.Add(obstaclePos);

                    var neighborGridObj = grid.GetValue(obstaclePos.x, obstaclePos.y);
                    var neighborGem = neighborGridObj?.GetGem();

                    if (neighborGem != null)
                    {
                        GemTypes neighborGemType = neighborGem.GetGemType();

                        if (neighborGemType.IsObstacle())
                        {
                            neighborGemType.Activate(neighborGem, obstaclePos.x, obstaclePos.y, grid);
                        }
                    }
                }
            }
        }
    }
    }

}