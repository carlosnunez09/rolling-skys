using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GravityCar : MonoBehaviour {

	[SerializeField, Range(1f, 720f)]
	float alignmentSpeed = 180f;

	[SerializeField, Range(1f, 1440f)]
	float surfaceSnapAlignmentSpeed = 720f;

	Rigidbody body;
	Quaternion gravityAlignment = Quaternion.identity;

	public Vector3 UpAxis { get; private set; } = Vector3.up;
	public Quaternion GravityAlignment => gravityAlignment;

	void Awake () {
		body = GetComponent<Rigidbody>();
		body.useGravity = false;
		body.freezeRotation = true;
		body.interpolation = RigidbodyInterpolation.Interpolate;
		gravityAlignment = Quaternion.FromToRotation(Vector3.up, transform.up);
		UpAxis = transform.up;
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
		UpdateAlignment(upAxis, fastAlign: false);
		return gravity;
	}

	public void UpdateAlignment (Vector3 upAxis, bool fastAlign = false) {
		// Zero-length upAxis (no gravity source active) would produce NaN inside
		// FromToRotation and corrupt the alignment quaternion permanently.
		if (upAxis.sqrMagnitude < 0.001f) return;

		Vector3 fromUp = gravityAlignment * Vector3.up;
		float dot = Mathf.Clamp(Vector3.Dot(fromUp, upAxis), -1f, 1f);
		float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float speed = fastAlign ? surfaceSnapAlignmentSpeed : alignmentSpeed;
		float maxAngle = speed * Time.fixedDeltaTime;

		Quaternion targetRotation;
		if (dot < -0.999f) {
			// 180-degree flip: rotate around car's pitch or roll axis rather than undefined axis
			Vector3 flipAxis = Vector3.Cross(fromUp, transform.forward);
			if (flipAxis.sqrMagnitude < 0.001f)
				flipAxis = Vector3.Cross(fromUp, transform.right);
			if (flipAxis.sqrMagnitude < 0.001f)
				flipAxis = transform.right;
			flipAxis.Normalize();
			targetRotation = Quaternion.AngleAxis(180f, flipAxis) * gravityAlignment;
		} else {
			targetRotation = Quaternion.FromToRotation(fromUp, upAxis) * gravityAlignment;
		}

		// Also guard the result: a NaN quaternion (from degenerate input) must never
		// replace a good one, or the car orientation becomes unrecoverable.
		if (float.IsNaN(targetRotation.x) || float.IsNaN(targetRotation.y) ||
		    float.IsNaN(targetRotation.z) || float.IsNaN(targetRotation.w)) return;

		gravityAlignment = angle <= maxAngle
			? targetRotation
			: Quaternion.SlerpUnclamped(gravityAlignment, targetRotation, maxAngle / angle);
	}
}
