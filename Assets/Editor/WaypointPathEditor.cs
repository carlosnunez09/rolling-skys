using System;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(WaypointPath))]
public class WaypointPathEditor : Editor {

    WaypointPath _path;
    bool         _isPlacing;

    // ── Planet picker ─────────────────────────────────────────────────────────
    Planet   _targetPlanet;
    Planet[] _scenePlanets = Array.Empty<Planet>();
    string[] _planetNames  = Array.Empty<string>();
    int      _pickerIndex  = 0;

    static readonly Color _btnOnColor  = new Color(1f, 0.35f, 0.35f);
    static readonly Color _btnOffColor = new Color(0.35f, 1f, 0.45f);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void OnEnable () {
        _path = (WaypointPath)target;
        ScanForPlanets();
    }

    void OnDisable () {
        _isPlacing = false;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    public override void OnInspectorGUI () {
        DrawDefaultInspector();

        // ── Planet picker ─────────────────────────────────────────────────────
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Planet (for gravity alignment)", EditorStyles.boldLabel);

        // Drag-and-drop field
        EditorGUI.BeginChangeCheck();
        var dragged = (Planet)EditorGUILayout.ObjectField("Target Planet", _targetPlanet, typeof(Planet), true);
        if (EditorGUI.EndChangeCheck()) {
            _targetPlanet = dragged;
            SyncPickerIndex();
        }

        // Scan + dropdown row
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Scan Scene", GUILayout.Width(90))) {
            ScanForPlanets();
        }

        if (_scenePlanets.Length > 0) {
            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(_pickerIndex, _planetNames);
            if (EditorGUI.EndChangeCheck()) {
                _pickerIndex  = newIdx;
                _targetPlanet = _scenePlanets[newIdx];
            }
        } else {
            EditorGUILayout.LabelField("No Planet components found in scene.");
        }
        EditorGUILayout.EndHorizontal();

        if (_targetPlanet != null) {
            EditorGUILayout.HelpBox($"Aligning to: {_targetPlanet.PlanetName}", MessageType.None);
        } else {
            EditorGUILayout.HelpBox("No planet selected — alignment falls back to nearest gravity sphere.", MessageType.Warning);
        }

        // ── Placement tools ───────────────────────────────────────────────────
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
            EditorGUILayout.HelpBox("Click any surface in the Scene view to drop a waypoint.", MessageType.Info);

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Remove Last"))      RemoveLast();
        if (GUILayout.Button("Align to Gravity")) AlignAll();
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Clear All Waypoints")) {
            if (EditorUtility.DisplayDialog("Clear Waypoints", "Remove every waypoint?", "Remove All", "Cancel"))
                ClearAll();
        }

        // ── Waypoint list ─────────────────────────────────────────────────────
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

    void OnSceneGUI () {
        if (!_path) return;

        Event e = Event.current;

        if (_isPlacing) {
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) {
                _isPlacing = false;
                e.Use();
                Repaint();
                return;
            }

            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(id);

            // Overlay banner
            Handles.BeginGUI();
            var style = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.yellow;
            string planetLabel = _targetPlanet != null ? $"  Planet: {_targetPlanet.PlanetName}" : "  No planet selected";
            GUI.Box(new Rect(8, 8, 380, 26),
                $"Waypoint Placement  —  Click surface  |  ESC to stop  |{planetLabel}", style);
            Handles.EndGUI();

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

            Handles.Label(
                wp.transform.position + wp.transform.up * 3.5f,
                $" {i}",
                new GUIStyle {
                    normal    = { textColor = Color.yellow },
                    fontStyle = FontStyle.Bold,
                    fontSize  = 12
                }
            );

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

    // ── Planet helpers ────────────────────────────────────────────────────────

    void ScanForPlanets () {
#if UNITY_2023_1_OR_NEWER
        _scenePlanets = FindObjectsByType<Planet>(FindObjectsSortMode.None);
#else
        _scenePlanets = FindObjectsOfType<Planet>();
#endif
        _planetNames = new string[_scenePlanets.Length];
        for (int i = 0; i < _scenePlanets.Length; i++)
            _planetNames[i] = $"{_scenePlanets[i].PlanetName}  ({_scenePlanets[i].gameObject.name})";

        SyncPickerIndex();
    }

    void SyncPickerIndex () {
        _pickerIndex = 0;
        if (_targetPlanet == null) return;
        for (int i = 0; i < _scenePlanets.Length; i++) {
            if (_scenePlanets[i] == _targetPlanet) { _pickerIndex = i; return; }
        }
    }

    // ── Gravity helpers ───────────────────────────────────────────────────────

    Vector3 GetUpAxisAt (Vector3 position) {
        // Prefer the explicitly selected planet.
        if (_targetPlanet != null)
            return (_targetPlanet.SurfaceNormal(position));

        // Try live gravity system (works in Play mode).
        Vector3 up = CustomGravity.GetUpAxis(position);
        if (up.sqrMagnitude > 0.01f) return up;

        // Edit-mode fallback: nearest GravitySphere in scene.
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

    // ── Toolbar helpers ───────────────────────────────────────────────────────

    void RemoveLast () {
        if (_path.Count == 0) return;
        Undo.RecordObject(_path, "Remove Last Waypoint");
        _path.RemoveWaypoint(_path.Count - 1);
        EditorUtility.SetDirty(_path);
    }

    void AlignAll () {
        Undo.RecordObject(_path, "Align Waypoints to Gravity");
        if (_targetPlanet != null) {
            foreach (Waypoint wp in _path.Waypoints)
                if (wp) wp.transform.up = _targetPlanet.SurfaceNormal(wp.transform.position);
            EditorUtility.SetDirty(_path);
        } else {
            _path.AlignAllToGravity();
            EditorUtility.SetDirty(_path);
        }
    }

    void ClearAll () {
        Undo.RecordObject(_path, "Clear All Waypoints");
        while (_path.Count > 0)
            _path.RemoveWaypoint(0);
        EditorUtility.SetDirty(_path);
    }
}
