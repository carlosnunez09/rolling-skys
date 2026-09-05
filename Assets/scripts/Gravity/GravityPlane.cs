using UnityEngine;

public class GravityPlane : GravitySource {

	[SerializeField]
	float gravity = 9.81f;

	public override float GravityStrength => gravity;

	public override int Priority {
		get => base.Priority != 0 ? base.Priority : 1;
		set => base.Priority = value;
	}

	[SerializeField, Min(0f)]
	float range = 1f;

	public override Vector3 GetGravity (Vector3 position) {
		Vector3 up = transform.up;
		float distance = Vector3.Dot(up, position - transform.position);
		if (distance > range) {
			return Vector3.zero;
		}
		float g = -gravity;
		if (distance > 0f) {
			g *= 1f - distance / range;
		}
		return g * up;
	}

	void OnDrawGizmos () {
		Vector3 scale = transform.localScale;
		scale.y = range;
		Gizmos.matrix =
			Matrix4x4.TRS(transform.position, transform.rotation, scale);
		Vector3 size = new Vector3(1f, 0f, 1f);
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireCube(Vector3.zero, size);
		if (range > 0f) {
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireCube(Vector3.up, size);
		}
	}
}