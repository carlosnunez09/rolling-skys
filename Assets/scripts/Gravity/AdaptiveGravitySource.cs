using UnityEngine;
using NaughtyAttributes;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// A single gravity source that can mimic and extend GravitySphere,
/// GravityBox and GravityPlane, with an Inspector button that wires up
/// a matching collider and sane defaults for whichever shape you pick.
public class AdaptiveGravitySource : GravitySource {

	// ─────────────────────────────────────────────────────────────────
	//  Enums
	// ─────────────────────────────────────────────────────────────────

	public enum GravityShape { Sphere, Oval, Box, Plane }

	/// AllFaces  = objects walk on all six faces (like GravityBox).
	/// SingleFace = only one face acts as a floor (finite GravityPlane).
	public enum BoxMode { AllFaces, SingleFace }

	public enum FaceAxis { UpY, DownY, RightX, LeftX, ForwardZ, BackZ }

	// ─────────────────────────────────────────────────────────────────
	//  Shape & polarity
	// ─────────────────────────────────────────────────────────────────

	[BoxGroup("Shape"), SerializeField]
	GravityShape shape = GravityShape.Sphere;

	[BoxGroup("Shape"), SerializeField]
	[Tooltip("ON  = objects outside are attracted (planet — walk on the outside).\n" +
	         "OFF = objects inside  are attracted (room   — walk on the inside).")]
	bool internalGravity = true;

	// ─────────────────────────────────────────────────────────────────
	//  Gravity strength
	// ─────────────────────────────────────────────────────────────────

	[BoxGroup("Gravity"), SerializeField, Range(0f, 30f)]
	float gravityStrength = 9.81f;

	// ─────────────────────────────────────────────────────────────────
	//  Sphere
	// ─────────────────────────────────────────────────────────────────

	[BoxGroup("Sphere"), SerializeField, ShowIf(nameof(IsSphere)), Min(0f), Label("Inner Falloff Radius")]
	float innerFalloffRadius = 1f;

	[BoxGroup("Sphere"), SerializeField, ShowIf(nameof(IsSphere)), Min(0f), Label("Inner Radius")]
	float innerRadius = 5f;

	[BoxGroup("Sphere"), SerializeField, ShowIf(nameof(IsSphere)), Min(0f), Label("Outer Radius  (surface)")]
	float outerRadius = 10f;

	[BoxGroup("Sphere"), SerializeField, ShowIf(nameof(IsSphere)), Min(0f), Label("Outer Falloff Radius")]
	float outerFalloffRadius = 15f;

	// ─────────────────────────────────────────────────────────────────
	//  Oval  (ellipsoid defined by Transform scale)
	// ─────────────────────────────────────────────────────────────────

	[BoxGroup("Oval"), SerializeField, ShowIf(nameof(IsOval))]
	[InfoBox("Oval axes are driven by Transform Scale — scale X / Y / Z to shape the ellipsoid. " +
	         "Press 'Adapt to Shape' to add a matching collider.")]
	[Min(0f), Label("Outer Band Distance")]
	float ovalOuterDistance = 2f;

	[BoxGroup("Oval"), SerializeField, ShowIf(nameof(IsOval)), Min(0f), Label("Outer Falloff Distance")]
	float ovalFalloffDistance = 6f;

	// ─────────────────────────────────────────────────────────────────
	//  Box
	// ─────────────────────────────────────────────────────────────────

	[BoxGroup("Box"), SerializeField, ShowIf(nameof(IsBox)), Label("Mode")]
	BoxMode boxMode = BoxMode.AllFaces;

	[BoxGroup("Box"), SerializeField, ShowIf(nameof(IsBox)), Label("Half-Extents")]
	Vector3 boundaryDistance = new Vector3(5f, 5f, 5f);

	[BoxGroup("Box"), SerializeField, ShowIf(nameof(IsBoxAllFaces)), Min(0f), Label("Inner Dead Zone")]
	float boxInnerDistance = 0f;

	[BoxGroup("Box"), SerializeField, ShowIf(nameof(IsBoxAllFaces)), Min(0f), Label("Inner Falloff")]
	float boxInnerFalloff = 0f;

	[BoxGroup("Box"), SerializeField, ShowIf(nameof(IsBox)), Min(0f), Label("Outer Dead Zone")]
	float boxOuterDistance = 0f;

