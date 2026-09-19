using System;
using UnityEditor;
using UnityEngine;

namespace MacFree.Editor
{
    /// Unity Build Automation pre-export hook. App Store Connect requires
    /// every upload's CFBundleVersion to EXCEED the highest ever uploaded
    /// for the version, so we stamp wall-clock minutes since a fixed epoch:
    /// monotonic, unique, and always above any previous MacFree upload.
    public static class MacFreePreExport
    {
        static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static int Stamp(DateTime utcNow)
        {
            return (int)(utcNow - Epoch).TotalMinutes;
        }

        public static void Run()
        {
            int bn = Stamp(DateTime.UtcNow);
            PlayerSettings.iOS.buildNumber = bn.ToString();
            Debug.Log("MacFree: iOS buildNumber set to " + bn
                + " (minutes since 2026-01-01 UTC).");
        }
    }
}
