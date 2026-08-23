using Match3Game;
using UnityEngine;

public class NukeGem : PowerUp
{
    [SerializeField] private NukePowerUp nukeData;

    public void SetTargetGemType(GemTypes target)
    {
        gemTypeToDestroy = target;

        if (nukeData != null)
        {
            Sprite nukeSprite = nukeData.GetNukeSprite(target);
            if (nukeSprite != null)
            {
                GetComponent<SpriteRenderer>().sprite = nukeSprite;
            }
            else
            {
                Debug.LogWarning($"No nuke sprite found for {target.name}.");
            }
        }
    }

    public override void SetType(GemTypes newType)
    {
        type = newType;
    }
}