	[BoxGroup("Box"), SerializeField, ShowIf(nameof(IsBox)), Min(0f), Label("Outer Falloff")]
	float boxOuterFalloff = 5f;

	[BoxGroup("Box"), SerializeField, ShowIf(nameof(IsBoxSingleFace)), Label("Active Face")]
	FaceAxis singleFaceAxis = FaceAxis.UpY;

	// ─────────────────────────────────────────────────────────────────
	//  Plane
	// ─────────────────────────────────────────────────────────────────

	[BoxGroup("Plane"), SerializeField, ShowIf(nameof(IsPlane)), Min(0f), Label("Attraction Range")]
	float planeRange = 10f;

	[BoxGroup("Plane"), SerializeField, ShowIf(nameof(IsPlane)), Label("Attract Both Sides")]
	bool planeBothSides = false;

	// ─────────────────────────────────────────────────────────────────
	//  Gizmos
	// ─────────────────────────────────────────────────────────────────

	[BoxGroup("Gizmos"), SerializeField, Label("Draw Gizmos")]
	bool showGizmos = true;

	[BoxGroup("Gizmos"), SerializeField, ShowIf(nameof(showGizmos)), Label("Surface")]
	Color gizmoSurface = Color.red;

	//draw if enabled 
	
	[BoxGroup("Gizmos"), SerializeField, ShowIf(nameof(showGizmos)), Label("Inner Zone")]
	Color gizmoInner = Color.yellow;

	[BoxGroup("Gizmos"), SerializeField, ShowIf(nameof(showGizmos)), Label("Outer Zone")]
	Color gizmoOuter = Color.cyan;

	// ─────────────────────────────────────────────────────────────────
	//  Mesh Collider override
	// ─────────────────────────────────────────────────────────────────

	[BoxGroup("Mesh Collider"), SerializeField]
	[InfoBox("When enabled the gravity direction is driven by the MeshCollider on this " +
	         "object instead of the mathematical shape above.\n" +
	         "• Planet (Internal ON)  — pulls toward the mesh centre with distance falloff.\n" +
	         "• Room   (Internal OFF) — pulls toward the nearest mesh surface point " +
	         "(requires Convex mesh collider; press Adapt to set it automatically).")]
	bool useMeshCollider = false;

	[BoxGroup("Mesh Collider"), SerializeField, ShowIf(nameof(useMeshCollider))]
	Mesh gravityMesh;

	// Planet mode: distance from centre at which gravity starts fading
	[BoxGroup("Mesh Collider"), SerializeField, ShowIf(nameof(IsMeshPlanet)), Min(0f), Label("Influence Radius")]
	float meshInfluenceRadius = 20f;

	[BoxGroup("Mesh Collider"), SerializeField, ShowIf(nameof(IsMeshPlanet)), Min(0f), Label("Falloff Start Radius")]
	float meshFalloffStart = 15f;

	// ─────────────────────────────────────────────────────────────────
	//  Adapt button
	// ─────────────────────────────────────────────────────────────────

	[Button("Adapt to Shape")]
	void AdaptToShape () {
		ComputeFactors();
#if UNITY_EDITOR
		SetupCollider();
		EditorUtility.SetDirty(this);
		EditorUtility.SetDirty(gameObject);
#endif
	}

	// ─────────────────────────────────────────────────────────────────
	//  ShowIf helpers (bool properties)
	// ─────────────────────────────────────────────────────────────────

	bool IsSphere        => shape == GravityShape.Sphere;
	bool IsOval          => shape == GravityShape.Oval;
	bool IsBox           => shape == GravityShape.Box;
	bool IsPlane         => shape == GravityShape.Plane;
	bool IsBoxAllFaces   => IsBox && boxMode == BoxMode.AllFaces;
	bool IsBoxSingleFace => IsBox && boxMode == BoxMode.SingleFace;
	bool IsMeshPlanet    => useMeshCollider && internalGravity;

	// ─────────────────────────────────────────────────────────────────
	//  Pre-computed reciprocals (avoid division each frame)
	// ─────────────────────────────────────────────────────────────────

	float sphereInnerFactor, sphereOuterFactor;
	float boxInnerFalloffFactor, boxOuterFalloffFactor;

	MeshCollider cachedMeshCollider;

	void Awake () {
		ComputeFactors();
		cachedMeshCollider = GetComponent<MeshCollider>();
	}
	void OnValidate () => ComputeFactors();

