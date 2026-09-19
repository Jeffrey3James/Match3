// Dependency-free tests of the production HTTP client and setup pipeline.
// All HTTP and Apple/signing calls are fake; no credentials or Unity install needed.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MacFree.Editor;
using SimpleJSON;

class UbaCompatibilityTests
{
    const string Project = "/api/v1/orgs/org/projects/project";
    const string Catalog = "/api/v1/versions/xcode";
    const string Xcodes = "[{\"value\":\"xcode26_4_1\"},{\"value\":\"xcode26_5_0\"}]";
    const string OneXcode = "[{\"value\":\"xcode26_5_0\"}]";
    const string Mac = "[{\"family\":\"mac\",\"value\":\"test-mac-image\",\"hidden\":false,\"deprecated\":false}]";
    const string Mismatch = "{\"error\":\"Could not find an operating system for the OS family 'mac' "
        + "that supported using: Xcode version 'xcode26_5_0' Unity version '6000_6_2f1'\"}";
    static int passed, failed;

    class Recorded
    {
        public string Method, Path, Body;
    }

    class FakeHttp : HttpMessageHandler
    {
        readonly Queue<Tuple<string, string, int, string>> replies =
            new Queue<Tuple<string, string, int, string>>();
        public readonly List<Recorded> Calls = new List<Recorded>();

        public FakeHttp Add(string method, string path, int status, string body)
        {
            replies.Enqueue(Tuple.Create(method, path, status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken token)
        {
            var call = new Recorded { Method = request.Method.Method,
                Path = request.RequestUri.PathAndQuery,
                Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync() };
            Calls.Add(call);
            Check(replies.Count > 0, "Unexpected HTTP call: " + call.Method + " " + call.Path);
            var reply = replies.Dequeue();
            Equal(reply.Item1, call.Method);
            Equal(reply.Item2, call.Path);
            return new HttpResponseMessage((HttpStatusCode)reply.Item3)
                { Content = new StringContent(reply.Item4) };
        }

        public void Complete() { Equal(0, replies.Count); }
    }

    class ProgressLog : IProgress<string>
    {
        public readonly List<string> Lines = new List<string>();
        public void Report(string value) { Lines.Add(value); }
    }

    static string Os(string xcode, string unity = "6000_6_2f1")
    {
        return "/api/v1/versions/operatingsystem?family=mac&unity_version="
            + unity + "&xcode_version=" + xcode;
    }

    static FakeHttp WithCatalog(string body = Xcodes)
    {
        return new FakeHttp().Add("GET", Catalog, 200, body);
    }

    static FakeHttp Create(FakeHttp h, int status = 201, string body = "{}")
    {
        return h.Add("GET", Project, 200, "{\"settings\":{\"scm\":{\"type\":\"oauth\"}}}")
            .Add("POST", Project + "/buildtargets", status, body);
    }

    static UbaApi Client(FakeHttp h) { return new UbaApi("test-only", "org", "project", h); }

    static Task<string> Run(FakeHttp h, string unity = "6000.6.2f1", IProgress<string> log = null)
    {
        return Client(h).EnsureCompatibleBuildTarget("macfree-ios", "MacFree iOS",
            "com.example.game", unity, "main", "Game/Subfolder", "credential", log);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Equal<T>(T expected, T actual)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual),
            "Expected " + expected + ", got " + actual);
    }

