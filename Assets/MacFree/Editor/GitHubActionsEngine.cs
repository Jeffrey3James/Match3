using System;
using System.IO;
using System.Threading.Tasks;

namespace MacFree.Editor
{
    /// The GitHub Actions engine: create+push the repo, generate signing,
    /// commit the workflow + ExportOptions, and set encrypted repo secrets —
    /// all via API and git, with no Unity dashboard. Build control drives the
    /// GitHub Actions run. Resumable via MacFreeSettings.CompletedSteps.
    public class GitHubActionsEngine : IBuildEngine
    {
        readonly MacFreeSettings s;
        readonly AscApi asc;
        readonly GitHubApi gh;
        readonly string settingsRoot;

        public GitHubActionsEngine(MacFreeSettings settings, AscApi ascApi,
            GitHubApi githubApi, string settingsRoot)
        {
            s = settings; asc = ascApi; gh = githubApi; this.settingsRoot = settingsRoot;
        }

        public string Name => "GitHub Actions";

        public static (string owner, string repo) SplitRepo(string fullName)
        {
            int i = (fullName ?? "").IndexOf('/');
            return i < 0 ? (fullName, "") : (fullName.Substring(0, i), fullName.Substring(i + 1));
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

        public async Task RunSetup(string bundleId, string productName, string unityVersion,
            string branch, string subdirectory, string projectRoot, IProgress<string> log)
        {
            if (string.IsNullOrEmpty(s.GitHubToken))
                throw new Exception("Paste a GitHub 'repo' token in step 1 first.");
            // The runner activates with serial + email + password (Personal
            // serials work too). All three are required — game-ci's activation
            // only matches when every one is set.
            if (string.IsNullOrEmpty(s.UnitySerial) || string.IsNullOrEmpty(s.UnityEmail)
                || string.IsNullOrEmpty(s.UnityPassword))
                throw new Exception("Add your Unity serial, email and password in step 1 first — "
                    + "the runner can't activate Unity without all three. \"Detect\" fills in the "
                    + "serial this editor is already activated with.");
            if (string.IsNullOrEmpty(s.TeamId))
                throw new Exception("Enter your Apple Team ID in step 1 first — the export step "
                    + "needs it to sign the archive.");

            // GitHub rejects a push that touches .github/workflows/ unless the
            // token has the 'workflow' scope — and this engine's whole job is to
            // commit that file. Check first: the push is the LAST step, so
            // otherwise the cert, profile and secrets are all created before it
            // fails. Empty scopes means "unknown" (fine-grained token), not
            // "missing", so only a populated list can veto.
            string[] scopes = await gh.GetTokenScopes();
            if (scopes.Length > 0 && Array.IndexOf(scopes, "workflow") < 0)
                throw new Exception("Your GitHub token lacks the 'workflow' scope, so GitHub will "
                    + "refuse to push " + WorkflowFiles.WorkflowPath + ". Create a new classic "
                    + "token with BOTH 'repo' and 'workflow' checked (use the link in step 1), "
                    + "paste it above, then run SET UP again.");

            // The bundle ID can change between runs (renamed app, copied
            // project). Everything bound to it must be rebuilt, or we'd reuse
            // the previous bundle's resource id and ship a profile — and repo
            // secrets — for the wrong app.
            if (s.InvalidateIfBundleChanged(bundleId))
                log.Report("Bundle ID changed — redoing the bundle-specific setup...");
            s.Save(settingsRoot);

            // 1) repo + push
            if (!s.StepDone("gha_repo"))
            {
                log.Report("Creating GitHub repo and pushing your project...");
                var (fullName, cloneUrl) = await gh.EnsureRepo(
                    GitHubApi.SanitizeRepoName(productName), true);
                s.GitHubRepo = fullName;
                await Task.Run(() =>
                    ProjectPatcher.InitCommitAndPush(projectRoot, cloneUrl, s.GitHubToken));
                Done("gha_repo");
            }

            // 2) signing (same as UBA)
            string keyPath = Path.Combine(SecretsDir, "signing.key");
            string csrPath = Path.Combine(SecretsDir, "signing.csr");
            if (!s.StepDone("gha_csr"))
            {
                log.Report("Generating key + CSR...");
                SigningFactory.CreateCsr(productName + " Distribution",
                    out byte[] csrDer, out byte[] key);
                File.WriteAllBytes(keyPath, key);
                File.WriteAllBytes(csrPath, csrDer);
                Done("gha_csr");
            }
            string cerPath = Path.Combine(SecretsDir, "distribution.cer");
            if (!s.StepDone("gha_cert"))
            {
                log.Report("Creating iOS Distribution certificate...");
                s.BundleIdResourceId = await asc.EnsureBundleId(bundleId, productName);
                var (certId, cer) = await asc.CreateDistributionCertificate(
                    File.ReadAllBytes(csrPath));
                s.CertificateId = certId;
                File.WriteAllBytes(cerPath, cer);
                Done("gha_cert");
            }
            string profPath = Path.Combine(SecretsDir, "profile.mobileprovision");
            if (!s.StepDone("gha_profile"))
            {
                log.Report("Creating App Store provisioning profile...");
                byte[] prof = await asc.CreateAppStoreProfile(
                    "MacFree " + bundleId, s.BundleIdResourceId, s.CertificateId);
                File.WriteAllBytes(profPath, prof);
                Done("gha_profile");
            }
            string p12Path = Path.Combine(SecretsDir, "signing.p12");
            if (!s.StepDone("gha_p12"))
            {
                if (string.IsNullOrEmpty(s.P12Password))
                    s.P12Password = Guid.NewGuid().ToString("N").Substring(0, 16);
                byte[] p12 = SigningFactory.CreateP12(
                    File.ReadAllBytes(cerPath), File.ReadAllBytes(keyPath), s.P12Password);
                File.WriteAllBytes(p12Path, p12);
                Done("gha_p12");
            }

            // 3) secrets — MUST run before the workflow file is pushed (step 4
            // below): the generated workflow has a `push: branches: [main]`
            // trigger, so pushing it fires a run immediately. If secrets don't
            // exist yet that run is doomed (dies at Unity activation), burns
            // macOS minutes at 10x billing, and leaves a stale failed run for
            // StartBuild to latch onto.
            if (!s.StepDone("gha_secrets"))
            {
                log.Report("Setting encrypted repo secrets...");
                var (owner, repo) = SplitRepo(s.GitHubRepo);
                string pem = s.ResolveP8Pem() ?? "";
                async Task Set(string n, string v) => await gh.SetSecret(owner, repo, n, v);
                await Set("MACFREE_P12_BASE64", Convert.ToBase64String(File.ReadAllBytes(p12Path)));
                await Set("MACFREE_P12_PASSWORD", s.P12Password);
                await Set("MACFREE_PROFILE_BASE64",
                    Convert.ToBase64String(File.ReadAllBytes(profPath)));
                await Set("MACFREE_ASC_KEY_ID", s.AscKeyId);
                await Set("MACFREE_ASC_ISSUER_ID", s.AscIssuerId);
                await Set("MACFREE_ASC_KEY_BASE64",
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pem)));
                await Set("MACFREE_TEAM_ID", s.TeamId ?? "");
                // No UNITY_LICENSE: game-ci's activation scripts never read it
                // (its own error message is misleading), so the serial trio is
                // the only thing that activates the runner — Personal included.
                {
                    await Set("UNITY_SERIAL", s.UnitySerial);
                    await Set("UNITY_EMAIL", s.UnityEmail);
                    await Set("UNITY_PASSWORD", s.UnityPassword);
                }
                Done("gha_secrets");
            }

