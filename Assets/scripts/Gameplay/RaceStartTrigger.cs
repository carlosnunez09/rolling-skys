using UnityEngine;

/// <summary>
/// Place on a GameObject with any Trigger Collider at the race start line.
/// When the player enters, it calls <see cref="RaceRuntime.LoadTrackAndStart"/>
/// with the configured path and settings.
///
/// The WaypointPath can span multiple planets — just lay waypoints wherever
/// the track goes across the world.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RaceStartTrigger : MonoBehaviour {

    [Header("Track")]
    [Tooltip("The waypoint path for this race. Can cross multiple planets.")]
    [SerializeField] WaypointPath _path;

    [Tooltip("Display name shown in the HUD.")]
    [SerializeField] string _trackName = "Unnamed Track";

    [Tooltip("Number of laps to complete.")]
    [SerializeField] int _totalLaps = 3;

    [Tooltip("Seconds for the countdown before the race begins.")]
    [SerializeField] float _countdownSeconds = 3f;

    [Header("References")]
    [SerializeField] RaceRuntime _raceRuntime;

    [Tooltip("Tag used to identify the player car's collider.")]
    [SerializeField] string _playerTag = "Player";

    void Awake () {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter (Collider other) {
        if (!other.CompareTag(_playerTag)) return;
        if (_raceRuntime == null || _path == null) return;
        _raceRuntime.LoadTrackAndStart(_path, _trackName, _totalLaps, _countdownSeconds);
    }

    // Editor gizmo — visualises the trigger zone without entering Play Mode.
    void OnDrawGizmos () {
        Gizmos.color  = new Color(0f, 1f, 0.3f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;

        if (TryGetComponent<BoxCollider>(out var box)) {
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0f, 1f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
        } else if (TryGetComponent<SphereCollider>(out var sphere)) {
            Gizmos.DrawSphere(sphere.center, sphere.radius);
            Gizmos.color = new Color(0f, 1f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }

        Gizmos.matrix = Matrix4x4.identity;

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 2f,
            $"START: {_trackName}\n{_totalLaps} lap(s)  |  {_countdownSeconds}s countdown");
#endif
    }
}
