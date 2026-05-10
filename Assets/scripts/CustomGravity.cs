using UnityEngine;
using System.Collections.Generic;

public static class CustomGravity {

	static List<GravitySource> sources = new List<GravitySource>();

	public static void Register (GravitySource source) {
		Debug.Assert(
			!sources.Contains(source),
			"Duplicate registration of gravity source!", source
		);
		sources.Add(source);
	}

	public static void Unregister (GravitySource source) {
		Debug.Assert(
			sources.Contains(source),
			"Unregistration of unknown gravity source!", source
		);
		sources.Remove(source);
	}
	
	public static Vector3 GetGravity (Vector3 position) {
		Vector3 g = Vector3.zero;
		for (int i = 0; i < sources.Count; i++) {
			g += sources[i].GetGravity(position);
		}
		return g;
	}

	public static Vector3 GetGravity (Vector3 position, out Vector3 upAxis) {
		Vector3 g = Vector3.zero;
		for (int i = 0; i < sources.Count; i++) {
			g += sources[i].GetGravity(position);
		}
		upAxis = -g.normalized;
		return g;
	}

	public static Vector3 GetUpAxis (Vector3 position) {
		Vector3 g = Vector3.zero;
		for (int i = 0; i < sources.Count; i++) {
			g += sources[i].GetGravity(position);
		}
		return -g.normalized;
	}

	public static string GetDominantSourceName (Vector3 position) {
		GravitySource dominant = null;
		float maxMag = 0f;
		for (int i = 0; i < sources.Count; i++) {
			float mag = sources[i].GetGravity(position).magnitude;
			if (mag > maxMag) {
				maxMag = mag;
				dominant = sources[i];
			}
		}
		return dominant != null ? dominant.gameObject.name : "None";
	}
}