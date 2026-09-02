// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// ScriptableObject base for the Act 2 standard-combo specials, mirroring the
// existing Act 1 pattern where PowerUpGems (Assets/_Scripts/GemTypes/PowerUpGems.cs)
// extends GemTypes and BombPowerUp/HammerPowerUp/etc. extend PowerUpGems.
// StripedTile / WrappedTile / ColorBombTile extend THIS class, not PowerUpGems,
// so Act 2 has its own parallel type hierarchy and never touches the Act 1
// PowerUpType enum or its switch(group.Count) mapping in Match3.FindMatches().
using Match3Game;
using UnityEngine;

namespace Match3Game.Act2
{
    public abstract class Act2SpecialTileGemType : GemTypes
    {
        [Header("Act 2 Special Tile Settings")]
        [SerializeField] protected GameObject act2SpecialFX;
        [SerializeField] protected AudioClip act2SpecialAudio;

        public Act2SpecialType act2SpecialType;

        protected virtual void OnEnable()
        {
            // Reuses the existing GemCategory enum (Normal/PowerUp/Obstacle) so the
            // rest of the pipeline (ExplodeGems, CheckForPowerup's category check,
            // etc.) treats Act 2 specials exactly like Act 1 power-ups: not a
            // "Normal" gem, so they survive the initial explode pass and instead
            // wait to be triggered/activated.
            gemCategory = GemCategory.PowerUp;
        }

        public GameObject GetAct2SpecialFX() => act2SpecialFX;

        /// <summary>
        /// Shared explode helper matching PowerUpGems.ExplodeGem's contract
        /// (Assets/_Scripts/GemTypes/PowerUpGems.cs) — plays audio, damages
        /// obstacles, destroys the visual, and clears the logical grid cell.
        /// Duplicated (not inherited) intentionally: Act2SpecialTileGemType does
        /// NOT derive from PowerUpGems, to keep the two systems fully isolated.
        /// </summary>
        public virtual void ExplodeGem(Gem gem, GridSystem2D<GridObj> grid, int x, int y, AudioClip audio)
        {
            if (gem != null)
            {
                if (audio != null)
                {
                    AudioManager.instance.PlayAudio(audio);
                }

                if (gem is ObstacleGem obstacleGem)
                {
                    obstacleGem.DamageObstacle();
                }

                Object.Destroy(gem.gameObject, 0.1f);
            }

            grid.SetValue(x, y, null);
        }
    }
}
