using UnityEditor;

public static class BuildScript
{
    public static void BuildIOS()
    {
        BuildPipeline.BuildPlayer(
            new BuildPlayerOptions
            {
                scenes = new[]
                {
                    "Assets/_Scenes/MainMenu.unity"
                },
                locationPathName = "build/iOS",
                target = BuildTarget.iOS,
                options = BuildOptions.Development
            }
        );
    }
}
