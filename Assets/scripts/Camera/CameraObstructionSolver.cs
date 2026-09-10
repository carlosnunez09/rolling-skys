using UnityEngine;

/// <summary>Shared collision boom: contract immediately, recover smoothly, ignore the followed car.</summary>
public sealed class CameraObstructionSolver {
	readonly RaycastHit[] hits = new RaycastHit[64];
	float resolvedDistance = -1f;
	Transform cachedTarget;
	Rigidbody cachedBody;

	public bool IsObstructed { get; private set; }

	public void Reset () {
		resolvedDistance = -1f;
		IsObstructed = false;
	}

	public Vector3 Resolve (Camera camera, Transform target, Vector3 pivot, Vector3 direction,
		float distance, LayerMask mask, float recoverySpeed, float comfortDistance, bool cutaway) {
		distance = Mathf.Max(0.1f, distance);
		direction.Normalize();
		CacheTarget(target);
		// Enclose the near-plane corners, including their offset from the camera origin.
		float halfHeight = camera.nearClipPlane * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
		float radius = Mathf.Max(0.15f, new Vector3(halfHeight * camera.aspect, halfHeight, camera.nearClipPlane).magnitude) + 0.05f;
		int count = Physics.SphereCastNonAlloc(pivot, radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
		RaycastHit[] results = hits;
		if (count == hits.Length) {
			results = Physics.SphereCastAll(pivot, radius, direction, distance, mask, QueryTriggerInteraction.Ignore);
			count = results.Length;
		}
		float allowed = distance;
		for (int i = 0; i < count; i++) {
			Collider collider = results[i].collider;
			if (IsTarget(collider)) continue;
			float limit = Mathf.Max(0f, results[i].distance - 0.05f);
			// Only supported materials may pass through the comfort zone; hard obstacles still win.
			if (cutaway && CameraOcclusionFade.SupportsCutaway(collider))
				limit = Mathf.Max(limit, Mathf.Min(comfortDistance, distance));
			allowed = Mathf.Min(allowed, limit);
		}
		IsObstructed = allowed < distance - 0.1f;
		if (resolvedDistance < 0f || allowed < resolvedDistance) resolvedDistance = allowed;
		else resolvedDistance = Mathf.Lerp(resolvedDistance, allowed, 1f - Mathf.Exp(-recoverySpeed * Time.deltaTime));
		return pivot + direction * resolvedDistance;
	}

	public bool IsPathBlocked (Vector3 from, Vector3 to, Transform target, LayerMask mask) {
		Vector3 line = to - from;
		if (line.sqrMagnitude < 0.0001f) return false;
		CacheTarget(target);
		int count = Physics.RaycastNonAlloc(from, line.normalized, hits, line.magnitude, mask, QueryTriggerInteraction.Ignore);
		RaycastHit[] results = hits;
		if (count == hits.Length) {
			results = Physics.RaycastAll(from, line.normalized, line.magnitude, mask, QueryTriggerInteraction.Ignore);
			count = results.Length;
		}
		for (int i = 0; i < count; i++)
			if (!IsTarget(results[i].collider)) return true;
		return false;
	}

	void CacheTarget (Transform target) {
		if (cachedTarget == target) return;
		cachedTarget = target;
		cachedBody = target != null ? target.GetComponentInParent<Rigidbody>() : null;
	}

	bool IsTarget (Collider collider) => collider == null ||
		(cachedTarget != null && (collider.transform.IsChildOf(cachedTarget) ||
		(collider.attachedRigidbody != null && collider.attachedRigidbody == cachedBody)));
}
