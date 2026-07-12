using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor helpers for local server/client testing with Netcode + Multiplayer Roles / Play Mode.
/// </summary>
public static class NetworkPlayModeSetup {

    const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

    [MenuItem("Rolling Skys/Networking/Open Main Menu Scene", priority = 0)]
    public static void OpenMainMenuScene () {
        if (!EditorApplication.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MainMenuScenePath);
        else
            Debug.LogWarning("Cannot switch scenes while playing. Stop Play Mode first.");
    }

    [MenuItem("Rolling Skys/Networking/Select Network Manager", priority = 1)]
    public static void SelectNetworkManager () {
        GameNetworkManager manager = Object.FindFirstObjectByType<GameNetworkManager>();
        if (manager == null) {
            Debug.LogWarning("No GameNetworkManager in the open scene. Open Main Menu first.");
            return;
        }

        Selection.activeGameObject = manager.gameObject;
        EditorGUIUtility.PingObject(manager.gameObject);
    }

    [MenuItem("Rolling Skys/Networking/Log Local Setup Tips", priority = 50)]
    public static void LogSetupTips () {
        Debug.Log(
            "Rolling Skys local multiplayer setup:\n" +
            "1. Project Settings > Multiplayer > Enable Multiplayer Roles (already enabled).\n" +
            "2. Use the Multiplayer Role toolbar dropdown: Client And Server, Server, or Client.\n" +
            "3. Window > Multiplayer > Multiplayer Play Mode to add Virtual Players as extra clients.\n" +
            "4. Open Assets/Scenes/MainMenu.unity, press Play, then Host (listen server) or Server + Client.\n" +
            "5. Clients use Address field host:port (default 127.0.0.1:7777).\n" +
            "6. Dedicated Linux server builds auto-call StartServer via UNITY_SERVER.\n" +
            "See NETWORKING.md for full details."
        );
    }
}
