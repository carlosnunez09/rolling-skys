using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(GravityCar))]
public class MovingCar : MonoBehaviour {

	// ── Engine ────────────────────────────────────────────────────────

	[BoxGroup("Engine"), SerializeField, Range(0f, 100f)]
	float maxSpeed = 20f;

	[BoxGroup("Engine"), SerializeField, Range(0f, 50f)]
	float maxReverseSpeed = 8f;

	[BoxGroup("Engine"), SerializeField, Range(0f, 500f)]
	float acceleration = 60f;

	[BoxGroup("Engine"), SerializeField, Range(0f, 500f)]
	float brakeForce = 120f;

	[BoxGroup("Engine"), SerializeField, Range(0f, 50f)]
	float coastDeceleration = 8f;

	// X = speed / maxSpeed (0–1), Y = torque multiplier (0–1).
	// High torque at low speed, tapers off toward max speed.
	[BoxGroup("Engine"), SerializeField, CurveRange(0f, 0f, 1f, 1f)]
	AnimationCurve torqueCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

	// ── Steering ──────────────────────────────────────────────────────

	[BoxGroup("Steering"), SerializeField, Range(1f, 30f)]
	float minTurningRadius = 6f;

	// ── Grip & Drift ──────────────────────────────────────────────────

	// Fraction of lateral velocity cancelled per frame (0 = ice, 1 = locked)
	[BoxGroup("Grip & Drift"), SerializeField, Range(0f, 1f)]
	float lateralGrip = 0.85f;

	// Same scale but much lower — real sliding during drift
	[BoxGroup("Grip & Drift"), SerializeField, Range(0f, 0.2f)]
	float driftGrip = 0.02f;

	// How much faster the car rotates while drifting (oversteer boost)
	[BoxGroup("Grip & Drift"), SerializeField, Range(1f, 4f)]
	float driftYawMultiplier = 1.8f;

	// ── Landing Slip ──────────────────────────────────────────────────

	[BoxGroup("Landing Slip"), SerializeField, Range(0f, 720f)]
	float maxSlipYawRate = 180f;

	[BoxGroup("Landing Slip"), SerializeField, Range(0f, 10f)]
	float slipRecoveryRate = 2f;

	// ── Jump ──────────────────────────────────────────────────────────

	[BoxGroup("Jump"), SerializeField, Range(0f, 10f)]
	float jumpHeight = 2f;

	// ── Ground Detection ──────────────────────────────────────────────

	[BoxGroup("Ground"), SerializeField, Range(0f, 90f)]
	float maxGroundAngle = 40f;

	[BoxGroup("Ground"), SerializeField, Range(0f, 100f)]
	float maxSnapSpeed = 50f;

	[BoxGroup("Ground"), SerializeField, Min(0f)]
	float probeDistance = 1f;

	[BoxGroup("Ground"), SerializeField]
	LayerMask probeMask = -1;

	// ── Runtime Stats (read-only) ─────────────────────────────────────

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Speed  m/s")]
	float statSpeed;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Forward Speed  m/s")]
	float statForwardSpeed;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Lateral Speed  m/s")]
	float statLateralSpeed;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Acceleration  m/s²")]
	float statAcceleration;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Yaw Rate  °/s")]
	float statYawRate;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Yaw Acceleration  °/s²")]
	float statYawAcceleration;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Grounded")]
	bool statGrounded;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Drifting")]
	bool statDrifting;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Landing Slip  0–1")]
	float statLandingSlip;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Ground Angle  °")]
	float statGroundAngle;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Gravity  m/s²")]
	float statGravityMagnitude;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Gravity Up Axis")]
	Vector3 statGravityUpAxis;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Active Gravity Source")]
	string statGravitySource;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Rigidbody Below")]
	bool statRigidbodyBelow;

	// ── Public stat accessors for HUD ────────────────────────────────
	public float Speed           => statSpeed;
	public float ForwardSpeed    => statForwardSpeed;
	public float LateralSpeed    => statLateralSpeed;
	public float Acceleration    => statAcceleration;
	public float YawRate         => statYawRate;
	public float YawAcceleration => statYawAcceleration;
	public bool  IsGrounded      => statGrounded;
	public bool  IsDrifting      => statDrifting;
	public float LandingSlip     => statLandingSlip;
	public float GroundAngle     => statGroundAngle;
	public float GravityStrength => statGravityMagnitude;
	public string GravitySource  => statGravitySource;
	public bool  RigidbodyBelow  => statRigidbodyBelow;

