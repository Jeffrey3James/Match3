using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    public static void BuildIOS()
    {
        BuildIOS(iOSSdkVersion.DeviceSDK);
    }

    public static void BuildIOSSimulator()
    {
        BuildIOS(iOSSdkVersion.SimulatorSDK);
    }

    private static void BuildIOS(iOSSdkVersion sdk)
    {
        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new BuildFailedException("No enabled scenes found in EditorBuildSettings.");
        }

        var previousSdk = PlayerSettings.iOS.sdkVersion;
        var previousArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture;

        try
        {
            PlayerSettings.iOS.sdkVersion = sdk;
            if (sdk == iOSSdkVersion.SimulatorSDK)
            {
                PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;
            }

            Debug.Log($"Building iOS ({sdk}) with scenes: {string.Join(", ", scenes)}");
            var report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = "build/iOS",
                    target = BuildTarget.iOS,
                    options = BuildOptions.Development
                }
            );

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"iOS build {report.summary.result}: {report.summary.totalErrors} errors.");
            }
        }
        finally
        {
            PlayerSettings.iOS.sdkVersion = previousSdk;
            PlayerSettings.iOS.simulatorSdkArchitecture = previousArchitecture;
        }
    }
}