	void ComputeFactors () {
		// ── Sphere ────────────────────────────────────────────────────
		innerFalloffRadius = Mathf.Max(innerFalloffRadius, 0f);
		innerRadius        = Mathf.Max(innerRadius, innerFalloffRadius);
		outerRadius        = Mathf.Max(outerRadius, innerRadius);
		outerFalloffRadius = Mathf.Max(outerFalloffRadius, outerRadius);
		sphereInnerFactor  = innerRadius > innerFalloffRadius
			? 1f / (innerRadius - innerFalloffRadius) : 0f;
		sphereOuterFactor  = outerFalloffRadius > outerRadius
			? 1f / (outerFalloffRadius - outerRadius) : 0f;

		// ── Oval ──────────────────────────────────────────────────────
		ovalOuterDistance  = Mathf.Max(ovalOuterDistance, 0f);
		ovalFalloffDistance = Mathf.Max(ovalFalloffDistance, ovalOuterDistance + 0.001f);

		// ── Box ───────────────────────────────────────────────────────
		boundaryDistance  = Vector3.Max(boundaryDistance, Vector3.zero);
		float maxInner    = Mathf.Min(boundaryDistance.x, Mathf.Min(boundaryDistance.y, boundaryDistance.z));
		boxInnerDistance  = Mathf.Min(boxInnerDistance, maxInner);
		boxInnerFalloff   = Mathf.Clamp(boxInnerFalloff, boxInnerDistance, maxInner);
		boxOuterFalloff   = Mathf.Max(boxOuterFalloff, boxOuterDistance + 0.001f);
		boxInnerFalloffFactor = boxInnerFalloff > boxInnerDistance
			? 1f / (boxInnerFalloff - boxInnerDistance) : 0f;
		boxOuterFalloffFactor = boxOuterFalloff > boxOuterDistance
			? 1f / (boxOuterFalloff - boxOuterDistance) : 0f;

		// ── Plane ─────────────────────────────────────────────────────
		planeRange = Mathf.Max(planeRange, 0f);
	}

	// ─────────────────────────────────────────────────────────────────
	//  GetGravity — entry point called by CustomGravity each frame
	// ─────────────────────────────────────────────────────────────────

	public override Vector3 GetGravity (Vector3 position) {
		if (useMeshCollider && cachedMeshCollider != null)
			return MeshGravity(position);

		switch (shape) {
			case GravityShape.Sphere: return SphereGravity(position);
			case GravityShape.Oval:   return OvalGravity(position);
			case GravityShape.Box:    return boxMode == BoxMode.AllFaces
				? BoxAllFacesGravity(position) : BoxSingleFaceGravity(position);
			case GravityShape.Plane:  return PlaneGravity(position);
			default:                  return Vector3.zero;
		}
	}

	// ─────────────────────────────────────────────────────────────────
	//  Mesh Collider gravity
	// ─────────────────────────────────────────────────────────────────

	Vector3 MeshGravity (Vector3 position) {
		if (internalGravity) {
			// Planet: pull toward the mesh centre with distance falloff.
			// Uses meshInfluenceRadius / meshFalloffStart for the envelope.
			Vector3 toCenter = transform.position - position;
			float   dist     = toCenter.magnitude;
			if (dist > meshInfluenceRadius) return Vector3.zero;
			float g = gravityStrength;
			if (meshInfluenceRadius > meshFalloffStart && dist > meshFalloffStart)
				g *= 1f - (dist - meshFalloffStart) / (meshInfluenceRadius - meshFalloffStart);
			return toCenter.normalized * g;
		} else {
			// Room: pull toward the nearest point on the mesh surface.
			// Requires a convex MeshCollider — press Adapt to set it up.
			Vector3 closest   = cachedMeshCollider.ClosestPoint(position);
			Vector3 toSurface = closest - position;
			float   dist      = toSurface.magnitude;
			if (dist < 0.001f) return Vector3.zero; // already on surface
			return toSurface.normalized * gravityStrength;
		}
	}

	// ─────────────────────────────────────────────────────────────────
	//  Sphere  (mirrors GravitySphere exactly, adds polarity)
	// ─────────────────────────────────────────────────────────────────

