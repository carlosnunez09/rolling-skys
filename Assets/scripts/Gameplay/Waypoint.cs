using UnityEngine;

public class Waypoint : MonoBehaviour {

    [SerializeField] float _width = 8f;
    [SerializeField] Color _gizmoColor = new Color(1f, 0.85f, 0f);

    public float Width => _width;

    void OnDrawGizmos() {
        Gizmos.color = _gizmoColor;
        Gizmos.DrawWireSphere(transform.position, 0.4f);
        Gizmos.DrawLine(transform.position, transform.position + transform.up * 2.5f);
    }

    void OnDrawGizmosSelected() {
        Gizmos.color = Color.white;
        DrawRing(transform.position, transform.up, _width * 0.5f);
    }

    void DrawRing(Vector3 center, Vector3 normal, float radius) {
        Vector3 perp = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) < 0.99f
            ? Vector3.Cross(normal, Vector3.up).normalized
            : Vector3.Cross(normal, Vector3.forward).normalized;
        Vector3 cross = Vector3.Cross(normal, perp);

        int segs = 32;
        for (int i = 0; i < segs; i++) {
            float a0 = i / (float)segs * 2f * Mathf.PI;
            float a1 = (i + 1) / (float)segs * 2f * Mathf.PI;
            Vector3 p0 = center + (Mathf.Cos(a0) * perp + Mathf.Sin(a0) * cross) * radius;
            Vector3 p1 = center + (Mathf.Cos(a1) * perp + Mathf.Sin(a1) * cross) * radius;
            Gizmos.DrawLine(p0, p1);
        }
    }
}
