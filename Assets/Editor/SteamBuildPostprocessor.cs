using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Automatically bundles steam_appid.txt and platform launch scripts into build output folders.
/// Works for all build triggers: Unity Editor File > Build, GameBuildPipeline, and GameCI in GitHub Actions.
/// </summary>
public class SteamBuildPostprocessor : IPostprocessBuildWithReport {

    public int callbackOrder => 0;

    public void OnPostprocessBuild (BuildReport report) {
        string outputPath = report.summary.outputPath;
        string outputDir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir)) {
            outputDir = outputPath;
        }

        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir)) {
            return;
        }

        // Copy steam_appid.txt so Steamworks initializes in standalone builds
        EnsureSteamAppId(outputDir);

        // If building Linux Standalone Player (Steam Deck / SteamOS), generate launcher script
        if (report.summary.platform == BuildTarget.StandaloneLinux64 &&
            EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Server) {
            string binaryName = Path.GetFileName(outputPath);
            EnsureLinuxLaunchScript(outputDir, binaryName);
        }
    }

    static void EnsureSteamAppId (string outputDir) {
        string targetPath = Path.Combine(outputDir, "steam_appid.txt");
        string sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "steam_appid.txt");

        try {
            string appId = "480";
            if (File.Exists(sourcePath)) {
                appId = File.ReadAllText(sourcePath).Trim();
            }

            File.WriteAllText(targetPath, appId);
            Debug.Log($"[SteamBuildPostprocessor] Bundled steam_appid.txt ({appId}) at: {targetPath}");
        } catch (Exception ex) {
            Debug.LogWarning($"[SteamBuildPostprocessor] Could not write steam_appid.txt: {ex.Message}");
        }
    }

    static void EnsureLinuxLaunchScript (string outputDir, string binaryName) {
        try {
            string scriptPath = Path.Combine(outputDir, "run_steamdeck.sh");
            string scriptContent =
                "#!/usr/bin/env bash\n" +
                "# Rolling Skys - Linux / Steam Deck Launch Script\n" +
                "SCRIPT_DIR=\"$(cd \"$(dirname \"${BASH_SOURCE[0]}\")\" && pwd)\"\n" +
                "cd \"$SCRIPT_DIR\"\n\n" +
                "EXE=\"./" + binaryName + "\"\n" +
                "if [ ! -f \"$EXE\" ]; then\n" +
                "    EXE=$(find . -maxdepth 1 -name \"*.x86_64\" -print -quit)\n" +
                "fi\n\n" +
                "if [ -n \"$EXE\" ] && [ -f \"$EXE\" ]; then\n" +
                "    chmod +x \"$EXE\"\n" +
                "    exec \"$EXE\" \"$@\"\n" +
                "else\n" +
                "    echo \"[Error] Could not find Linux executable in $SCRIPT_DIR\"\n" +
                "    exit 1\n" +
                "fi\n";

            File.WriteAllText(scriptPath, scriptContent);
            Debug.Log($"[SteamBuildPostprocessor] Generated run_steamdeck.sh at: {scriptPath}");
        } catch (Exception ex) {
            Debug.LogWarning($"[SteamBuildPostprocessor] Could not write run_steamdeck.sh: {ex.Message}");
        }
    }
}
