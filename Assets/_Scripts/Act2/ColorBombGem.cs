// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// On-board instance component for a Color Bomb tile. Carries the target gem
// color to clear, exactly like NukeGem (Assets/_Scripts/Gems/NukeGem.cs)
// carries its `gemTypeToDestroy` — same pattern, separate class so Act 2 has
// no dependency on Act 1's PowerUp/NukeGem hierarchy.
using UnityEngine;

namespace Match3Game.Act2
{
    public class ColorBombGem : Act2SpecialGem
    {
        [SerializeField] private GemTypes targetGemType;

        public void SetTargetGemType(GemTypes target)
        {
            targetGemType = target;
        }

        public GemTypes GetTargetGemType() => targetGemType;
    }
}
