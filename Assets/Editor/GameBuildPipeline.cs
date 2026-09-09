using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build automation pipeline for Rolling Skys.
/// Generates standalone builds for:
/// - Windows 64-bit Client (RollingSkys.exe)
/// - Linux 64-bit Client for Steam Deck / SteamOS (RollingSkys.x86_64)
/// - Linux 64-bit Dedicated Headless Server (DockerServer/ServerBuild)
///
/// Automatically bundles steam_appid.txt so Steamworks overlay and networking initialize properly.
/// </summary>
public static class GameBuildPipeline {

    public const string WindowsClientPath = "Builds/Windows/RollingSkys.exe";
    public const string LinuxClientPath = "Builds/Linux/RollingSkys.x86_64";
    public const string LinuxServerPath = "Builds/DockerServer/ServerBuild";
    public const string SteamAppIdFileName = "steam_appid.txt";
    public const string DefaultAppId = "480"; // Spacewar dev test ID

    [MenuItem("Build/Windows Client (x64)", priority = 10)]
    public static void BuildWindowsClient () {
        BuildClient(
            target: BuildTarget.StandaloneWindows64,
            outputPath: WindowsClientPath,
            platformName: "Windows (x64)"
        );
    }

    [MenuItem("Build/Linux Client (Steam Deck & SteamOS)", priority = 11)]
    public static void BuildLinuxClient () {
        BuildClient(
            target: BuildTarget.StandaloneLinux64,
            outputPath: LinuxClientPath,
            platformName: "Linux (Steam Deck / SteamOS)"
        );
    }

    [MenuItem("Build/Build All Clients (Windows & Linux)", priority = 20)]
    public static void BuildAllClients () {
        Debug.Log("[GameBuildPipeline] Starting dual-platform build for Windows and Linux (Steam Deck)...");
        BuildWindowsClient();
        BuildLinuxClient();
        Debug.Log("[GameBuildPipeline] All client builds completed successfully!");
    }

    [MenuItem("Build/Linux Dedicated Server", priority = 30)]
    public static void BuildLinuxServer () {
        Debug.Log("[GameBuildPipeline] Starting Linux dedicated server build...");
        ServerBuild.BuildLinuxServer();
    }

    static void BuildClient (BuildTarget target, string outputPath, string platformName) {
        DateTime startTime = DateTime.Now;
        Debug.Log($"[GameBuildPipeline] >>> Starting {platformName} Client Build...");

        string outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir)) {
            Directory.CreateDirectory(outputDir);
        }

        string[] scenes = GetEnabledScenes();
        if (scenes.Length == 0) {
            throw new InvalidOperationException("[GameBuildPipeline] No enabled scenes found in EditorBuildSettings!");
        }

        Debug.Log($"[GameBuildPipeline] Building {scenes.Length} scene(s): {string.Join(", ", scenes)}");

        var options = new BuildPlayerOptions {
            scenes = scenes,
            locationPathName = outputPath,
            target = target,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded) {
            throw new Exception($"[GameBuildPipeline] {platformName} build FAILED: {summary.result} ({summary.totalErrors} errors, {summary.totalWarnings} warnings).");
        }

        // Post-build setup: copy steam_appid.txt to output folder
        EnsureSteamAppIdFile(outputDir);

        // For Linux / Steam Deck, create a helper run script
        if (target == BuildTarget.StandaloneLinux64) {
            CreateLinuxLaunchScript(outputDir, Path.GetFileName(outputPath));
        }

        TimeSpan duration = DateTime.Now - startTime;
        long sizeMb = (long)(summary.totalSize / (1024 * 1024));
        Debug.Log($"[GameBuildPipeline] SUCCESS: {platformName} build completed in {duration.TotalSeconds:F1}s! Output: {outputPath} (~{sizeMb} MB)");
    }

    static string[] GetEnabledScenes () {
        return EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
    }

    static void EnsureSteamAppIdFile (string destinationDirectory) {
        if (string.IsNullOrEmpty(destinationDirectory)) return;

        string targetPath = Path.Combine(destinationDirectory, SteamAppIdFileName);
        string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), SteamAppIdFileName);

        try {
            string appIdContent = DefaultAppId;
            if (File.Exists(sourcePath)) {
                appIdContent = File.ReadAllText(sourcePath).Trim();
            }

            File.WriteAllText(targetPath, appIdContent);
            Debug.Log($"[GameBuildPipeline] Copied {SteamAppIdFileName} ({appIdContent}) to {destinationDirectory}");
        } catch (Exception ex) {
            Debug.LogWarning($"[GameBuildPipeline] Could not copy {SteamAppIdFileName} to {destinationDirectory}: {ex.Message}");
        }
    }

    static void CreateLinuxLaunchScript (string outputDir, string binaryName) {
        try {
            string scriptPath = Path.Combine(outputDir, "run_steamdeck.sh");
            string scriptContent =
                "#!/usr/bin/env bash\n" +
                "# Rolling Skys - Linux / Steam Deck Launch Script\n" +
                "SCRIPT_DIR=\"$(cd \"$(dirname \"${BASH_SOURCE[0]}\")\" && pwd)\"\n" +
                "cd \"$SCRIPT_DIR\"\n" +
                "chmod +x \"./" + binaryName + "\"\n" +
                "exec \"./" + binaryName + "\" \"$@\"\n";

            File.WriteAllText(scriptPath, scriptContent);
            Debug.Log($"[GameBuildPipeline] Generated Linux launch helper: {scriptPath}");
        } catch (Exception ex) {
            Debug.LogWarning($"[GameBuildPipeline] Could not write run_steamdeck.sh: {ex.Message}");
        }
    }
}
