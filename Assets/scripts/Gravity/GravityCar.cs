using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GravityCar : MonoBehaviour {

	[SerializeField, Range(1f, 360f)]
	float alignmentSpeed = 180f;

	Rigidbody body;
	Quaternion gravityAlignment = Quaternion.identity;

	public Vector3 UpAxis { get; private set; } = Vector3.up;
	public Quaternion GravityAlignment => gravityAlignment;

	void Awake () {
		body = GetComponent<Rigidbody>();
		body.useGravity = false;
		body.freezeRotation = true;
		body.interpolation = RigidbodyInterpolation.Interpolate;
	}

	// Called by MovingCar each FixedUpdate. Applies gravity force and returns it.
	public Vector3 UpdateAndApplyGravity () {
		Vector3 gravity = CustomGravity.GetGravity(body.position, out Vector3 upAxis);
		UpAxis = upAxis;
		UpdateAlignment(upAxis);
		body.AddForce(gravity, ForceMode.Acceleration);
		return gravity;
	}

	/// <summary>
	/// Updates up-axis alignment without applying a gravity force.
	/// Used while a BoostPad drives the car along a scripted arc.
	/// </summary>
	public Vector3 RefreshGravityState () {
		Vector3 gravity = CustomGravity.GetGravity(body.position, out Vector3 upAxis);
		UpAxis = upAxis;
		UpdateAlignment(upAxis);
		return gravity;
	}

	void UpdateAlignment (Vector3 upAxis) {
		// Zero-length upAxis (no gravity source active) would produce NaN inside
		// FromToRotation and corrupt the alignment quaternion permanently.
		if (upAxis.sqrMagnitude < 0.001f) return;

		Vector3 fromUp = gravityAlignment * Vector3.up;
		float dot = Mathf.Clamp(Vector3.Dot(fromUp, upAxis), -1f, 1f);
		float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float maxAngle = alignmentSpeed * Time.fixedDeltaTime;
		Quaternion newAlignment = Quaternion.FromToRotation(fromUp, upAxis) * gravityAlignment;

		// Also guard the result: a NaN quaternion (from degenerate input) must never
		// replace a good one, or the car orientation becomes unrecoverable.
		if (float.IsNaN(newAlignment.x) || float.IsNaN(newAlignment.y) ||
		    float.IsNaN(newAlignment.z) || float.IsNaN(newAlignment.w)) return;

		gravityAlignment = angle <= maxAngle
			? newAlignment
			: Quaternion.SlerpUnclamped(gravityAlignment, newAlignment, maxAngle / angle);
	}
}
