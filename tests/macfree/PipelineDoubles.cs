// Test-only stand-ins for Unity persistence and Apple signing dependencies.
// The production SetupPipeline/UbaApi/ErrorTranslator are compiled unchanged.
// Any unintended signing, repository or hook operation fails the test.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnityEngine
{
    public static class Application { public static string dataPath = "/tmp/macfree-tests/Assets"; }
}

namespace MacFree.Editor
{
    public class MacFreeSettings
    {
        public string BuildTargetId = "", CredentialId = "", BundleIdResourceId = "";
        public string CertificateId = "", P12Password = "", AscKeyId = "", AscIssuerId = "";
        public string GitHubToken = "", GitHubRepo = "";
        readonly HashSet<string> steps = new HashSet<string>();
        public bool StepDone(string step) { return steps.Contains(step); }
        public void MarkStep(string step) { steps.Add(step); }
        public void Save(string root) { }
        public bool InvalidateIfBundleChanged(string bundle) { return false; }
        public string ResolveP8Pem() { throw new Exception("Unexpected key access"); }
    }

    public class AscApi
    {
        public Task<string> EnsureBundleId(string bundle, string product)
        { throw new Exception("Unexpected Apple bundle operation"); }
        public Task<(string, byte[])> CreateDistributionCertificate(byte[] csr)
        { throw new Exception("Unexpected Apple certificate operation"); }
        public Task<byte[]> CreateAppStoreProfile(string name, string bundle, string cert)
        { throw new Exception("Unexpected Apple profile operation"); }
    }

    public static class SigningFactory
    {
        public static void CreateCsr(string name, out byte[] csr, out byte[] key)
        { throw new Exception("Unexpected CSR creation"); }
        public static byte[] CreateP12(byte[] cer, byte[] key, string password)
        { throw new Exception("Unexpected P12 creation"); }
    }

    public class GitHubApi
    {
        public GitHubApi(string token) { }
        public static string SanitizeRepoName(string name) { return name; }
        public Task<(string, string)> EnsureRepo(string name, bool isPrivate)
        { throw new Exception("Unexpected repository creation"); }
    }

    public static class ProjectPatcher
    {
        public static void WriteHooks(string root) { throw new Exception("Unexpected hook write"); }
        public static void CommitHooks(string root) { throw new Exception("Unexpected commit"); }
        public static void InitCommitAndPush(string root, string url, string token)
        { throw new Exception("Unexpected push"); }
    }
}
