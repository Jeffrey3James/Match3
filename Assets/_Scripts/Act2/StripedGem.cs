// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// On-board instance component for a Striped tile. Mirrors the relationship
// between PowerUp (Assets/_Scripts/Gems/PowerUpGem.cs) and NukeGem
// (Assets/_Scripts/Gems/NukeGem.cs), which carry per-instance state (like
// NukeGem's target color) alongside the shared ScriptableObject logic.
// StripedGem carries the per-instance orientation (row-clear vs column-clear),
// since that depends on how THIS particular match was oriented, not on the
// StripedTile asset itself.
using UnityEngine;

namespace Match3Game.Act2
{
    public class StripedGem : Act2SpecialGem
    {
        [SerializeField] private StripedOrientation orientation;

        public void SetOrientation(StripedOrientation newOrientation)
        {
            orientation = newOrientation;

            if (type is StripedTile stripedTile)
            {
                var sprite = stripedTile.GetSpriteFor(orientation);
                if (sprite != null)
                {
                    GetComponent<SpriteRenderer>().sprite = sprite;
                }
            }
        }

        public StripedOrientation GetOrientation() => orientation;
    }
}
