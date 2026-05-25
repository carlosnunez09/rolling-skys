using System.Collections.Generic;
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
	float coastDeceleration = 14f;

	// X = speed / maxSpeed (0–1), Y = torque multiplier (0–1).
	// High torque at low speed, tapers off toward max speed.
	[BoxGroup("Engine"), SerializeField, CurveRange(0f, 0f, 1f, 1f)]
	AnimationCurve torqueCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

	// Speed the car can still creep toward beyond maxSpeed — reached very slowly.
	[BoxGroup("Engine"), SerializeField, Range(0f, 200f), Label("Top Speed  m/s")]
	float topSpeed = 30f;

	// Peak force applied at the start of the overdrive zone (at maxSpeed).
	// Tapers linearly to zero at topSpeed so the approach feels natural.
	[BoxGroup("Engine"), SerializeField, Range(0f, 100f), Label("Overdrive Force")]
	float overdriveForce = 6f;

	// ── Steering ──────────────────────────────────────────────────────

	[BoxGroup("Steering"), SerializeField, Range(1f, 30f)]
	float minTurningRadius = 5.2f;

	// ── Grip & Drift ──────────────────────────────────────────────────

	// Fraction of lateral velocity cancelled per frame (0 = ice, 1 = locked)
	[BoxGroup("Grip & Drift"), SerializeField, Range(0f, 1f)]
	float lateralGrip = 0.93f;

	// Same scale but much lower — real sliding during drift
	[BoxGroup("Grip & Drift"), SerializeField, Range(0f, 0.2f)]
	float driftGrip = 0.02f;

	// How much faster the car rotates while drifting (oversteer boost)
	[BoxGroup("Grip & Drift"), SerializeField, Range(1f, 4f)]
	float driftYawMultiplier = 1.8f;

	// ── Skid Marks ────────────────────────────────────────────────────

	// Assign a transparent vertex-color material (URP Particles/Unlit, Surface=Transparent)
	[BoxGroup("Skid Marks"), SerializeField]
	Material skidMaterial;

	[BoxGroup("Skid Marks"), SerializeField, Range(0.05f, 0.5f)]
	float markWidth = 0.22f;

	[BoxGroup("Skid Marks"), SerializeField, Range(1f, 30f)]
	float fadeTime = 8f;

	// Half-distance between left and right rear wheels
	[BoxGroup("Skid Marks"), SerializeField, Range(0.1f, 2f), Label("Half Track Width")]
	float wheelSpread = 0.55f;

	// Down-offset from pivot to rear axle (tune to match your model)
	[BoxGroup("Skid Marks"), SerializeField, Range(-2f, 0f), Label("Axle Height Offset")]
	float axleHeightOffset = -0.35f;

	[BoxGroup("Skid Marks"), SerializeField, Range(0.02f, 1f), Label("Min Segment Length  m")]
	float minSegmentLength = 0.12f;

	[BoxGroup("Skid Marks"), SerializeField, Range(0f, 5f), Label("Min Lateral Speed  m/s")]
	float minSkidLateralSpeed = 0.6f;

	[BoxGroup("Skid Marks"), SerializeField, Min(64), Label("Max Trail Points")]
	int maxSkidPoints = 512;

	[BoxGroup("Skid Marks"), SerializeField, Range(0f, 0.05f), Label("Surface Lift  m")]
	float skidGroundOffset = 0.018f;

	// ── Landing Slip ──────────────────────────────────────────────────

	[BoxGroup("Landing Slip"), SerializeField, Range(0f, 720f)]
	float maxSlipYawRate = 180f;

	[BoxGroup("Landing Slip"), SerializeField, Range(0f, 10f)]
	float slipRecoveryRate = 3.5f;

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
	public float GroundFriction  => _groundFriction;

	/// Normalised speed ratio [0–1] — matches the X axis of the torque curve.
	public float SpeedRatio => maxSpeed > 0f ? Mathf.Clamp01(statSpeed / maxSpeed) : 0f;

	/// Current torque multiplier sampled from the curve [0–1].
	public float TorqueRatio => torqueCurve.Evaluate(SpeedRatio);

	/// Simulated RPM: maps SpeedRatio to a 0–8000 RPM scale for the dial.
	public float RPM => SpeedRatio * 8000f;

	/// Max speed setting — needed by the HUD to scale the speedometer dial.
	public float MaxSpeed => maxSpeed;

	/// The torque AnimationCurve — exposed so the HUD graph can sample it at N points.
	public AnimationCurve TorqueCurve => torqueCurve;

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

	// Smoothed values used only for inspector display — decoupled from gameplay physics.
	float _smoothSpeed;
	float _smoothFwdSpeed;
	float _smoothLatSpeed;
	float _smoothYawRate;
	float _smoothAccel;

	// Surface friction — accumulated from SurfaceFriction components on contact objects.
	// 1 = normal grip, 0 = frictionless ice.  Reset each physics step in ClearState.
	float _groundFriction      = 1f;
	float _groundFrictionSum   = 0f;
	int   _groundFrictionCount = 0;

	// ── Skid Mark State ───────────────────────────────────────────────

	struct TrailPoint {
		public Vector3 left, right;
		public float   time;
		public bool    breakBefore; // true = start a new disconnected strip
	}

	readonly List<TrailPoint>[] skidTrails   = { new List<TrailPoint>(), new List<TrailPoint>() };
	readonly bool[]   skidTrailActive        = { false, false };
	readonly Vector3?[] skidLastPos          = { null, null };
	Mesh  skidMesh;
	bool  skidDirty;

	// Runtime-created objects that need explicit cleanup in OnDestroy.
	GameObject _skidGo;
	Material   _skidFallbackMat;

	// Cached mesh arrays — reused each rebuild to avoid per-frame GC.
	Vector3[] _skidVerts;
	Color[]   _skidColors;
	Vector2[] _skidUVs;
	int[]     _skidTris;
	int       _skidCachedQuads = -1;
	// Pre-allocated list for alpha-only colour updates (no topology change).
	readonly List<Color> _skidAlphaBuffer = new List<Color>();

	bool OnGround => groundContactCount > 0;

	InputAction moveAction, jumpAction, driftAction;

	void OnValidate () {
		minGroundDot = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
	}

	void Awake () {
		body = GetComponent<Rigidbody>();
		gravityCar = GetComponent<GravityCar>();
		OnValidate();
		yaw      = transform.eulerAngles.y;
		// Initialise so the first acceleration reading is 0, not a spike from
		// (currentSpeed - 0) / fixedDeltaTime on the very first FixedUpdate.
		prevSpeed = 0f;

		// World-space child that holds the skid mark mesh (stays put as the car moves)
		_skidGo = new GameObject("SkidMarks");
		_skidGo.transform.SetParent(null);
		skidMesh = new Mesh { name = "SkidMarks" };
		var mf = _skidGo.AddComponent<MeshFilter>();
		var mr = _skidGo.AddComponent<MeshRenderer>();
		mf.mesh = skidMesh;
		if (skidMaterial != null) {
			mr.material = skidMaterial;
		} else {
			_skidFallbackMat = CreateFallbackSkidMaterial();
			mr.material      = _skidFallbackMat;
		}
		mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		mr.receiveShadows    = false;
		mr.lightProbeUsage   = UnityEngine.Rendering.LightProbeUsage.Off;

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

	// When false the car physics still runs but all input is zeroed out.
	bool _inputEnabled = true;

	// Scripted boost-pad trajectory — input off and driving forces skipped.
	bool _trajectoryLocked;
	bool _inputEnabledBeforeTrajectory;

	/// <summary>True while a BoostPad is driving this car along a fixed arc.</summary>
	public bool IsTrajectoryLocked => _trajectoryLocked;

	/// <summary>
	/// Lock or unlock scripted-trajectory mode (used by BoostPad).
	/// Restores the prior input-enabled state when unlocking.
	/// </summary>
	public void SetTrajectoryLocked (bool locked) {
		if (_trajectoryLocked == locked) return;
		_trajectoryLocked = locked;
		if (locked) {
			_inputEnabledBeforeTrajectory = _inputEnabled;
			SetInputEnabled(false);
		} else {
			SyncYawFromTransform();
			SetInputEnabled(_inputEnabledBeforeTrajectory);
		}
	}

	void SyncYawFromTransform () {
		Quaternion rel = Quaternion.Inverse(gravityCar.GravityAlignment) * transform.rotation;
		yaw = rel.eulerAngles.y;
		if (yaw > 180f) yaw -= 360f;
		yawVelocity = 0f;
	}

	/// <summary>
	/// Enable or disable player input without disabling the component.
	/// Physics, gravity, and stats continue to update regardless.
	/// </summary>
	public void SetInputEnabled (bool on) {
		_inputEnabled = on;
		if (on) {
			moveAction.Enable();
			jumpAction.Enable();
			driftAction.Enable();
		} else {
			moveAction.Disable();
			jumpAction.Disable();
			driftAction.Disable();
			// Clear any queued jump so a press before lockout doesn't fire
			// the moment input is re-enabled (e.g. race countdown locking input).
			desiredJump = false;
		}
	}

	void OnEnable () {
		if (_inputEnabled) {
			moveAction.Enable();
			jumpAction.Enable();
			driftAction.Enable();
		}
	}

	void OnDisable () {
		moveAction.Disable();
		jumpAction.Disable();
		driftAction.Disable();
	}

	void OnDestroy () {
		if (_skidGo != null)       Destroy(_skidGo);
		if (skidMesh != null)      Destroy(skidMesh);
		if (_skidFallbackMat != null) Destroy(_skidFallbackMat);
		moveAction?.Dispose();
		jumpAction?.Dispose();
		driftAction?.Dispose();
	}

	void Update () {
		desiredJump |= jumpAction.WasPressedThisFrame();
		UpdateSkidAlpha();
	}

	void FixedUpdate () {
		Vector3 gravity;
		Vector3 upAxis;

		if (_trajectoryLocked) {
			// BoostPad drives position/velocity; only refresh stats and gravity alignment.
			gravity = gravityCar.RefreshGravityState();
			upAxis  = gravityCar.UpAxis;
			velocity = body.linearVelocity;
			UpdateState(upAxis);
			var rot = body.rotation;
			Vector3 trajForward = rot * Vector3.forward;
			Vector3 trajRight   = rot * Vector3.right;
			float fwd = Vector3.Dot(velocity, trajForward);
			float lat = Vector3.Dot(velocity, trajRight);
			UpdateStats(gravity, false, fwd, lat, velocity.magnitude);
			ClearState();
			return;
		}

		gravity = gravityCar.UpdateAndApplyGravity();
		upAxis  = gravityCar.UpAxis;

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
		float fwdSpeed     = Vector3.Dot(velocity, forward);
		float lateralSpeed = 0f;

		if (OnGround) {
			float absSpeed    = Mathf.Abs(fwdSpeed);
			float reverseSign = fwdSpeed >= 0f ? 1f : -1f;

			// ── Steering ──────────────────────────────────────────────────────
			float yawBoost = isDrifting ? driftYawMultiplier : 1f;

			// [OLD] float steeringYaw = steer * (absSpeed / minTurningRadius) * Mathf.Rad2Deg * reverseSign * yawBoost;
			// Problem: yawBoost applied with no ceiling. At high speed + drift the result
			// can exceed the car's natural turning rate, causing uncontrolled spinning.
			// Fix: compute the raw yaw rate first (correct physics: ω = v/r → deg/s),
			// then clamp the final result to the natural maximum yaw at maxSpeed so
			// drift oversteer feels aggressive but never breaks control entirely.
			float rawSteeringYaw = steer * (absSpeed / minTurningRadius) * Mathf.Rad2Deg * reverseSign;
			float yawRateCap     = (maxSpeed / minTurningRadius) * Mathf.Rad2Deg;
			float steeringYaw    = Mathf.Clamp(rawSteeringYaw * yawBoost, -yawRateCap, yawRateCap);

			// Blend air-spin momentum into steering during landing slip
			yawVelocity = Mathf.Lerp(steeringYaw, landingYawVelocity, landingSlip);
			yaw += yawVelocity * Time.fixedDeltaTime;

			rotation = ComputeRotation();
			forward  = rotation * Vector3.forward;
			right    = rotation * Vector3.right;

			// ── Engine ────────────────────────────────────────────────────────
			if (throttle > 0f) {
				if (fwdSpeed < maxSpeed) {
					// Normal torque-curve zone
					float speedRatio       = Mathf.Clamp01(fwdSpeed / maxSpeed);
					float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
					body.AddForce(forward * (throttle * acceleration * torqueMultiplier), ForceMode.Acceleration);

				// [OLD] } else if (fwdSpeed < topSpeed) {
				// Problem: overdrive fired even while drifting, making a drift at maxSpeed
				// push the car further above the normal speed limit — drift became a free
				// speed exploit. Disable the overdrive zone entirely during drift.
				} else if (fwdSpeed < topSpeed && !isDrifting) {
					float overdriveRatio = Mathf.Clamp01((fwdSpeed - maxSpeed) / Mathf.Max(topSpeed - maxSpeed, 0.01f));
					float force          = Mathf.Lerp(overdriveForce, 0f, overdriveRatio);
					body.AddForce(forward * (throttle * force), ForceMode.Acceleration);
				}
				// At or above topSpeed (or drifting at/above maxSpeed): no further push.

			} else if (throttle < 0f && fwdSpeed > 0f) {
				// Blend smoothly from full brake (at speed) down to reverse torque (near zero).
				// Scale brake force by surface friction — slippery surfaces have less bite.
				float brakeBlend       = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fwdSpeed / 2f));
				float speedRatio       = Mathf.Clamp01(fwdSpeed / maxSpeed);
				float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
				float brakeF           = -brakeForce * brakeBlend * _groundFriction;
				float reverseF         = throttle * acceleration * torqueMultiplier * (1f - brakeBlend);
				body.AddForce(forward * (brakeF + reverseF), ForceMode.Acceleration);

			} else if (throttle < 0f) {
				// Only push backward if we haven't already reached maxReverseSpeed.
				// The torque curve alone can't guarantee this because it may still
				// have a non-zero value at speedRatio = 1.
				if (fwdSpeed > -maxReverseSpeed) {
					float speedRatio       = Mathf.Clamp01(-fwdSpeed / maxReverseSpeed);
					float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
					body.AddForce(forward * (throttle * acceleration * torqueMultiplier), ForceMode.Acceleration);
				}

			} else {
				// Coasting — decelerate toward zero.
				// [OLD] body.AddForce(forward * (-fwdSpeed * coastDeceleration), ForceMode.Acceleration);
				// Problem: force scales with speed. At fwdSpeed=20 with coastDeceleration=8
				// this produces −160 m/s², which is stronger than intentional braking (brakeForce=120).
				// High-speed coasting would nearly stop the car in a single frame.
				// Fix: cap the coast force to a fraction of brakeForce so coasting is always
				// gentler than pressing the brake pedal.
				// Scale coasting drag by surface friction — on ice the car barely slows.
				float rawCoastForce  = -fwdSpeed * coastDeceleration * _groundFriction;
				float coastForceCap  = brakeForce * 0.25f * _groundFriction;
				body.AddForce(forward * Mathf.Clamp(rawCoastForce, -coastForceCap, coastForceCap), ForceMode.Acceleration);
			}

			// ── Lateral grip ──────────────────────────────────────────────────
			// [OLD] float baseGrip      = isDrifting ? driftGrip : lateralGrip;
			// [OLD] float effectiveGrip = baseGrip * (1f - landingSlip);
			// Problem: when drifting AND landing at the same time, effectiveGrip → 0,
			// leaving the car with zero lateral correction and no ability to recover.
			// Fix: maintain a minimum grip floor so the car never loses all lateral
			// stability, even in the worst combined scenario.
			float baseGrip      = isDrifting ? driftGrip : lateralGrip;
			// Surface friction scales grip down — 1=normal road, near 0=ice.
			// Keep a very small floor so the car can still be steered off a slippery surface.
			float effectiveGrip = Mathf.Max(
				baseGrip * (1f - landingSlip * 0.75f) * _groundFriction,
				0.01f);

			lateralSpeed = Vector3.Dot(velocity, right);
			body.AddForce(-right * lateralSpeed * effectiveGrip, ForceMode.VelocityChange);

			// ── Drift speed cap ───────────────────────────────────────────────
			// Cap the surface-plane component only — not total 3D velocity.
			// Clamping the full magnitude would remove valid vertical momentum on
			// ramps and curved surfaces, fighting slope-following gravity.
			if (isDrifting) {
				Vector3 surfaceVel   = Vector3.ProjectOnPlane(body.linearVelocity, upAxis);
				float   surfaceSpeed = surfaceVel.magnitude;
				if (surfaceSpeed > maxSpeed) {
					// Subtract only the in-plane excess; vertical velocity is untouched.
					body.AddForce(-surfaceVel.normalized * (surfaceSpeed - maxSpeed), ForceMode.VelocityChange);
				}
			}

			body.MoveRotation(rotation);
		} else {
			if (yawVelocity != 0f) yaw += yawVelocity * Time.fixedDeltaTime;
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

		// Re-read velocity AFTER all physics ops (grip, drift cap, snap, jump have
		// all modified the actual rigidbody velocity since the cached snapshot).
		Vector3 postVel     = body.linearVelocity;
		float finalFwdSpeed = Vector3.Dot(postVel, forward);
		float finalLatSpeed = Vector3.Dot(postVel, right);
		UpdateStats(gravity, isDrifting, finalFwdSpeed, finalLatSpeed, postVel.magnitude);
		UpdateSkidMarks(upAxis, isDrifting, finalLatSpeed);
		ClearState();
	}

	void UpdateStats (Vector3 gravity, bool isDrifting, float fwdSpeed, float latSpeed, float currentSpeed) {
		float rawAccel      = (currentSpeed - prevSpeed) / Time.fixedDeltaTime;
		statYawAcceleration = (yawVelocity - prevYawVelocity) / Time.fixedDeltaTime;
		prevSpeed           = currentSpeed;
		prevYawVelocity     = yawVelocity;

		// EMA smoothing for inspector display — prevents the values from flickering when
		// the object is selected or Gizmos are on (which forces constant Inspector repaints).
		// Gameplay physics uses local fwdSpeed/velocity directly, not these stat fields.
		const float displayThreshold = 0.5f;
		float       a                = 15f * Time.fixedDeltaTime; // ~67 ms time constant

		_smoothAccel    = Mathf.Lerp(_smoothAccel,    Mathf.Clamp(rawAccel, -200f, 200f), a);
		_smoothSpeed    = Mathf.Lerp(_smoothSpeed,    currentSpeed, a);
		_smoothFwdSpeed = Mathf.Lerp(_smoothFwdSpeed, fwdSpeed,     a);
		_smoothLatSpeed = Mathf.Lerp(_smoothLatSpeed, latSpeed,     a);
		_smoothYawRate  = Mathf.Lerp(_smoothYawRate,  yawVelocity,  a);

		statAcceleration = _smoothAccel;
		statSpeed        = _smoothSpeed    < displayThreshold               ? 0f : _smoothSpeed;
		statForwardSpeed = Mathf.Abs(_smoothFwdSpeed) < displayThreshold    ? 0f : _smoothFwdSpeed;
		statLateralSpeed = Mathf.Abs(_smoothLatSpeed) < displayThreshold    ? 0f : _smoothLatSpeed;
		statYawRate      = _smoothYawRate;
		statGrounded     = OnGround;
		statDrifting     = isDrifting && OnGround;
		statLandingSlip  = landingSlip;
		statGroundAngle  = OnGround ? Vector3.Angle(gravityCar.UpAxis, contactNormal) : 0f;
		statGravityMagnitude = gravity.magnitude;
		statGravityUpAxis    = gravityCar.UpAxis;
		statGravitySource    = CustomGravity.GetDominantSourceName(body.position);

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
			// Average friction across all ground contacts this step.
			_groundFriction = _groundFrictionCount > 0
				? _groundFrictionSum / _groundFrictionCount
				: 1f;
		} else {
			contactNormal   = upAxis;
			_groundFriction = 1f;
		}
	}

	void ClearState () {
		groundContactCount    = 0;
		contactNormal         = Vector3.zero;
		_groundFrictionSum    = 0f;
		_groundFrictionCount  = 0;
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
		// Read surface friction once per colliding object (not per contact point).
		var sf = collision.gameObject.GetComponent<SurfaceFriction>();
		float friction = sf != null ? sf.Friction : 1f;

		for (int i = 0; i < collision.contactCount; i++) {
			Vector3 normal = collision.GetContact(i).normal;
			if (Vector3.Dot(gravityCar.UpAxis, normal) >= minGroundDot) {
				groundContactCount   += 1;
				contactNormal        += normal;
				_groundFrictionSum   += friction;
				_groundFrictionCount += 1;
			}
		}
	}

	// ── Skid Marks ────────────────────────────────────────────────────

	void UpdateSkidMarks (Vector3 upAxis, bool isDrifting, float lateralSpeed) {
		float now        = Time.time;
		bool  shouldMark = OnGround && isDrifting && Mathf.Abs(lateralSpeed) >= minSkidLateralSpeed;
		Vector3 carRight = transform.right;

		for (int w = 0; w < 2; w++) {
			// Expire old points from the front
			int remove = 0;
			while (remove < skidTrails[w].Count && now - skidTrails[w][remove].time > fadeTime)
				remove++;
			if (remove > 0) { skidTrails[w].RemoveRange(0, remove); skidDirty = true; }
		}

		for (int w = 0; w < 2; w++) {
			float   side  = w == 0 ? -1f : 1f;
			Vector3 probe = transform.position
			              + carRight * (side * wheelSpread)
			              + upAxis   * axleHeightOffset;

			if (!shouldMark) {
				if (skidTrailActive[w]) { skidTrailActive[w] = false; skidLastPos[w] = null; }
				continue;
			}

			// Cast from above the wheel position downward to find ground
			if (!Physics.Raycast(probe + upAxis, -upAxis, out RaycastHit hit, 2.5f, probeMask))
				continue;

			Vector3 p = hit.point + hit.normal * skidGroundOffset;

			if (skidLastPos[w].HasValue && Vector3.Distance(p, skidLastPos[w].Value) < minSegmentLength)
				continue;

			// Build left/right edge vertices aligned along travel direction
			Vector3 along  = skidLastPos[w].HasValue
			               ? (p - skidLastPos[w].Value).normalized
			               : transform.forward;
			Vector3 across = Vector3.Cross(along, hit.normal).normalized * markWidth;

			skidTrails[w].Add(new TrailPoint {
				left        = p - across,
				right       = p + across,
				time        = now,
				breakBefore = !skidTrailActive[w]
			});

			skidLastPos[w]     = p;
			skidTrailActive[w] = true;
			skidDirty          = true;

			if (skidTrails[w].Count > maxSkidPoints)
				skidTrails[w].RemoveRange(0, skidTrails[w].Count - maxSkidPoints);
		}

		if (skidDirty) { RebuildSkidMesh(now); skidDirty = false; }
	}

	// Smooth per-frame alpha fade — updates vertex colors without rebuilding topology.
	// Uses a pre-allocated List to avoid the per-call array allocation from mesh.colors.
	void UpdateSkidAlpha () {
		if (skidMesh == null || skidMesh.vertexCount == 0) return;
		float now = Time.time;
		_skidAlphaBuffer.Clear();
		skidMesh.GetColors(_skidAlphaBuffer);
		int vi = 0;

		for (int w = 0; w < 2; w++) {
			var trail = skidTrails[w];
			for (int i = 1; i < trail.Count; i++) {
				if (trail[i].breakBefore) continue;
				if (vi + 3 >= _skidAlphaBuffer.Count) break;
				float alpA = Mathf.Clamp01(1f - (now - trail[i - 1].time) / fadeTime);
				float alpB = Mathf.Clamp01(1f - (now - trail[i].time)     / fadeTime);
				Color c;
				c = _skidAlphaBuffer[vi];     c.a = alpA; _skidAlphaBuffer[vi]     = c;
				c = _skidAlphaBuffer[vi + 1]; c.a = alpA; _skidAlphaBuffer[vi + 1] = c;
				c = _skidAlphaBuffer[vi + 2]; c.a = alpB; _skidAlphaBuffer[vi + 2] = c;
				c = _skidAlphaBuffer[vi + 3]; c.a = alpB; _skidAlphaBuffer[vi + 3] = c;
				vi += 4;
			}
		}
		skidMesh.SetColors(_skidAlphaBuffer);
	}

	void RebuildSkidMesh (float now) {
		int quads = 0;
		for (int w = 0; w < 2; w++) {
			var trail = skidTrails[w];
			for (int i = 1; i < trail.Count; i++)
				if (!trail[i].breakBefore) quads++;
		}

		if (quads == 0) { skidMesh.Clear(); _skidCachedQuads = -1; return; }

		// Only reallocate when the quad count actually changes — avoids a GC alloc
		// every frame during a continuous drift where the trail length is stable.
		if (_skidCachedQuads != quads) {
			_skidVerts  = new Vector3[quads * 4];
			_skidColors = new Color  [quads * 4];
			_skidUVs    = new Vector2[quads * 4];
			_skidTris   = new int    [quads * 6];
			_skidCachedQuads = quads;
		}
		var verts  = _skidVerts;
		var colors = _skidColors;
		var uvs    = _skidUVs;
		var tris   = _skidTris;
		int vi = 0, ti = 0;

		for (int w = 0; w < 2; w++) {
			var trail = skidTrails[w];
			for (int i = 1; i < trail.Count; i++) {
				if (trail[i].breakBefore) continue;
				var   a  = trail[i - 1]; var   b  = trail[i];
				float aA = Mathf.Clamp01(1f - (now - a.time) / fadeTime);
				float aB = Mathf.Clamp01(1f - (now - b.time) / fadeTime);
				var   cA = new Color(0.04f, 0.04f, 0.04f, aA);
				var   cB = new Color(0.04f, 0.04f, 0.04f, aB);

				verts[vi]      = a.left;  verts[vi + 1] = a.right;
				verts[vi + 2]  = b.left;  verts[vi + 3] = b.right;
				colors[vi]     = cA;       colors[vi + 1] = cA;
				colors[vi + 2] = cB;       colors[vi + 3] = cB;
				uvs[vi]        = new Vector2(0, 0); uvs[vi + 1] = new Vector2(1, 0);
				uvs[vi + 2]    = new Vector2(0, 1); uvs[vi + 3] = new Vector2(1, 1);

				tris[ti]     = vi;     tris[ti + 1] = vi + 2; tris[ti + 2] = vi + 1;
				tris[ti + 3] = vi + 1; tris[ti + 4] = vi + 2; tris[ti + 5] = vi + 3;
				vi += 4; ti += 6;
			}
		}

		skidMesh.Clear();
		skidMesh.vertices  = verts;
		skidMesh.colors    = colors;
		skidMesh.uv        = uvs;
		skidMesh.triangles = tris;
	}

	static Material CreateFallbackSkidMaterial () {
		var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
		          ?? Shader.Find("Particles/Standard Unlit")
		          ?? Shader.Find("Unlit/Color");
		var mat = new Material(shader);
		mat.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
		if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1); // Transparent
		mat.renderQueue = 3000;
		return mat;
	}

	// ── Gizmos ────────────────────────────────────────────────────────────
