using UnityEngine;
using NaughtyAttributes;

[RequireComponent(typeof(Rigidbody))]
public class CustomGravityRigidbody : MonoBehaviour {

	// ── Gravity ───────────────────────────────────────────────────────

	// Multiply the gravity force (0 = weightless, 2 = double gravity)
	[BoxGroup("Gravity"), SerializeField, Range(0f, 4f)]
	float gravityScale = 1f;

	// ── Alignment ─────────────────────────────────────────────────────

	// Smoothly rotate the object to stand upright relative to its gravity source.
	// Enables freezeRotation so physics won't fight the alignment.
	[BoxGroup("Alignment"), SerializeField]
	bool alignToGravity = false;

	[BoxGroup("Alignment"), SerializeField, ShowIf("alignToGravity"), Range(1f, 720f)]
	float alignmentSpeed = 180f;

	// ── Sleep Optimisation ────────────────────────────────────────────

	// Allow the rigidbody to sleep when nearly stationary (saves CPU).
	// Disable if the object needs to keep responding to gravity source changes while still.
	[BoxGroup("Sleep"), SerializeField]
	bool floatToSleep = false;

	// ─────────────────────────────────────────────────────────────────

	Rigidbody body;
	Quaternion gravityAlignment;
	float floatDelay;

	void Awake () {
		body = GetComponent<Rigidbody>();
		body.useGravity = false;

		// Preserve the object's spawn orientation as the baseline alignment
		gravityAlignment = transform.rotation;
		if (alignToGravity) body.freezeRotation = true;
	}

	void FixedUpdate () {
		if (floatToSleep) {
			if (body.IsSleeping()) {
				floatDelay = 0f;
				return;
			}
			if (body.linearVelocity.sqrMagnitude < 0.0001f) {
				floatDelay += Time.deltaTime;
				if (floatDelay >= 1f) return;
			} else {
				floatDelay = 0f;
			}
		}

		Vector3 gravity = CustomGravity.GetGravity(body.position, out Vector3 upAxis);
		body.AddForce(gravity * gravityScale, ForceMode.Acceleration);

		if (alignToGravity) AlignToUp(upAxis);
	}

	// Smoothly rotates the object so its local up matches the gravity up axis.
	// Uses the same incremental slerp pattern as GravityCar so alignment speed
	// is frame-rate independent and works at any gravity transition rate.
	void AlignToUp (Vector3 upAxis) {
		Vector3 fromUp  = gravityAlignment * Vector3.up;
		float dot       = Mathf.Clamp(Vector3.Dot(fromUp, upAxis), -1f, 1f);
		float angle     = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float maxAngle  = alignmentSpeed * Time.fixedDeltaTime;
		Quaternion target = Quaternion.FromToRotation(fromUp, upAxis) * gravityAlignment;
		gravityAlignment  = angle <= maxAngle
			? target
			: Quaternion.SlerpUnclamped(gravityAlignment, target, maxAngle / angle);
		body.MoveRotation(gravityAlignment);
	}
}
