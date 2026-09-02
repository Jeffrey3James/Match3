using UnityEngine;

namespace Match3Game.Juice {
    /// <summary>
    /// Static entry point every gameplay system uses to trigger "juice": hitstop, camera shake,
    /// pitched pops, particle bursts, punch-scale, and one-shot audio cues.
    ///
    /// All methods are safe to call before a scene is loaded, from any thread the main-thread
    /// Unity API tolerates, and before <see cref="MatchJuiceRuntime"/> exists in the scene.
    /// On first call a hidden DontDestroyOnLoad singleton is spun up automatically.
    ///
    /// Nothing here throws: gameplay coroutines (cascades, matches) must never be killed by a
    /// missing clip / missing prefab / missing camera. Missing = warn once + no-op.
    /// </summary>
    public static class MatchJuice {

        /// <summary>Freeze <see cref="Time.timeScale"/> to 0 for <paramref name="ms"/> milliseconds
        /// (real time, ignores timescale) then restore whatever the prior scale was.</summary>
        public static void Hitstop(int ms) {
            if (ms <= 0) return;
            MatchJuiceRuntime.EnsureExists().DoHitstop(ms);
        }

        /// <summary>Perlin-noise camera shake. <paramref name="amp"/> is in world units on
        /// <see cref="Camera.main"/>.localPosition, <paramref name="dur"/> in seconds.</summary>
        public static void Shake(float amp, float dur) {
            if (amp <= 0f || dur <= 0f) return;
            MatchJuiceRuntime.EnsureExists().DoShake(amp, dur);
        }

        /// <summary>Plays the base match pop with pitch = 1 + 0.08 * cascadeDepth.
        /// Cascade depth 0 = first match in the chain; each deeper cascade rises 8%.</summary>
        public static void PitchedPop(int cascadeDepth) {
            MatchJuiceRuntime.EnsureExists().DoPitchedPop(cascadeDepth);
        }

        /// <summary>Spawn a short-lived sparkle burst at a world position tinted by <paramref name="color"/>.
        /// Emits 6–12 particles from the shared prefab. If the prefab is not assigned, no-op.</summary>
        public static void BurstAt(Vector3 worldPos, Color color) {
            MatchJuiceRuntime.EnsureExists().DoBurst(worldPos, color);
        }

        /// <summary>DOTween punch-scale on the given transform. Safe to call on the same transform
        /// repeatedly — DOTween completes/overrides via <see cref="DG.Tweening.DOTweenModuleUI"/> defaults.</summary>
        public static void PunchScale(Transform t, float amount = 0.2f, float dur = 0.25f) {
            if (t == null) return;
            MatchJuiceRuntime.EnsureExists().DoPunchScale(t, amount, dur);
        }

        /// <summary>Coin flight cue. Fire when a coin sprite begins its arc into the HUD.</summary>
        public static void CoinFlyCue() {
            MatchJuiceRuntime.EnsureExists().DoCoinFlyCue();
        }

        /// <summary>Chime played the moment a power-up gem is first created by a match.</summary>
        public static void PowerUpChime() {
            MatchJuiceRuntime.EnsureExists().DoPowerUpChime();
        }
    }
}
