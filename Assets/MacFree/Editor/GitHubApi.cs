using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using SimpleJSON;

namespace MacFree.Editor
{
    /// Tiny GitHub REST client: just enough to create the repo MacFree pushes
    /// to. Auth is a classic 'repo' PAT (the same token Build Automation uses
    /// to clone). Handler is injectable so tests never hit the network.
    public class GitHubApi
    {
        readonly HttpClient http;
        const string Base = "https://api.github.com";

        public GitHubApi(string token, HttpMessageHandler handler = null)
        {
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            // GitHub rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MacFree-iOS-Builder");
        }

        async Task<JSONNode> Send(HttpMethod method, string path, string json, string op)
        {
            var req = new HttpRequestMessage(method, Base + path);
            if (json != null)
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if ((int)resp.StatusCode >= 300)
                throw new MacFreeApiException(op, (int)resp.StatusCode, body);
            return string.IsNullOrEmpty(body) ? new JSONObject() : JSON.Parse(body);
        }

        /// GitHub repo names allow letters, digits, '-', '_', '.'; fold
        /// everything else to '-' and collapse runs. Used to derive a repo
        /// name from the Unity product name.
        public static string SanitizeRepoName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unity-ios-app";
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'
                    ? char.ToLowerInvariant(c) : '-');
            string r = sb.ToString().Trim('-');
            while (r.Contains("--")) r = r.Replace("--", "-");
            return string.IsNullOrEmpty(r) ? "unity-ios-app" : r;
        }

        /// Verify the stored token can actually CLONE the target repo, so a
        /// bad/expired/wrong-scope token fails instantly instead of ~30 minutes
        /// into the cloud checkout ("GIT PAT: Exceeded maximum retries").
        /// Returns null when access is fine, otherwise a human-readable problem.
        /// Note: this validates the token MacFree holds; it cannot see the
        /// separate token stored in the Build Automation dashboard, so a clean
        /// result still requires that dashboard token to match.
        public async Task<string> CheckRepoCloneAccess(string ownerSlashName)
        {
            if (string.IsNullOrEmpty(ownerSlashName) || !ownerSlashName.Contains("/"))
                return null; // no repo yet — nothing to verify
            JSONNode repo;
            try
            {
                repo = await Send(HttpMethod.Get, "/repos/" + ownerSlashName, null,
                    "Check repository access");
            }
            catch (MacFreeApiException e) when (e.Status == 404)
            {
                return "GitHub token can't see " + ownerSlashName + " (404) — it is "
                    + "likely expired, or has no access to this private repo. Create a "
                    + "classic 'repo' token (link in step 1) on the account that owns it.";
            }
            catch (MacFreeApiException e) when (e.Status == 401)
            {
                return "GitHub token was rejected (401 — expired or revoked). Create a "
                    + "fresh classic 'repo' token in step 1.";
            }
            catch
            {
                // This check is a fast-fail convenience, never a gate: a
                // transient GitHub failure (403 rate-limit, 5xx outage, network
                // blip) must never block a ship — especially for UBA, which
                // never contacted GitHub at all before this check existed.
                // "No problem detected" is the safe default; proceed.
                return null;
            }
            // GitHub returns a "permissions" object on authenticated repo reads;
            // pull==false means the token can see the repo but cannot clone it.
            var perms = repo["permissions"];
            if (perms.IsObject && perms.Count > 0 && !perms["pull"].AsBool)
                return "GitHub token can see " + ownerSlashName + " but lacks clone "
                    + "(Contents: Read) access. Regenerate it with the full 'repo' scope.";
            return null;
        }

        /// The scopes a classic PAT carries, read from GitHub's X-OAuth-Scopes
        /// response header. Returns an empty array when GitHub reports none —
        /// notably for fine-grained tokens, which don't use this header — so
        /// callers must treat "empty" as "unknown", never as "missing".
        public async Task<string[]> GetTokenScopes()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, Base + "/user");
            var resp = await http.SendAsync(req);
            System.Collections.Generic.IEnumerable<string> values;
            if (!resp.Headers.TryGetValues("X-OAuth-Scopes", out values))
                return new string[0];
            var list = new System.Collections.Generic.List<string>();
            foreach (string chunk in string.Join(",", values).Split(','))
            {
                string t = chunk.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list.ToArray();
        }

        /// The login of the token's owner — used to form owner/name and the
        /// remote URL without asking the user who they are.
        public async Task<string> GetLogin()
        {
            var n = await Send(HttpMethod.Get, "/user", null, "Get GitHub user");
            return n["login"].Value;
        }

        /// Create the repo (private) if it doesn't exist; return its details.
        /// A 422 "name already exists" is treated as success — we then GET the
        /// existing repo so re-running setup is idempotent.
        public async Task<(string fullName, string cloneUrl)> EnsureRepo(
            string name, bool isPrivate)
        {
            string login = await GetLogin();
            try
            {
                var made = await Send(HttpMethod.Post, "/user/repos",
                    "{\"name\":\"" + JsonUtil.Esc(name) + "\",\"private\":"
                    + (isPrivate ? "true" : "false")
                    + ",\"description\":\"Built with MacFree — iOS Builder\"}",
                    "Create GitHub repository");
                return (made["full_name"].Value, made["clone_url"].Value);
            }
            catch (MacFreeApiException e) when (e.Status == 422
                && e.Body.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var got = await Send(HttpMethod.Get, "/repos/" + login + "/" + name,
                    null, "Get GitHub repository");
                return (got["full_name"].Value, got["clone_url"].Value);
            }
        }

        /// The repo's Actions secrets public key (for sealed-box encryption).
        public async Task<(string keyId, string keyBase64)> GetActionsPublicKey(
            string owner, string repo)
        {
            var n = await Send(HttpMethod.Get,
                "/repos/" + owner + "/" + repo + "/actions/secrets/public-key",
                null, "Get Actions public key");
            return (n["key_id"].Value, n["key"].Value);
        }

        /// Create/update an Actions secret. The value is sealed-box encrypted
        /// to the repo public key (GitHub requires libsodium sealed box).
        public async Task SetSecret(string owner, string repo, string name, string plaintext)
        {
            var (keyId, keyB64) = await GetActionsPublicKey(owner, repo);
            byte[] recipientPk = Convert.FromBase64String(keyB64);
            byte[] sealedBytes = SealedBox.Seal(
                Encoding.UTF8.GetBytes(plaintext ?? ""), recipientPk);
            string body = "{\"encrypted_value\":\""
                + JsonUtil.Esc(Convert.ToBase64String(sealedBytes))
                + "\",\"key_id\":\"" + JsonUtil.Esc(keyId) + "\"}";
            await Send(HttpMethod.Put,
                "/repos/" + owner + "/" + repo + "/actions/secrets/" + name,
                body, "Set Actions secret " + name);
        }

        /// Trigger the workflow (workflow_dispatch) on a branch.
        public Task DispatchWorkflow(string owner, string repo, string workflowFile, string branch)
        {
            return Send(HttpMethod.Post,
                "/repos/" + owner + "/" + repo + "/actions/workflows/" + workflowFile
                    + "/dispatches",
                "{\"ref\":\"" + JsonUtil.Esc(branch) + "\"}", "Dispatch workflow");
        }

        /// Newest run id for a workflow file (0 if none yet).
        public async Task<long> LatestRunId(string owner, string repo, string workflowFile)
        {
            var n = await Send(HttpMethod.Get,
                "/repos/" + owner + "/" + repo + "/actions/workflows/" + workflowFile
                    + "/runs?per_page=1",
                null, "List workflow runs");
            var runs = n["workflow_runs"].AsArray;
            return runs.Count == 0 ? 0L : runs[0]["id"].AsLong;
        }

        /// A run's (status, conclusion). status ∈ {queued,in_progress,completed};
        /// conclusion ∈ {success,failure,cancelled,...} once completed.
        public async Task<(string status, string conclusion)> GetRunStatus(
            string owner, string repo, long runId)
        {
            var n = await Send(HttpMethod.Get,
                "/repos/" + owner + "/" + repo + "/actions/runs/" + runId, null, "Get run");
            return (n["status"].Value, n["conclusion"].Value);
        }

        /// Best-effort tail of a run's logs (a zip). Returns "" when the log
        /// isn't retrievable, so the UI degrades to the run's web URL.
        public async Task<string> GetRunLogTail(string owner, string repo, long runId, int lines)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get,
                    Base + "/repos/" + owner + "/" + repo + "/actions/runs/" + runId + "/logs");
                var resp = await http.SendAsync(req);
                if ((int)resp.StatusCode >= 300) return "";
                byte[] zip = await resp.Content.ReadAsByteArrayAsync();
                string text = ExtractZipText(zip);
                var all = text.Split('\n');
                int from = Math.Max(0, all.Length - lines);
                return string.Join("\n", all, from, all.Length - from);
            }
            catch { return ""; }
        }

        // GitHub's run-log zip has one .txt per step, and post-steps
        // ("Post Run actions/cache@v4", "Complete job") always run and always
        // sort last — so a plain concatenation's *tail* is boilerplate, not
        // the failure. Prefer the first entry that contains an "##[error]"
        // marker (GitHub Actions' own error-annotation prefix) and return
        // from a little before that marker onward; fall back to concatenating
        // everything when no entry has one.
        public static string ExtractZipText(byte[] zip)
        {
            var sb = new StringBuilder();
            string errorTail = null;
            using (var ms = new System.IO.MemoryStream(zip))
            using (var archive = new System.IO.Compression.ZipArchive(
                ms, System.IO.Compression.ZipArchiveMode.Read))
                foreach (var e in archive.Entries)
                {
                    if (!e.FullName.EndsWith(".txt")) continue;
                    string text;
                    using (var r = new System.IO.StreamReader(e.Open()))
                        text = r.ReadToEnd();
                    sb.Append(text).Append('\n');
                    if (errorTail == null)
                    {
                        int idx = text.IndexOf("##[error]", StringComparison.Ordinal);
                        if (idx >= 0)
                            errorTail = text.Substring(Math.Max(0, idx - 500));
                    }
                }
            return errorTail ?? sb.ToString();
        }
    }
}