            // 4) workflow + ExportOptions committed and pushed
            if (!s.StepDone("gha_files"))
            {
                log.Report("Writing the workflow and pushing...");
                WorkflowFiles.WriteInto(projectRoot, s.RunnerImage, unityVersion,
                    s.TeamId, bundleId, "MacFree " + bundleId);
                var (fullName, cloneUrl) = await gh.EnsureRepo(
                    GitHubApi.SanitizeRepoName(productName), true);
                await Task.Run(() =>
                    ProjectPatcher.InitCommitAndPush(projectRoot, cloneUrl, s.GitHubToken));
                Done("gha_files");
            }
            log.Report("SET UP complete — no dashboard needed.");
        }

        // Build control — implemented in Task 9.
        public async Task<long> StartBuild()
        {
            var (owner, repo) = SplitRepo(s.GitHubRepo);
            // Capture the newest run BEFORE dispatching, so we can tell a
            // freshly-registered run apart from a stale previous one — GitHub's
            // dispatch->run registration isn't instant, and a fixed sleep can
            // return either the old run (reporting its stale result as this
            // build's) or 0 (no run yet -> the caller starts polling
            // GET /actions/runs/0, which 404s forever).
            long before = await gh.LatestRunId(owner, repo, "macfree-ios.yml");
            await gh.DispatchWorkflow(owner, repo, "macfree-ios.yml", "main");
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(2000);
                long id = await gh.LatestRunId(owner, repo, "macfree-ios.yml");
                if (id != 0 && id != before) return id;
            }
            throw new Exception("The GitHub Actions run did not appear within 60s of "
                + "dispatch — check the repo's Actions tab.");
        }

        public async Task<(string status, string commit)> GetBuildStatus(long id)
        {
            var (owner, repo) = SplitRepo(s.GitHubRepo);
            var (status, conclusion) = await gh.GetRunStatus(owner, repo, id);
            return (NormalizeRun(status, conclusion), "");
        }

        public Task<string> GetBuildLog(long id, int tailLines)
        {
            var (owner, repo) = SplitRepo(s.GitHubRepo);
            return gh.GetRunLogTail(owner, repo, id, tailLines);
        }

        /// Map a GitHub Actions run's (status, conclusion) to the normalized set.
        public static string NormalizeRun(string status, string conclusion)
        {
            if (status == "queued") return "queued";
            if (status != "completed") return "running";
            switch (conclusion)
            {
                case "success": return "success";
                case "cancelled": return "canceled";
                default: return "failure";
            }
        }
    }
}