	// ── Private State ─────────────────────────────────────────────────

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

	float prevSpeed;
	float prevYawVelocity;

	bool OnGround => groundContactCount > 0;

	InputAction moveAction, jumpAction, driftAction;

	void OnValidate () {
		minGroundDot = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
	}

	void Awake () {
		body = GetComponent<Rigidbody>();
		gravityCar = GetComponent<GravityCar>();
		OnValidate();
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
		Vector3 upAxis  = gravityCar.UpAxis;

		stepsSinceLastGrounded += 1;
		stepsSinceLastJump     += 1;
		velocity = body.linearVelocity;

		UpdateState(upAxis);

		// Landing — spike slip proportional to spin speed at impact
		if (!wasGrounded && OnGround) {
			landingYawVelocity = yawVelocity;
			landingSlip = Mathf.Clamp01(Mathf.Abs(yawVelocity) / maxSlipYawRate);
		}
		wasGrounded = OnGround;
		landingSlip = Mathf.MoveTowards(landingSlip, 0f, slipRecoveryRate * Time.fixedDeltaTime);

		Vector2 input    = moveAction.ReadValue<Vector2>();
		float   throttle = input.y;
		float   steer    = input.x;
		bool    isDrifting = driftAction.IsPressed();

		Quaternion rotation = ComputeRotation();
		Vector3 forward = rotation * Vector3.forward;
		Vector3 right   = rotation * Vector3.right;
		float fwdSpeed  = Vector3.Dot(velocity, forward);

		if (OnGround) {
			float absSpeed    = Mathf.Abs(fwdSpeed);
			float reverseSign = fwdSpeed >= 0f ? 1f : -1f;

			// Drift oversteer: car rotates faster than velocity follows
			float yawBoost    = isDrifting ? driftYawMultiplier : 1f;
			float steeringYaw = steer * (absSpeed / minTurningRadius) * Mathf.Rad2Deg * reverseSign * yawBoost;

			// Blend air-spin momentum into steering during landing slip
			yawVelocity = Mathf.Lerp(steeringYaw, landingYawVelocity, landingSlip);
			yaw += yawVelocity * Time.fixedDeltaTime;

			rotation = ComputeRotation();
			forward  = rotation * Vector3.forward;
			right    = rotation * Vector3.right;

			// Non-linear engine: torqueCurve maps speed ratio → force multiplier
			if (throttle > 0f) {
				float speedRatio       = Mathf.Clamp01(fwdSpeed / maxSpeed);
				float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
				body.AddForce(forward * (throttle * acceleration * torqueMultiplier), ForceMode.Acceleration);
			} else if (throttle < 0f && fwdSpeed > 0f) {
				// Blend smoothly from full brake (at speed) down to reverse torque (near zero)
				float brakeBlend      = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fwdSpeed / 2f));
				float speedRatio      = Mathf.Clamp01(-fwdSpeed / maxReverseSpeed);
				float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
				float brakeF          = -brakeForce * brakeBlend;
				float reverseF        = throttle * acceleration * torqueMultiplier * (1f - brakeBlend);
				body.AddForce(forward * (brakeF + reverseF), ForceMode.Acceleration);
			} else if (throttle < 0f) {
				float speedRatio      = Mathf.Clamp01(-fwdSpeed / maxReverseSpeed);
				float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
				body.AddForce(forward * (throttle * acceleration * torqueMultiplier), ForceMode.Acceleration);
			} else {
				body.AddForce(forward * (-fwdSpeed * coastDeceleration), ForceMode.Acceleration);
			}

			// Lateral grip — low during drift, reduced further by landing slip
			float baseGrip      = isDrifting ? driftGrip : lateralGrip;
			float effectiveGrip = baseGrip * (1f - landingSlip);
			float lateralSpeed  = Vector3.Dot(velocity, right);
			body.AddForce(-right * lateralSpeed * effectiveGrip, ForceMode.VelocityChange);

