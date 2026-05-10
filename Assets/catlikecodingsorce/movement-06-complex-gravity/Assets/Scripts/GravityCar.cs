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
	}

	// Called by MovingCar each FixedUpdate. Applies gravity force and returns it.
	public Vector3 UpdateAndApplyGravity () {
		Vector3 gravity = CustomGravity.GetGravity(body.position, out Vector3 upAxis);
		UpAxis = upAxis;
		UpdateAlignment(upAxis);
		body.AddForce(gravity, ForceMode.Acceleration);
		return gravity;
	}

	void UpdateAlignment (Vector3 upAxis) {
		Vector3 fromUp = gravityAlignment * Vector3.up;
		float dot = Mathf.Clamp(Vector3.Dot(fromUp, upAxis), -1f, 1f);
		float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float maxAngle = alignmentSpeed * Time.fixedDeltaTime;
		Quaternion newAlignment = Quaternion.FromToRotation(fromUp, upAxis) * gravityAlignment;
		gravityAlignment = angle <= maxAngle
			? newAlignment
			: Quaternion.SlerpUnclamped(gravityAlignment, newAlignment, maxAngle / angle);
	}
}
