using UnityEngine;
using NaughtyAttributes;

/// <summary>
/// Drop on any wall, barrier, guardrail, or parent track-boundary object.
/// Automatically applies a low-friction physics material (default 0.05 / 5%) to all attached
/// colliders so cars glide smoothly along barriers without snagging or stopping dead.
/// </summary>
[SelectionBase]
[DisallowMultipleComponent]
public class TrackWall : MonoBehaviour {

	[Header("Wall Friction Settings")]
	[SerializeField, Range(0f, 0.3f), Tooltip("Friction along the wall: 0.0 = frictionless glide, 0.05 = subtle 5% scrub.")]
	float wallFriction = 0.05f;

	[SerializeField, Tooltip("Apply this wall physics material to all child colliders recursively.")]
	bool applyToChildren = true;

	[SerializeField, Tooltip("Optional reference to a PhysicMaterial asset. If null, an optimized low-friction material is created automatically.")]
	PhysicsMaterial customMaterial;

	PhysicsMaterial _runtimeMaterial;

	public float WallFriction => wallFriction;

	void Awake () {
		ApplyWallMaterial();
	}

	void OnValidate () {
		if (wallFriction < 0f) wallFriction = 0f;
		ApplyWallMaterial();
	}

	[Button("Apply Material to Colliders")]
	public void ApplyWallMaterial () {
		PhysicsMaterial matToUse = customMaterial;
		if (matToUse == null) {
			if (_runtimeMaterial == null) {
				_runtimeMaterial = new PhysicsMaterial("TrackWall_Runtime") {
					dynamicFriction = wallFriction,
					staticFriction  = wallFriction,
					bounciness      = 0f,
					frictionCombine = PhysicsMaterialCombine.Minimum,
					bounceCombine   = PhysicsMaterialCombine.Minimum
				};
			} else {
				_runtimeMaterial.dynamicFriction = wallFriction;
				_runtimeMaterial.staticFriction  = wallFriction;
			}
			matToUse = _runtimeMaterial;
		}

		Collider[] colliders = applyToChildren
			? GetComponentsInChildren<Collider>(true)
			: GetComponents<Collider>();

		foreach (Collider col in colliders) {
			if (col != null && !col.isTrigger) {
				col.sharedMaterial = matToUse;
			}
		}
	}
}
