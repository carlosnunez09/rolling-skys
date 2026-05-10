using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

[RequireComponent(typeof(Camera))]
public class OrbitCamera : MonoBehaviour {

	// ── Focus ─────────────────────────────────────────────────────────

	[BoxGroup("Focus"), SerializeField]
	Transform focus = default;

	[BoxGroup("Focus"), SerializeField, Range(0f, 10f), Label("Lag Radius")]
	float focusRadius = 5f;

	[BoxGroup("Focus"), SerializeField, Range(0f, 1f), Label("Centering Speed")]
	float focusCentering = 0.5f;

	// ── Distance & Occlusion ──────────────────────────────────────────

	[BoxGroup("Distance"), SerializeField, Range(1f, 20f)]
	float distance = 5f;

	[BoxGroup("Distance"), SerializeField]
	LayerMask obstructionMask = -1;

	// ── Rotation ──────────────────────────────────────────────────────

	[BoxGroup("Rotation"), SerializeField, Range(1f, 360f), Label("Manual Speed  °/s")]
	float rotationSpeed = 90f;

	[BoxGroup("Rotation"), SerializeField, Min(0f), Label("Auto Delay  s")]
	float alignDelay = 0.5f;

	[BoxGroup("Rotation"), SerializeField, Range(0f, 90f), Label("Smooth Range  °")]
	float alignSmoothRange = 45f;

	// Final-stage rotation damping — kills sub-frame flicker without visible lag
	[BoxGroup("Rotation"), SerializeField, Range(5f, 60f), Label("Output Smooth Speed")]
	float rotationSmoothSpeed = 25f;

	// ── Pitch (speed-driven) ──────────────────────────────────────────

	[BoxGroup("Pitch"), SerializeField, Range(0f, 89f), Label("Stopped Angle  °")]
	float topDownAngle = 60f;

	[BoxGroup("Pitch"), SerializeField, Range(0f, 89f), Label("Full Speed Angle  °")]
	float behindAngle = 15f;

	[BoxGroup("Pitch"), SerializeField, Range(1f, 100f), Label("Full Speed Threshold  m/s")]
	float speedForFullAngle = 20f;

	[BoxGroup("Pitch"), SerializeField, Range(1f, 20f), Label("Smooth Speed")]
	float pitchSmoothSpeed = 4f;

	[BoxGroup("Pitch"), SerializeField, Range(-89f, 89f), Label("Min Vertical  °")]
	float minVerticalAngle = -45f;

	[BoxGroup("Pitch"), SerializeField, Range(-89f, 89f), Label("Max Vertical  °")]
	float maxVerticalAngle = 45f;

	// ── Gravity Alignment ─────────────────────────────────────────────

	[BoxGroup("Gravity"), SerializeField, Min(0f), Label("Alignment Speed  °/s")]
	float upAlignmentSpeed = 360f;

	Camera regularCamera;
	Rigidbody focusBody;

	float smoothedSpeed;
	Vector3 smoothedFocusForward = Vector3.forward;

	InputAction lookAction;

	Vector3 focusPoint, previousFocusPoint;

	Vector2 orbitAngles = new Vector2(45f, 0f);

	float lastManualRotationTime;

	Quaternion gravityAlignment = Quaternion.identity;

	Quaternion orbitRotation;

	Vector3 CameraHalfExtends {
		get {
			Vector3 halfExtends;
			halfExtends.y =
				regularCamera.nearClipPlane *
				Mathf.Tan(0.5f * Mathf.Deg2Rad * regularCamera.fieldOfView);
			halfExtends.x = halfExtends.y * regularCamera.aspect;
			halfExtends.z = 0f;
			return halfExtends;
		}
	}

	void OnValidate () {
		if (maxVerticalAngle < minVerticalAngle) {
			maxVerticalAngle = minVerticalAngle;
		}
	}

	void Awake () {
		regularCamera = GetComponent<Camera>();
		focusBody = focus != null ? focus.GetComponent<Rigidbody>() : null;
		focusPoint = focus.position;
		transform.localRotation = orbitRotation = Quaternion.Euler(orbitAngles);

		lookAction = new InputAction("Look", InputActionType.Value);
		lookAction.AddCompositeBinding("2DVector")
			.With("Up",    "<Keyboard>/upArrow")
			.With("Down",  "<Keyboard>/downArrow")
			.With("Left",  "<Keyboard>/leftArrow")
			.With("Right", "<Keyboard>/rightArrow");
		lookAction.AddBinding("<Gamepad>/rightStick");
	}

	void OnEnable () {
		lookAction.Enable();
	}

	void OnDisable () {
		lookAction.Disable();
	}

