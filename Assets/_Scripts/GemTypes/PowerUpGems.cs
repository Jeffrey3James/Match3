using Match3Game;
using UnityEngine;

public class PowerUpGems : GemTypes 
{
    [Header("Power Up Settings")]
    [SerializeField] private int powerUpValue;
    [SerializeField] private GameObject powerUpFX;
    [SerializeField] protected AudioClip powerUpAudio;

    public enum PowerUpType { Bomb, Rocket, Hammer, Missile, Nuke}
    public PowerUpType powerUpType;

    protected virtual void OnEnable()
    {
        gemCategory = GemCategory.PowerUp; // Set the category to PowerUp
    }

    public int GetPowerUpValue()
    {
        return powerUpValue;
    }

    public GameObject GetPowerUpFX()
    {
        return powerUpFX;
    }

    public virtual void ExplodeGem(Gem gem, GridSystem2D<GridObj> grid, int x, int y, AudioClip powerUpAudio)
    {
        if (gem != null)
        {
            AudioManager.instance.PlayAudio(powerUpAudio); // Play bomb sound effect

            ObstacleHandler(gem);
            Object.Destroy(gem.gameObject, 0.1f); // Remove visual
        }

        grid.SetValue(x, y, null); // Remove from logical grid
    }

    public void ObstacleHandler(Gem gem)
    {
        if (gem is ObstacleGem obstacleGem)
        {
            obstacleGem.DamageObstacle(); // Handle obstacle damage
            return;
        }
    }

    private void CreatePowerUpFX()
    {
        if (GetPowerUpFX() != null)
        {
            var fxInstance = Instantiate(GetPowerUpFX());
            fxInstance.transform.position = Vector3.zero; // Set position as needed
            Object.Destroy(fxInstance, 2f); // Destroy after 2 seconds

            var fx = Instantiate(GetPowerUpFX());
            Destroy(fx, 5f);
        }
        else
        {
            Debug.LogWarning("PowerUp FX is not assigned for Bomb PowerUp.");
        }
    }
}
