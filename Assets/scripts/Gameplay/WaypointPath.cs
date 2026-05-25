using UnityEngine;
using System.Collections.Generic;

public class WaypointPath : MonoBehaviour {

    [SerializeField] List<Waypoint> _waypoints       = new List<Waypoint>();
    [SerializeField] bool           _isLoop           = true;
    [SerializeField] Color          _pathColor        = Color.green;
    [SerializeField] Color          _gateColor        = new Color(0f, 1f, 0.5f);
    [Tooltip("Default overshoot radius applied to every new waypoint added to this path.")]
    [SerializeField] float          _defaultOvershoot = 0f;

    public int Count => _waypoints.Count;
    public bool IsLoop => _isLoop;
    public IReadOnlyList<Waypoint> Waypoints => _waypoints;

    // ── Query helpers ────────────────────────────────────────────────────────

    public Waypoint GetWaypoint(int index) {
        if (_waypoints.Count == 0) return null;
        return _waypoints[Mathf.Clamp(index, 0, _waypoints.Count - 1)];
    }

    public int GetNextIndex(int index) {
        int n = index + 1;
        return n >= _waypoints.Count ? (_isLoop ? 0 : _waypoints.Count - 1) : n;
    }

    public int GetPrevIndex(int index) {
        int p = index - 1;
        return p < 0 ? (_isLoop ? _waypoints.Count - 1 : 0) : p;
    }

    // Direction from waypoint[index] toward the next waypoint.
    public Vector3 GetWaypointForward(int index) {
        if (_waypoints.Count < 2) return Vector3.forward;
        int nextIdx = GetNextIndex(index);
        if (nextIdx == index) return Vector3.forward;
        Waypoint a = _waypoints[index];
        Waypoint b = _waypoints[nextIdx];
        if (!a || !b) return Vector3.forward;
        return (b.transform.position - a.transform.position).normalized;
    }

    public int GetClosestWaypointIndex(Vector3 position) {
        int best = 0;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < _waypoints.Count; i++) {
            if (!_waypoints[i]) continue;
            float sqr = (position - _waypoints[i].transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = i; }
        }
        return best;
    }

    // Returns true when position is within the gate of waypointIndex.
    public bool IsInsideGate(Vector3 position, int waypointIndex) {
        Waypoint wp = GetWaypoint(waypointIndex);
        if (!wp) return false;
        return Vector3.Distance(position, wp.transform.position) <= wp.DetectionRadius;
    }

    // ── Editing (used by the editor tool at runtime too) ─────────────────────

    public Waypoint AddWaypoint(Vector3 worldPosition, Vector3 upAxis) {
        GameObject go = new GameObject($"Waypoint_{_waypoints.Count:D2}");
        go.transform.SetParent(transform, true);
        go.transform.position = worldPosition;
        go.transform.up = upAxis.sqrMagnitude > 0.01f ? upAxis : Vector3.up;

        Waypoint wp = go.AddComponent<Waypoint>();
        wp.OvershootRadius = _defaultOvershoot;
        _waypoints.Add(wp);
        RenumberAll();
        return wp;
    }

    public void RemoveWaypoint(int index) {
        if (index < 0 || index >= _waypoints.Count) return;
        Waypoint wp = _waypoints[index];
        _waypoints.RemoveAt(index);
        if (wp && wp.gameObject) {
#if UNITY_EDITOR
            DestroyImmediate(wp.gameObject);
#else
            Destroy(wp.gameObject);
#endif
        }
        RenumberAll();
    }

    public void MoveWaypointUp(int index) {
        if (index <= 0 || index >= _waypoints.Count) return;
        (_waypoints[index - 1], _waypoints[index]) = (_waypoints[index], _waypoints[index - 1]);
        RenumberAll();
    }

    public void MoveWaypointDown(int index) {
        if (index < 0 || index >= _waypoints.Count - 1) return;
        (_waypoints[index], _waypoints[index + 1]) = (_waypoints[index + 1], _waypoints[index]);
        RenumberAll();
    }

    public void AlignAllToGravity() {
        foreach (var wp in _waypoints) {
            if (!wp) continue;
            Vector3 up = CustomGravity.GetUpAxis(wp.transform.position);
            if (up.sqrMagnitude > 0.01f)
                wp.transform.up = up;
        }
    }

    void RenumberAll() {
        for (int i = 0; i < _waypoints.Count; i++)
            if (_waypoints[i])
                _waypoints[i].gameObject.name = $"Waypoint_{i:D2}";
    }

    // ── Gizmos ───────────────────────────────────────────────────────────────

    void OnDrawGizmos() {
        if (_waypoints.Count < 1) return;

        for (int i = 0; i < _waypoints.Count; i++) {
            Waypoint cur = _waypoints[i];
            if (!cur) continue;

            int nextIdx = GetNextIndex(i);
            Waypoint nxt = _waypoints[nextIdx];

            // Line to next
            bool drawLine = nxt && (_isLoop || i < _waypoints.Count - 1) && nextIdx != i;
            if (drawLine) {
                Gizmos.color = _pathColor;
                Gizmos.DrawLine(cur.transform.position, nxt.transform.position);

                // Arrow at midpoint
                Vector3 mid = (cur.transform.position + nxt.transform.position) * 0.5f;
                Vector3 dir = (nxt.transform.position - cur.transform.position).normalized;
                DrawArrow(mid, dir, cur.transform.up, 1.5f);
            }

            // Gate rectangle
            Gizmos.color = _gateColor;
            DrawGate(cur, GetWaypointForward(i));
        }
    }

    void DrawArrow(Vector3 pos, Vector3 dir, Vector3 up, float size) {
        Vector3 right = Vector3.Cross(up, dir).normalized;
        Gizmos.color = Color.white;
        Gizmos.DrawLine(pos, pos + dir * size);
        Gizmos.DrawLine(pos + dir * size, pos + dir * size * 0.6f + right * size * 0.3f);
        Gizmos.DrawLine(pos + dir * size, pos + dir * size * 0.6f - right * size * 0.3f);
    }

    void DrawGate(Waypoint wp, Vector3 forward) {
        Vector3 pos = wp.transform.position;
        Vector3 up  = wp.transform.up;
        Vector3 right = Vector3.Cross(up, forward).normalized;
        if (right.sqrMagnitude < 0.01f) return;

        float hw = wp.Width * 0.5f;
        float h  = wp.Width * 0.6f;

        Vector3 bl = pos - right * hw;
        Vector3 br = pos + right * hw;
        Vector3 tl = bl + up * h;
        Vector3 tr = br + up * h;

        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(bl, tl);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tl, tr);
    }
}
