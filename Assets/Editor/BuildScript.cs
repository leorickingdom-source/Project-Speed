using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Headless player builds, so a build is a command rather than a sequence of clicks in a dialog
// nobody remembers the settings of.
//
//   Unity.exe -quit -batchmode -nographics -projectPath <project>
//             -executeMethod BuildScript.BuildWindows -logFile build.log
//
// Output goes to Builds/Playtest-<bundleVersion>/, which is the naming the hand-made builds
// already used — the version comes from PlayerSettings, so bumping it in the Inspector is the
// only thing that decides where a build lands, and two builds of different versions can never
// overwrite each other.
public static class BuildScript
{
    const string OutputRoot = "Builds";

    [MenuItem("Build/Windows Playtest")]
    public static void BuildWindows() => Run(BuildTarget.StandaloneWindows64, ".exe");

    static void Run(BuildTarget target, string extension)
    {
        // Whatever is ticked in Build Settings, in that order. Reading the list rather than
        // hardcoding paths means a scene added to the game cannot be forgotten here.
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[Build] No enabled scenes in Build Settings — nothing to build.");
            Finish(false);
            return;
        }

        string dir = Path.Combine(OutputRoot, $"Playtest-{PlayerSettings.bundleVersion}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, PlayerSettings.productName + extension);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = target,
            targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            options = BuildOptions.None,
        };

        Debug.Log($"[Build] {target} -> {path}\n[Build] scenes: {string.Join(", ", scenes)}");

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        bool ok = summary.result == BuildResult.Succeeded;

        Debug.Log($"[Build] {summary.result} in {summary.totalTime.TotalSeconds:0}s, " +
                  $"{summary.totalSize / (1024 * 1024)} MB, " +
                  $"{summary.totalErrors} errors, {summary.totalWarnings} warnings");

        Finish(ok);
    }

    // Batch runs must set the process exit code themselves — BuildPlayer failing does not fail
    // the Unity process, so without this a broken build reports success to whatever called it.
    // Interactive menu runs must NOT exit: that would close the Editor out from under you.
    static void Finish(bool ok)
    {
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
