using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;

/// <summary>
/// Attach to any planet GameObject that also has a GravitySource component.
/// Gives the planet a name, owns its race tracks, and self-registers with
/// PlanetRegistry so the travel and minimap systems can find all planets.
/// </summary>
[RequireComponent(typeof(GravitySource))]
public class Planet : MonoBehaviour {

    [BoxGroup("Identity"), SerializeField]
    string _planetName = "Planet";

    [BoxGroup("Minimap"), SerializeField, Min(1f), Tooltip("How far above the car's equatorial plane waypoint dots stay fully visible before fading out. Set to ~20-30% of this planet's radius.")]
    float _dotFadeDistance = 50f;

    [BoxGroup("Tracks"), SerializeField]
    List<WaypointPath> _tracks = new List<WaypointPath>();

    public string                      PlanetName      => _planetName;
    public float                       DotFadeDistance => _dotFadeDistance;
    public IReadOnlyList<WaypointPath> Tracks          => _tracks;
    public GravitySource            GravitySource { get; private set; }

    void Awake () {
        GravitySource = GetComponent<GravitySource>();
        PlanetRegistry.Register(this);
    }

    void OnDestroy () => PlanetRegistry.Unregister(this);

    /// World-space surface-normal direction from this planet toward a position.
    public Vector3 SurfaceNormal (Vector3 worldPos) =>
        (worldPos - transform.position).normalized;
}

/// <summary>
/// Lightweight static registry of all Planet instances in the scene.
/// Mirrors the pattern used by CustomGravity so systems can query planets
/// without scene references.
/// </summary>
public static class PlanetRegistry {

    static List<Planet> _planets = new List<Planet>();

    // Clear stale references when entering Play Mode with Domain Reload disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset () => _planets = new List<Planet>();

    public static void Register   (Planet p) { if (!_planets.Contains(p)) _planets.Add(p); }
    public static void Unregister (Planet p) => _planets.Remove(p);

    public static IReadOnlyList<Planet> All => _planets;

    /// Returns the planet whose gravity is strongest at the given world position.
    public static Planet GetDominant (Vector3 position) {
        Planet best   = null;
        float  maxMag = 0f;
        for (int i = 0; i < _planets.Count; i++) {
            if (_planets[i] == null) continue;
            float mag = _planets[i].GravitySource.GetGravity(position).magnitude;
            if (mag > maxMag) { maxMag = mag; best = _planets[i]; }
        }
        return best;
    }
}
