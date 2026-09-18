// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// On-board instance component for a Wrapped tile. No extra per-instance state
// is needed beyond what Act2SpecialGem already provides (unlike StripedGem,
// which needs orientation) — kept as its own class anyway so each Act 2
// special has a symmetrical, easy-to-find file, matching how the Act 1 system
// gives every power-up its own file even when logic overlaps.
namespace Match3Game.Act2
{
    public class WrappedGem : Act2SpecialGem
    {
    }
}
