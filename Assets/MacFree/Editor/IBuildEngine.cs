using System;
using System.Threading.Tasks;

namespace MacFree.Editor
{
    /// The build back-end MacFree drives. Two implementations: UbaEngine
    /// (Unity Build Automation) and GitHubActionsEngine. Status strings are
    /// normalized to: queued | running | success | failure | canceled.
    public interface IBuildEngine
    {
        string Name { get; }
        Task RunSetup(string bundleId, string productName, string unityVersion,
            string branch, string subdirectory, string projectRoot, IProgress<string> log);
        Task<long> StartBuild();
        Task<(string status, string commit)> GetBuildStatus(long id);
        Task<string> GetBuildLog(long id, int tailLines);
    }
}
