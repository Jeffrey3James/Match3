using UnityEngine;

/// <summary>
/// TEMPORARY STUB — real implementation lives on Agent B's branch.
/// C references these static helpers by name and can't compile without them
/// if B hasn't merged first. Signatures come from PLAN.md; empty bodies keep
/// C's PR self-contained. When B lands, its version wins the merge.
/// </summary>
public static class MatchJuice
{
    // Global time freeze in milliseconds. B will actually stop timeScale.
    public static void Hitstop(int ms) { }

    // Camera shake. B will drive a ScreenShaker component.
    public static void Shake(Camera camera, float amp, float dur) { }

    // Pop cue whose pitch rises with the cascade depth.
    public static void PitchedPop(int cascadeDepth) { }

    // Particle burst at a world position, tinted.
    public static void BurstAt(Vector3 worldPos, Color color) { }

    // One-shot coin arrival sfx/particle. Called per coin as they arrive at the HUD.
    public static void CoinFlyCue() { }

    // One-shot star arrival cue used by CoinFly.FlyStars(...).
    public static void StarFlyCue() { }

    // Punch-scale tween on any transform. B swaps for a DOTween version.
    public static void PunchScale(Transform t) { }
}
