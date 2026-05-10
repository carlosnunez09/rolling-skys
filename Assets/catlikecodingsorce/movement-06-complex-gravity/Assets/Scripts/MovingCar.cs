using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(GravityCar))]
public class MovingCar : MonoBehaviour {

	[SerializeField, Range(0f, 100f)]
	float maxSpeed = 20f;

	[SerializeField, Range(0f, 50f)]
	float maxReverseSpeed = 8f;

	// Force applied per second while accelerating
	[SerializeField, Range(0f, 200f)]
	float acceleration = 30f;

	// Max deceleration force when braking (S while moving forward)
	[SerializeField, Range(0f, 200f)]
	float brakeForce = 80f;

	// Gentle drag when coasting (no throttle)
	[SerializeField, Range(0f, 50f)]
	float coastDeceleration = 8f;

	// Tightest circle the car can make at full steer (meters). Smaller = sharper turns.
	[SerializeField, Range(1f, 30f)]
	float minTurningRadius = 6f;

	// 0 = ice, 1 = full grip — fraction of lateral velocity cancelled per frame
	[SerializeField, Range(0f, 1f)]
	float lateralGrip = 0.85f;

	// Grip while drift button is held — keep low for a loose slide
	[SerializeField, Range(0f, 1f)]
	float driftGrip = 0.1f;

	// Yaw rate (deg/s) at landing that causes maximum grip slip
	[SerializeField, Range(0f, 720f)]
	float maxSlipYawRate = 180f;

	// How quickly full grip recovers after a slip landing (slip units per second)
	[SerializeField, Range(0f, 10f)]
	float slipRecoveryRate = 2f;

	[SerializeField, Range(0f, 10f)]
	float jumpHeight = 2f;

	[SerializeField, Range(0, 90)]
	float maxGroundAngle = 40f;

	[SerializeField, Range(0f, 100f)]
	float maxSnapSpeed = 50f;

	[SerializeField, Min(0f)]
	float probeDistance = 1f;

	[SerializeField]
	LayerMask probeMask = -1;

	Rigidbody body;
	GravityCar gravityCar;

	float yaw;
	float yawVelocity;
	float landingYawVelocity;
	float landingSlip;
	bool wasGrounded;
	float minGroundDot;
	Vector3 velocity;
	Vector3 contactNormal;
	int groundContactCount;
	int stepsSinceLastGrounded, stepsSinceLastJump;
	bool desiredJump;

	bool OnGround => groundContactCount > 0;

	InputAction moveAction, jumpAction, driftAction;

	void OnValidate () {
		minGroundDot = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
	}

	void Awake () {
		body = GetComponent<Rigidbody>();
		gravityCar = GetComponent<GravityCar>();
		OnValidate();

		// Extract initial yaw so the car keeps its scene-set facing direction
		yaw = transform.eulerAngles.y;

		moveAction = new InputAction("CarMove", InputActionType.Value);
		moveAction.AddCompositeBinding("2DVector")
			.With("Up",    "<Keyboard>/w")
			.With("Down",  "<Keyboard>/s")
			.With("Left",  "<Keyboard>/a")
			.With("Right", "<Keyboard>/d");
		moveAction.AddBinding("<Gamepad>/leftStick");

		jumpAction = new InputAction("CarJump", InputActionType.Button);
		jumpAction.AddBinding("<Keyboard>/space");
		jumpAction.AddBinding("<Gamepad>/buttonSouth");

		driftAction = new InputAction("CarDrift", InputActionType.Button);
		driftAction.AddBinding("<Keyboard>/leftShift");
		driftAction.AddBinding("<Gamepad>/leftShoulder");
	}

	void OnEnable () {
		moveAction.Enable();
		jumpAction.Enable();
		driftAction.Enable();
	}

	void OnDisable () {
		moveAction.Disable();
		jumpAction.Disable();
		driftAction.Disable();
	}

	void Update () {
		desiredJump |= jumpAction.WasPressedThisFrame();
	}

