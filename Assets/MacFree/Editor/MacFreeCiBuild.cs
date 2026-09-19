using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace MacFree.Editor
{
    /// The build method game-ci calls on the runner. Stamps the monotonic
    /// build number (shared with the UBA pre-export so ASC's high-water mark
    /// is always exceeded) and builds the iOS Xcode project to build/iOS.
    public static class MacFreeCiBuild
    {
        public const string OutputPath = "build/iOS";

        // Set only while a MacFree CI build is running, so the signing
        // post-process below never touches a user's ordinary local iOS builds.
        static bool s_ciBuild;

        public static int NextBuildNumber(DateTime utcNow)
        {
            return MacFreePreExport.Stamp(utcNow);
        }

        public static void Run()
        {
            PlayerSettings.iOS.buildNumber = NextBuildNumber(DateTime.UtcNow).ToString();

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                scenes = new[] { "Assets/Scenes/SampleScene.unity" };

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                locationPathName = OutputPath,
                options = BuildOptions.None
            };
            s_ciBuild = true;
            BuildReport report;
            try { report = BuildPipeline.BuildPlayer(opts); }
            finally { s_ciBuild = false; }
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("MacFree CI build failed: " + report.summary.result
                    + " (" + report.summary.totalErrors + " errors)");
            Debug.Log("MacFree CI build succeeded -> " + OutputPath);
        }

        /// Pin manual signing per target in the generated Xcode project. The
        /// provisioning profile must go on the app target ONLY: UnityFramework
        /// is a framework, and xcodebuild aborts the archive ("UnityFramework
        /// does not support provisioning profiles") if a profile is forced onto
        /// it. That's why the profile can't just be passed on the xcodebuild
        /// command line — that applies to every target — so we set it here,
        /// where each target is addressable. Team and signing identity stay on
        /// the command line because they're valid for both targets. Runs only
        /// for MacFree's own CI build (guarded by s_ciBuild).
        [PostProcessBuild(1000)]
        public static void ConfigureSigning(BuildTarget target, string builtProjectPath)
        {
            if (target != BuildTarget.iOS || !s_ciBuild) return;
            string pbxPath = PBXProject.GetPBXProjectPath(builtProjectPath);
            var proj = new PBXProject();
            proj.ReadFromFile(pbxPath);
            string app = proj.GetUnityMainTargetGuid();
            string framework = proj.GetUnityFrameworkTargetGuid();
            string profileName = "MacFree " + PlayerSettings.applicationIdentifier;

            proj.SetBuildProperty(app, "CODE_SIGN_STYLE", "Manual");
            proj.SetBuildProperty(app, "PROVISIONING_PROFILE_SPECIFIER", profileName);
            proj.SetBuildProperty(framework, "CODE_SIGN_STYLE", "Manual");
            proj.SetBuildProperty(framework, "PROVISIONING_PROFILE_SPECIFIER", "");

            proj.WriteToFile(pbxPath);
        }
    }
}
