// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// MonoBehaviour base for the Act 2 special-tile GameObjects that live on the
// board (the "instance" side), mirroring the existing Act 1 pattern where
// `PowerUp : Gem` (Assets/_Scripts/Gems/PowerUpGem.cs) is the on-board
// component driven by a `PowerUpGems : GemTypes` ScriptableObject
// (Assets/_Scripts/GemTypes/PowerUpGems.cs). Act2SpecialGem plays the same
// role for StripedTile / WrappedTile / ColorBombTile.
using Match3Game;
using UnityEngine;

namespace Match3Game.Act2
{
    public class Act2SpecialGem : Gem
    {
        [SerializeField] protected Act2SpecialType act2SpecialType;

        public Act2SpecialType GetAct2SpecialType() => act2SpecialType;
    }
}