	void FixedUpdate () {
		Vector3 gravity = gravityCar.UpdateAndApplyGravity();
		Vector3 upAxis = gravityCar.UpAxis;

		stepsSinceLastGrounded += 1;
		stepsSinceLastJump += 1;
		velocity = body.linearVelocity;

		UpdateState(upAxis);

		// Landing detection — spike slip when touching down while spinning
		if (!wasGrounded && OnGround) {
			landingYawVelocity = yawVelocity;
			landingSlip = Mathf.Clamp01(Mathf.Abs(yawVelocity) / maxSlipYawRate);
		}
		wasGrounded = OnGround;
		landingSlip = Mathf.MoveTowards(landingSlip, 0f, slipRecoveryRate * Time.fixedDeltaTime);

		Vector2 input = moveAction.ReadValue<Vector2>();
		float throttle = input.y;
		float steer = input.x;

		Quaternion rotation = ComputeRotation();
		Vector3 forward = rotation * Vector3.forward;
		Vector3 right   = rotation * Vector3.right;
		float fwdSpeed  = Vector3.Dot(velocity, forward);

		if (OnGround) {
			// Turning radius model: yaw rate = steer * speed / radius
			float absSpeed    = Mathf.Abs(fwdSpeed);
			float reverseSign = fwdSpeed >= 0f ? 1f : -1f;
			float steeringYaw = steer * (absSpeed / minTurningRadius) * Mathf.Rad2Deg * reverseSign;

			// Blend between air spin and steering during landing slip
			yawVelocity = Mathf.Lerp(steeringYaw, landingYawVelocity, landingSlip);
			yaw += yawVelocity * Time.fixedDeltaTime;

			// Recompute after steering update — now includes ramp tilt
			rotation = ComputeRotation();
			forward  = rotation * Vector3.forward;
			right    = rotation * Vector3.right;

			// Engine / braking
			if (throttle > 0f) {
				float speedError = throttle * maxSpeed - fwdSpeed;
				body.AddForce(forward * Mathf.Clamp(speedError * acceleration, 0f, acceleration), ForceMode.Acceleration);
			} else if (throttle < 0f && fwdSpeed > 0.3f) {
				body.AddForce(-forward * brakeForce, ForceMode.Acceleration);
			} else if (throttle < 0f) {
				float speedError = throttle * maxReverseSpeed - fwdSpeed;
				body.AddForce(forward * Mathf.Clamp(speedError * acceleration, -acceleration, 0f), ForceMode.Acceleration);
			} else {
				body.AddForce(-forward * fwdSpeed * coastDeceleration, ForceMode.Acceleration);
			}

			// Lateral grip — reduced during drift or landing slip
			float baseGrip = driftAction.IsPressed() ? driftGrip : lateralGrip;
			float effectiveGrip = baseGrip * (1f - landingSlip);
			float lateralSpeed = Vector3.Dot(velocity, right);
			body.AddForce(-right * lateralSpeed * effectiveGrip, ForceMode.VelocityChange);

			body.MoveRotation(rotation);
		} else if (yawVelocity != 0f) {
			// In air — carry rotational momentum from the last ground contact
			yaw += yawVelocity * Time.fixedDeltaTime;
			body.MoveRotation(gravityCar.GravityAlignment * Quaternion.AngleAxis(yaw, Vector3.up));
		}

		// Hop
		if (desiredJump && OnGround) {
			desiredJump = false;
			stepsSinceLastJump = 0;
			float jumpSpeed = Mathf.Sqrt(2f * gravity.magnitude * jumpHeight);
			float alignedSpeed = Vector3.Dot(velocity, upAxis);
			if (alignedSpeed > 0f) {
				jumpSpeed = Mathf.Max(jumpSpeed - alignedSpeed, 0f);
			}
			body.linearVelocity += upAxis * jumpSpeed;
		}

		ClearState();
	}

	// On flat ground: same as gravity alignment + yaw.
	// On a ramp: tilts the car to match the surface normal.
	Quaternion ComputeRotation () {
		Quaternion heading = gravityCar.GravityAlignment * Quaternion.AngleAxis(yaw, Vector3.up);
		if (OnGround && contactNormal.sqrMagnitude > 0f) {
			Vector3 surfaceForward = Vector3.ProjectOnPlane(heading * Vector3.forward, contactNormal);
			if (surfaceForward.sqrMagnitude > 0.001f) {
				return Quaternion.LookRotation(surfaceForward.normalized, contactNormal.normalized);
			}
		}
		return heading;
	}

	void UpdateState (Vector3 upAxis) {
		if (OnGround || SnapToGround(upAxis)) {
			stepsSinceLastGrounded = 0;
			if (groundContactCount > 1) {
				contactNormal.Normalize();
			}
		} else {
			contactNormal = upAxis;
		}
	}

	void ClearState () {
		groundContactCount = 0;
		contactNormal = Vector3.zero;
	}

	bool SnapToGround (Vector3 upAxis) {
		if (stepsSinceLastGrounded > 1 || stepsSinceLastJump <= 2) {
			return false;
		}
		float speed = velocity.magnitude;
		if (speed > maxSnapSpeed) {
			return false;
		}
		if (!Physics.Raycast(body.position, -upAxis, out RaycastHit hit, probeDistance, probeMask)) {
			return false;
		}
		if (Vector3.Dot(upAxis, hit.normal) < minGroundDot) {
			return false;
		}
		groundContactCount = 1;
		contactNormal = hit.normal;
		float dot = Vector3.Dot(velocity, hit.normal);
		if (dot > 0f) {
			body.linearVelocity = velocity - hit.normal * dot;
		}
		return true;
	}

	void OnCollisionEnter (Collision collision) {
		EvaluateCollision(collision);
	}

	void OnCollisionStay (Collision collision) {
		EvaluateCollision(collision);
	}

	void EvaluateCollision (Collision collision) {
		for (int i = 0; i < collision.contactCount; i++) {
			Vector3 normal = collision.GetContact(i).normal;
			if (Vector3.Dot(gravityCar.UpAxis, normal) >= minGroundDot) {
				groundContactCount += 1;
				contactNormal += normal;
			}
		}
	}
}
