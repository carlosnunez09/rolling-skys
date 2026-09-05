using UnityEngine;

public class GravitySphere : GravitySource {

	[SerializeField]
	float gravity = 9.81f;

	public override float GravityStrength => gravity;

	public override int Priority {
		get {
			if (base.Priority != 0) return base.Priority;
			return outerRadius <= 100f ? 1 : 0;
		}
		set => base.Priority = value;
	}

	[SerializeField, Min(0f)]
	float innerFalloffRadius = 1f, innerRadius = 5f;

	[SerializeField, Min(0f)]
	float outerRadius = 10f, outerFalloffRadius = 15f;

	float innerFalloffFactor, outerFalloffFactor;

	public override Vector3 GetGravity (Vector3 position) {
		Vector3 vector = transform.position - position;
		float distance = vector.magnitude;
		if (distance > outerFalloffRadius || distance < innerFalloffRadius) {
			return Vector3.zero;
		}
		float d = Mathf.Max(distance, 0.0001f);
		float g = gravity / d;
		if (distance > outerRadius) {
			g *= 1f - (distance - outerRadius) * outerFalloffFactor;
		}
		else if (distance < innerRadius) {
			g *= 1f - (innerRadius - distance) * innerFalloffFactor;
		}
		return g * vector;
	}

	void Awake () {
		OnValidate();
	}

	void OnValidate () {
		innerFalloffRadius = Mathf.Max(innerFalloffRadius, 0f);
		innerRadius = Mathf.Max(innerRadius, innerFalloffRadius);
		outerRadius = Mathf.Max(outerRadius, innerRadius);
		outerFalloffRadius = Mathf.Max(outerFalloffRadius, outerRadius);

		innerFalloffFactor = innerRadius > innerFalloffRadius
			? 1f / (innerRadius - innerFalloffRadius) : 0f;
		outerFalloffFactor = outerFalloffRadius > outerRadius
			? 1f / (outerFalloffRadius - outerRadius) : 0f;
	}

	void OnDrawGizmos () {
		Vector3 p = transform.position;
		if (innerFalloffRadius > 0f && innerFalloffRadius < innerRadius) {
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(p, innerFalloffRadius);
		}
		Gizmos.color = Color.yellow;
		if (innerRadius > 0f && innerRadius < outerRadius) {
			Gizmos.DrawWireSphere(p, innerRadius);
		}
		Gizmos.DrawWireSphere(p, outerRadius);
		if (outerFalloffRadius > outerRadius) {
			Gizmos.color = Color.cyan;
			Gizmos.DrawWireSphere(p, outerFalloffRadius);
		}
	}
}