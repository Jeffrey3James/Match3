using UnityEngine;

// stub — will be overwritten by B
// Provides the static API that Agent A's code calls so this branch compiles
// before Agent B's Juice PR lands. If B ships a real MatchJuice, git merge
// resolution should favor B's file over this stub.
public static class MatchJuice
{
    // stub — will be overwritten by B
    public static void Hitstop(int ms) { }

    // stub — will be overwritten by B
    public static void PitchedPop(int cascadeDepth) { }

    // stub — will be overwritten by B
    public static void BurstAt(Vector3 pos, Color c) { }

    // stub — will be overwritten by B
    public static void Shake(float amp, float dur) { }
}
