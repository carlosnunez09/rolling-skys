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
	float coastDeceleration = 8f;

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

		// World-space child that holds the skid mark mesh (stays put as the car moves)
		var skidGo = new GameObject("SkidMarks");
		skidGo.transform.SetParent(null);
		skidMesh = new Mesh { name = "SkidMarks" };
		var mf = skidGo.AddComponent<MeshFilter>();
		var mr = skidGo.AddComponent<MeshRenderer>();
		mf.mesh      = skidMesh;
		mr.material  = skidMaterial != null ? skidMaterial : CreateFallbackSkidMaterial();
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

	void Update () {
		desiredJump |= jumpAction.WasPressedThisFrame();
		UpdateSkidAlpha();
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
		float fwdSpeed     = Vector3.Dot(velocity, forward);
		float lateralSpeed = 0f;

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

			// Non-linear engine: torqueCurve maps speed ratio → force multiplier.
			// Past maxSpeed the car enters an overdrive zone with a small tapering
			// force that slowly pushes it toward topSpeed.
			if (throttle > 0f) {
				if (fwdSpeed < maxSpeed) {
					// Normal torque-curve zone
					float speedRatio       = Mathf.Clamp01(fwdSpeed / maxSpeed);
					float torqueMultiplier = torqueCurve.Evaluate(speedRatio);
					body.AddForce(forward * (throttle * acceleration * torqueMultiplier), ForceMode.Acceleration);
				} else if (fwdSpeed < topSpeed) {
					// Overdrive zone: force tapers from overdriveForce → 0 as we
					// approach topSpeed, so the buildup feels slow and earned.
					float overdriveRatio = Mathf.Clamp01((fwdSpeed - maxSpeed) / Mathf.Max(topSpeed - maxSpeed, 0.01f));
					float force          = Mathf.Lerp(overdriveForce, 0f, overdriveRatio);
					body.AddForce(forward * (throttle * force), ForceMode.Acceleration);
				}
				// At or above topSpeed: no further push — coast or drag will handle it
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
			lateralSpeed = Vector3.Dot(velocity, right);
			body.AddForce(-right * lateralSpeed * effectiveGrip, ForceMode.VelocityChange);

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

		float finalFwdSpeed = Vector3.Dot(velocity, forward);
		UpdateStats(gravity, isDrifting, finalFwdSpeed, lateralSpeed);
		UpdateSkidMarks(upAxis, isDrifting, lateralSpeed);
		ClearState();
	}

	void UpdateStats (Vector3 gravity, bool isDrifting, float fwdSpeed, float latSpeed) {
		float currentSpeed = velocity.magnitude;

		statAcceleration    = (currentSpeed - prevSpeed) / Time.fixedDeltaTime;
		statYawAcceleration = (yawVelocity - prevYawVelocity) / Time.fixedDeltaTime;
		prevSpeed           = currentSpeed;
		prevYawVelocity     = yawVelocity;

		// Snap to zero below 0.5 m/s so physics solver noise doesn't show as movement
		const float displayThreshold = 0.5f;
		statSpeed        = currentSpeed < displayThreshold ? 0f : currentSpeed;
		statForwardSpeed = Mathf.Abs(fwdSpeed) < displayThreshold ? 0f : fwdSpeed;
		statLateralSpeed = Mathf.Abs(latSpeed) < displayThreshold ? 0f : latSpeed;
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

	// Smooth per-frame alpha fade — updates vertex colors without rebuilding topology
	void UpdateSkidAlpha () {
		if (skidMesh == null || skidMesh.vertexCount == 0) return;
		float now    = Time.time;
		var   colors = skidMesh.colors;
		int   vi     = 0;

		for (int w = 0; w < 2; w++) {
			var trail = skidTrails[w];
			for (int i = 1; i < trail.Count; i++) {
				if (trail[i].breakBefore) continue;
				if (vi + 3 >= colors.Length) break;
				float alpA = Mathf.Clamp01(1f - (now - trail[i - 1].time) / fadeTime);
				float alpB = Mathf.Clamp01(1f - (now - trail[i].time)     / fadeTime);
				colors[vi].a     = alpA; colors[vi + 1].a = alpA;
				colors[vi + 2].a = alpB; colors[vi + 3].a = alpB;
				vi += 4;
			}
		}
		skidMesh.colors = colors;
	}

	void RebuildSkidMesh (float now) {
		int quads = 0;
		for (int w = 0; w < 2; w++) {
			var trail = skidTrails[w];
			for (int i = 1; i < trail.Count; i++)
				if (!trail[i].breakBefore) quads++;
		}

		if (quads == 0) { skidMesh.Clear(); return; }

		var verts  = new Vector3[quads * 4];
		var colors = new Color[quads * 4];
		var uvs    = new Vector2[quads * 4];
		var tris   = new int[quads * 6];
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
}
