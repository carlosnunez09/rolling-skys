using UnityEngine;
using NaughtyAttributes;

/// <summary>
/// Drop on any GameObject with a Collider.
/// When MovingCar lands on or collides with it, the car's lateral grip,
/// braking, and coasting forces are all multiplied by Friction.
///
/// 1.0 = normal road, no change.
/// 0.05 = near-frictionless ice — the car will slide and barely brake.
/// </summary>
public class SurfaceFriction : MonoBehaviour {

	[SerializeField, Range(0f, 1f),
	 Tooltip("0 = frictionless ice  |  1 = normal grip\n" +
	         "Scales the car's lateral grip, braking, and coast drag.")]
	float friction = 0.08f;

	public float Friction => friction;

#if UNITY_EDITOR
	void OnDrawGizmosSelected () {
		// Colour-code the gizmo so you can see at a glance how slippery a surface is.
		// Blue = icy, green = grippy.
		Color c = Color.Lerp(new Color(0.2f, 0.5f, 1f, 0.6f),
		                     new Color(0.15f, 1f, 0.3f, 0.6f), friction);
		Gizmos.color = c;

		var col = GetComponent<Collider>();
		if (col != null) {
			Gizmos.matrix = transform.localToWorldMatrix;
			if (col is BoxCollider box) {
				Gizmos.DrawWireCube(box.center, box.size * 1.02f);
			} else if (col is SphereCollider sphere) {
				Gizmos.DrawWireSphere(sphere.center, sphere.radius * 1.02f);
			} else if (col is MeshCollider mesh && mesh.sharedMesh != null) {
				Gizmos.DrawWireMesh(mesh.sharedMesh);
			} else {
				Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
			}
			Gizmos.matrix = Matrix4x4.identity;
		}

		UnityEditor.Handles.Label(
			transform.position + Vector3.up * 0.5f,
			$"Friction  {friction:P0}",
			new GUIStyle(GUI.skin.label) {
				normal    = { textColor = c },
				fontStyle = FontStyle.Bold,
				fontSize  = 10
			});
	}
#endif
}
