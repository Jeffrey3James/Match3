// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// Shared enum/type identifiers for the Act 2 standard combo-table specials.
// Deliberately NOT added to PowerUpGems.PowerUpType (Assets/_Scripts/GemTypes/PowerUpGems.cs)
// so the existing Act 1 enum and its switch statements remain untouched.
using Match3Game;

namespace Match3Game.Act2
{
    /// <summary>
    /// The three standard match-3 specials Act 2 introduces. These are separate
    /// from PowerUpGems.PowerUpType (Bomb/Rocket/Hammer/Missile/Nuke) — Act 1 and
    /// Act 2 specials are intentionally different type hierarchies so neither
    /// system can accidentally cross-trigger the other.
    /// </summary>
    public enum Act2SpecialType
    {
        Striped,
        Wrapped,
        ColorBomb
    }

    /// <summary>
    /// Orientation a Striped tile inherits from the match that created it.
    /// A 4-in-a-row horizontal match makes a tile that clears its row;
    /// a 4-in-a-column vertical match makes a tile that clears its column.
    /// </summary>
    public enum StripedOrientation
    {
        Horizontal,
        Vertical
    }
}
