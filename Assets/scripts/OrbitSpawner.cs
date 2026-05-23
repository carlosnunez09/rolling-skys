using System.Collections.Generic;
using UnityEngine;

public class OrbitSpawner : MonoBehaviour
{
    public enum SpawnMode
    {
        Ring,   // equatorial belt / asteroid ring
        Cover   // blanket the entire sphere surface (clouds, atmosphere)
    }

    // ── Target ────────────────────────────────────────────────────────────────
    [Header("Orbit Target")]
    [Tooltip("The planet or object to orbit around. Defaults to this transform if null.")]
    [SerializeField] private Transform orbitCenter;

    // ── Prefabs ───────────────────────────────────────────────────────────────
    [Header("Spawned Objects")]
    [Tooltip("One or more prefabs to randomly pick from when spawning.")]
    [SerializeField] private GameObject[] prefabs;

    [Tooltip("Total number of objects to spawn.")]
    [SerializeField, Min(1)] private int count = 40;

    // ── Mode ──────────────────────────────────────────────────────────────────
    [Header("Spawn Mode")]
    [SerializeField] private SpawnMode mode = SpawnMode.Ring;

    // ── Ring Shape ────────────────────────────────────────────────────────────
    [Header("Ring Shape  (Ring mode only)")]
    [SerializeField, Min(0f)] private float minRadius = 20f;
    [SerializeField, Min(0f)] private float maxRadius = 30f;
    [SerializeField, Min(0f)] private float verticalScatter = 2f;
    [SerializeField] private Vector3 orbitPlaneEuler = Vector3.zero;

    // ── Cover Shape ───────────────────────────────────────────────────────────
    [Header("Cover Shape  (Cover mode only)")]
    [Tooltip("Radius of the sphere shell to place objects on (planet radius + altitude).")]
    [SerializeField, Min(0f)] private float coverRadius = 25f;

    [Tooltip("Random radial jitter so clouds don't sit on a perfect shell.")]
    [SerializeField, Min(0f)] private float coverScatter = 2f;

    [Tooltip("Fibonacci sphere gives even spacing. Disable for clumpy random placement.")]
    [SerializeField] private bool evenDistribution = true;

    [Tooltip("Orient objects so their forward axis points away from the planet.")]
    [SerializeField] private bool faceOutward = false;

    // ── Object Scale ──────────────────────────────────────────────────────────
    [Header("Object Scale")]
    [SerializeField] private float minScale = 0.5f;
    [SerializeField] private float maxScale = 1.5f;

    // ── Rotation ──────────────────────────────────────────────────────────────
    [Header("Orbit Rotation")]
    [Tooltip("Degrees per second the layer rotates around the planet.")]
    [SerializeField] private float orbitSpeed = 5f;

    [Tooltip("If true, each object also spins on its own local axis.")]
    [SerializeField] private bool selfRotate = true;

    [Tooltip("Maximum self-rotation speed in degrees per second (randomly assigned).")]
    [SerializeField] private float maxSelfRotateSpeed = 60f;

    // ── Scatter ───────────────────────────────────────────────────────────────
    [Header("Scatter")]
    [Tooltip("Randomises each ring object's angular position. Disable for even ring spacing.")]
    [SerializeField] private bool randomiseAngles = true;

    [Tooltip("Random seed for reproducible layouts.")]
    [SerializeField] private int seed = 42;

    // ── Gizmos ────────────────────────────────────────────────────────────────
    [Header("Gizmos")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private Color gizmoColor = new Color(0.4f, 0.8f, 1f, 0.4f);

    // ── Runtime state ─────────────────────────────────────────────────────────
    private readonly List<Transform> spawnedObjects = new();
    private readonly List<float> selfRotateSpeeds = new();
    private readonly List<Vector3> selfRotateAxes = new();
    private Quaternion orbitPlaneRotation;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (orbitCenter == null) orbitCenter = transform;
        orbitPlaneRotation = Quaternion.Euler(orbitPlaneEuler);
        Spawn();
    }

