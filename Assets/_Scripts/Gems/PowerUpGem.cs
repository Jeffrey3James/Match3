using Match3Game;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;

public class PowerUp : Gem 
{
    [SerializeField] protected GemTypes gemTypeToDestroy;

    public void SetTypeToDestroy(GemTypes type)
    {
        gemTypeToDestroy = type;
        GetComponent<SpriteRenderer>().sprite = type.sprite;
    }

    public GemTypes GetTypeToDestroy()
    {
        return gemTypeToDestroy;
    }
}

