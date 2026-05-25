using UnityEngine;

/// Attach to the checkpoint marker prefab.
/// Scales its X axis to match the parent Waypoint's width, and highlights
/// yellow when it is the player's next checkpoint.
[ExecuteAlways]
public class CheckpointMarker : MonoBehaviour {

    [ColorUsage(true, true)] [SerializeField] Color _defaultColor = Color.white;
    [ColorUsage(true, true)] [SerializeField] Color _nextColor    = Color.yellow;

    Waypoint              _waypoint;
    int                   _waypointIndex = -1;
    RaceRuntime           _race;
    Renderer[]            _renderers;
    MaterialPropertyBlock _block;
    bool                  _isNext;

    static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    void OnEnable () {
        _block     = new MaterialPropertyBlock();
        _renderers = GetComponentsInChildren<Renderer>();
        ApplyColor(_defaultColor);
    }

    // Called whenever a field is changed in the Inspector — updates the preview.
    void OnValidate () {
        if (_block == null) _block = new MaterialPropertyBlock();
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>();
        ApplyColor(_defaultColor);
    }

    void Start () {
        if (!Application.isPlaying) return;

        _waypoint = GetComponentInParent<Waypoint>();
        _race     = FindAnyObjectByType<RaceRuntime>();

        if (_waypoint != null) {
            Vector3 s = transform.localScale;
            s.x = _waypoint.Width;
            transform.localScale = s;
        }

        CacheWaypointIndex();
    }

    void Update () {
        if (!Application.isPlaying) return;
        if (_race == null || _waypointIndex < 0) return;

        bool shouldBeNext = _race.Phase == RaceRuntime.RacePhase.Racing
                         && _race.PlayerNextWaypointIndex == _waypointIndex;

        if (shouldBeNext == _isNext) return;
        _isNext = shouldBeNext;
        ApplyColor(_isNext ? _nextColor : _defaultColor);
    }

    void CacheWaypointIndex () {
        if (_race == null || _race.ActivePath == null) return;

        for (int i = 0; i < _race.ActivePath.Count; i++) {
            if (_race.ActivePath.GetWaypoint(i) == _waypoint) {
                _waypointIndex = i;
                return;
            }
        }
    }

    void ApplyColor (Color color) {
        if (_block == null || _renderers == null) return;
        _block.SetColor(EmissionColorID, color);
        foreach (Renderer r in _renderers)
            if (r) r.SetPropertyBlock(_block);
    }
}