	void LateUpdate () {
		UpdateGravityAlignment();
		UpdateFocusPoint();
		if (ManualRotation() || AutomaticRotation()) {
			ConstrainAngles();
		}
		UpdatePitchFromSpeed();
		orbitRotation = Quaternion.Euler(orbitAngles);
		Quaternion lookRotation = gravityAlignment * orbitRotation;

		Vector3 lookDirection = lookRotation * Vector3.forward;
		Vector3 lookPosition = focusPoint - lookDirection * distance;

		Vector3 rectOffset = lookDirection * regularCamera.nearClipPlane;
		Vector3 rectPosition = lookPosition + rectOffset;
		Vector3 castFrom = focus.position;
		Vector3 castLine = rectPosition - castFrom;
		float castDistance = castLine.magnitude;
		Vector3 castDirection = castLine / castDistance;

		if (Physics.BoxCast(
			castFrom, CameraHalfExtends, castDirection, out RaycastHit hit,
			lookRotation, castDistance, obstructionMask
		)) {
			rectPosition = castFrom + castDirection * hit.distance;
			lookPosition = rectPosition - rectOffset;
		}
		
		// Slerp the final rotation rather than snapping — kills frame-to-frame flicker
		// while remaining fast enough to feel instant on real turns.
		Quaternion smoothedRotation = Quaternion.Slerp(
			transform.rotation, lookRotation,
			Mathf.Clamp01(rotationSmoothSpeed * Time.deltaTime));
		transform.SetPositionAndRotation(lookPosition, smoothedRotation);
	}

	void UpdateGravityAlignment () {
		Vector3 fromUp = gravityAlignment * Vector3.up;
		Vector3 toUp = CustomGravity.GetUpAxis(focusPoint);
		float dot = Mathf.Clamp(Vector3.Dot(fromUp, toUp), -1f, 1f);
		float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float maxAngle = upAlignmentSpeed * Time.deltaTime;

		Quaternion newAlignment =
			Quaternion.FromToRotation(fromUp, toUp) * gravityAlignment;
		if (angle <= maxAngle) {
			gravityAlignment = newAlignment;
		}
		else {
			gravityAlignment = Quaternion.SlerpUnclamped(
				gravityAlignment, newAlignment, maxAngle / angle
			);
		}
	}

	void UpdateFocusPoint () {
		previousFocusPoint = focusPoint;
		Vector3 targetPoint = focus.position;
		if (focusRadius > 0f) {
			float distance = Vector3.Distance(targetPoint, focusPoint);
			float t = 1f;
			if (distance > 0.01f && focusCentering > 0f) {
				t = Mathf.Pow(1f - focusCentering, Time.unscaledDeltaTime);
			}
			if (distance > focusRadius) {
				t = Mathf.Min(t, focusRadius / distance);
			}
			focusPoint = Vector3.Lerp(targetPoint, focusPoint, t);
		}
		else {
			focusPoint = targetPoint;
		}
	}

	bool ManualRotation () {
		float x = lookAction.ReadValue<Vector2>().x;
		const float e = 0.001f;
		if (x < -e || x > e) {
			orbitAngles.y += rotationSpeed * Time.unscaledDeltaTime * x;
			lastManualRotationTime = Time.unscaledTime;
			return true;
		}
		return false;
	}

	void UpdatePitchFromSpeed () {
		float rawSpeed = focusBody != null ? focusBody.linearVelocity.magnitude : 0f;
		// Dead-zone: physics solver produces noise up to ~0.5 m/s even when stopped.
		// Only start tracking speed once the car is clearly moving.
		if (rawSpeed < 1f) rawSpeed = 0f;
		smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed, 3f * Time.deltaTime);
		if (smoothedSpeed < 0.05f) smoothedSpeed = 0f; // hard-stop residual drift
		float t = Mathf.Clamp01(smoothedSpeed / speedForFullAngle);
		float targetPitch = Mathf.Lerp(topDownAngle, behindAngle, t);
		orbitAngles.x = Mathf.Lerp(orbitAngles.x, targetPitch, pitchSmoothSpeed * Time.deltaTime);
	}

	bool AutomaticRotation () {
		if (Time.unscaledTime - lastManualRotationTime < alignDelay) {
			return false;
		}

		// Smooth the car's facing direction so discrete physics rotations don't jitter the yaw
		smoothedFocusForward = Vector3.Slerp(smoothedFocusForward, focus.forward, 12f * Time.unscaledDeltaTime);
		Vector3 facingAligned = Quaternion.Inverse(gravityAlignment) * smoothedFocusForward;
		Vector2 headingVector = new Vector2(facingAligned.x, facingAligned.z);

		if (headingVector.sqrMagnitude < 0.0001f) {
			return false;
		}

		float headingAngle = GetAngle(headingVector.normalized);
		float deltaAbs = Mathf.Abs(Mathf.DeltaAngle(orbitAngles.y, headingAngle));
		float rotationChange = rotationSpeed * Time.unscaledDeltaTime;
		if (deltaAbs < alignSmoothRange) {
			rotationChange *= deltaAbs / alignSmoothRange;
		}
		else if (180f - deltaAbs < alignSmoothRange) {
			rotationChange *= (180f - deltaAbs) / alignSmoothRange;
		}
		orbitAngles.y = Mathf.MoveTowardsAngle(orbitAngles.y, headingAngle, rotationChange);
		return true;
	}

	void ConstrainAngles () {
		orbitAngles.x =
			Mathf.Clamp(orbitAngles.x, minVerticalAngle, maxVerticalAngle);

		if (orbitAngles.y < 0f) {
			orbitAngles.y += 360f;
		}
		else if (orbitAngles.y >= 360f) {
			orbitAngles.y -= 360f;
		}
	}

	static float GetAngle (Vector2 direction) {
		float angle = Mathf.Acos(direction.y) * Mathf.Rad2Deg;
		return direction.x < 0f ? 360f - angle : angle;
	}
}
