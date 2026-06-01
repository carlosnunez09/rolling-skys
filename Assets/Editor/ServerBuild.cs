using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class ServerBuild {

    const string OutputPath = "Builds/DockerServer/ServerBuild";

    public static void BuildLinuxServer () {
        string outputDirectory = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes are configured in EditorBuildSettings.");

        var options = new BuildPlayerOptions {
            scenes = scenes,
            locationPathName = OutputPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
            throw new Exception($"Linux dedicated server build failed: {summary.result} ({summary.totalErrors} errors, {summary.totalWarnings} warnings).");

        UnityEngine.Debug.Log($"Linux dedicated server build created at {OutputPath}. Size: {summary.totalSize} bytes.");
    }
}
