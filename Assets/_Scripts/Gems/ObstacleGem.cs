using Match3Game;
using System.Runtime.CompilerServices;
using UnityEngine;
using System.Collections.Generic;
using Unity.VisualScripting;

public class ObstacleGem : Gem
{

    private int startingObstacleHealth= -1;
    private int currentObstacleHealth;
    public bool IsObstacle => type.IsObstacle();

    private const int SIXTY_PERCENT = 60;
    private const int THIRTY_PERCENT = 30;


    public override void Initialize(int x, int y, GridSystem2D<GridObj> grid)
    {
        base.Initialize(x, y, grid);
        // Only if it's an obstacle
        if(GameEventsManager.instance == null) return;
        GameEventsManager.instance.gameEvents.onMatchMade += MadeMatch;
    }

    private void MadeMatch()
    {
        if (IsObstacle)
        {
            if (currentObstacleHealth <= 0)
            {
                RemoveObstacle(this, grid, x, y);
            }
        }
    }

    public int SetHealth(int amount)
    {
        startingObstacleHealth = amount;
        currentObstacleHealth = startingObstacleHealth;
        return currentObstacleHealth;
    }

    public int GetObstacleHealth() => currentObstacleHealth;

    public void DamageObstacle()
    {
        if (currentObstacleHealth < 0) return; // Not an obstacle

        currentObstacleHealth--;
        SwapSpriteBasedOnHealth();
        Debug.Log($"Obstacle at ({x}, {y}) took damage. Remaining: {currentObstacleHealth}");
        if (currentObstacleHealth<= 0)
        {
            var gridObj = grid.GetValue(x, y);

            if (gridObj != null)
            {
                var obstacleGem = gridObj.GetGem();
                RemoveObstacle(obstacleGem, grid, x, y);
                return;
            }
        }
    }

    public void SwapSpriteBasedOnHealth()
    {
        Obstacle obstacle = type as Obstacle;
        List<Sprite> spriteList = obstacle.GetGemTypeSprites();

        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        int damagePercentage = (int)((currentObstacleHealth / (float)startingObstacleHealth) * 100);
        if (spriteList != null && spriteList.Count != 0)
        {
            if (damagePercentage > SIXTY_PERCENT
                && damagePercentage < 100)
            {
                renderer.sprite = spriteList[0];
            }
            else if (damagePercentage < SIXTY_PERCENT
                && damagePercentage > THIRTY_PERCENT)
            {
                renderer.sprite = spriteList[1];
            }
        }
    }

    private void RemoveObstacle(Gem gem, GridSystem2D<GridObj> grid, int x, int y)
    {
        if (gem != null)
        {
            // Add Audio For damaging and detroying obstacles
            if (isBeingDestroyed == true) return;
            GameEventsManager.instance.gameEvents.ObstacleCleared();
            gemChannel.Invoke(-1);
            Object.Destroy(gem.gameObject, 0.1f); // Remove visual
            isBeingDestroyed = true; // Set the flag to prevent multiple removals
            grid.SetValue(x, y, null); // Remove from logical grid
            Debug.Log($"removed obstacle gem {(x, y)}");
        }
    }

    #region Event Channel Setup
    public override void CreateEvent()
    {
        gemChannel = IntEventChannel.CreateInstance<IntEventChannel>();
        gemChannel.name = gameObject.name + "ObstacleEventChannel";
    }

    public override IntEventChannel GetChannel()
    {
        if (gemChannel == null)
        {
            CreateEvent();
        }
        return gemChannel;
    }

    public override void SetChannel(EventChannel<int> channel)
    {
       gemChannel = channel as IntEventChannel;
    }
    #endregion
}


