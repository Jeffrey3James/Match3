using UnityEngine;
using Match3Game;
using System.Collections.Generic;

public class LevelEditorRuntime : MonoBehaviour
{
    public Dictionary<int, List<GameObject>> spawnedObstacles = new Dictionary<int, List<GameObject>>();
    public List<Vector2Int> activeLevelShaperGroups = new List<Vector2Int>();

    public Gem gemPrefab;
    public ObstacleGem obstaclePrefab;
    public LevelShaperComponent levelShaperPrefab;

    public GridSystem2D<GridObj> gridToConfigure;
    public GemTypes[] gemTypes;

    public void CreateGem(int x, int y)
    {
        var gem = Instantiate(
            gemPrefab,
            gridToConfigure.GetWorldPositionCenter(x, y),
            Quaternion.identity,
            transform
        );

        gem.SetType(gemTypes[Random.Range(0, gemTypes.Length)]);

        var gridObj = new GridObj(gridToConfigure, x, y);
        gridObj.SetGem(gem);
        gridToConfigure.SetValue(x, y, gridObj);
        gem.Initialize(x, y, gridToConfigure);
    }

    public void ClearGem(int x, int y)
    {
        var gridObj = gridToConfigure.GetValue(x, y);
        if (gridObj != null)
        {
            if(gridObj.GetGem() == null) return;
            DestroyImmediate(gridObj.GetGem().gameObject);
            gridToConfigure.SetValue(x, y, null);
        }
    }

    public void InstantiateObstacleConfigEditor(ObstacleConfig obstacleConfig, int Id)
    {
        for (int i = 0; i < obstacleConfig.GetLocation().Count; i++)
        {
            var location = obstacleConfig.GetLocation()[i];
            var cell = gridToConfigure.GetValue(location.x, location.y);

            ClearGem(location.x, location.y);

            var obstacleGem = Instantiate(
                obstaclePrefab,
                gridToConfigure.GetWorldPositionCenter(location.x, location.y),
                Quaternion.identity,
                transform
            );

            obstacleGem.SetType(obstacleConfig.obstacle);
            obstacleGem.SetHealth(obstacleConfig.GetHealth());

            var gridObj = new GridObj(gridToConfigure, location.x, location.y);
            gridObj.SetGem(obstacleGem);
            gridToConfigure.SetValue(location.x, location.y, gridObj);

            obstacleGem.Initialize(location.x, location.y, gridToConfigure);
            obstacleGem.SetXY(location.x, location.y, gridToConfigure);

            // Store in the list for this obstacle ID
            spawnedObstacles[Id].Add(obstacleGem.gameObject);
        }
    }

    public void InstantiateLevelShaperEditor(List<Vector2Int> positions)
    {
        activeLevelShaperGroups.Clear(); // Clear previous level shaper groups

        for (int i = 0; i < positions.Count; i++)
        {
            var position = positions[i];

            ClearGem(position.x, position.y);
            var levelShaper = Instantiate(
                levelShaperPrefab,
                gridToConfigure.GetWorldPositionCenter(position.x, position.y),
                Quaternion.identity,
                transform
            );

            var gridObj = new GridObj(gridToConfigure, position.x, position.y);
            gridObj.SetLevelShaper(levelShaper);
            gridToConfigure.SetValue(position.x, position.y, gridObj);
            levelShaper.Initialize(position.x, position.y, gridToConfigure);
            activeLevelShaperGroups.Add(new Vector2Int(position.x, position.y));
        }

    }
}

