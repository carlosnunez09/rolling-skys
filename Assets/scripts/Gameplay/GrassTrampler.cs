using UnityEngine;

/// Attach to a car or player to bend nearby grass and leave a trail that
/// persists for <see cref="recoveryDuration"/> seconds before springing back.
/// Stamps are written by distance traveled so coverage is consistent at any speed.
/// Slot selection always targets the most-expired stamp so no active mark is
/// overwritten before it has finished recovering.
[ExecuteAlways]
public class GrassTrampler : MonoBehaviour
{
    [Tooltip("World-space radius of the live trample effect (and each history stamp).")]
    public float radius = 3f;

    [Tooltip("How far tips bend at the centre of the trample zone.")]
    public float strength = 3f;

    [Tooltip("Seconds before a trampled spot fully recovers.")]
    public float recoveryDuration = 30f;

    [Tooltip("Minimum distance traveled (world units) before a new stamp is written. " +
             "Smaller = denser trail, more GPU work per vertex.")]
    public float stampDistance = 2f;

    // ── History buffer ─────────────────────────────────────────────────────────
    // Each element: xyz = world position, w = Time.time when stamped.
    // 64 stamps × 2 m each = 128 m of continuous trail.
    // Must match TRAMPLE_HIST in GrassToon.shader.
    const int HistSize = 64;
    readonly Vector4[] _history = new Vector4[HistSize];
    Vector3 _lastStampPos       = new Vector3(float.MaxValue, 0f, 0f);

    // ── Shader property IDs ────────────────────────────────────────────────────
    static readonly int _idPos      = Shader.PropertyToID("_TramplePos");
    static readonly int _idRadius   = Shader.PropertyToID("_TrampleRadius");
    static readonly int _idStrength = Shader.PropertyToID("_TrampleStrength");
    static readonly int _idHistory  = Shader.PropertyToID("_TrampleHistory");
    static readonly int _idTime     = Shader.PropertyToID("_TrampleGameTime");
    static readonly int _idRecovery = Shader.PropertyToID("_RecoveryDuration");

    void OnEnable()
    {
        // w = -1e9 → shader age = huge → fade = 0 → slot ignored.
        for (int i = 0; i < HistSize; i++)
            _history[i] = new Vector4(0f, 0f, 0f, -1e9f);

        _lastStampPos = new Vector3(float.MaxValue, 0f, 0f);
        PushAll();
    }

    void Update()
    {
        if (!Application.isPlaying)
        {
            PushAll();
            return;
        }

        Vector3 pos = transform.position;
        if (Vector3.Distance(pos, _lastStampPos) >= stampDistance)
        {
            int slot = FindMostExpiredSlot();
            _history[slot] = new Vector4(pos.x, pos.y, pos.z, Time.time);
            _lastStampPos  = pos;
        }

        PushAll();
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(_idStrength, 0f);
        Shader.SetGlobalFloat(_idRadius,   0f);
    }

    /// <summary>
    /// Returns the index of the slot safest to overwrite:
    /// an empty sentinel slot if one exists, otherwise the oldest timestamp
    /// (furthest into its recovery, closest to having fully faded).
    /// </summary>
    int FindMostExpiredSlot()
    {
        float oldestTime = float.MaxValue;
        int   oldestIdx  = 0;

        for (int i = 0; i < HistSize; i++)
        {
            // Sentinel slot — completely unused, take it immediately.
            if (_history[i].w < -1e8f) return i;

            if (_history[i].w < oldestTime)
            {
                oldestTime = _history[i].w;
                oldestIdx  = i;
            }
        }

        return oldestIdx;
    }

    void PushAll()
    {
        Vector3 pos = transform.position;
        float   now = Application.isPlaying ? Time.time : 0f;

        Shader.SetGlobalVector(_idPos,    new Vector4(pos.x, pos.y, pos.z, 0f));
        Shader.SetGlobalFloat(_idRadius,   radius);
        Shader.SetGlobalFloat(_idStrength, strength);
        Shader.SetGlobalVectorArray(_idHistory, _history);
        Shader.SetGlobalFloat(_idTime,     now);
        Shader.SetGlobalFloat(_idRecovery, recoveryDuration);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.2f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);

        // Yellow wire-spheres show active history stamps.
        float now = Application.isPlaying ? Time.time : 0f;
        for (int i = 0; i < HistSize; i++)
        {
            if (_history[i].w < -1e8f) continue;
            float age  = now - _history[i].w;
            float fade = 1f - Mathf.Clamp01(age / Mathf.Max(recoveryDuration, 0.01f));
            if (fade < 0.01f) continue;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, fade * 0.7f);
            Gizmos.DrawWireSphere(new Vector3(_history[i].x, _history[i].y, _history[i].z),
                                  radius * 0.35f);
        }
    }
}
