using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using SimpleJSON;

namespace MacFree.Editor
{
    public class MacFreeApiException : Exception
    {
        public int Status;
        public string Body;
        public MacFreeApiException(string op, int status, string body)
            : base(op + " failed (HTTP " + status + "): " + body)
        { Status = status; Body = body; }
    }

    /// Typed client for the classic Unity Cloud Build (Build Automation)
    /// REST API. Field names mirror Fixtures/uba-buildtarget.json exactly.
    public class UbaApi
    {
        readonly HttpClient http;
        readonly string apiRoot;
        readonly string baseUrl;
        readonly string orgId;

        public UbaApi(string apiKey, string orgId, string projectId,
            HttpMessageHandler handler = null)
        {
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", apiKey);
            this.orgId = orgId;
            apiRoot = "https://build-api.cloud.unity3d.com/api/v1";
            baseUrl = apiRoot + "/orgs/" + orgId + "/projects/" + projectId;
        }

        async Task<JSONNode> Send(HttpMethod method, string path, HttpContent content, string op)
        {
            return await SendUrl(method, baseUrl + path, content, op);
        }

        // Same request/error handling as Send, but against an absolute URL -
        // used for endpoints that are NOT scoped to org/project (e.g. the
        // supported-Xcode-versions list at /api/v1/versions/xcode).
        async Task<JSONNode> SendUrl(HttpMethod method, string url, HttpContent content, string op)
        {
            var req = new HttpRequestMessage(method, url) { Content = content };
            var resp = await http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if ((int)resp.StatusCode >= 300)
                throw new MacFreeApiException(op, (int)resp.StatusCode, body);
            return string.IsNullOrEmpty(body) ? new JSONObject() : JSON.Parse(body);
        }

        static StringContent Json(string s)
        {
            return new StringContent(s, Encoding.UTF8, "application/json");
        }

