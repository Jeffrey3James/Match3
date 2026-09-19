using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using SimpleJSON;

namespace MacFree.Editor
{
    /// Typed client for the App Store Connect API (v1): bundle ids,
    /// certificates, provisioning profiles, apps and builds.
    public class AscApi
    {
        readonly HttpClient http;
        readonly Func<string> token;
        const string Base = "https://api.appstoreconnect.apple.com/v1";

        public AscApi(Func<string> tokenProvider, HttpMessageHandler handler = null)
        {
            token = tokenProvider;
            http = handler == null ? new HttpClient() : new HttpClient(handler);
        }

        async Task<JSONNode> Send(HttpMethod method, string path, string json, string op)
        {
            var req = new HttpRequestMessage(method, Base + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token());
            if (json != null)
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if ((int)resp.StatusCode >= 300)
                throw new MacFreeApiException(op, (int)resp.StatusCode, body);
            return string.IsNullOrEmpty(body) ? new JSONObject() : JSON.Parse(body);
        }

        public async Task<string> EnsureBundleId(string identifier, string name)
        {
            var found = await Send(HttpMethod.Get,
                "/bundleIds?filter%5Bidentifier%5D=" + Uri.EscapeDataString(identifier),
                null, "Find bundle id");
            foreach (JSONNode d in found["data"].AsArray)
                if (d["attributes"]["identifier"].Value == identifier)
                    return d["id"].Value;

            string body = "{\"data\":{\"type\":\"bundleIds\",\"attributes\":{"
                + "\"identifier\":\"" + JsonUtil.Esc(identifier) + "\",\"name\":\"" + JsonUtil.Esc(name)
                + "\",\"platform\":\"IOS\"}}}";
            var made = await Send(HttpMethod.Post, "/bundleIds", body, "Register bundle id");
            return made["data"]["id"].Value;
        }