	Vector3 SphereGravity (Vector3 position) {
		Vector3 toCenter = transform.position - position;
		float distance   = toCenter.magnitude;

		if (distance > outerFalloffRadius || distance < innerFalloffRadius)
			return Vector3.zero;

		float g = gravityStrength / distance;
		if (distance > outerRadius)
			g *= 1f - (distance - outerRadius) * sphereOuterFactor;
		else if (distance < innerRadius)
			g *= 1f - (innerRadius - distance) * sphereInnerFactor;

		// internalGravity = true  → pull toward center (planet)
		// internalGravity = false → push toward surface from inside (hollow room)
		return g * (internalGravity ? toCenter : -toCenter);
	}

	// ─────────────────────────────────────────────────────────────────
	//  Oval  (ellipsoid — shape driven by Transform.localScale)
	// ─────────────────────────────────────────────────────────────────

	Vector3 OvalGravity (Vector3 position) {
		// In local space the ellipsoid is a unit sphere
		Vector3 localPos  = transform.InverseTransformPoint(position);
		float   localDist = localPos.magnitude; // 1 = on the surface

		// Ellipsoid surface normal (gradient of the implicit function x²/a² + … = 1)
		Vector3 scale = transform.lossyScale;
		Vector3 ellipNormal = new Vector3(
			localPos.x / Mathf.Max(scale.x * scale.x, 0.0001f),
			localPos.y / Mathf.Max(scale.y * scale.y, 0.0001f),
			localPos.z / Mathf.Max(scale.z * scale.z, 0.0001f));

		float avgScale     = (scale.x + scale.y + scale.z) / 3f;
		float outerLocal   = avgScale > 0f ? ovalOuterDistance / avgScale  : ovalOuterDistance;
		float falloffLocal = avgScale > 0f ? ovalFalloffDistance / avgScale : ovalFalloffDistance;

		if (internalGravity) {
			// Planet oval — attract from outside (localDist > 1)
			if (localDist <= 1f) return Vector3.zero;
			float distFromSurface = localDist - 1f;
			if (distFromSurface > outerLocal + falloffLocal) return Vector3.zero;
			float g = gravityStrength;
			if (distFromSurface > outerLocal)
				g *= 1f - (distFromSurface - outerLocal) / falloffLocal;
			return transform.TransformDirection(-ellipNormal).normalized * g;
		} else {
			// Room oval — attract toward surface from inside (localDist < 1)
			if (localDist >= 1f) return Vector3.zero;
			return transform.TransformDirection(ellipNormal).normalized * gravityStrength;
		}
	}

	// ─────────────────────────────────────────────────────────────────
	//  Box — All Faces  (mirrors GravityBox exactly, adds polarity)
	// ─────────────────────────────────────────────────────────────────

	Vector3 BoxAllFacesGravity (Vector3 position) {
		Vector3 localPos = transform.InverseTransformDirection(position - transform.position);
		Vector3 vector   = Vector3.zero;
		int     outside  = 0;

		if      (localPos.x >  boundaryDistance.x) { vector.x =  boundaryDistance.x - localPos.x; outside++; }
		else if (localPos.x < -boundaryDistance.x) { vector.x = -boundaryDistance.x - localPos.x; outside++; }
		if      (localPos.y >  boundaryDistance.y) { vector.y =  boundaryDistance.y - localPos.y; outside++; }
		else if (localPos.y < -boundaryDistance.y) { vector.y = -boundaryDistance.y - localPos.y; outside++; }
		if      (localPos.z >  boundaryDistance.z) { vector.z =  boundaryDistance.z - localPos.z; outside++; }
		else if (localPos.z < -boundaryDistance.z) { vector.z = -boundaryDistance.z - localPos.z; outside++; }

		if (internalGravity) {
			// Planet box — attract from outside
			if (outside == 0) return Vector3.zero;
			float distance = outside == 1
				? Mathf.Abs(vector.x + vector.y + vector.z)
				: vector.magnitude;
			if (distance > boxOuterFalloff) return Vector3.zero;
			float g = gravityStrength / distance;
			if (distance > boxOuterDistance)
				g *= 1f - (distance - boxOuterDistance) * boxOuterFalloffFactor;
			return transform.TransformDirection(g * vector);
		} else {
			// Room box — attract toward nearest wall from inside
			if (outside > 0) return Vector3.zero;
			Vector3 distances;
			distances.x = boundaryDistance.x - Mathf.Abs(localPos.x);
			distances.y = boundaryDistance.y - Mathf.Abs(localPos.y);
			distances.z = boundaryDistance.z - Mathf.Abs(localPos.z);
			if (distances.x < distances.y) {
				vector.x = distances.x < distances.z
					? BoxWallComponent(localPos.x, distances.x)
					: BoxWallComponent(localPos.z, distances.z);
			} else if (distances.y < distances.z) {
				vector.y = BoxWallComponent(localPos.y, distances.y);
			} else {
				vector.z = BoxWallComponent(localPos.z, distances.z);
			}
			return transform.TransformDirection(vector);
		}
	}

