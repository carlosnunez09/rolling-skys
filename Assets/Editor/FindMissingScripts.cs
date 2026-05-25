using UnityEngine;
using UnityEditor;

public static class FindMissingScripts {

    [MenuItem("Tools/Find Missing Scripts")]
    static void Find () {
        int found = 0;

        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) {
            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++) {
                if (components[i] == null) {
                    Debug.LogError(
                        $"Missing script on: <b>{go.name}</b>  " +
                        $"(scene: {go.scene.name})  " +
                        $"component slot #{i}",
                        go);
                    found++;
                }
            }
        }

        if (found == 0)
            Debug.Log("No missing scripts found.");
        else
            Debug.LogWarning($"Found {found} missing script(s). Click the errors above to ping the GameObject.");
    }
}