			body.MoveRotation(rotation);
		} else if (yawVelocity != 0f) {
			yaw += yawVelocity * Time.fixedDeltaTime;
			body.MoveRotation(gravityCar.GravityAlignment * Quaternion.AngleAxis(yaw, Vector3.up));
		}

		// Hop
		if (desiredJump && OnGround) {
			desiredJump    = false;
			stepsSinceLastJump = 0;
			float jumpSpeed    = Mathf.Sqrt(2f * gravity.magnitude * jumpHeight);
			float alignedSpeed = Vector3.Dot(velocity, upAxis);
			if (alignedSpeed > 0f) jumpSpeed = Mathf.Max(jumpSpeed - alignedSpeed, 0f);
			body.linearVelocity += upAxis * jumpSpeed;
		}

		UpdateStats(forward, right, gravity, isDrifting);
		ClearState();
	}

	void UpdateStats (Vector3 forward, Vector3 right, Vector3 gravity, bool isDrifting) {
		float currentSpeed = velocity.magnitude;

		statAcceleration    = (currentSpeed - prevSpeed) / Time.fixedDeltaTime;
		statYawAcceleration = (yawVelocity - prevYawVelocity) / Time.fixedDeltaTime;
		prevSpeed           = currentSpeed;
		prevYawVelocity     = yawVelocity;

		// Snap to zero below 0.5 m/s so physics solver noise doesn't show as movement
		const float displayThreshold = 0.5f;
		statSpeed        = currentSpeed < displayThreshold ? 0f : currentSpeed;
		statForwardSpeed = Mathf.Abs(Vector3.Dot(velocity, forward)) < displayThreshold ? 0f : Vector3.Dot(velocity, forward);
		statLateralSpeed = Mathf.Abs(Vector3.Dot(velocity, right))   < displayThreshold ? 0f : Vector3.Dot(velocity, right);
		statYawRate        = yawVelocity;
		statGrounded       = OnGround;
		statDrifting       = isDrifting && OnGround;
		statLandingSlip    = landingSlip;
		statGroundAngle    = OnGround ? Vector3.Angle(gravityCar.UpAxis, contactNormal) : 0f;
		statGravityMagnitude = gravity.magnitude;
		statGravityUpAxis  = gravityCar.UpAxis;
		statGravitySource  = CustomGravity.GetDominantSourceName(body.position);

		// Check if there is a Rigidbody directly beneath the car
		statRigidbodyBelow = Physics.Raycast(body.position, -gravityCar.UpAxis, out RaycastHit hit, probeDistance * 2f)
			&& hit.rigidbody != null;
	}

	// On flat ground: gravity alignment + yaw.
	// On a ramp: tilts to match the contact normal.
	Quaternion ComputeRotation () {
		Quaternion heading = gravityCar.GravityAlignment * Quaternion.AngleAxis(yaw, Vector3.up);
		if (OnGround && contactNormal.sqrMagnitude > 0f) {
			Vector3 surfaceForward = Vector3.ProjectOnPlane(heading * Vector3.forward, contactNormal);
			if (surfaceForward.sqrMagnitude > 0.001f)
				return Quaternion.LookRotation(surfaceForward.normalized, contactNormal.normalized);
		}
		return heading;
	}

	void UpdateState (Vector3 upAxis) {
		if (OnGround || SnapToGround(upAxis)) {
			stepsSinceLastGrounded = 0;
			if (groundContactCount > 1) contactNormal.Normalize();
		} else {
			contactNormal = upAxis;
		}
	}

	void ClearState () {
		groundContactCount = 0;
		contactNormal      = Vector3.zero;
	}

	bool SnapToGround (Vector3 upAxis) {
		if (stepsSinceLastGrounded > 4 || stepsSinceLastJump <= 2) return false;
		if (velocity.magnitude > maxSnapSpeed) return false;
		if (!Physics.Raycast(body.position, -upAxis, out RaycastHit hit, probeDistance, probeMask)) return false;
		if (Vector3.Dot(upAxis, hit.normal) < minGroundDot) return false;

		groundContactCount = 1;
		contactNormal      = hit.normal;
		float dot = Vector3.Dot(velocity, hit.normal);
		if (dot > 0f) body.linearVelocity = velocity - hit.normal * dot;
		return true;
	}

	void OnCollisionEnter (Collision collision) => EvaluateCollision(collision);
	void OnCollisionStay  (Collision collision) => EvaluateCollision(collision);

	void EvaluateCollision (Collision collision) {
		for (int i = 0; i < collision.contactCount; i++) {
			Vector3 normal = collision.GetContact(i).normal;
			if (Vector3.Dot(gravityCar.UpAxis, normal) >= minGroundDot) {
				groundContactCount += 1;
				contactNormal      += normal;
			}
		}
	}
}
