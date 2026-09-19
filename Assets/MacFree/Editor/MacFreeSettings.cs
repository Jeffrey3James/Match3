using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MacFree.Editor
{
    /// Persistent tool settings. Lives in UserSettings/ (git-ignored by
    /// Unity's default template) so credentials never reach the repo.
    [Serializable]
    public class MacFreeSettings
    {
        public string AscKeyId = "";
        public string AscIssuerId = "";
        public string AscP8Path = "";
        public string AscP8Base64 = "";
        public string UbaApiKey = "";
        public string UbaOrgId = "";
        public string UbaProjectId = "";
        public string GitHubToken = "";   // classic 'repo' PAT, for repo create + push
        public string GitHubRepo = "";     // owner/name once created (informational)
        public string BuildTargetId = "";
        public string CredentialId = "";
        public string BundleIdResourceId = "";
        public string CertificateId = "";
        public string AppId = "";
        public string P12Password = "";
        public string Engine = "uba";     // "uba" | "gha"
        public string TeamId = "";         // Apple team id, for ExportOptions
        // Serial + email + password is the runner's only activation path — a
        // .ulf is useless (no game-ci activation script reads UNITY_LICENSE,
        // and Unity no longer issues one for Personal). Personal serials start
        // with "F" and are supported. See UnityLicenseInfo.
        public string UnitySerial = "";    // Personal (F...) or Plus/Pro
        public string UnityEmail = "";
        public string UnityPassword = "";
        public string RunnerImage = "macos-15";
        public string XcodeVersion = "";   // "" = runner default
        public string SetupBundleId = "";  // the bundle id CompletedSteps refer to
        // The crash panel's one-line summary, refreshed by MacFreeCrashWindow.
        // CrashCount is -1 until the first refresh, which is how the panel
        // tells "no crashes" apart from "never looked".
        public string CrashSummary = "";
        public int CrashCount = -1;
        public List<string> CompletedSteps = new List<string>();

        public bool StepDone(string id) { return CompletedSteps.Contains(id); }
        public void MarkStep(string id) { if (!StepDone(id)) CompletedSteps.Add(id); }

        /// Steps that are bound to a specific bundle ID. The key/CSR/certificate
        /// are deliberately absent: a Distribution certificate is team-wide, so
        /// it survives a bundle change. `gha_cert` IS listed because that step
        /// also resolves the bundle-id resource.
        static readonly string[] BundleScopedSteps =
        {
            "bundleId", "profile", "p12+upload", "target", "envvars",
            "gha_cert", "gha_profile", "gha_p12", "gha_files", "gha_secrets"
        };

        /// Setup state is scoped to a bundle ID: the bundle-id resource, the
        /// provisioning profile, the uploaded credential and the build target
        /// are all bound to it. When the project's bundle ID changes after setup
        /// ran, that state is stale — and because every step is skipped once
        /// marked done, reusing it silently binds the new app to the OLD bundle.
        /// Apple accepts the mismatched profile and the failure only surfaces
        /// later ("provisioning profile is only valid for (com.old.id)"), far
        /// from its cause. Drop the bundle-scoped steps so they regenerate.
        /// Returns true when state was actually invalidated (first run is not).
        public bool InvalidateIfBundleChanged(string bundleId)
        {
            if (string.IsNullOrEmpty(bundleId) || SetupBundleId == bundleId) return false;
            bool stale = !string.IsNullOrEmpty(SetupBundleId);
            if (stale)
            {
                foreach (string step in BundleScopedSteps) CompletedSteps.Remove(step);
                BundleIdResourceId = "";
                AppId = "";
                // The summary counts crashes for the OLD app; AppId is being
                // dropped, so leaving it would show another app's crashes.
                CrashSummary = "";
                CrashCount = -1;
            }
            SetupBundleId = bundleId;
            return stale;
        }

        static string FileFor(string root)
        {
            if (string.IsNullOrEmpty(root))
                root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(root, "UserSettings", "MacFree", "settings.json");
        }

        public static MacFreeSettings Load(string root = null)
        {
            string f = FileFor(root);
            if (!File.Exists(f)) return new MacFreeSettings();
            try { return JsonUtility.FromJson<MacFreeSettings>(File.ReadAllText(f)) ?? new MacFreeSettings(); }
            catch
            {
                UnityEngine.Debug.LogWarning("MacFree: settings file was unreadable - starting fresh. " + f);
                return new MacFreeSettings();
            }
        }

        public void Save(string root = null)
        {
            string f = FileFor(root);
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            File.WriteAllText(f, JsonUtility.ToJson(this, true));
        }

        /// The .p8 PEM: prefer the picked file (and cache its content);
        /// fall back to the cached copy if the file moved.
        public string ResolveP8Pem()
        {
            if (!string.IsNullOrEmpty(AscP8Path) && File.Exists(AscP8Path))
            {
                string pem = File.ReadAllText(AscP8Path);
                AscP8Base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(pem));
                return pem;
            }
            if (!string.IsNullOrEmpty(AscP8Base64))
                return Encoding.UTF8.GetString(Convert.FromBase64String(AscP8Base64));
            return null;
        }
    }
}