    static async Task<T> Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T e) { return e; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    static void Test(string name, Func<Task> test)
    {
        try { test().GetAwaiter().GetResult(); Console.WriteLine("PASS " + name); passed++; }
        catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e); failed++; }
    }

    static MacFreeSettings ResumableSettings()
    {
        var s = new MacFreeSettings { CredentialId = "credential" };
        foreach (string step in new[] { "bundleId", "csr", "cert", "profile",
            "p12+upload", "envvars", "hooks" }) s.MarkStep(step);
        return s;
    }

    static Task Pipeline(FakeHttp h, MacFreeSettings s)
    {
        return new SetupPipeline(s, new AscApi(), Client(h), "/tmp/macfree-compatibility-tests")
            .RunAll("com.example.game", "Game", "6000.6.2f1", "main", "",
                "/tmp/macfree-compatibility-tests", new ProgressLog());
    }

    static int Main()
    {
        Test("incompatible newest Xcode falls back before any target write", async () =>
        {
            var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 200, "[]")
                .Add("GET", Os("xcode26_4_1"), 200, Mac);
            Create(h);
            var log = new ProgressLog();
            Equal("xcode26_4_1", await Run(h, log: log));
            var body = JSON.Parse(h.Calls[h.Calls.Count - 1].Body);
            Equal("6000_6_2f1", body["settings"]["unityVersion"].Value);
            Check(body["settings"]["autoDetectUnityVersion"].IsBoolean,
                "Missing explicit auto-detect setting");
            Check(body["settings"]["fallbackPatchVersion"].IsBoolean,
                "Missing explicit patch-fallback setting");
            Equal(false, body["settings"]["autoDetectUnityVersion"].AsBool);
            Equal(false, body["settings"]["fallbackPatchVersion"].AsBool);
            Equal("mac", body["settings"]["operatingSystemSelected"].Value);
            Equal("xcode26_4_1", body["settings"]["platform"]["xcodeVersion"].Value);
            Equal("oauth", body["settings"]["scm"]["type"].Value);
            Equal("main", body["settings"]["scm"]["branch"].Value);
            Equal("Game/Subfolder", body["settings"]["scm"]["subdirectory"].Value);
            Equal("credential", body["credentials"]["signing"]["credentialid"].Value);
            Equal("MacFree.Editor.MacFreePreExport.Run",
                body["settings"]["advanced"]["unity"]["preExportMethod"].Value);
            Equal("ci/macfree-upload.sh",
                body["settings"]["advanced"]["unity"]["postBuildScript"].Value);
            Check(log.Lines.Exists(x => x.Contains("configured")), "Missing success progress");
            h.Complete();
        });
        Test("newest compatible pair is selected without probing older versions", async () =>
        {
            var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 200, Mac);
            Create(h);
            Equal("xcode26_5_0", await Run(h));
            h.Complete();
        });
        Test("filters aliases prereleases malformed hidden deprecated and pre-26 entries", async () =>
        {
            var h = WithCatalog("[{\"value\":\"latest\"},{\"value\":\"xcode27_0_0beta1\"},"
                + "{\"value\":\"xcode29_0_0\",\"hidden\":true},"
                + "{\"value\":\"xcode28_0_0\",\"deprecated\":true},{\"value\":\"xcode16_4_0\"},"
                + "{\"value\":\"xcode26_bad_0\"},{\"value\":\"xcode26_+5_0\"},"
                + "{\"value\":\"xcode26_5_0_extra\"},{\"value\":\"26_5_0\"},"
                + "{\"value\":\"xcode26_5_0\"},{\"value\":\"xcode26_5_0\"}]")
                .Add("GET", Os("xcode26_5_0"), 200, "[]");
            var e = await Throws<InvalidOperationException>(() => Run(h));
            Check(e.Message.Contains("6000.6.2f1"), "Missing exact Unity version");
            Check(e.Message.Contains("Checked: xcode26_5_0."), "Candidates not deduplicated");
            h.Complete();
        });
        Test("numeric version ordering is not lexical ordering", async () =>
        {
            var h = WithCatalog("[{\"value\":\"xcode26_9_0\"},{\"value\":\"xcode26_10_0\"}]")
                .Add("GET", Os("xcode26_10_0"), 200, Mac);
            Create(h);
            Equal("xcode26_10_0", await Run(h));
            h.Complete();
        });
        Test("all incompatible pairs give an actionable error and no target writes", async () =>
        {
            var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 200, "[]")
                .Add("GET", Os("xcode26_4_1"), 200, "[]");
            var e = await Throws<InvalidOperationException>(() => Run(h));
            Check(e.Message.Contains("xcode26_5_0, xcode26_4_1"), "Missing checked versions");
            Check(e.Message.Contains("was not changed"), "Missing no-downgrade explanation");
            Check(e.Message.Contains("Do not reset"), "Missing signing guidance");
            Check(h.Calls.TrueForAll(c => c.Method == "GET"), "Unexpected target write");
            h.Complete();
        });
        Test("hidden deprecated non-mac and invalid images do not qualify", async () =>
        {
            var h = WithCatalog(OneXcode).Add("GET", Os("xcode26_5_0"), 200,
                "[{\"family\":\"mac\",\"value\":\"hidden\",\"hidden\":true},"
                + "{\"family\":\"mac\",\"value\":\"old\",\"deprecated\":true},"
                + "{\"family\":\"windows\",\"value\":\"win\"},{\"family\":\"mac\"}]");
            await Throws<InvalidOperationException>(() => Run(h));
            h.Complete();
        });
        Test("an eligible image later in the response qualifies", async () =>
        {
            var h = WithCatalog(OneXcode).Add("GET", Os("xcode26_5_0"), 200,
                "[{\"family\":\"mac\",\"value\":\"old\",\"deprecated\":true},"
                + "{\"family\":\"mac\",\"value\":\"usable\"}]");
            Create(h);
            Equal("xcode26_5_0", await Run(h));
            h.Complete();
        });
        Test("provisioning race retries only the known OS rejection", async () =>
        {
            var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 200, Mac);
            Create(h, 500, Mismatch);
            h.Add("GET", Os("xcode26_4_1"), 200, Mac);
            Create(h);
            Equal("xcode26_4_1", await Run(h));
            h.Complete();
        });
        Test("exhausted provisioning failures preserve the last mismatch", async () =>
        {
            var h = WithCatalog(OneXcode).Add("GET", Os("xcode26_5_0"), 200, Mac);
            Create(h, 500, Mismatch);
            var e = await Throws<InvalidOperationException>(() => Run(h));
            Check(e.InnerException is MacFreeApiException, "Lost server diagnostic");
            h.Complete();
        });
        Test("HTTP 400 OS mismatch also falls back", async () =>
        {
            var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 200, Mac);
            Create(h, 400, Mismatch);
            h.Add("GET", Os("xcode26_4_1"), 200, Mac);
            Create(h);
            Equal("xcode26_4_1", await Run(h));
            h.Complete();
        });
        foreach (int status in new[] { 401, 403, 429, 500 })
        {
            int code = status;
            Test("unrelated target HTTP " + code + " is not swallowed", async () =>
            {
                var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 200, Mac);
                Create(h, code, "{\"error\":\"unrelated error\"}");
                Equal(code, (await Throws<MacFreeApiException>(() => Run(h))).Status);
                h.Complete();
            });
        }
        Test("compatibility lookup authorization failure stops immediately", async () =>
        {
            var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 403, "{\"error\":\"forbidden\"}");
            Equal(403, (await Throws<MacFreeApiException>(() => Run(h))).Status);
            h.Complete();
        });
        Test("global catalog failure stops immediately", async () =>
        {
            var h = new FakeHttp().Add("GET", Catalog, 401, "{\"error\":\"unauthorized\"}");
            Equal(401, (await Throws<MacFreeApiException>(() => Run(h))).Status);
            h.Complete();
        });
        foreach (int conflict in new[] { 409, 500 })
        {
            int code = conflict;
            Test("existing target HTTP " + code + " updates without changing the Unity version", async () =>
            {
                var h = WithCatalog(OneXcode).Add("GET", Os("xcode26_5_0"), 200, Mac);
                Create(h, code, "{\"error\":\"Build target name already in use for this project!\"}");
                h.Add("PUT", Project + "/buildtargets/macfree-ios", 200, "{}");
                await Run(h);
                Equal(h.Calls[h.Calls.Count - 2].Body, h.Calls[h.Calls.Count - 1].Body);
                h.Complete();
            });
        }
        Test("OS rejection during existing-target update also falls back", async () =>
        {
            var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 200, Mac);
            Create(h, 409, "{}");
            h.Add("PUT", Project + "/buildtargets/macfree-ios", 500, Mismatch)
                .Add("GET", Os("xcode26_4_1"), 200, Mac);
            Create(h, 409, "{}");
            h.Add("PUT", Project + "/buildtargets/macfree-ios", 200, "{}");
            Equal("xcode26_4_1", await Run(h));
            h.Complete();
        });
        Test("malformed Xcode catalog fails clearly", async () =>
        {
            var h = WithCatalog("{}");
            var e = await Throws<InvalidOperationException>(() => Run(h));
            Check(e.Message.Contains("invalid Xcode catalog"), "Wrong error");
            h.Complete();
        });
        Test("malformed OS catalog fails clearly", async () =>
        {
            var h = WithCatalog().Add("GET", Os("xcode26_5_0"), 200, "{}");
            var e = await Throws<InvalidOperationException>(() => Run(h));
            Check(e.Message.Contains("invalid operating-system"), "Wrong error");
            h.Complete();
        });
        Test("empty catalog does not fall back to Xcode 16 or latest", async () =>
        {
            var h = WithCatalog("[]");
            var e = await Throws<InvalidOperationException>(() => Run(h));
            Check(e.Message.Contains("none available"), "Missing empty catalog diagnostic");
            h.Complete();
        });
        Test("underscore Unity token is not altered", async () =>
        {
            var h = WithCatalog(OneXcode).Add("GET", Os("xcode26_5_0"), 200, Mac);
            Create(h);
            await Run(h, "6000_6_2f1");
            h.Complete();
        });
        Test("missing Unity version makes no API calls", async () =>
        {
            var h = new FakeHttp();
            await Throws<ArgumentException>(() => Run(h, null));
            Equal(0, h.Calls.Count);
        });
        Test("error translator explains provisioning without recommending signing reset", () =>
        {
            string text = ErrorTranslator.Translate(new MacFreeApiException("Create build target", 500, Mismatch));
            Check(text.Contains("not a game compilation error"), "Missing useful explanation");
            Check(text.Contains("Do not reset Apple signing"), "Missing signing preservation");
            return Task.CompletedTask;
        });
        Test("setup resumes failed target without repeating Apple signing", async () =>
        {
            var h = WithCatalog(OneXcode).Add("GET", Os("xcode26_5_0"), 200, Mac);
            Create(h);
            var s = ResumableSettings();
            await Pipeline(h, s);
            Check(s.StepDone("target"), "Target not marked complete after success");
            Check(s.StepDone("cert") && s.StepDone("p12+upload"), "Signing state lost");
            h.Complete();
        });
        Test("failed compatibility leaves target incomplete and signing intact", async () =>
        {
            var h = WithCatalog(OneXcode).Add("GET", Os("xcode26_5_0"), 200, "[]");
            var s = ResumableSettings();
            await Throws<InvalidOperationException>(() => Pipeline(h, s));
            Check(!s.StepDone("target"), "Failed target marked complete");
            Check(s.StepDone("cert") && s.StepDone("p12+upload"), "Signing state lost");
            h.Complete();
        });
        Console.WriteLine(passed + " passed; " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