	float BoxWallComponent (float coord, float distToWall) {
		if (distToWall > boxInnerFalloff) return 0f;
		float g = gravityStrength;
		if (distToWall > boxInnerDistance)
			g *= 1f - (distToWall - boxInnerDistance) * boxInnerFalloffFactor;
		return coord > 0f ? -g : g;
	}

	// ─────────────────────────────────────────────────────────────────
	//  Box — Single Face  (finite GravityPlane)
	// ─────────────────────────────────────────────────────────────────

	Vector3 BoxSingleFaceGravity (Vector3 position) {
		Vector3 normal   = FaceWorldNormal(singleFaceAxis);
		float   distance = Vector3.Dot(normal, position - transform.position);

		if (internalGravity) {
			// Attract from the front side of the face
			if (distance <= 0f || distance > boxOuterFalloff) return Vector3.zero;
			float g = gravityStrength;
			if (distance > boxOuterDistance)
				g *= 1f - (distance - boxOuterDistance) * boxOuterFalloffFactor;
			return -normal * g;
		} else {
			// Attract from the back side
			if (distance >= 0f || -distance > boxOuterFalloff) return Vector3.zero;
			float g = gravityStrength;
			if (-distance > boxOuterDistance)
				g *= 1f - (-distance - boxOuterDistance) * boxOuterFalloffFactor;
			return normal * g;
		}
	}

	Vector3 FaceWorldNormal (FaceAxis axis) {
		switch (axis) {
			case FaceAxis.UpY:      return  transform.up;
			case FaceAxis.DownY:    return -transform.up;
			case FaceAxis.RightX:   return  transform.right;
			case FaceAxis.LeftX:    return -transform.right;
			case FaceAxis.ForwardZ: return  transform.forward;
			case FaceAxis.BackZ:    return -transform.forward;
			default:                return  transform.up;
		}
	}

	// ─────────────────────────────────────────────────────────────────
	//  Plane  (mirrors GravityPlane, adds both-sides and polarity)
	// ─────────────────────────────────────────────────────────────────

	Vector3 PlaneGravity (Vector3 position) {
		Vector3 up       = transform.up;
		float   distance = Vector3.Dot(up, position - transform.position);

		if (planeBothSides) {
			float absDist = Mathf.Abs(distance);
			if (absDist > planeRange) return Vector3.zero;
			float g = gravityStrength * (planeRange > 0f ? 1f - absDist / planeRange : 1f);
			return (distance >= 0f ? -up : up) * g;
		}

		// One-sided
		if (internalGravity) {
			// Attract from above
			if (distance > planeRange || distance < 0f) return Vector3.zero;
			float g = -gravityStrength;
			if (distance > 0f && planeRange > 0f) g *= 1f - distance / planeRange;
			return g * up;
		} else {
			// Attract from below
			if (-distance > planeRange || distance > 0f) return Vector3.zero;
			float g = gravityStrength;
			if (distance < 0f && planeRange > 0f) g *= 1f - (-distance) / planeRange;
			return g * up;
		}
	}

