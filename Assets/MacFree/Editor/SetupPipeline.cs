using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MacFree.Editor
{
    /// The SET UP button: every remote step in order, idempotent and
    /// resumable. Each completed step is recorded in settings; re-running
    /// skips what is done and retries what failed.
    public class SetupPipeline
    {
        readonly MacFreeSettings s;
        readonly AscApi asc;
        readonly UbaApi uba;
        readonly GitHubApi github;
        readonly string settingsRoot;

        public SetupPipeline(MacFreeSettings settings, AscApi ascApi, UbaApi ubaApi,
            string settingsRoot = null, GitHubApi githubApi = null)
        {
            s = settings;
            asc = ascApi;
            uba = ubaApi;
            github = githubApi;
            this.settingsRoot = settingsRoot;
        }

        string SecretsDir
        {
            get
            {
                string root = settingsRoot ?? Path.GetFullPath(
                    Path.Combine(UnityEngine.Application.dataPath, ".."));
                string d = Path.Combine(root, "UserSettings", "MacFree");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        void Done(string step) { s.MarkStep(step); s.Save(settingsRoot); }

        public async Task RunAll(string bundleId, string productName,
            string unityVersion, string branch, string subdirectory,
            string projectRoot, IProgress<string> progress)
        {
            // The bundle ID can change between runs (renamed app, copied
            // project). Everything bound to it must be rebuilt, or we'd reuse
            // the previous bundle's resource id and ship a profile for the
            // wrong app.
            if (s.InvalidateIfBundleChanged(bundleId))
                progress.Report("Bundle ID changed — redoing the bundle-specific setup...");
            s.Save(settingsRoot);

            if (!s.StepDone("bundleId"))
            {
                progress.Report("Registering bundle ID with Apple...");
                s.BundleIdResourceId = await asc.EnsureBundleId(bundleId, productName);
                Done("bundleId");
            }

            string keyPath = Path.Combine(SecretsDir, "signing.key");
            string csrPath = Path.Combine(SecretsDir, "signing.csr");
            if (!s.StepDone("csr"))
            {
                progress.Report("Generating private key + CSR (in-editor, no Mac)...");
                byte[] csrDer, key;
                SigningFactory.CreateCsr(productName + " Distribution", out csrDer, out key);
                File.WriteAllBytes(keyPath, key);
                File.WriteAllBytes(csrPath, csrDer);
                Done("csr");
            }

            string cerPath = Path.Combine(SecretsDir, "distribution.cer");
            if (!s.StepDone("cert"))
            {
                progress.Report("Creating iOS Distribution certificate...");
                var (certId, cer) = await asc.CreateDistributionCertificate(
                    File.ReadAllBytes(csrPath));
                s.CertificateId = certId;
                File.WriteAllBytes(cerPath, cer);
                Done("cert");
            }

            string profPath = Path.Combine(SecretsDir, "profile.mobileprovision");
            if (!s.StepDone("profile"))
            {
                progress.Report("Creating App Store provisioning profile...");
                byte[] prof = await asc.CreateAppStoreProfile(
                    "MacFree " + bundleId, s.BundleIdResourceId, s.CertificateId);
                File.WriteAllBytes(profPath, prof);
                Done("profile");
            }

            if (!s.StepDone("p12+upload"))
            {
                progress.Report("Assembling .p12 and uploading credentials to Build Automation...");
                if (string.IsNullOrEmpty(s.P12Password))
                    s.P12Password = Guid.NewGuid().ToString("N").Substring(0, 16);
                byte[] p12 = SigningFactory.CreateP12(
                    File.ReadAllBytes(cerPath), File.ReadAllBytes(keyPath), s.P12Password);
                s.CredentialId = await uba.UploadIosCredentials(
                    "MacFree " + bundleId, p12, s.P12Password, File.ReadAllBytes(profPath));
                Done("p12+upload");
            }

            if (string.IsNullOrEmpty(s.BuildTargetId)) s.BuildTargetId = "macfree-ios";
            if (!s.StepDone("target"))
            {
                progress.Report("Finding a compatible Unity / macOS / Xcode configuration...");
                await uba.EnsureCompatibleBuildTarget(s.BuildTargetId, "MacFree iOS", bundleId,
                    unityVersion, branch, subdirectory, s.CredentialId, progress);
                Done("target");
            }

            if (!s.StepDone("envvars"))
            {
                progress.Report("Setting TestFlight upload credentials on the target...");
                string pem = s.ResolveP8Pem();
                await uba.SetEnvVars(s.BuildTargetId, new Dictionary<string, string>
                {
                    { "MACFREE_ASC_KEY_ID", s.AscKeyId },
                    { "MACFREE_ASC_ISSUER_ID", s.AscIssuerId },
                    { "MACFREE_ASC_KEY_B64", Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes(pem ?? "")) }
                });
                Done("envvars");
            }

            if (!s.StepDone("hooks"))
            {
                progress.Report("Writing and committing the upload hook...");
                ProjectPatcher.WriteHooks(projectRoot);
                ProjectPatcher.CommitHooks(projectRoot); // auto — no button click
                Done("hooks");
            }

            // Create the GitHub repo and push automatically when a token is
            // present. Build Automation clones from this repo, so without it
            // the build fails at checkout. Skipped when no token is set (the
            // user is bringing their own repo).
            if (!string.IsNullOrEmpty(s.GitHubToken) && !s.StepDone("repo"))
            {
                progress.Report("Creating GitHub repo and pushing your project...");
                var gh = github ?? new GitHubApi(s.GitHubToken);
                string name = GitHubApi.SanitizeRepoName(productName);
                var (fullName, cloneUrl) = await gh.EnsureRepo(name, true);
                string tok = s.GitHubToken, root = projectRoot;
                await Task.Run(() =>
                    ProjectPatcher.InitCommitAndPush(root, cloneUrl, tok));
                s.GitHubRepo = fullName;
                Done("repo");
            }

            progress.Report("SET UP complete.");
        }
    }
}
