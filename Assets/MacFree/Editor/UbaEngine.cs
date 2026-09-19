using System;
using System.Threading.Tasks;

namespace MacFree.Editor
{
    /// Adapter: the existing UBA setup + build flow behind IBuildEngine. No
    /// behavior change — forwards verbatim to SetupPipeline and UbaApi.
    public class UbaEngine : IBuildEngine
    {
        readonly MacFreeSettings s;
        readonly AscApi asc;
        readonly UbaApi uba;
        readonly string settingsRoot;
        readonly GitHubApi github;

        public UbaEngine(MacFreeSettings settings, AscApi ascApi, UbaApi ubaApi,
            string settingsRoot, GitHubApi githubApi)
        {
            s = settings; asc = ascApi; uba = ubaApi;
            this.settingsRoot = settingsRoot; github = githubApi;
        }

        public string Name => "Unity Build Automation";

        public Task RunSetup(string bundleId, string productName, string unityVersion,
            string branch, string subdirectory, string projectRoot, IProgress<string> log)
        {
            return new SetupPipeline(s, asc, uba, settingsRoot, github)
                .RunAll(bundleId, productName, unityVersion, branch, subdirectory, projectRoot, log);
        }

        public async Task<long> StartBuild() => await uba.StartBuild(s.BuildTargetId);

        public async Task<(string status, string commit)> GetBuildStatus(long id)
        {
            var (st, commit) = await uba.GetBuildStatus(s.BuildTargetId, (int)id);
            return (Normalize(st), commit);
        }

        public Task<string> GetBuildLog(long id, int tailLines)
            => uba.GetLogTail(s.BuildTargetId, (int)id, tailLines);

        /// Map UBA's build statuses onto the normalized set.
        public static string Normalize(string ubaStatus)
        {
            switch (ubaStatus)
            {
                case "success": return "success";
                case "failure": return "failure";
                case "canceled": return "canceled";
                case "queued":
                case "created":
                case "sentToBuilder": return "queued";
                default: return "running"; // started, restarted, ...
            }
        }
    }
}