        public async Task<string> UploadIosCredentials(string label, byte[] p12,
            string p12Password, byte[] mobileProvision)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(label), "label");
            form.Add(new StringContent(p12Password), "certificatePass");
            var certPart = new ByteArrayContent(p12);
            certPart.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            form.Add(certPart, "fileCertificate", "macfree.p12");
            var provPart = new ByteArrayContent(mobileProvision);
            provPart.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            form.Add(provPart, "fileProvisioningProfile", "macfree.mobileprovision");
            var node = await Send(HttpMethod.Post, "/credentials/signing/ios", form,
                "Upload signing credentials");
            return node["credentialid"].Value;
        }

        // Build Automation publishes the Xcode versions it currently offers
        // at a project-independent endpoint. We pick the newest one that is
        // neither deprecated nor hidden, so setup never pins a version Apple
        // or UBA will later retire - and it needs no per-machine config. The
        // API used to accept the alias "latest"; it now rejects it and
        // requires a specific version, which is why this lookup exists.
        public async Task<string> GetLatestXcodeVersion()
        {
            var node = await SendUrl(HttpMethod.Get, apiRoot + "/versions/xcode",
                null, "List Xcode versions");
            var arr = node.AsArray;
            string best = null;
            long bestScore = -1;
            for (int i = 0; i < arr.Count; i++)
            {
                var v = arr[i];
                if (v["deprecated"].AsBool || v["hidden"].AsBool) continue;
                long score = ScoreXcode(v["value"].Value);
                if (score > bestScore) { bestScore = score; best = v["value"].Value; }
            }
            if (string.IsNullOrEmpty(best))
                throw new MacFreeApiException("List Xcode versions", 200,
                    "Build Automation returned no usable Xcode versions.");
            return best;
        }

        // "xcode26_5_0" -> 26*1e6 + 5*1e3 + 0, so the newest sorts highest no
        // matter what order the API returns the versions in.
        static long ScoreXcode(string value)
        {
            if (string.IsNullOrEmpty(value)) return -1;
            string digits = value.StartsWith("xcode") ? value.Substring(5) : value;
            string[] parts = digits.Split('_');
            long score = 0;
            for (int i = 0; i < 3; i++)
            {
                long n = 0;
                if (i < parts.Length) long.TryParse(parts[i], out n);
                score = score * 1000 + n;
            }
            return score;
        }

        // The numeric organization id ("orgFk") the Unity Cloud dashboard uses
        // in its URLs. The org id everywhere else can be a handle, GUID, or
        // numeric, but a deep-link into the browser dashboard only routes by
        // this numeric form - so resolve it before building that URL.
        public async Task<string> GetOrgForeignKey()
        {
            var node = await SendUrl(HttpMethod.Get, apiRoot + "/orgs/" + orgId,
                null, "Get organization");
            return node["orgFk"].Value;
        }

        // Field names/nesting mirror Fixtures/uba-buildtarget.json exactly:
        // ROOT-level {name, platform, enabled, settings, credentials} - note
        // credentials is a SIBLING of settings, not nested inside it (fixture
        // top-level keys) - settings.autoBuild,
        // settings.scm.{type,branch,subdirectory},
        // settings.platform.{bundleId,xcodeVersion},
        // settings.advanced.unity.{preExportMethod,postBuildScript},
        // credentials.signing.credentialid.
        // scmType is echoed from the live project (GetScmType); the author's
        // capture shows "oauth" (GitHub linked via UBA's OAuth/GitHub-App
        // integration), which is also the fallback when the project GET
        // yields nothing - see Fixtures/README.md.
        string TargetBody(string name, string bundleId, string unityVersion,
            string branch, string subdirectory, string credentialId, string scmType,
            string xcodeVersion)
        {
            // UBA expects underscore-tokenized version strings (e.g.
            // "6000_3_19f1"), not Unity's dotted Application.unityVersion
            // ("6000.3.19f1") - normalize here so every caller is safe.
            unityVersion = unityVersion.Replace('.', '_');
            var scmSub = string.IsNullOrEmpty(subdirectory) ? "" :
                ",\"subdirectory\":\"" + JsonUtil.Esc(subdirectory) + "\"";
            return "{\"name\":\"" + JsonUtil.Esc(name) + "\",\"platform\":\"ios\",\"enabled\":true,"
                + "\"settings\":{\"autoBuild\":false,"
                + "\"unityVersion\":\"" + JsonUtil.Esc(unityVersion) + "\","
                + "\"scm\":{\"type\":\"" + JsonUtil.Esc(scmType) + "\",\"branch\":\"" + JsonUtil.Esc(branch) + "\"" + scmSub + "},"
                + "\"platform\":{\"bundleId\":\"" + JsonUtil.Esc(bundleId) + "\",\"xcodeVersion\":\"" + JsonUtil.Esc(xcodeVersion) + "\"},"
                + "\"advanced\":{\"unity\":{"
                + "\"preExportMethod\":\"MacFree.Editor.MacFreePreExport.Run\","
                + "\"postBuildScript\":\"ci/macfree-upload.sh\"}}},"
                + "\"credentials\":{\"signing\":{\"credentialid\":\"" + JsonUtil.Esc(credentialId) + "\"}}}";
        }

        public async Task EnsureBuildTarget(string targetId, string targetName,
            string bundleId, string unityVersion, string branch,
            string subdirectory, string credentialId, string xcodeVersion)
        {
            // Echo the project's live SCM type so we never clobber a
            // dashboard-configured connection; "oauth" is the observed
            // real-world default when the project GET tells us nothing.
            string scmType = await GetScmType() ?? "oauth";
            string body = TargetBody(targetName, bundleId, unityVersion,
                branch, subdirectory, credentialId, scmType, xcodeVersion);
            try
            {
                await Send(HttpMethod.Post, "/buildtargets", Json(body), "Create build target");
            }
            // The observed real conflict is NOT a 409: the live API returns
            // HTTP 500 with the exact string "Build target name already in
            // use for this project!" - so the substring match is
            // load-bearing, not just a nicety. 409 kept for future-proofing.
            catch (MacFreeApiException e) when (e.Body.Contains("already in use")
                || e.Status == 409)
            {
                await Send(HttpMethod.Put, "/buildtargets/" + targetId, Json(body),
                    "Update build target");
            }
        }

        public Task SetEnvVars(string targetId, Dictionary<string, string> vars)
        {
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in vars)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(JsonUtil.Esc(kv.Key)).Append("\":\"").Append(JsonUtil.Esc(kv.Value)).Append("\"");
            }
            sb.Append("}");
            return Send(HttpMethod.Put, "/buildtargets/" + targetId + "/envvars",
                Json(sb.ToString()), "Set environment variables");
        }

        public async Task<int> StartBuild(string targetId)
        {
            var node = await Send(HttpMethod.Post,
                "/buildtargets/" + targetId + "/builds",
                Json("{\"clean\":false}"), "Start build");
            var first = node.IsArray ? node[0] : node;
            if (first["error"] != null && !string.IsNullOrEmpty(first["error"].Value))
                throw new MacFreeApiException("Start build", 400, first["error"].Value);
            return first["build"].AsInt;
        }

        // buildStatus / lastBuiltRevision field names verified against
        // Fixtures/uba-build-status.json - both present verbatim, no
        // deviation from the brief needed here.
        public async Task<(string status, string commit)> GetBuildStatus(string targetId, int number)
        {
            var n = await Send(HttpMethod.Get,
                "/buildtargets/" + targetId + "/builds/" + number, null, "Get build status");
            return (n["buildStatus"].Value, n["lastBuiltRevision"].Value);
        }

        public async Task<string> GetLogTail(string targetId, int number, int lines = 200)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                baseUrl + "/buildtargets/" + targetId + "/builds/" + number + "/log?compact=true");
            var resp = await http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if ((int)resp.StatusCode >= 300)
                throw new MacFreeApiException("Get build log", (int)resp.StatusCode, body);
            var all = body.Split('\n');
            int from = Math.Max(0, all.Length - lines);
            return string.Join("\n", all, from, all.Length - from);
        }

        public async Task<string> GetScmType()
        {
            try
            {
                var n = await Send(HttpMethod.Get, "", null, "Get project");
                var scm = n["settings"]["scm"]["type"].Value;
                return string.IsNullOrEmpty(scm) ? null : scm;
            }
            catch (MacFreeApiException) { return null; }
        }
    }
}
