using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MacFree.Editor
{
    /// MacFree - iOS Builder. Three panels: CONNECT (credentials),
    /// SET UP (one idempotent button), SHIP (build + TestFlight verify),
    /// plus the preflight checklist. All remote work runs as Tasks pumped
    /// by the editor loop; the UI never blocks.
    public class MacFreeWindow : EditorWindow
    {
        MacFreeSettings s;
        List<PreflightItem> checks = new List<PreflightItem>();
        Task activeTask;
        string activeLabel = "";
        string lastError = "";
        string shipStatus = "";
        int shipBuildNumber = -1;
        long shipRunId;
        int expectedStamp;
        int ascPolls;
        double nextPollAt;
        Vector2 scroll;
        bool? scmConnected;
        string orgForeignKey;

        [MenuItem("Tools/MacFree iOS Builder")]
        static void Open()
        {
            var w = GetWindow<MacFreeWindow>("MacFree");
            w.minSize = new Vector2(420, 560);
        }

        void OnEnable()
        {
            s = MacFreeSettings.Load();
            RefreshChecks();
            EditorApplication.update += Pump;
        }

        void OnDisable() { EditorApplication.update -= Pump; }

        void OnFocus() { RefreshChecks(); }

        void RefreshChecks()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            checks = Preflight.RunLocalChecks(root);
            if (s.Engine == "uba" && ConnectReady()) KickScmCheck();
        }

        /// One-shot background check of whether Build Automation has a
        /// source-repo connection; this hits the network (UbaApi) so it
        /// cannot live in the synchronous Preflight.RunLocalChecks.
        async void KickScmCheck()
        {
            try
            {
                string type = await Uba().GetScmType();
                scmConnected = !string.IsNullOrEmpty(type);
            }
            catch
            {
                scmConnected = false;
            }
            // Resolve the numeric org id the dashboard URL needs, so "Open
            // project" lands on THIS project (non-fatal - falls back below).
            try { orgForeignKey = await Uba().GetOrgForeignKey(); } catch { }
            Repaint();
        }

        void Pump()
        {
            if (activeTask != null && activeTask.IsCompleted)
            {
                if (activeTask.IsFaulted)
                    lastError = Flatten(activeTask.Exception);
                activeTask = null;
                activeLabel = "";
                s.Save();
                RefreshChecks();
                Repaint();
            }
            if (shipBuildNumber >= 0 && activeTask == null
                && EditorApplication.timeSinceStartup > nextPollAt)
            {
                nextPollAt = EditorApplication.timeSinceStartup + 20.0;
                // Don't blank a still-relevant lastError (e.g. a prior poll's
                // "hasn't appeared on ASC yet" warning) just because another
                // routine poll kicked off.
                StartTask("Checking build status...", PollShip(), clearError: false);
            }
        }

        static string Flatten(AggregateException e)
        {
            var inner = e.GetBaseException();
            return ErrorTranslator.Translate(inner);
        }

        void StartTask(string label, Task t, bool clearError = true)
        {
            if (clearError) lastError = "";
            activeLabel = label;
            activeTask = t;
        }

        AscApi Asc() { return new AscApi(() => AscJwt.GetCached(s)); }
        UbaApi Uba() { return new UbaApi(s.UbaApiKey, s.UbaOrgId, s.UbaProjectId); }

        /// The build back-end SET UP/SHIP drive: GitHub Actions when selected,
        /// Unity Build Automation otherwise (default).
        IBuildEngine ActiveEngine()
        {
            if (s.Engine == "gha")
                return new GitHubActionsEngine(s, Asc(), new GitHubApi(s.GitHubToken), null);
            return new UbaEngine(s, Asc(), Uba(), null, new GitHubApi(s.GitHubToken));
        }

        /// Create (or reuse) a private GitHub repo named after the product and
        /// push the project to it, so Build Automation has something to clone.
        /// Reads Unity APIs on the main thread, does blocking git off-thread.
        async Task CreateAndPushRepo()
        {
            if (string.IsNullOrEmpty(s.GitHubToken))
                throw new Exception("Paste a GitHub 'repo' token in step 1 first.");
            string name = GitHubApi.SanitizeRepoName(PlayerSettings.productName);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string token = s.GitHubToken;
            var gh = new GitHubApi(token);
            var (fullName, cloneUrl) = await gh.EnsureRepo(name, true);
            await Task.Run(() =>
                ProjectPatcher.InitCommitAndPush(root, cloneUrl, token));
            s.GitHubRepo = fullName;
            s.Save();
            RefreshChecks();
        }

        /// Deep-link straight to THIS project in Unity Cloud, so the button
        /// never lands on whatever project was last opened. Uses the numeric
        /// org id the dashboard routes by (resolved into orgForeignKey), and
        /// falls back to the raw org id, then the dashboard home.
        string ProjectDashboardUrl()
        {
            string org = !string.IsNullOrEmpty(orgForeignKey) ? orgForeignKey : s.UbaOrgId;
            if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(s.UbaProjectId))
                return "https://cloud.unity.com";
            return "https://cloud.unity.com/home/organizations/" + org
                + "/projects/" + s.UbaProjectId;
        }

        /// SET UP's completion, engine-aware. "hooks" is marked only by the
        /// UBA SetupPipeline; GitHubActionsEngine.RunSetup marks gha_* steps
        /// and never "hooks" — so gating SET UP/SHIP on "hooks" alone leaves
        /// the gha engine permanently unable to unlock BUILD & SHIP.
        /// "gha_secrets" is the LAST step RunSetup marks.
        bool SetupComplete()
        {
            return s.Engine == "gha" ? s.StepDone("gha_secrets") : s.StepDone("hooks");
        }

        bool ConnectReady()
        {
            if (s.Engine == "gha")
                return !string.IsNullOrEmpty(s.AscKeyId)
                    && !string.IsNullOrEmpty(s.AscIssuerId)
                    && s.ResolveP8Pem() != null
                    && !string.IsNullOrEmpty(s.GitHubToken);
            return !string.IsNullOrEmpty(s.AscKeyId)
                && !string.IsNullOrEmpty(s.AscIssuerId)
                && s.ResolveP8Pem() != null
                && !string.IsNullOrEmpty(s.UbaApiKey)
                && !string.IsNullOrEmpty(s.UbaOrgId)
                && !string.IsNullOrEmpty(s.UbaProjectId);
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Label("MacFree — iOS Builder", EditorStyles.boldLabel);
            GUILayout.Label("Ship iOS to TestFlight from Windows or Linux. No Mac.",
                EditorStyles.miniLabel);
            EditorGUILayout.Space();

            DrawConnect();
            EditorGUILayout.Space();
            DrawSetup();
            EditorGUILayout.Space();
            DrawShip();
            EditorGUILayout.Space();
            DrawCrashes();
            EditorGUILayout.Space();
            DrawChecklist();

            if (!string.IsNullOrEmpty(activeLabel))
                EditorGUILayout.HelpBox(activeLabel, MessageType.Info);
            if (!string.IsNullOrEmpty(lastError))
                EditorGUILayout.HelpBox(lastError, MessageType.Error);
            EditorGUILayout.EndScrollView();
        }

        void Panel(string title, bool ok, Action body)
        {
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label((ok ? "✓ " : "○ ") + title, EditorStyles.boldLabel);
            body();
            EditorGUILayout.EndVertical();
        }

        void DrawConnect()
        {
            Panel("1. CONNECT", ConnectReady(), () =>
            {
                int engIdx = s.Engine == "gha" ? 1 : 0;
                int newIdx = EditorGUILayout.Popup("Engine", engIdx,
                    new[] { "Unity Build Automation", "GitHub Actions (no dashboard)" });
                s.Engine = newIdx == 1 ? "gha" : "uba";
                EditorGUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(".p8 key file",
                    string.IsNullOrEmpty(s.AscP8Path) ? "(none)" : Path.GetFileName(s.AscP8Path));
                if (GUILayout.Button("Pick...", GUILayout.Width(60)))
                {
                    string p = EditorUtility.OpenFilePanel(
                        "App Store Connect API key", "", "p8");
                    if (!string.IsNullOrEmpty(p)) { s.AscP8Path = p; s.ResolveP8Pem(); s.Save(); }
                }
                EditorGUILayout.EndHorizontal();
                s.AscKeyId = EditorGUILayout.TextField("Key ID", s.AscKeyId).Trim();
                s.AscIssuerId = EditorGUILayout.TextField("Issuer ID", s.AscIssuerId).Trim();
                if (GUILayout.Button("Where do I get these? (App Store Connect → Integrations)",
                    EditorStyles.linkLabel))
                    Application.OpenURL("https://appstoreconnect.apple.com/access/integrations/api");

                EditorGUILayout.Space(4);
                if (s.Engine == "uba")
                {
                    if (string.IsNullOrEmpty(s.UbaOrgId))
                    { s.UbaOrgId = CloudProjectSettings.organizationId; }
                    if (string.IsNullOrEmpty(s.UbaProjectId))
                    { s.UbaProjectId = CloudProjectSettings.projectId; }
                    s.UbaOrgId = EditorGUILayout.TextField("Unity Org ID", s.UbaOrgId).Trim();
                    s.UbaProjectId = EditorGUILayout.TextField("Unity Project ID", s.UbaProjectId).Trim();
                    if (string.IsNullOrEmpty(s.UbaOrgId) || string.IsNullOrEmpty(s.UbaProjectId))
                    {
                        // Auto-fill reads Unity's CloudProjectSettings, which is
                        // empty until the project is linked to Unity Cloud. Once
                        // linked, the fields above fill themselves on repaint.
                        EditorGUILayout.HelpBox(
                            "Empty? This project isn't linked to Unity Cloud yet. In the editor: "
                            + "Edit → Project Settings → Services → create or select a cloud "
                            + "project (same thing behind the cloud icon, top-right). The IDs "
                            + "then fill in automatically — or paste them from your project's "
                            + "URL on cloud.unity.com.",
                            MessageType.Info);
                        if (GUILayout.Button("Open Services settings", GUILayout.Width(170)))
                            SettingsService.OpenProjectSettings("Project/Services");
                    }
                    s.UbaApiKey = EditorGUILayout.TextField("Build Automation API key", s.UbaApiKey).Trim();
                    if (GUILayout.Button("Where? (Unity Cloud → Build Automation → Settings)",
                        EditorStyles.linkLabel))
                        Application.OpenURL(ProjectDashboardUrl());
                }

                EditorGUILayout.Space(4);
                s.GitHubToken = EditorGUILayout.PasswordField(
                    "GitHub token (repo)", s.GitHubToken).Trim();
                // 'workflow' is required on top of 'repo': GitHub refuses to
                // push anything under .github/workflows/ without it, which is
                // exactly what the GitHub Actions engine commits. Pre-checking
                // both here saves a rejected push and a second token round-trip.
                if (GUILayout.Button("Create a classic token with 'repo' + 'workflow' scopes",
                    EditorStyles.linkLabel))
                    Application.OpenURL("https://github.com/settings/tokens/new"
                        + "?scopes=repo,workflow&description=MacFree");

                if (s.Engine == "gha")
                {
                    s.TeamId = EditorGUILayout.TextField("Apple Team ID", s.TeamId).Trim();
                    EditorGUILayout.LabelField("Unity license (runner)", EditorStyles.boldLabel);
                    // Serial + email + password is the ONLY activation the
                    // runner supports (works for Personal too — its serial
                    // starts with F). A .ulf is useless here: no game-ci
                    // activation script reads UNITY_LICENSE, and Unity no
                    // longer issues one for Personal anyway.
                    EditorGUILayout.BeginHorizontal();
                    s.UnitySerial = EditorGUILayout.TextField("Unity serial", s.UnitySerial).Trim();
                    if (GUILayout.Button("Detect", GUILayout.Width(60)))
                    {
                        string found = UnityLicenseInfo.DetectSerial();
                        if (string.IsNullOrEmpty(found))
                            lastError = "Couldn't read this machine's Unity license ("
                                + UnityLicenseInfo.DefaultLicensePath() + "). Make sure this "
                                + "editor is activated, or paste the serial from your Unity "
                                + "account manually.";
                        else { s.UnitySerial = found; s.Save(); GUI.FocusControl(null); }
                    }
                    EditorGUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(s.UnitySerial))
                        GUILayout.Label(UnityLicenseInfo.IsPersonal(s.UnitySerial)
                            ? "Unity Personal serial — supported on the runner."
                            : "Unity Plus/Pro serial.", EditorStyles.miniLabel);
                    else
                        GUILayout.Label("\"Detect\" reads the serial this editor is activated with.",
                            EditorStyles.miniLabel);
                    s.UnityEmail = EditorGUILayout.TextField("Unity email", s.UnityEmail).Trim();
                    s.UnityPassword = EditorGUILayout.PasswordField("Unity password", s.UnityPassword);
                    GUILayout.Label("Stored as encrypted repo secrets; the runner needs them to "
                        + "activate Unity.", EditorStyles.miniLabel);
                }
                if (GUI.changed) s.Save();
            });
        }

        void DrawSetup()
        {
            Panel("2. SET UP", SetupComplete(), () =>
            {
                GUILayout.Label("Registers the bundle ID, creates the certificate and\n"
                    + "provisioning profile, uploads signing to Build Automation,\n"
                    + "creates the build target and writes the upload hook.",
                    EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(!ConnectReady() || activeTask != null))
                {
                    if (GUILayout.Button("SET UP EVERYTHING", GUILayout.Height(32)))
                    {
                        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                        var progress = new Progress<string>(m => { activeLabel = m; Repaint(); });
                        StartTask("Setting up...", ActiveEngine().RunSetup(
                            PlayerSettings.applicationIdentifier,
                            PlayerSettings.productName,
                            Application.unityVersion,
                            GitInfo.CurrentBranch(root) ?? "main",
                            GitInfo.UnitySubdirectory(root),
                            root, progress));
                    }
                }
                if (s.CompletedSteps.Count > 0)
                    GUILayout.Label("Done: " + string.Join(", ", s.CompletedSteps),
                        EditorStyles.miniLabel);
                if (GUILayout.Button("Reset setup state (advanced)", EditorStyles.linkLabel))
                { s.CompletedSteps.Clear(); s.Save(); }
            });
        }

        void DrawShip()
        {
            Panel("3. SHIP", SetupComplete(), () =>
            {
                // Apple hard-rejects any upload without a compliant icon
                // (ITMS-91111) AFTER the whole cloud build - gate it here
                // instead of wasting the ~30 minutes.
                var iconCheck = checks == null ? null
                    : checks.Find(c => c.Label.StartsWith("App icon"));
                bool iconBlocked = iconCheck != null && !iconCheck.Ok;
                if (iconBlocked)
                    EditorGUILayout.HelpBox(
                        "Apple rejects every upload without a 1024x1024 opaque app icon "
                        + "(ITMS-91111). Use the App icon fix in Preflight below, then ship.",
                        MessageType.Warning);
                using (new EditorGUI.DisabledScope(iconBlocked
                    || !SetupComplete() || activeTask != null || shipBuildNumber >= 0))
                {
                    if (GUILayout.Button("BUILD & SHIP TO TESTFLIGHT ✈", GUILayout.Height(36)))
                        StartTask("Starting cloud build...", StartShip());
                }
                if (!string.IsNullOrEmpty(shipStatus))
                    EditorGUILayout.HelpBox(shipStatus, MessageType.Info);
                if (shipBuildNumber >= 0 && GUILayout.Button("Stop watching"))
                { shipBuildNumber = -1; shipStatus = ""; }
            });
        }

        async Task StartShip()
        {
            // Fail fast if the GitHub token can't clone the repo — otherwise the
            // cloud checkout burns ~30 min before dying on "GIT PAT: Exceeded
            // maximum retries". Catches expired/wrong-scope tokens instantly.
            if (!string.IsNullOrEmpty(s.GitHubToken) && !string.IsNullOrEmpty(s.GitHubRepo))
            {
                string problem = await new GitHubApi(s.GitHubToken)
                    .CheckRepoCloneAccess(s.GitHubRepo);
                if (problem != null)
                {
                    // The dashboard advice only applies to Unity Build Automation —
                    // GitHub Actions has no such dashboard/source-control page.
                    if (s.Engine == "uba")
                        problem += "\n\nThen paste the SAME token into "
                            + "Unity Cloud → Build Automation → Settings → Source control, "
                            + "Save, and ship again — that is the credential the cloud "
                            + "runner clones with (MacFree cannot set it for you).";
                    throw new Exception(problem);
                }
            }
            expectedStamp = MacFreePreExport.Stamp(System.DateTime.UtcNow);
            ascPolls = 0;
            shipRunId = await ActiveEngine().StartBuild();
            shipBuildNumber = (int)System.Math.Min(int.MaxValue, shipRunId); // display
            shipStatus = "Cloud build started (" + s.Engine.ToUpper() + "). This can take 10-45 min.";
            nextPollAt = EditorApplication.timeSinceStartup + 20.0;
        }

        async Task PollShip()
        {
            string status;
            try { (status, _) = await ActiveEngine().GetBuildStatus(shipRunId); }
            catch (MacFreeApiException e) when (e.Status >= 500 || e.Status == 429)
            {
                // Transient Build Automation hiccup (gateway 5xx / throttling).
                // The build keeps running; just poll again instead of alarming.
                shipStatus = "Status check hiccup (HTTP " + e.Status + ") — retrying...";
                return;
            }
            if (status == "success")
            {
                shipStatus = "Build succeeded. Verifying arrival on App Store Connect...";
                if (string.IsNullOrEmpty(s.AppId))
                    s.AppId = await Asc().FindAppId(PlayerSettings.applicationIdentifier);
                if (string.IsNullOrEmpty(s.AppId))
                {
                    shipStatus = "Build uploaded. Create the app record on App Store Connect to track it here.";
                    shipBuildNumber = -1;
                }
                else
                {
                    string v = await Asc().NewestBuildVersion(s.AppId);
                    int vn;
                    bool matches = v != null && int.TryParse(v, out vn) && vn >= expectedStamp;
                    if (matches)
                    {
                        shipStatus = "On TestFlight ✈  build " + v + " (Apple finishes processing in ~10 min).";
                        shipBuildNumber = -1;
                    }
                    else
                    {
                        ascPolls++;
                        // Apple can take 5-15 min to register an upload; give
                        // it ~12 min of polls before concluding it never landed.
                        if (ascPolls >= 36)
                        {
                            // The cloud build "succeeds" even when the upload
                            // hook fails (UBA doesn't fail builds on post-build
                            // script errors), so the real reason is in the log.
                            string uploadTail = "";
                            try { uploadTail = await ActiveEngine().GetBuildLog(
                                shipRunId, 80); } catch { }
                            lastError = "Build succeeded but the new build hasn't appeared "
                                + "on App Store Connect. Open the build log to check the "
                                + "'=== MacFree upload ===' section."
                                + ErrorTranslator.BuildLogHint(uploadTail);
                            shipStatus = "";
                            shipBuildNumber = -1;
                        }
                        else
                        {
                            shipStatus = "Build uploaded — waiting for App Store Connect to register it...";
                        }
                    }
                }
            }
            else if (status == "failure" || status == "canceled")
            {
                string tail = await ActiveEngine().GetBuildLog(shipRunId, 80);
                lastError = "Build " + status + ".\n" + tail + ErrorTranslator.BuildLogHint(tail);
                shipStatus = ""; shipBuildNumber = -1; shipRunId = -1;
            }
            else
            {
                shipStatus = "Cloud build: " + status + "...";
            }
        }

        /// Crashes are read from stored state only: opening MacFree must never
        /// cost an API call, and the crash window is what refreshes it.
        void DrawCrashes()
        {
            Panel("4. CRASHES", s.CrashCount == 0, () =>
            {
                GUILayout.Label(s.CrashCount < 0
                    ? "Crashes your TestFlight testers hit appear here, with the likely cause."
                    : s.CrashSummary, EditorStyles.wordWrappedMiniLabel);
                // The crash window auto-refreshes on open, which needs a
                // working ASC JWT; with no .p8 configured that throws
                // "No .p8 key content." (AscJwt.CreateToken) and the window's
                // very first impression is that six-word CLR message.
                using (new EditorGUI.DisabledScope(!ConnectReady()))
                {
                    if (GUILayout.Button("Open crash reports"))
                        MacFreeCrashWindow.Open(s);
                }
                if (!ConnectReady())
                    GUILayout.Label("Finish step 1 (CONNECT) first.", EditorStyles.miniLabel);
            });
        }

        void DrawChecklist()
        {
            GUILayout.Label("Preflight", EditorStyles.boldLabel);
            foreach (var c in checks)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label((c.Ok ? "✓" : "✗") + "  " + c.Label);
                if (!c.Ok && c.Fix != null && GUILayout.Button(
                    c.FixLabel ?? "Fix", GUILayout.Width(90)))
                { c.Fix(); RefreshChecks(); }
                EditorGUILayout.EndHorizontal();
            }
            if (s.Engine == "uba" && scmConnected.HasValue)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label((scmConnected.Value ? "✓" : "✗") + "  Build Automation repo connected");
                if (!scmConnected.Value && GUILayout.Button("Open project", GUILayout.Width(110)))
                    Application.OpenURL(ProjectDashboardUrl());
                EditorGUILayout.EndHorizontal();
                if (!scmConnected.Value)
                {
                    // MacFree can create the repo and push for you; the ONE
                    // step it can't do headlessly is authorizing the repo in
                    // Build Automation's own settings page (browser).
                    using (new EditorGUI.DisabledScope(activeTask != null))
                    {
                        if (GUILayout.Button("Create GitHub repo & push this project"))
                            StartTask("Creating GitHub repo and pushing...", CreateAndPushRepo());
                    }
                    if (!string.IsNullOrEmpty(s.GitHubRepo))
                        EditorGUILayout.HelpBox("Repo pushed: " + s.GitHubRepo, MessageType.None);
                    EditorGUILayout.HelpBox(
                        "1. Paste a GitHub 'repo' token in step 1, then click \"Create GitHub "
                        + "repo & push\" above (or push your own repo).\n" +
                        "2. Click \"Open project\", go to Build Automation → Settings → Source "
                        + "control, set provider GitHub, paste the token, pick "
                        + (string.IsNullOrEmpty(s.GitHubRepo) ? "your repository" : s.GitHubRepo)
                        + ", and Save.\n" +
                        "Then click BUILD & SHIP.",
                        MessageType.Info);
                }
            }
            if (GUILayout.Button("Re-check", GUILayout.Width(90))) RefreshChecks();
        }
    }

    /// Minimal git introspection for branch + Unity project subdirectory.
    public static class GitInfo
    {
        public static string CurrentBranch(string projectRoot)
        {
            string dir = projectRoot;
            while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null) return null;
            string head = Path.Combine(dir, ".git", "HEAD");
            if (!File.Exists(head)) return null;
            string line = File.ReadAllText(head).Trim();
            const string prefix = "ref: refs/heads/";
            return line.StartsWith(prefix) ? line.Substring(prefix.Length) : null;
        }

        /// Path of the Unity project relative to the repo root ("" if same).
        public static string UnitySubdirectory(string projectRoot)
        {
            string dir = projectRoot;
            while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null || dir.Length >= projectRoot.Length) return "";
            return projectRoot.Substring(dir.Length + 1).Replace('\\', '/');
        }
    }
}