        public async Task<(string certId, byte[] cerDer)> CreateDistributionCertificate(byte[] csrDer)
        {
            // csrContent is base64 (already safe for JSON), but every
            // interpolated string in a body is escaped uniformly regardless.
            string csrContent = JsonUtil.Esc(Convert.ToBase64String(csrDer));
            string body = "{\"data\":{\"type\":\"certificates\",\"attributes\":{"
                + "\"certificateType\":\"IOS_DISTRIBUTION\","
                + "\"csrContent\":\"" + csrContent + "\"}}}";
            JSONNode n;
            try
            {
                n = await Send(HttpMethod.Post, "/certificates", body,
                    "Create distribution certificate");
            }
            // Apple caps Distribution certificates per team; when the cap is
            // hit it returns 409 "You already have a current iOS Distribution
            // certificate...". A fresh machine/project can't reuse the old one
            // (its private key lives elsewhere), so revoke the existing
            // Distribution cert(s) and issue a new one automatically.
            catch (MacFreeApiException e) when (e.Status == 409
                && e.Body.IndexOf("already have a current",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                await RevokeDistributionCertificates();
                n = await Send(HttpMethod.Post, "/certificates", body,
                    "Create distribution certificate (after revoking old)");
            }
            byte[] cer = Convert.FromBase64String(
                n["data"]["attributes"]["certificateContent"].Value);
            return (n["data"]["id"].Value, cer);
        }

        /// Revoke every existing Distribution certificate on the team so a
        /// fresh one can be issued. This invalidates provisioning profiles
        /// that referenced them — MacFree rebuilds its own profile against the
        /// new cert on the very next step, so its pipeline stays intact. Only
        /// touches distribution certs (never development).
        public async Task RevokeDistributionCertificates()
        {
            var list = await Send(HttpMethod.Get, "/certificates?limit=200",
                null, "List certificates");
            foreach (JSONNode d in list["data"].AsArray)
            {
                string type = d["attributes"]["certificateType"].Value;
                if (type != "IOS_DISTRIBUTION" && type != "DISTRIBUTION") continue;
                string id = d["id"].Value;
                if (!string.IsNullOrEmpty(id))
                    await Send(HttpMethod.Delete, "/certificates/" + id, null,
                        "Revoke certificate");
            }
        }

        /// Delete every profile with this exact name. Apple happily creates
        /// duplicate names and then refuses forever with 409 "Multiple profiles
        /// found with the name X", which bricks setup for that bundle id until
        /// someone cleans them up by hand. Returns how many were removed.
        public async Task<int> DeleteProfilesNamed(string profileName)
        {
            var list = await Send(HttpMethod.Get,
                "/profiles?filter%5Bname%5D=" + Uri.EscapeDataString(profileName) + "&limit=200",
                null, "List provisioning profiles");
            int removed = 0;
            foreach (JSONNode d in list["data"].AsArray)
            {
                // The filter is server-side; re-check so we never delete a
                // profile that merely resembles ours.
                if (d["attributes"]["name"].Value != profileName) continue;
                string id = d["id"].Value;
                if (string.IsNullOrEmpty(id)) continue;
                await Send(HttpMethod.Delete, "/profiles/" + id, null,
                    "Delete provisioning profile");
                removed++;
            }
            return removed;
        }

        /// Create the App Store profile, replacing any existing profile of the
        /// same name. Replace rather than reuse: a profile named
        /// "MacFree &lt;bundle id&gt;" is MacFree's own, and re-running SET UP may
        /// have changed the bundle-id resource or re-issued the Distribution
        /// certificate (which invalidates old profiles anyway) — so a fresh
        /// profile is always the correct one, and this keeps re-runs idempotent
        /// instead of piling up duplicates Apple will later reject.
        public async Task<byte[]> CreateAppStoreProfile(string profileName,
            string bundleIdResourceId, string certId)
        {
            await DeleteProfilesNamed(profileName);
            string body = "{\"data\":{\"type\":\"profiles\",\"attributes\":{"
                + "\"name\":\"" + JsonUtil.Esc(profileName) + "\",\"profileType\":\"IOS_APP_STORE\"},"
                + "\"relationships\":{"
                + "\"bundleId\":{\"data\":{\"type\":\"bundleIds\",\"id\":\"" + JsonUtil.Esc(bundleIdResourceId) + "\"}},"
                + "\"certificates\":{\"data\":[{\"type\":\"certificates\",\"id\":\"" + JsonUtil.Esc(certId) + "\"}]}}}}";
            var n = await Send(HttpMethod.Post, "/profiles", body, "Create provisioning profile");
            return Convert.FromBase64String(n["data"]["attributes"]["profileContent"].Value);
        }

        public async Task<string> FindAppId(string bundleId)
        {
            var n = await Send(HttpMethod.Get,
                "/apps?filter%5BbundleId%5D=" + Uri.EscapeDataString(bundleId), null, "Find app");
            var arr = n["data"].AsArray;
            return arr.Count > 0 ? arr[0]["id"].Value : null;
        }

        public async Task<string> NewestBuildVersion(string appId)
        {
            var n = await Send(HttpMethod.Get,
                "/builds?filter%5Bapp%5D=" + appId
                + "&sort=-uploadedDate&limit=1&fields%5Bbuilds%5D=version,processingState",
                null, "List builds");
            var arr = n["data"].AsArray;
            return arr.Count > 0 ? arr[0]["attributes"]["version"].Value : null;
        }

        /// TestFlight crash submissions for an app, newest first. Apple only
        /// collects a crash when the tester taps Share on the crash dialog, so
        /// this is a sample of real crashes, never a complete record.
        public async Task<List<CrashSubmission>> ListCrashSubmissions(string appId, int limit)
        {
            if (limit < 1) limit = 1;
            if (limit > 200) limit = 200;   // Apple's cap; over it is a 400.
            var n = await Send(HttpMethod.Get,
                "/apps/" + Uri.EscapeDataString(appId) + "/betaFeedbackCrashSubmissions"
                + "?sort=-createdDate&limit=" + limit,
                null, "List crash submissions");
            var list = new List<CrashSubmission>();
            foreach (JSONNode d in n["data"].AsArray)
            {
                var a = d["attributes"];
                list.Add(new CrashSubmission
                {
                    Id = d["id"].Value,
                    CreatedDate = a["createdDate"].Value,
                    DeviceModel = a["deviceModel"].Value,
                    OsVersion = a["osVersion"].Value,
                    AppUptimeMs = a["appUptimeInMilliseconds"].AsLong,
                    DiskBytesAvailable = a["diskBytesAvailable"].AsLong,
                    BatteryPercentage = a["batteryPercentage"].AsDouble,
                    Comment = a["comment"].Value,
                });
            }
            return list;
        }

        /// The crash log text for one submission, or null when Apple has none.
        /// Two shapes exist in the wild: an inline `logText` attribute, and a
        /// `downloadUrl` fetched separately — which is why this deliberately
        /// sends no `fields[betaCrashLogs]` filter, as that would hide one of
        /// them. The pre-signed download URL needs no Authorization header.
        public async Task<string> GetCrashLogText(string submissionId)
        {
            var n = await Send(HttpMethod.Get,
                "/betaFeedbackCrashSubmissions/" + Uri.EscapeDataString(submissionId) + "/crashLog",
                null, "Get crash log");
            var a = n["data"]["attributes"];
            string inline = a["logText"].Value;
            if (!string.IsNullOrEmpty(inline)) return inline;
            string url = a["downloadUrl"].Value;
            if (string.IsNullOrEmpty(url)) return null;
            return await http.GetStringAsync(url);
        }
    }
}
