using UnityEngine;
using System.Collections.Generic;

public static class CustomGravity {

	static List<GravitySource> sources = new List<GravitySource>();

	// Reset the static list at the start of every Play Mode session so stale
	// references from a previous session (no-Domain-Reload mode) can't corrupt it.
	[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
	static void ResetOnDomainReload () => sources = new List<GravitySource>();

	public static void Register (GravitySource source) {
		// Guard against duplicate registration (can happen on assembly hot-reload).
		if (!sources.Contains(source))
			sources.Add(source);
	}

	public static void Unregister (GravitySource source) {
		sources.Remove(source); // Remove is a no-op if not present — safe to call redundantly.
	}

	static Vector3 SumGravity (Vector3 position) {
		Vector3 g = Vector3.zero;
		for (int i = 0; i < sources.Count; i++) {
			// Null-guard: destroyed sources may linger until the next GC pass.
			if (sources[i] != null)
				g += sources[i].GetGravity(position);
		}
		return g;
	}

	public static Vector3 GetGravity (Vector3 position) => SumGravity(position);

	public static Vector3 GetGravity (Vector3 position, out Vector3 upAxis) {
		Vector3 g = SumGravity(position);
		// When gravity is zero (no active sources) fall back to world-up so the
		// alignment quaternion never receives a zero vector and produces NaN.
		upAxis = g.sqrMagnitude > 1e-6f ? -g.normalized : Vector3.up;
		return g;
	}

	public static Vector3 GetUpAxis (Vector3 position) {
		Vector3 g = SumGravity(position);
		return g.sqrMagnitude > 1e-6f ? -g.normalized : Vector3.up;
	}

	public static string GetDominantSourceName (Vector3 position) {
		GravitySource dominant = null;
		float maxMag = 0f;
		for (int i = 0; i < sources.Count; i++) {
			if (sources[i] == null) continue;
			float mag = sources[i].GetGravity(position).magnitude;
			if (mag > maxMag) { maxMag = mag; dominant = sources[i]; }
		}
		return dominant != null ? dominant.gameObject.name : "None";
	}
}