    private void Update()
    {
        if (orbitCenter == null) return;

        // Cover mode rotates around the planet's own up axis (like an atmosphere layer).
        // Ring mode rotates around the tilted orbital plane axis.
        Vector3 rotAxis = mode == SpawnMode.Cover
            ? orbitCenter.up
            : orbitCenter.rotation * orbitPlaneRotation * Vector3.up;

        float deltaAngle = orbitSpeed * Time.deltaTime;

        for (int i = 0; i < spawnedObjects.Count; i++)
        {
            Transform t = spawnedObjects[i];
            if (t == null) continue;

            t.position = RotateAround(t.position, orbitCenter.position, rotAxis, deltaAngle);

            if (selfRotate)
                t.Rotate(selfRotateAxes[i], selfRotateSpeeds[i] * Time.deltaTime, Space.Self);
        }
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    private void Spawn()
    {
        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogWarning($"[OrbitSpawner] No prefabs assigned on '{name}'.");
            return;
        }

        ClearSpawned();
        Random.InitState(seed);

        if (mode == SpawnMode.Ring)
            SpawnRing();
        else
            SpawnCover();
    }

    private void SpawnRing()
    {
        Quaternion planeRot = orbitCenter.rotation * Quaternion.Euler(orbitPlaneEuler);

        for (int i = 0; i < count; i++)
        {
            float angle = randomiseAngles
                ? Random.Range(0f, 360f)
                : (360f / count) * i;

            float radius = Random.Range(minRadius, maxRadius);
            float height = Random.Range(-verticalScatter, verticalScatter);

            Vector3 localPos = new Vector3(
                Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                height,
                Mathf.Sin(angle * Mathf.Deg2Rad) * radius
            );

            SpawnAt(orbitCenter.position + planeRot * localPos, Random.rotation);
        }
    }

    private void SpawnCover()
    {
        for (int i = 0; i < count; i++)
        {
            // Direction on the unit sphere
            Vector3 dir = evenDistribution
                ? FibonacciSphere(i, count)
                : Random.onUnitSphere;

            // Apply planet orientation so "up" aligns with planet poles
            Vector3 worldDir = orbitCenter.rotation * dir;

            float r = coverRadius + Random.Range(-coverScatter, coverScatter);
            Vector3 worldPos = orbitCenter.position + worldDir * r;

            Quaternion rot = faceOutward
                ? Quaternion.LookRotation(worldDir)
                : Random.rotation;

            SpawnAt(worldPos, rot);
        }
    }

    private void SpawnAt(Vector3 worldPos, Quaternion rot)
    {
        GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
        GameObject obj    = Instantiate(prefab, worldPos, rot, transform);
        obj.transform.localScale = Vector3.one * Random.Range(minScale, maxScale);

        spawnedObjects.Add(obj.transform);
        selfRotateSpeeds.Add(Random.Range(0f, maxSelfRotateSpeed));
        selfRotateAxes.Add(Random.onUnitSphere);
    }

    private void ClearSpawned()
    {
        foreach (Transform t in spawnedObjects)
            if (t != null) Destroy(t.gameObject);

        spawnedObjects.Clear();
        selfRotateSpeeds.Clear();
        selfRotateAxes.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Distributes n points evenly across a unit sphere using the golden-ratio spiral.
    private static Vector3 FibonacciSphere(int i, int n)
    {
        const float goldenRatio = 1.6180339887f;
        float theta = Mathf.Acos(1f - 2f * (i + 0.5f) / n);
        float phi   = 2f * Mathf.PI * i / goldenRatio;
        return new Vector3(
            Mathf.Sin(theta) * Mathf.Cos(phi),
            Mathf.Cos(theta),
            Mathf.Sin(theta) * Mathf.Sin(phi)
        );
    }

    private static Vector3 RotateAround(Vector3 point, Vector3 pivot, Vector3 axis, float angleDeg)
        => Quaternion.AngleAxis(angleDeg, axis) * (point - pivot) + pivot;

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        Transform center = orbitCenter != null ? orbitCenter : transform;
        Gizmos.color = gizmoColor;

        if (mode == SpawnMode.Ring)
        {
            Quaternion planeRot = center.rotation * Quaternion.Euler(orbitPlaneEuler);
            DrawCircle(center.position, planeRot, minRadius);
            DrawCircle(center.position, planeRot, maxRadius);
        }
        else
        {
            Gizmos.DrawWireSphere(center.position, coverRadius);
            if (coverScatter > 0f)
                Gizmos.DrawWireSphere(center.position, coverRadius + coverScatter);
        }
    }

    private static void DrawCircle(Vector3 center, Quaternion rotation, float radius, int segments = 64)
    {
        float step = 360f / segments;
        Vector3 prev = center + rotation * new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float rad  = step * i * Mathf.Deg2Rad;
            Vector3 next = center + rotation * new Vector3(
                Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (orbitCenter == null) orbitCenter = transform;
        orbitPlaneRotation = Quaternion.Euler(orbitPlaneEuler);
        Spawn();
    }
#endif
}
