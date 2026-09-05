using UnityEngine;
using System.Collections.Generic;

public static class CustomGravity {

	static List<GravitySource> sources = new List<GravitySource>();

	struct ActiveSample {
		public GravitySource source;
		public Vector3 gravity;
		public float magnitude;
		public float weight;
		public int priority;
	}

	static readonly List<ActiveSample> sampleBuffer = new List<ActiveSample>(16);

	// Reset the static list at the start of every Play Mode session so stale
	// references from a previous session (no-Domain-Reload mode) can't corrupt it.
	[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
	static void ResetOnDomainReload () {
		sources = new List<GravitySource>();
		sampleBuffer.Clear();
	}

	public static void Register (GravitySource source) {
		// Guard against duplicate registration (can happen on assembly hot-reload).
		if (source != null && !sources.Contains(source))
			sources.Add(source);
	}

	public static void Unregister (GravitySource source) {
		sources.Remove(source); // Remove is a no-op if not present — safe to call redundantly.
	}

	public static Vector3 SafeSlerp (Vector3 from, Vector3 to, float t, Vector3 fallbackAxis = default) {
		float dot = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
		if (dot > 0.9999f) return Vector3.Lerp(from, to, t).normalized;
		if (dot < -0.9999f) {
			Vector3 axis = fallbackAxis;
			if (axis.sqrMagnitude < 0.001f)
				axis = Vector3.Cross(from, Vector3.forward);
			if (axis.sqrMagnitude < 0.001f)
				axis = Vector3.Cross(from, Vector3.right);
			if (axis.sqrMagnitude < 0.001f)
				axis = Vector3.Cross(from, Vector3.up);
			axis.Normalize();
			return (Quaternion.AngleAxis(180f * t, axis) * from).normalized;
		}
		return Vector3.Slerp(from, to, t);
	}

	public static Vector3 EvaluateGravity (Vector3 position) {
		sampleBuffer.Clear();
		int maxPriority = int.MinValue;

		for (int i = 0; i < sources.Count; i++) {
			GravitySource src = sources[i];
			if (src == null) continue;

			Vector3 g = src.GetGravity(position);
			float mag = g.magnitude;
			if (mag < 0.0001f) continue;

			float nominal = Mathf.Max(src.GravityStrength, 0.001f);
			float weight = Mathf.Clamp01(mag / nominal);
			int priority = src.Priority;

			if (priority > maxPriority) maxPriority = priority;

			sampleBuffer.Add(new ActiveSample {
				source = src,
				gravity = g,
				magnitude = mag,
				weight = weight,
				priority = priority
			});
		}

		if (sampleBuffer.Count == 0) return Vector3.zero;

		int primaryCount = 0;
		ActiveSample primary = default;
		ActiveSample secondary = default;
		bool hasSecondary = false;

		for (int i = 0; i < sampleBuffer.Count; i++) {
			ActiveSample s = sampleBuffer[i];
			if (s.priority == maxPriority) {
				if (primaryCount == 0 || s.weight > primary.weight) {
					primary = s;
				}
				primaryCount++;
			} else {
				if (!hasSecondary || s.weight > secondary.weight) {
					secondary = s;
					hasSecondary = true;
				}
			}
		}

		// When a higher-priority source (e.g. local road/plate) is in falloff and overlapping
		// with a lower-priority source (e.g. planet), blend directionally without opposing vector cancellation!
		if (hasSecondary && primary.weight < 0.999f) {
			Vector3 dirPrimary = primary.gravity.normalized;
			Vector3 dirSecondary = secondary.gravity.normalized;
			float blendWeight = primary.weight; // 0 = secondary, 1 = primary

			Vector3 blendedDir = SafeSlerp(dirSecondary, dirPrimary, blendWeight);
			float blendedMag = Mathf.Lerp(secondary.magnitude, primary.source.GravityStrength, blendWeight);
			return blendedDir * blendedMag;
		}

		if (primaryCount > 1) {
			Vector3 combined = Vector3.zero;
			for (int i = 0; i < sampleBuffer.Count; i++) {
				if (sampleBuffer[i].priority == maxPriority) {
					Vector3 g = sampleBuffer[i].gravity;
					if (combined.sqrMagnitude > 0.001f && Vector3.Dot(combined.normalized, g.normalized) < 0f) {
						float wCombined = combined.magnitude;
						float wNew = g.magnitude;
						float t = wNew / Mathf.Max(wCombined + wNew, 0.0001f);
						Vector3 dir = SafeSlerp(combined.normalized, g.normalized, t);
						float mag = Mathf.Lerp(wCombined, wNew, t);
						combined = dir * mag;
					} else {
						combined += g;
					}
				}
			}
			return combined;
		}

		return primary.gravity;
	}

	public static Vector3 GetGravity (Vector3 position) => EvaluateGravity(position);

	public static Vector3 GetGravity (Vector3 position, out Vector3 upAxis) {
		Vector3 g = EvaluateGravity(position);
		// When gravity is zero (no active sources) fall back to world-up so the
		// alignment quaternion never receives a zero vector and produces NaN.
		upAxis = g.sqrMagnitude > 1e-6f ? -g.normalized : Vector3.up;
		return g;
	}

	public static Vector3 GetUpAxis (Vector3 position) {
		Vector3 g = EvaluateGravity(position);
		return g.sqrMagnitude > 1e-6f ? -g.normalized : Vector3.up;
	}

	public static string GetDominantSourceName (Vector3 position) {
		GravitySource dominant = null;
		float maxScore = -1f;
		for (int i = 0; i < sources.Count; i++) {
			GravitySource src = sources[i];
			if (src == null) continue;
			Vector3 g = src.GetGravity(position);
			float mag = g.magnitude;
			if (mag < 0.001f) continue;
			float score = src.Priority * 1000f + mag;
			if (score > maxScore) { maxScore = score; dominant = src; }
		}
		return dominant != null ? dominant.gameObject.name : "None";
	}
}