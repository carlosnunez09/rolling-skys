using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(GravityCar))]
public class MovingCar : NetworkBehaviour {

	[BoxGroup("Offline Test"), SerializeField, Label("Offline Scene Test")]
	bool _offlineSceneTestMode;

	public bool OfflineSceneTestMode {
		get => _offlineSceneTestMode;
		set {
			_offlineSceneTestMode = value;
			ApplyOfflineSceneTestState();
		}
	}

	public bool OfflineSceneTestActive {
		get {
			if (!_offlineSceneTestMode || IsSpawned) return false;

			NetworkManager networkManager = NetworkManager.Singleton;
			return networkManager == null || !networkManager.IsListening;
		}
	}

	public bool HasLocalControl => IsOwner || OfflineSceneTestActive;

	public static bool AnyOfflineSceneTestCarActive () {
		MovingCar[] cars = FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude);
		foreach (MovingCar car in cars)
			if (car != null && car.OfflineSceneTestActive)
				return true;

		return false;
	}

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

	[BoxGroup("Grip & Drift"), SerializeField, Range(0f, 60f), Label("Max Drift Angle °")]
	float maxDriftAngle = 36f;

	[BoxGroup("Grip & Drift"), SerializeField, Range(1f, 30f), Label("Drift Angle Rate")]
	float driftAngleRate = 8f;

	[BoxGroup("Grip & Drift"), SerializeField, Range(0.1f, 2f), Label("Counter-Steer Authority")]
	float counterSteerAuthority = 1.0f;

	[BoxGroup("Grip & Drift"), SerializeField, Range(0f, 40f), Label("Mini-Turbo Impulse  m/s")]
	float miniTurboImpulse = 11f;

	[BoxGroup("Grip & Drift"), SerializeField, Range(0.3f, 3f), Label("Mini-Turbo Charge Time  s")]
	float miniTurboChargeTime = 1.2f;

	[BoxGroup("Grip & Drift"), SerializeField, Range(5f, 60f), Label("Yaw Inertia Smooth Rate")]
	float yawInertiaSmoothRate = 22f;

	// ── Surface Handling ──────────────────────────────────────────────

	[BoxGroup("Surface Handling"), SerializeField, Range(0.1f, 1f), Label("Min Surface Speed Mult")]
	float minSurfaceSpeedMultiplier = 0.35f;

	[BoxGroup("Surface Handling"), SerializeField, Range(1f, 30f), Label("Surface Transition Speed")]
	float surfaceTransitionSpeed = 8f;

	// ── Downforce ─────────────────────────────────────────────────────

	[BoxGroup("Downforce"), SerializeField, Range(0f, 150f), Label("Downforce Strength")]
	float downforceStrength = 32f;

	[BoxGroup("Downforce"), SerializeField, Range(0f, 60f), Label("Curvature Adhesion")]
	float curvatureAdhesion = 18f;

	[BoxGroup("Downforce"), SerializeField, Range(0.1f, 5f), Label("Min Adhesion Velocity  m/s")]
	float minAdhesionVelocity = 0.5f;

	// ── Air Control ───────────────────────────────────────────────────

	[BoxGroup("Air Control"), SerializeField, Range(0f, 180f), Label("Air Pitch Speed  °/s")]
	float airPitchSpeed = 80f;

	[BoxGroup("Air Control"), SerializeField, Range(0f, 180f), Label("Air Yaw Speed  °/s")]
	float airYawSpeed = 90f;

	[BoxGroup("Air Control"), SerializeField, Range(0f, 180f), Label("Air Roll Speed  °/s")]
	float airRollSpeed = 65f;

	[BoxGroup("Air Control"), SerializeField, Range(0.5f, 15f), Label("Air Auto-Right Speed")]
	float airAutoRightSpeed = 4.5f;

	[BoxGroup("Air Control"), SerializeField, Range(0.5f, 20f), Label("Air Angular Damping")]
	float airAngularDamping = 5f;

	[BoxGroup("Air Control"), SerializeField, Label("Pre-Align To Landing")]
	bool preAlignToLanding = true;

	[BoxGroup("Air Control"), SerializeField, Range(1f, 12f), Label("Landing Probe Distance  m")]
	float landingProbeDistance = 4.5f;

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

	[BoxGroup("Jump"), SerializeField, Range(0.5f, 5f), Label("Min Jump Height  m")]
	float minJumpHeight = 1.2f;

	[BoxGroup("Jump"), SerializeField, Range(1f, 10f), Label("Max Jump Height  m")]
	float maxJumpHeight = 3.8f;

	[BoxGroup("Jump"), SerializeField, Range(0.1f, 0.6f), Label("Max Jump Hold Duration  s")]
	float maxJumpHoldDuration = 0.25f;

	[BoxGroup("Jump"), SerializeField, Range(0.05f, 0.5f), Label("Jump Cooldown  s")]
	float jumpCooldown = 0.2f;

	[BoxGroup("Jump"), SerializeField, Range(0.05f, 0.3f), Label("Jump Buffer Duration  s")]
	float jumpBufferDuration = 0.15f;

	[SerializeField, HideInInspector]
	float jumpHeight = 2f;

	// ── Ground Detection ──────────────────────────────────────────────

	[BoxGroup("Ground"), SerializeField, Range(0f, 90f)]
	float maxGroundAngle = 70f;

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

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Drift Angle  °")]
	float statDriftAngle;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Mini Turbo Ready")]
	bool statMiniTurboReady;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Surface Name")]
	string statSurfaceName = "Default";

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Surface Speed Mult")]
	float statSurfaceSpeedMultiplier = 1f;

	[BoxGroup("Stats"), SerializeField, ReadOnly, Label("Downforce  m/s²")]
	float statDownforce;

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
	public float DriftDirection  => _driftDirection;
	public float DriftAngle      => _driftAngle;
	public bool  IsJumping       => _jumpActive;
	public float JumpHeight      => maxJumpHeight;
	public bool  MiniTurboReady  => _miniTurboReady;
	public float MiniTurboChargeRatio => miniTurboChargeTime > 0f ? Mathf.Clamp01(_driftChargeTimer / miniTurboChargeTime) : 0f;
	public string SurfaceName    => _currentSurfaceName;
	public string SurfaceTag     => _currentSurfaceTag;
	public float SurfaceSpeedMultiplier => _groundSpeedMultiplier;
	public bool  IsOnHazard      => _isOnHazard;
	public float Downforce       => _statDownforce;
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
	bool  _jumpActive;
	float _jumpHoldTimer;
	float _jumpCooldownTimer;
	float _jumpBufferTimer;

	float prevSpeed;
	float prevYawVelocity;

	// Smoothed values used only for inspector display — decoupled from gameplay physics.
	float _smoothSpeed;
	float _smoothFwdSpeed;
	float _smoothLatSpeed;
	float _smoothYawRate;
	float _smoothAccel;

	// Surface friction & speed multipliers — smoothly sampled from PlanetSurface & SurfaceFriction.
	float _groundFriction         = 1f;
	float _groundSpeedMultiplier  = 1f;
	float _targetFriction         = 1f;
	float _targetSpeedMultiplier  = 1f;
	string _currentSurfaceName    = "Default";
	string _currentSurfaceTag     = "";
	bool   _isOnHazard;
	float  _hazardDamagePerSecond;

	// Accumulated per-step surface samples
	int    _surfaceSampleCount;
	float  _accumFriction;
	float  _accumSpeedMultiplier;
	float  _accumDamage;
	int    _accumHazardCount;
	string _lastSurfaceName;
	string _lastSurfaceTag;

	// Downforce state
	float _statDownforce;

	// Drift & mini-turbo state
	float _driftAngle;
	float _driftDirection;
	float _driftChargeTimer;
	bool  _miniTurboReady;

	// Airborne attitude offsets
	float _airPitch;
	float _airRoll;

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
	readonly NetworkVariable<bool> _networkDrifting = new NetworkVariable<bool>(
		false,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner);

	// Runtime-created objects that need explicit cleanup in OnDestroy.
	GameObject _skidGo;
	Material   _skidFallbackMat;
	bool    _remoteSkidInitialized;
	Vector3 _remoteLastSkidPosition;
	Vector3 _remoteSmoothedVelocity;
	// Cached mesh arrays — reused each rebuild to avoid per-frame GC.
	Vector3[] _skidVerts;
	Color[]   _skidColors;
	Vector2[] _skidUVs;
	int[]     _skidTris;
	int       _skidCachedQuads = -1;
	// Pre-allocated list for alpha-only colour updates (no topology change).
	readonly List<Color> _skidAlphaBuffer = new List<Color>();

	bool OnGround => groundContactCount > 0 && !_jumpActive && stepsSinceLastJump >= 8;

	InputAction moveAction, jumpAction, driftAction;

	void OnValidate () {
		if (maxGroundAngle < 65f) maxGroundAngle = 65f;
		minGroundDot = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
		if (jumpHeight > 0f && maxJumpHeight == 3.8f && jumpHeight != 2f) {
			maxJumpHeight = jumpHeight;
			minJumpHeight = Mathf.Max(jumpHeight * 0.35f, 0.8f);
		}
		if (Application.isPlaying)
			ApplyOfflineSceneTestState();
	}

	[Button("Enable Offline Scene Test")]
	void EnableOfflineSceneTest () => OfflineSceneTestMode = true;

	[Button("Disable Offline Scene Test")]
	void DisableOfflineSceneTest () => OfflineSceneTestMode = false;

	void Awake () {
		body = GetComponent<Rigidbody>();
		gravityCar = GetComponent<GravityCar>();
		OnValidate();
		yaw      = transform.eulerAngles.y;
		// Initialise so the first acceleration reading is 0, not a spike from
		// (currentSpeed - 0) / fixedDeltaTime on the very first FixedUpdate.
		prevSpeed = 0f;

#if !UNITY_SERVER || UNITY_EDITOR
		// Skid mark mesh — client visual only, never needed on server
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

		// Input actions — server has no player, no input
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
#endif
	}

	void Start () {
		ApplyOfflineSceneTestState();
	}

	public override void OnNetworkSpawn () {
		ResetRemoteSkidTracking();

		if (HasLocalControl) {
#if !UNITY_SERVER || UNITY_EDITOR
			body.isKinematic = false;
			SetInputEnabled(true);
			// Register this car as the local player so RaceRuntime HUD and
			// input-gating target the correct car without a manual Inspector link.
			RaceRuntime raceRuntime = FindAnyObjectByType<RaceRuntime>();
			raceRuntime?.SetPlayerCar(this);
#endif
			return;
		}

		// Non-owner: this car is a ghost driven by NetworkTransform.
		// Kill local physics so it doesn't fight the received transforms.
		SetInputEnabled(false);
		body.isKinematic = true;
	}

	// When false the car physics still runs but all input is zeroed out.
	bool _inputEnabled = true;

	// Scripted boost-pad trajectory — input off and driving forces skipped.
	bool _trajectoryLocked;
	bool _inputEnabledBeforeTrajectory;

	/// <summary>True while a BoostPad is driving this car along a fixed arc.</summary>
	public bool IsTrajectoryLocked => _trajectoryLocked;

	void ApplyOfflineSceneTestState () {
		if (!OfflineSceneTestActive) return;

		if (body == null)
			body = GetComponent<Rigidbody>();

		body.isKinematic = false;
		SetInputEnabled(true);

#if !UNITY_SERVER || UNITY_EDITOR
		RaceRuntime raceRuntime = FindAnyObjectByType<RaceRuntime>();
		raceRuntime?.SetPlayerCar(this);
#endif
	}

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
			moveAction?.Enable();
			jumpAction?.Enable();
			driftAction?.Enable();
		} else {
			moveAction?.Disable();
			jumpAction?.Disable();
			driftAction?.Disable();
			// Clear any queued jump so a press before lockout doesn't fire
			// the moment input is re-enabled (e.g. race countdown locking input).
			desiredJump = false;
			_jumpBufferTimer = 0f;
		}
	}

	void OnEnable () {
		if (_inputEnabled) {
			moveAction?.Enable();
			jumpAction?.Enable();
			driftAction?.Enable();
		}
	}

	void OnDisable () {
		moveAction?.Disable();
		jumpAction?.Disable();
		driftAction?.Disable();
	}

	public override void OnDestroy () {
		base.OnDestroy();
		if (_skidGo != null)       Destroy(_skidGo);
		if (skidMesh != null)      Destroy(skidMesh);
		if (_skidFallbackMat != null) Destroy(_skidFallbackMat);
		moveAction?.Dispose();
		jumpAction?.Dispose();
		driftAction?.Dispose();
	}

	void Update () {
#if !UNITY_SERVER || UNITY_EDITOR
		if (HasLocalControl) {
			if (jumpAction.WasPressedThisFrame()) {
				_jumpBufferTimer = jumpBufferDuration;
				desiredJump = true;
			}
		} else {
			UpdateRemoteSkidMarks(Time.deltaTime);
		}

		UpdateSkidAlpha();
#endif
	}

	void FixedUpdate () {
		if (!HasLocalControl) return; // non-owners are kinematic, NetworkTransform drives them

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

		if (_jumpBufferTimer > 0f) {
			_jumpBufferTimer -= Time.fixedDeltaTime;
			if (_jumpBufferTimer <= 0f) desiredJump = false;
		}
		if (_jumpCooldownTimer > 0f) {
			_jumpCooldownTimer -= Time.fixedDeltaTime;
		}

		UpdateState(upAxis);

		// Landing — spike slip proportional to spin speed at impact, reset airborne attitude
		if (!wasGrounded && OnGround) {
			landingYawVelocity = yawVelocity;
			landingSlip = Mathf.Clamp01(Mathf.Abs(yawVelocity) / maxSlipYawRate);
			_airPitch = 0f;
			_airRoll  = 0f;
			_jumpActive = false;
			_jumpCooldownTimer = jumpCooldown;
		} else if (wasGrounded && !OnGround) {
			// Takeoff / Launch — seamlessly absorb drift angle into yaw heading
			// and ensure drift rotation speed carries into airborne yaw velocity!
			float driftSign = _driftDirection != 0f ? _driftDirection : (Mathf.Abs(_driftAngle) > 5f ? Mathf.Sign(_driftAngle) : 0f);
			yaw += _driftAngle;
			_driftAngle = 0f;
			if (driftSign != 0f) {
				float minDriftSpin = driftSign * 65f;
				if (driftSign > 0f && yawVelocity < minDriftSpin) yawVelocity = minDriftSpin;
				else if (driftSign < 0f && yawVelocity > minDriftSpin) yawVelocity = minDriftSpin;
			}
		}
		wasGrounded = OnGround;
		landingSlip = Mathf.MoveTowards(landingSlip, 0f, slipRecoveryRate * Time.fixedDeltaTime);

		Vector2 input      = moveAction.ReadValue<Vector2>();
		float   throttle   = input.y;
		float   steer      = input.x;
		bool    isDrifting = driftAction.IsPressed();
		if (IsSpawned && _networkDrifting.Value != isDrifting)
			_networkDrifting.Value = isDrifting;

		Quaternion rotation = ComputeRotation();
		Vector3 forward = rotation * Vector3.forward;
		Vector3 right   = rotation * Vector3.right;
		float fwdSpeed     = Vector3.Dot(velocity, forward);
		float lateralSpeed = 0f;

		if (OnGround) {
			// Deflect any velocity pushing into the ramp surface along the surface plane
			// so the car glides smoothly up slopes without blunt-impact speed loss!
			if (contactNormal.sqrMagnitude > 0.001f) {
				float normalDot = Vector3.Dot(velocity, contactNormal);
				if (normalDot < 0f) {
					velocity -= contactNormal * normalDot;
					body.linearVelocity = velocity;
					fwdSpeed = Vector3.Dot(velocity, forward);
				}
			}

			float absSpeed    = Mathf.Abs(fwdSpeed);
			float reverseSign = fwdSpeed >= 0f ? 1f : -1f;

			// ── Downforce & Curvature Adhesion ────────────────────────────────
			// Keeps the car glued to spherical planets and curved tracks without
			// bouncing off into orbit at high speeds. Suppressed briefly after jumping.
			if (stepsSinceLastJump > 8) {
				float speedRatio     = Mathf.Clamp01(velocity.magnitude / Mathf.Max(maxSpeed, 0.1f));
				float aeroDownforce  = downforceStrength * (speedRatio * speedRatio);
				float adhesion       = velocity.magnitude >= minAdhesionVelocity
					? curvatureAdhesion
					: curvatureAdhesion * (velocity.magnitude / minAdhesionVelocity);
				float totalDownforce = aeroDownforce + adhesion;
				_statDownforce = totalDownforce;
				Vector3 downDir = contactNormal.sqrMagnitude > 0.001f ? -contactNormal : -upAxis;
				body.AddForce(downDir * totalDownforce, ForceMode.Acceleration);
			} else {
				_statDownforce = 0f;
			}

			// ── Drift State, Counter-Steering & Mini-Turbo ────────────────────
			// When holding drift without steering, car enters neutral slip-and-slide
			// preserving forward linear momentum. Only lock in a directional power slide
			// if the player actively steers (or already has substantial lateral skid).
			if (isDrifting && absSpeed > 2.5f) {
				if (_driftDirection == 0f) {
					if (Mathf.Abs(steer) > 0.15f) {
						_driftDirection = Mathf.Sign(steer);
					} else if (Mathf.Abs(landingYawVelocity) > 20f && landingSlip > 0.05f) {
						_driftDirection = Mathf.Sign(landingYawVelocity);
					} else {
						float latVel = Vector3.Dot(velocity, right);
						if (Mathf.Abs(latVel) > 2.0f)
							_driftDirection = Mathf.Sign(latVel);
					}
				}
			} else if (!isDrifting || absSpeed < 1.5f) {
				// Mini-turbo burst upon exiting a sustained directional drift!
				if (_miniTurboReady && OnGround && absSpeed > 2f) {
					body.AddForce(forward * miniTurboImpulse, ForceMode.VelocityChange);
				}
				_miniTurboReady    = false;
				_driftChargeTimer  = 0f;
				_driftDirection    = 0f;
			}

			float targetDriftAngle = 0f;
			if (_driftDirection != 0f) {
				// Turning into drift widens slip angle; counter-steering stabilizes and tightens arc
				float steerWithDrift = steer * _driftDirection;
				float angleFactor    = Mathf.Clamp(1f + steerWithDrift * counterSteerAuthority, 0.25f, 1.5f);
				targetDriftAngle     = _driftDirection * (maxDriftAngle * angleFactor);

				_driftChargeTimer += Time.fixedDeltaTime;
				if (_driftChargeTimer >= miniTurboChargeTime)
					_miniTurboReady = true;
			}
			_driftAngle = Mathf.MoveTowards(_driftAngle, targetDriftAngle, driftAngleRate * 10f * Time.fixedDeltaTime);

			// ── Steering with Rotational Inertia ──────────────────────────────
			// Yaw multiplier only engages when in active directional drift; neutral slide preserves straight steering
			float yawBoost = (_driftDirection != 0f) ? driftYawMultiplier : 1f;
			float rawSteeringYaw    = steer * (absSpeed / minTurningRadius) * Mathf.Rad2Deg * reverseSign;
			float yawRateCap        = (maxSpeed / minTurningRadius) * Mathf.Rad2Deg * (isDrifting ? 1.35f : 1f);
			float targetSteeringYaw = Mathf.Clamp(rawSteeringYaw * yawBoost, -yawRateCap, yawRateCap);

			if (_driftDirection != 0f && Mathf.Abs(steer) < 0.1f) {
				// Gentle self-steer along the drift curve when releasing the stick during directional drift
				targetSteeringYaw = _driftDirection * (yawRateCap * 0.35f);
			}

			float steerRate = yawInertiaSmoothRate * 20f;
			yawVelocity = Mathf.MoveTowards(yawVelocity, targetSteeringYaw, steerRate * Time.fixedDeltaTime);

			// Blend air-spin momentum into steering during landing slip
			if (landingSlip > 0.001f)
				yawVelocity = Mathf.Lerp(yawVelocity, landingYawVelocity, landingSlip);

			yaw += yawVelocity * Time.fixedDeltaTime;

			rotation = ComputeRotation();
			forward  = rotation * Vector3.forward;
			right    = rotation * Vector3.right;

			// ── Engine with Surface Speed Scaling ─────────────────────────────
			float effectiveMaxSpeed = maxSpeed * _groundSpeedMultiplier;
			float effectiveTopSpeed = topSpeed * _groundSpeedMultiplier;
			float effectiveAccel    = acceleration * Mathf.Clamp(_groundSpeedMultiplier, 0.35f, 1.25f);

			if (throttle > 0f) {
				if (fwdSpeed < effectiveMaxSpeed) {
					float speedRatio       = Mathf.Clamp01(fwdSpeed / effectiveMaxSpeed);
					float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
					body.AddForce(forward * (throttle * effectiveAccel * torqueMultiplier), ForceMode.Acceleration);

				} else if (fwdSpeed < effectiveTopSpeed && !isDrifting) {
					float overdriveRatio = Mathf.Clamp01((fwdSpeed - effectiveMaxSpeed) / Mathf.Max(effectiveTopSpeed - effectiveMaxSpeed, 0.01f));
					float force          = Mathf.Lerp(overdriveForce, 0f, overdriveRatio);
					body.AddForce(forward * (throttle * force), ForceMode.Acceleration);
				}

			} else if (throttle < 0f && fwdSpeed > 0f) {
				float brakeBlend       = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fwdSpeed / 2f));
				float speedRatio       = Mathf.Clamp01(fwdSpeed / Mathf.Max(effectiveMaxSpeed, 0.1f));
				float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
				float brakeF           = -brakeForce * brakeBlend * _groundFriction;
				float reverseF         = throttle * effectiveAccel * torqueMultiplier * (1f - brakeBlend);
				body.AddForce(forward * (brakeF + reverseF), ForceMode.Acceleration);

			} else if (throttle < 0f) {
				float effectiveReverse = maxReverseSpeed * _groundSpeedMultiplier;
				if (fwdSpeed > -effectiveReverse) {
					float speedRatio       = Mathf.Clamp01(-fwdSpeed / effectiveReverse);
					float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
					body.AddForce(forward * (throttle * effectiveAccel * torqueMultiplier), ForceMode.Acceleration);
				}

			} else {
				float coastDecel     = isDrifting ? coastDeceleration * 0.25f : coastDeceleration;
				float rawCoastForce  = -fwdSpeed * coastDecel * _groundFriction;
				float coastForceCap  = brakeForce * 0.25f * _groundFriction;
				body.AddForce(forward * Mathf.Clamp(rawCoastForce, -coastForceCap, coastForceCap), ForceMode.Acceleration);
			}

			// ── Lateral Grip ──────────────────────────────────────────────────
			float baseGrip      = isDrifting ? driftGrip : lateralGrip;
			float effectiveGrip = Mathf.Max(
				baseGrip * (1f - landingSlip * 0.75f) * _groundFriction,
				0.01f);

			lateralSpeed = Vector3.Dot(velocity, right);
			body.AddForce(-right * lateralSpeed * effectiveGrip, ForceMode.VelocityChange);

			// ── Drift Speed Cap ───────────────────────────────────────────────
			// In neutral slip, preserve linear momentum without capping; in directional drift cap to top speed.
			if (isDrifting && _driftDirection != 0f) {
				Vector3 surfaceVel   = Vector3.ProjectOnPlane(body.linearVelocity, upAxis);
				float   surfaceSpeed = surfaceVel.magnitude;
				if (surfaceSpeed > effectiveTopSpeed) {
					body.AddForce(-surfaceVel.normalized * (surfaceSpeed - effectiveTopSpeed), ForceMode.VelocityChange);
				}
			}

			body.MoveRotation(rotation);
		} else {
			// ── In-Air Control, Auto-Righting & Landing Pre-Alignment ─────────
			_statDownforce    = 0f;
			_driftAngle       = 0f;
			_driftChargeTimer = 0f;
			_miniTurboReady   = false;
			_driftDirection   = 0f;

			// Conserve rotation speed in the air with gentle aerodynamic damping
			yawVelocity = Mathf.MoveTowards(yawVelocity, 0f, airAngularDamping * 1.5f * Time.fixedDeltaTime);

			float pitchInput = throttle;
			float yawInput   = steer;
			float rollInput  = isDrifting ? -steer * 0.75f : 0f;

			yaw += (yawInput * airYawSpeed + yawVelocity) * Time.fixedDeltaTime;

			_airPitch += pitchInput * airPitchSpeed * Time.fixedDeltaTime;
			_airRoll  += rollInput * airRollSpeed * Time.fixedDeltaTime;

			_airPitch = Mathf.MoveTowards(_airPitch, 0f, airAutoRightSpeed * 12f * Time.fixedDeltaTime);
			_airRoll  = Mathf.MoveTowards(_airRoll,  0f, airAutoRightSpeed * 12f * Time.fixedDeltaTime);

			_airPitch = Mathf.Clamp(_airPitch, -75f, 75f);
			_airRoll  = Mathf.Clamp(_airRoll,  -75f, 75f);

			Quaternion landingTilt = Quaternion.identity;
			if (preAlignToLanding && Vector3.Dot(velocity, upAxis) < 0f) {
				if (Physics.Raycast(body.position, -upAxis, out RaycastHit landHit, landingProbeDistance, probeMask)) {
					if (Vector3.Dot(upAxis, landHit.normal) >= minGroundDot) {
						float distFactor = 1f - Mathf.Clamp01(landHit.distance / landingProbeDistance);
						Quaternion targetTilt = Quaternion.FromToRotation(upAxis, landHit.normal);
						landingTilt = Quaternion.Slerp(Quaternion.identity, targetTilt, distFactor * 0.7f);
					}
				}
			}

			Quaternion baseAirHeading = gravityCar.GravityAlignment * Quaternion.AngleAxis(yaw, Vector3.up);
			Quaternion airRotation = landingTilt * baseAirHeading * Quaternion.Euler(_airPitch, 0f, _airRoll);
			body.MoveRotation(airRotation);
		}

		// ── Jump & Variable Height Sustain ────────────────────────────────
		bool canJump = OnGround && !_jumpActive && _jumpCooldownTimer <= 0f && stepsSinceLastJump > 8;
		if (desiredJump && canJump) {
			desiredJump        = false;
			_jumpBufferTimer   = 0f;
			_jumpActive        = true;
			_jumpHoldTimer     = 0f;
			stepsSinceLastJump = 0;

			// Seamlessly transfer drift angle into yaw heading upon jump launch
			// and guarantee drift spin momentum carries into the jump
			float driftSign = _driftDirection != 0f ? _driftDirection : (Mathf.Abs(_driftAngle) > 5f ? Mathf.Sign(_driftAngle) : 0f);
			yaw += _driftAngle;
			_driftAngle = 0f;
			if (driftSign != 0f) {
				float minDriftSpin = driftSign * 65f;
				if (driftSign > 0f && yawVelocity < minDriftSpin) yawVelocity = minDriftSpin;
				else if (driftSign < 0f && yawVelocity > minDriftSpin) yawVelocity = minDriftSpin;
			}

			float gMag         = gravity.magnitude;
			float launchSpeed  = Mathf.Sqrt(2f * gMag * minJumpHeight);
			float alignedSpeed = Vector3.Dot(velocity, upAxis);
			if (alignedSpeed > 0f)
				launchSpeed = Mathf.Max(launchSpeed - alignedSpeed * 0.5f, launchSpeed * 0.4f);
			body.linearVelocity += upAxis * launchSpeed;
		}

		// Variable jump sustain while holding Space during ascent
		if (_jumpActive) {
			_jumpHoldTimer += Time.fixedDeltaTime;
			float vertSpeed   = Vector3.Dot(body.linearVelocity, upAxis);
			bool  holdingJump = jumpAction != null && jumpAction.IsPressed();

			if (holdingJump && _jumpHoldTimer < maxJumpHoldDuration && vertSpeed > 0.2f) {
				float gMag         = gravity.magnitude;
				float targetDeltaV = Mathf.Max(Mathf.Sqrt(2f * gMag * maxJumpHeight) - Mathf.Sqrt(2f * gMag * minJumpHeight), 0f);
				float sustainAccel = maxJumpHoldDuration > 0f ? (targetDeltaV / maxJumpHoldDuration) : 0f;
				body.AddForce(upAxis * sustainAccel, ForceMode.Acceleration);
			} else {
				// Space was released early, hold duration expired, or apex reached — sustain ends
				_jumpActive = false;
			}
		}

		// Re-read velocity AFTER all physics ops
		Vector3 postVel     = body.linearVelocity;
		float finalFwdSpeed = Vector3.Dot(postVel, forward);
		float finalLatSpeed = Vector3.Dot(postVel, right);
		UpdateStats(gravity, isDrifting, finalFwdSpeed, finalLatSpeed, postVel.magnitude);
		UpdateSkidMarks(upAxis, isDrifting, finalLatSpeed, false, finalFwdSpeed);
		ClearState();
	}

	void UpdateStats (Vector3 gravity, bool isDrifting, float fwdSpeed, float latSpeed, float currentSpeed) {
		float rawAccel      = (currentSpeed - prevSpeed) / Time.fixedDeltaTime;
		statYawAcceleration = (yawVelocity - prevYawVelocity) / Time.fixedDeltaTime;
		prevSpeed           = currentSpeed;
		prevYawVelocity     = yawVelocity;

		const float displayThreshold = 0.5f;
		float       a                = 15f * Time.fixedDeltaTime;

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
		statDriftAngle   = _driftAngle;
		statMiniTurboReady = _miniTurboReady;
		statSurfaceName  = _currentSurfaceName;
		statSurfaceSpeedMultiplier = _groundSpeedMultiplier;
		statDownforce    = _statDownforce;
		statLandingSlip  = landingSlip;
		statGroundAngle  = OnGround ? Vector3.Angle(gravityCar.UpAxis, contactNormal) : 0f;
		statGravityMagnitude = gravity.magnitude;
		statGravityUpAxis    = gravityCar.UpAxis;
		statGravitySource    = CustomGravity.GetDominantSourceName(body.position);

		statRigidbodyBelow = Physics.Raycast(body.position, -gravityCar.UpAxis, out RaycastHit hit, probeDistance * 2f)
			&& hit.rigidbody != null;
	}

	// On flat ground: gravity alignment + yaw + drift slip angle.
	// On a ramp: tilts to match the contact normal.
	Quaternion ComputeRotation () {
		Quaternion heading = gravityCar.GravityAlignment * Quaternion.AngleAxis(yaw + _driftAngle, Vector3.up);
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

			if (_surfaceSampleCount > 0) {
				_targetFriction        = _accumFriction / _surfaceSampleCount;
				_targetSpeedMultiplier = Mathf.Max(minSurfaceSpeedMultiplier, _accumSpeedMultiplier / _surfaceSampleCount);
				_hazardDamagePerSecond = _accumDamage / _surfaceSampleCount;
				_isOnHazard            = _accumHazardCount > 0;
				_currentSurfaceName    = _lastSurfaceName;
				_currentSurfaceTag     = _lastSurfaceTag;
			} else {
				_targetFriction        = 1f;
				_targetSpeedMultiplier = 1f;
				_hazardDamagePerSecond = 0f;
				_isOnHazard            = false;
				_currentSurfaceName    = "Ground";
				_currentSurfaceTag     = "";
			}

			float blend = 1f - Mathf.Exp(-surfaceTransitionSpeed * Time.fixedDeltaTime);
			_groundFriction        = Mathf.Lerp(_groundFriction,        _targetFriction,        blend);
			_groundSpeedMultiplier = Mathf.Lerp(_groundSpeedMultiplier, _targetSpeedMultiplier, blend);
		} else {
			contactNormal   = upAxis;
			float blend = 1f - Mathf.Exp(-surfaceTransitionSpeed * Time.fixedDeltaTime);
			_groundFriction        = Mathf.Lerp(_groundFriction,        1f, blend);
			_groundSpeedMultiplier = Mathf.Lerp(_groundSpeedMultiplier, 1f, blend);
			_currentSurfaceName    = "Air";
			_currentSurfaceTag     = "";
			_isOnHazard            = false;
			_hazardDamagePerSecond = 0f;
		}
	}

	void ClearState () {
		groundContactCount    = 0;
		contactNormal         = Vector3.zero;
		_surfaceSampleCount   = 0;
		_accumFriction        = 0f;
		_accumSpeedMultiplier = 0f;
		_accumDamage          = 0f;
		_accumHazardCount     = 0;
	}

	bool SnapToGround (Vector3 upAxis) {
		if (_jumpActive || stepsSinceLastJump < 25) return false;
		if (stepsSinceLastGrounded > 4) return false;
		if (velocity.magnitude > maxSnapSpeed) return false;

		// Probe along the vehicle's down direction (or world down) to find ground under the chassis on slopes
		Vector3 probeDir = contactNormal.sqrMagnitude > 0.001f ? -contactNormal : -transform.up;
		if (!Physics.Raycast(body.position, probeDir, out RaycastHit hit, probeDistance * 1.5f, probeMask)) {
			if (!Physics.Raycast(body.position, -upAxis, out hit, probeDistance, probeMask))
				return false;
		}

		Vector3 localUp = CustomGravity.GetUpAxis(hit.point);
		bool isGround = Vector3.Dot(upAxis, hit.normal) >= minGroundDot ||
		                Vector3.Dot(localUp, hit.normal) >= minGroundDot ||
		                (wasGrounded && Vector3.Dot(transform.up, hit.normal) >= minGroundDot);
		if (!isGround) return false;

		groundContactCount = 1;
		contactNormal      = hit.normal;
		SampleSurface(hit.collider.gameObject, hit.point, hit.triangleIndex);
		float dot = Vector3.Dot(velocity, hit.normal);
		if (dot > 0f) body.linearVelocity = velocity - hit.normal * dot;
		return true;
	}

	void SampleSurface (GameObject contactObj, Vector3 worldPoint, int triangleIndex = -1) {
		if (contactObj == null) return;

		float friction  = 1f;
		float speedMult = 1f;
		float damage    = 0f;
		bool  hazard    = false;
		string surfaceName = contactObj.name;
		string surfaceTag  = contactObj.tag;

		var sf = contactObj.GetComponent<SurfaceFriction>();
		if (sf == null) sf = contactObj.GetComponentInParent<SurfaceFriction>();
		if (sf != null) friction *= sf.Friction;

		var ps = contactObj.GetComponent<PlanetSurface>();
		if (ps == null) ps = contactObj.GetComponentInParent<PlanetSurface>();
		if (ps != null) {
			PlanetSurface.SurfaceProperties props = ps.GetSurfaceProperties(worldPoint, triangleIndex);
			friction  *= props.frictionMultiplier;
			speedMult *= props.speedMultiplier;
			damage     = props.damagePerSecond;
			hazard     = props.isHazard;
			if (!string.IsNullOrEmpty(props.layerName) && props.layerName != "Default")
				surfaceName = props.layerName;
			if (!string.IsNullOrEmpty(props.tag))
				surfaceTag = props.tag;
		}

		_surfaceSampleCount++;
		_accumFriction        += friction;
		_accumSpeedMultiplier += speedMult;
		_accumDamage          += damage;
		if (hazard) _accumHazardCount++;
		_lastSurfaceName = surfaceName;
		_lastSurfaceTag  = surfaceTag;
	}

	void OnCollisionEnter (Collision collision) {
		if (IsSpawned && !HasLocalControl) return;
		EvaluateCollision(collision);
	}

	void OnCollisionStay  (Collision collision) {
		if (IsSpawned && !HasLocalControl) return;
		EvaluateCollision(collision);
	}

	void EvaluateCollision (Collision collision) {
		if (_jumpActive && stepsSinceLastJump < 8) return;
		bool sampled = false;
		for (int i = 0; i < collision.contactCount; i++) {
			ContactPoint contact = collision.GetContact(i);
			Vector3 normal = contact.normal;
			Vector3 localUp = CustomGravity.GetUpAxis(contact.point);
			bool isGround = Vector3.Dot(gravityCar.UpAxis, normal) >= minGroundDot ||
			                Vector3.Dot(localUp, normal) >= minGroundDot ||
			                (wasGrounded && Vector3.Dot(transform.up, normal) >= minGroundDot);
			if (isGround) {
				groundContactCount += 1;
				contactNormal      += normal;
				if (!sampled) {
					SampleSurface(collision.gameObject, contact.point, -1);
					sampled = true;
				}
			}
		}
	}

	// ── Skid Marks ────────────────────────────────────────────────────

	void ResetRemoteSkidTracking () {
		_remoteSkidInitialized = false;
		_remoteLastSkidPosition = transform.position;
		_remoteSmoothedVelocity = Vector3.zero;
		for (int i = 0; i < skidTrailActive.Length; i++) {
			skidTrailActive[i] = false;
			skidLastPos[i] = null;
		}
	}

	void UpdateRemoteSkidMarks (float deltaTime) {
		if (!IsSpawned || HasLocalControl || skidMesh == null) return;

		Vector3 currentPosition = transform.position;
		if (!_remoteSkidInitialized) {
			_remoteSkidInitialized = true;
			_remoteLastSkidPosition = currentPosition;
			return;
		}

		Vector3 displacement = currentPosition - _remoteLastSkidPosition;
		_remoteLastSkidPosition = currentPosition;

		if (displacement.sqrMagnitude > 100f) {
			_remoteSmoothedVelocity = Vector3.zero;
			return;
		}

		float dt = Mathf.Max(deltaTime, 0.0001f);
		Vector3 observedVelocity = displacement / dt;
		float velocityBlend = 1f - Mathf.Exp(-18f * dt);
		_remoteSmoothedVelocity = Vector3.Lerp(_remoteSmoothedVelocity, observedVelocity, velocityBlend);

		Vector3 upAxis = CustomGravity.GetUpAxis(currentPosition);
		Vector3 forward = transform.forward;
		Vector3 right = transform.right;
		float forwardSpeed = Vector3.Dot(_remoteSmoothedVelocity, forward);
		float lateralSpeed = Vector3.Dot(_remoteSmoothedVelocity, right);
		bool remoteGrounded = Physics.Raycast(
			currentPosition + upAxis,
			-upAxis,
			out RaycastHit hit,
			Mathf.Max(probeDistance + 1f, 2.5f),
			probeMask)
			&& Vector3.Dot(upAxis, hit.normal) >= minGroundDot;

		statSpeed = _remoteSmoothedVelocity.magnitude;
		statForwardSpeed = forwardSpeed;
		statLateralSpeed = lateralSpeed;
		statGrounded = remoteGrounded;
		statDrifting = _networkDrifting.Value && remoteGrounded;

		UpdateSkidMarks(upAxis, _networkDrifting.Value, lateralSpeed, remoteGrounded, forwardSpeed);
	}

	void UpdateSkidMarks (Vector3 upAxis, bool isDrifting, float lateralSpeed, bool groundOverride = false, float forwardSpeed = 0f) {
		float now        = Time.time;
		bool  isGrounded = groundOverride || OnGround;
		bool  isSlipping = Mathf.Abs(lateralSpeed) >= minSkidLateralSpeed || (isDrifting && Mathf.Abs(forwardSpeed) >= 4f);
		bool  shouldMark = isGrounded && isDrifting && isSlipping;
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