#if UNITY_EDITOR
	void OnDrawGizmos () {
		// Small always-visible dot so you can spot the car in a busy scene.
		// Green = grounded, orange = airborne.
		Gizmos.color = statGrounded
			? new Color(0.2f, 1f, 0.3f, 0.55f)
			: new Color(1f, 0.55f, 0.1f, 0.55f);
		Gizmos.DrawSphere(transform.position, 0.18f);
	}

	void OnDrawGizmosSelected () {
		if (gravityCar == null) gravityCar = GetComponent<GravityCar>();
		var rb = GetComponent<Rigidbody>();
		if (rb == null || gravityCar == null) return;

		Vector3 pos     = transform.position;
		Vector3 up      = gravityCar.UpAxis.sqrMagnitude > 0.001f ? gravityCar.UpAxis : Vector3.up;
		Quaternion rot  = ComputeRotation();
		Vector3 forward = rot * Vector3.forward;
		Vector3 right   = rot * Vector3.right;
		Vector3 vel     = rb.linearVelocity;

		float fwdSpd = Vector3.Dot(vel, forward);
		float latSpd = Vector3.Dot(vel, right);
		float totalSpd = vel.magnitude;

		// ── Gravity up axis ───────────────────────────────────────────────
		UnityEditor.Handles.color = new Color(1f, 0.95f, 0.2f, 0.9f);
		UnityEditor.Handles.DrawLine(pos, pos + up * 2.5f);
		UnityEditor.Handles.SphereHandleCap(0, pos + up * 2.5f,
			Quaternion.identity, 0.12f, EventType.Repaint);
		UnityEditor.Handles.Label(pos + up * 2.7f, "UP", GizmoLabelStyle(Color.yellow));

		// ── Ground probe ray ─────────────────────────────────────────────
		UnityEditor.Handles.color = statGrounded
			? new Color(0.3f, 1f, 0.4f, 0.8f)
			: new Color(1f, 0.3f, 0.3f, 0.8f);
		UnityEditor.Handles.DrawDottedLine(pos, pos - up * probeDistance, 3f);
		UnityEditor.Handles.Label(pos - up * (probeDistance + 0.3f),
			statGrounded ? "GROUNDED" : "AIRBORNE", GizmoLabelStyle(statGrounded ? Color.green : Color.red));

		// ── Contact normal ────────────────────────────────────────────────
		if (statGrounded && contactNormal.sqrMagnitude > 0.001f) {
			UnityEditor.Handles.color = new Color(0.3f, 0.7f, 1f, 0.85f);
			Vector3 contactBase = pos - up * 0.4f;
			UnityEditor.Handles.DrawLine(contactBase, contactBase + contactNormal.normalized * 1.8f);
			UnityEditor.Handles.Label(contactBase + contactNormal.normalized * 2f,
				$"Normal  {statGroundAngle:F0}°", GizmoLabelStyle(new Color(0.4f, 0.8f, 1f)));
		}

		// ── Forward velocity (green) ──────────────────────────────────────
		if (Mathf.Abs(fwdSpd) > 0.1f) {
			UnityEditor.Handles.color = new Color(0.15f, 1f, 0.25f, 0.9f);
			Vector3 fwdVec = forward * fwdSpd * 0.18f;
			UnityEditor.Handles.DrawLine(pos, pos + fwdVec);
			UnityEditor.Handles.ArrowHandleCap(0, pos + fwdVec * 0.85f,
				Quaternion.LookRotation(fwdVec.normalized),
				Mathf.Abs(fwdSpd) * 0.04f, EventType.Repaint);
		}

		// ── Lateral velocity (red/orange = the number to keep small) ─────
		if (Mathf.Abs(latSpd) > 0.1f) {
			UnityEditor.Handles.color = Mathf.Abs(latSpd) > 3f
				? new Color(1f, 0.2f, 0.15f, 0.9f)   // high lateral = red (danger)
				: new Color(1f, 0.65f, 0.1f, 0.9f);  // low lateral = orange (ok)
			Vector3 latVec = right * latSpd * 0.18f;
			UnityEditor.Handles.DrawLine(pos, pos + latVec);
			UnityEditor.Handles.ArrowHandleCap(0, pos + latVec * 0.85f,
				Quaternion.LookRotation(latVec.normalized),
				Mathf.Abs(latSpd) * 0.04f, EventType.Repaint);
		}

		// ── Speed ring — shows current speed vs maxSpeed ──────────────────
		// White ring = maxSpeed. Filled arc scaled to current speed.
		float speedFraction = maxSpeed > 0f ? Mathf.Clamp01(totalSpd / maxSpeed) : 0f;
		float ringR = 1.6f;
		UnityEditor.Handles.color = new Color(1f, 1f, 1f, 0.18f);
		UnityEditor.Handles.DrawWireDisc(pos, up, ringR);
		UnityEditor.Handles.color = statDrifting
			? new Color(1f, 0.4f, 0.1f, 0.7f)
			: new Color(0.25f, 0.85f, 1f, 0.7f);
		UnityEditor.Handles.DrawSolidArc(pos, up, forward, speedFraction * 360f, ringR);

		// ── Yaw rate arc ──────────────────────────────────────────────────
		if (Mathf.Abs(yawVelocity) > 1f) {
			UnityEditor.Handles.color = new Color(0.8f, 0.3f, 1f, 0.75f);
			float arcDeg = Mathf.Clamp(yawVelocity * 0.25f, -180f, 180f);
			UnityEditor.Handles.DrawSolidArc(pos, up, forward, arcDeg, ringR * 1.18f);
		}

		// ── Landing slip indicator ─────────────────────────────────────────
		if (statLandingSlip > 0.05f) {
			UnityEditor.Handles.color = new Color(1f, 0.9f, 0.1f, statLandingSlip * 0.85f);
			UnityEditor.Handles.DrawWireDisc(pos, up, ringR * 1.35f);
			UnityEditor.Handles.Label(pos + right * (ringR * 1.5f),
				$"Slip {statLandingSlip:P0}", GizmoLabelStyle(Color.yellow));
		}

		// ── Skid wheel positions ──────────────────────────────────────────
		if (statDrifting) {
			for (int w = 0; w < 2; w++) {
				float side = w == 0 ? -1f : 1f;
				Vector3 wheelProbe = pos + right * (side * wheelSpread) + up * axleHeightOffset;
				UnityEditor.Handles.color = new Color(0.1f, 0.08f, 0.05f, 0.7f);
				UnityEditor.Handles.DrawSolidDisc(wheelProbe, up, markWidth);
			}
		}

		// ── Stats label ───────────────────────────────────────────────────
		string statsText =
			$"Speed   {totalSpd:F1} / {maxSpeed:F0} m/s\n" +
			$"Fwd     {fwdSpd:+0.0;-0.0} m/s\n" +
			$"Lateral {latSpd:+0.0;-0.0} m/s\n" +
			$"Yaw     {yawVelocity:F0} °/s\n" +
			$"Drift   {(statDrifting ? "YES" : "no")}";
		UnityEditor.Handles.Label(pos + up * 3.2f + forward * 0.4f,
			statsText, GizmoLabelStyle(Color.white));
	}

	// Simple label style — no Texture2D creation (that crashes inside gizmo draw calls).
	static GUIStyle GizmoLabelStyle (Color col) => new GUIStyle(GUI.skin.label) {
		fontStyle = FontStyle.Bold,
		fontSize  = 10,
		normal    = { textColor = col }
	};
#endif
}