	// ─────────────────────────────────────────────────────────────────
	//  Editor-only: collider setup + gizmos
	// ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
	void SetupCollider () {
		// Remove any existing non-trigger colliders
		foreach (Collider c in GetComponents<Collider>()) {
			if (!c.isTrigger) Undo.DestroyObjectImmediate(c);
		}

		if (useMeshCollider) {
			var mc = Undo.AddComponent<MeshCollider>(gameObject);
			if (gravityMesh != null) mc.sharedMesh = gravityMesh;
			// Room mode needs a convex mesh so ClosestPoint works from inside
			mc.convex = !internalGravity;
			cachedMeshCollider = mc;
			return;
		}

		switch (shape) {
			case GravityShape.Sphere: {
				var sc = Undo.AddComponent<SphereCollider>(gameObject);
				sc.radius = outerRadius;
				break;
			}
			case GravityShape.Oval: {
				var sc = Undo.AddComponent<SphereCollider>(gameObject);
				sc.radius = 1f;
				break;
			}
			case GravityShape.Box: {
				var bc = Undo.AddComponent<BoxCollider>(gameObject);
				bc.size = boundaryDistance * 2f;
				break;
			}
			case GravityShape.Plane: {
				var bc = Undo.AddComponent<BoxCollider>(gameObject);
				Vector3 s = transform.localScale;
				bc.size = new Vector3(s.x > 0f ? 1f : 10f, 0.05f, s.z > 0f ? 1f : 10f);
				break;
			}
		}
	}

	void OnDrawGizmos () {
		if (!showGizmos || !internalGravity) return;

		if (useMeshCollider) {
			DrawMeshGizmos();
			return;
		}

		switch (shape) {
			case GravityShape.Sphere: DrawSphereGizmos(); break;
			case GravityShape.Oval:   DrawOvalGizmos();   break;
			case GravityShape.Box:    DrawBoxGizmos();     break;
			case GravityShape.Plane:  DrawPlaneGizmos();   break;
		}
	}

	void DrawMeshGizmos () {
		Vector3 p = transform.position;
		// Falloff start sphere
		if (meshFalloffStart > 0f && meshFalloffStart < meshInfluenceRadius) {
			Gizmos.color = gizmoInner;
			Gizmos.DrawWireSphere(p, meshFalloffStart);
		}
		// Influence radius sphere
		Gizmos.color = gizmoOuter;
		Gizmos.DrawWireSphere(p, meshInfluenceRadius);
		// Mesh wireframe preview
		if (gravityMesh != null) {
			Gizmos.color  = gizmoSurface;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.DrawWireMesh(gravityMesh);
			Gizmos.matrix = Matrix4x4.identity;
		}
	}

	void DrawSphereGizmos () {
		Vector3 p = transform.position;
		if (innerFalloffRadius > 0f && innerFalloffRadius < innerRadius) {
			Gizmos.color = gizmoOuter; Gizmos.DrawWireSphere(p, innerFalloffRadius);
		}
		if (innerRadius > 0f && innerRadius < outerRadius) {
			Gizmos.color = gizmoInner; Gizmos.DrawWireSphere(p, innerRadius);
		}
		Gizmos.color = gizmoSurface; Gizmos.DrawWireSphere(p, outerRadius);
		if (outerFalloffRadius > outerRadius) {
			Gizmos.color = gizmoOuter; Gizmos.DrawWireSphere(p, outerFalloffRadius);
		}
	}

	void DrawOvalGizmos () {
		Gizmos.matrix = transform.localToWorldMatrix;
		Gizmos.color  = gizmoSurface;
		Gizmos.DrawWireSphere(Vector3.zero, 1f);
		Gizmos.color  = gizmoOuter;
		Gizmos.DrawWireSphere(Vector3.zero, 1f + ovalOuterDistance);
		Gizmos.matrix = Matrix4x4.identity;
	}

	void DrawBoxGizmos () {
		Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
		Gizmos.color  = gizmoSurface;
		Gizmos.DrawWireCube(Vector3.zero, boundaryDistance * 2f);
		if (boxOuterDistance > 0f) {
			Gizmos.color = gizmoInner;
			Gizmos.DrawWireCube(Vector3.zero, (boundaryDistance + Vector3.one * boxOuterDistance) * 2f);
		}
		if (boxOuterFalloff > boxOuterDistance) {
			Gizmos.color = gizmoOuter;
			Gizmos.DrawWireCube(Vector3.zero, (boundaryDistance + Vector3.one * boxOuterFalloff) * 2f);
		}
		Gizmos.matrix = Matrix4x4.identity;
	}

	void DrawPlaneGizmos () {
		Vector3 s = transform.localScale;
		Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation,
		                               new Vector3(s.x, planeRange, s.z));
		Gizmos.color  = gizmoSurface;
		Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 0f, 1f));
		Gizmos.color  = gizmoOuter;
		Gizmos.DrawWireCube(Vector3.up * 0.5f, Vector3.one);
		Gizmos.matrix = Matrix4x4.identity;
	}
#endif
}
