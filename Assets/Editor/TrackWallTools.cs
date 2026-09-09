using UnityEngine;
using UnityEditor;

public static class TrackWallTools {

	const string MaterialPath = "Assets/Physics/TrackWall.physicMaterial";

	[MenuItem("Tools/Rolling Skies/Apply TrackWall Material to Selected", false, 100)]
	public static void ApplyMaterialToSelected () {
		PhysicsMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(MaterialPath);
		if (mat == null) {
			Debug.LogError($"[TrackWallTools] Could not find TrackWall material at {MaterialPath}");
			return;
		}

		GameObject[] selected = Selection.gameObjects;
		if (selected == null || selected.Length == 0) {
			EditorUtility.DisplayDialog("Track Wall", "Please select one or more GameObjects with Colliders in the Hierarchy or Scene view.", "OK");
			return;
		}

		int count = 0;
		foreach (GameObject go in selected) {
			Undo.RecordObject(go, "Apply TrackWall Material");
			Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
			foreach (Collider col in colliders) {
				if (col != null && !col.isTrigger) {
					Undo.RecordObject(col, "Apply TrackWall Material");
					col.sharedMaterial = mat;
					EditorUtility.SetDirty(col);
					count++;
				}
			}

			// Add TrackWall component if not present
			if (!go.GetComponent<TrackWall>()) {
				Undo.AddComponent<TrackWall>(go);
			}

			// Tag as Wall if untagged
			if (go.CompareTag("Untagged")) {
				go.tag = "Wall";
				EditorUtility.SetDirty(go);
			}
		}

		Debug.Log($"[TrackWallTools] Applied TrackWall physics material to {count} collider(s) across {selected.Length} object(s).");
	}

	[MenuItem("Tools/Rolling Skies/Apply TrackWall Material to Selected", true)]
	public static bool ValidateApplyMaterialToSelected () {
		return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
	}
}
