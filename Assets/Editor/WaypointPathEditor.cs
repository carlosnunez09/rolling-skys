using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(WaypointPath))]
public class WaypointPathEditor : Editor {

    WaypointPath _path;
    bool _isPlacing;

    static readonly Color _btnOnColor  = new Color(1f, 0.35f, 0.35f);
    static readonly Color _btnOffColor = new Color(0.35f, 1f, 0.45f);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void OnEnable() {
        _path = (WaypointPath)target;
    }

    void OnDisable() {
        _isPlacing = false;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Placement Tools", EditorStyles.boldLabel);

        GUI.backgroundColor = _isPlacing ? _btnOnColor : _btnOffColor;
        string btnText = _isPlacing
            ? "■  Stop Placing  (or press ESC in Scene view)"
            : "◆  Place Waypoints in Scene";
        if (GUILayout.Button(btnText, GUILayout.Height(32))) {
            _isPlacing = !_isPlacing;
            SceneView.RepaintAll();
        }
        GUI.backgroundColor = Color.white;

        if (_isPlacing)
            EditorGUILayout.HelpBox("Click any surface in the Scene view to drop a waypoint. Waypoints auto-align to the planet's gravity.", MessageType.Info);

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Remove Last"))      RemoveLast();
        if (GUILayout.Button("Align to Gravity")) AlignAll();
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Clear All Waypoints")) {
            if (EditorUtility.DisplayDialog("Clear Waypoints", "Remove every waypoint?", "Remove All", "Cancel"))
                ClearAll();
        }

        // Waypoint list
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField($"Waypoints  ({_path.Count})", EditorStyles.boldLabel);

        for (int i = 0; i < _path.Count; i++) {
            Waypoint wp = _path.Waypoints[i];
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField($"[{i:D2}]  {(wp ? wp.gameObject.name : "null")}", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("↑", GUILayout.Width(24)) && i > 0) {
                Undo.RecordObject(_path, "Reorder Waypoints");
                _path.MoveWaypointUp(i);
                EditorUtility.SetDirty(_path);
            }
            if (GUILayout.Button("↓", GUILayout.Width(24)) && i < _path.Count - 1) {
                Undo.RecordObject(_path, "Reorder Waypoints");
                _path.MoveWaypointDown(i);
                EditorUtility.SetDirty(_path);
            }
            if (GUILayout.Button("⊙", GUILayout.Width(24)) && wp) {
                Selection.activeGameObject = wp.gameObject;
                SceneView.FrameLastActiveSceneView();
            }
            if (GUILayout.Button("✕", GUILayout.Width(24))) {
                Undo.RecordObject(_path, "Remove Waypoint");
                _path.RemoveWaypoint(i);
                EditorUtility.SetDirty(_path);
                GUIUtility.ExitGUI();
                return;
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    // ── Scene GUI ─────────────────────────────────────────────────────────────

    void OnSceneGUI() {
        if (!_path) return;

        Event e = Event.current;

        if (_isPlacing) {
            // ESC cancels placement
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) {
                _isPlacing = false;
                e.Use();
                Repaint();
                return;
            }

            // Grab scene input so clicks don't deselect the path object
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(id);

            // Overlay banner
            Handles.BeginGUI();
            var style = new GUIStyle(GUI.skin.box) {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = Color.yellow;
            float bw = 330, bh = 26;
            GUI.Box(new Rect(8, 8, bw, bh), "Waypoint Placement Mode  —  Click surface  |  ESC to stop", style);
            Handles.EndGUI();

            // Place on left click
            if (e.type == EventType.MouseDown && e.button == 0) {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit)) {
                    Undo.RecordObject(_path, "Add Waypoint");
                    Vector3 up = GetUpAxisAt(hit.point);
                    _path.AddWaypoint(hit.point + up * 0.05f, up);
                    EditorUtility.SetDirty(_path);
                }
                e.Use();
            }
        }

        // Position handles for every waypoint
        for (int i = 0; i < _path.Count; i++) {
            Waypoint wp = _path.Waypoints[i];
            if (!wp) continue;

            // Index label floating above the post
            Handles.Label(
                wp.transform.position + wp.transform.up * 3.5f,
                $" {i}",
                new GUIStyle {
                    normal    = { textColor = Color.yellow },
                    fontStyle = FontStyle.Bold,
                    fontSize  = 12
                }
            );

            // Drag handle
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(wp.transform.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(wp.transform, "Move Waypoint");
                wp.transform.position = newPos;
                wp.transform.up = GetUpAxisAt(newPos);
                EditorUtility.SetDirty(wp);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void RemoveLast() {
        if (_path.Count == 0) return;
        Undo.RecordObject(_path, "Remove Last Waypoint");
        _path.RemoveWaypoint(_path.Count - 1);
        EditorUtility.SetDirty(_path);
    }

    void AlignAll() {
        Undo.RecordObject(_path, "Align Waypoints to Gravity");
        _path.AlignAllToGravity();
        EditorUtility.SetDirty(_path);
    }

    void ClearAll() {
        Undo.RecordObject(_path, "Clear All Waypoints");
        while (_path.Count > 0)
            _path.RemoveWaypoint(0);
        EditorUtility.SetDirty(_path);
    }

    // Tries the live gravity system first; falls back to the nearest GravitySphere
    // in the scene (needed in edit mode before play, when sources aren't registered).
    static Vector3 GetUpAxisAt(Vector3 position) {
        Vector3 up = CustomGravity.GetUpAxis(position);
        if (up.sqrMagnitude > 0.01f) return up;

        GravitySphere[] spheres = FindObjectsOfType<GravitySphere>();
        GravitySphere nearest = null;
        float minDist = float.MaxValue;
        foreach (var s in spheres) {
            float d = (position - s.transform.position).magnitude;
            if (d < minDist) { minDist = d; nearest = s; }
        }

        return nearest
            ? (position - nearest.transform.position).normalized
            : Vector3.up;
    }
}
