using UnityEngine;
using NaughtyAttributes;

/// <summary>
/// Driving follow camera. Gravity-aligned third-person orbit with speed-driven pitch,
/// auto-yaw, optional idle/manual cinematic orbit, and the shared obstruction/cutaway
/// boom in <see cref="FollowCameraPose"/>.
/// </summary>
[RequireComponent(typeof(Camera))]
public class OrbitCamera : MonoBehaviour {

	// ── Focus ─────────────────────────────────────────────────────────

	[BoxGroup("Focus"), SerializeField]
	Transform focus = default;

	[BoxGroup("Focus"), SerializeField]
	bool autoFindLocalPlayer = true;

	[BoxGroup("Focus"), SerializeField, Range(0f, 10f), Label("Lag Radius")]
	float focusRadius = 5f;

	[BoxGroup("Focus"), SerializeField, Range(0f, 1f), Label("Centering Speed")]
	float focusCentering = 0.5f;

	[BoxGroup("Focus"), SerializeField, Range(0f, 5f), Label("Focus Height Offset  m")]
	float focusHeightOffset = 0.75f;

	// ── Distance & Occlusion ──────────────────────────────────────────

	[BoxGroup("Distance"), SerializeField, Range(1f, 50f)]
	float distance = 5f;

	[BoxGroup("Distance"), SerializeField]
	LayerMask obstructionMask = -1;

	[BoxGroup("Distance"), SerializeField, Min(0.1f)]
	float collisionRecoverySpeed = 4f;

	[BoxGroup("Distance"), SerializeField, Min(0.5f)]
	float minimumComfortDistance = 3f;

	[BoxGroup("Distance"), SerializeField]
	bool revealThroughToonObjects = true;

	readonly FollowCameraPose pose = new FollowCameraPose();
	readonly CameraFocusTracker focusTracker = new CameraFocusTracker();
	CameraGravityAlignment gravityAlignment;
	CameraLookInput lookInput;

	// ── Cinematic Orbit ───────────────────────────────────────────────

	[BoxGroup("Cinematic Orbit"), SerializeField, Label("Enable Cinematic Mode")]
	bool enableCinematicOrbit = true;

	[BoxGroup("Cinematic Orbit"), SerializeField, Label("Auto-Engage When Idle")]
	bool autoCinematicWhenIdle = true;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(1f, 30f), Label("Idle Delay  s")]
	float idleTimeToCinematic = 4.0f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Label("Exit On Movement")]
	bool exitCinematicOnMove = true;

	[BoxGroup("Cinematic Orbit"), SerializeField, Label("Toggle Key (C / D-Pad Up)")]
	bool allowKeyToggle = true;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(-90f, 90f), Label("Orbit Speed  °/s")]
	float cinematicOrbitSpeed = 6f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Label("Keep Driving Distance")]
	bool matchDrivingDistance = true;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(2f, 50f), Label("Custom Distance  m")]
	float cinematicDistance = 15f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Label("Keep Driving Pitch")]
	bool matchDrivingPitch = true;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0f, 85f), Label("Custom Pitch  °")]
	float cinematicBasePitch = 22f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0f, 30f), Label("Pitch Wave Amp  °")]
	float cinematicPitchWaveAmp = 0f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0.02f, 2f), Label("Wave Frequency  Hz")]
	float cinematicWaveFrequency = 0.2f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0f, 10f), Label("Distance Breath Amp  m")]
	float cinematicDistanceBreath = 0f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0.5f, 10f), Label("Transition Speed")]
	float cinematicTransitionSpeed = 1.5f;

	// ── Rotation ──────────────────────────────────────────────────────

	[BoxGroup("Rotation"), SerializeField, Range(1f, 360f), Label("Manual Speed  °/s")]
	float rotationSpeed = 90f;

	[BoxGroup("Rotation"), SerializeField, Min(0f), Label("Auto Delay  s")]
	float alignDelay = 0.5f;

	[BoxGroup("Rotation"), SerializeField, Range(0f, 90f), Label("Smooth Range  °")]
	float alignSmoothRange = 45f;

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
	Vector3 smoothedFocusForward;

	Vector2 orbitAngles = new Vector2(45f, 0f);
	float lastManualRotationTime;
	Quaternion orbitRotation;

	bool _manualCinematic;
	bool _idleCinematic;
	float _idleTimer;
	float _cinematicWeight;
	float _cinematicWaveTimer;
	float _cinematicTargetDistance;

	public bool IsCinematicActive => enableCinematicOrbit && (_manualCinematic || _idleCinematic);
	public float CinematicWeight => _cinematicWeight;

	public void SetCinematicMode (bool active) {
		_manualCinematic = active;
		_idleTimer = 0f;
		_idleCinematic = false;
	}

	public void ToggleCinematicMode () => SetCinematicMode(!IsCinematicActive);

	void OnValidate () {
		if (maxVerticalAngle < minVerticalAngle)
			maxVerticalAngle = minVerticalAngle;
	}

	void Awake () {
		gravityAlignment.Identity();
		regularCamera = GetComponent<Camera>();
		pose.Bind(gameObject);
		pose.SetFadeEnabled(revealThroughToonObjects);
		_cinematicTargetDistance = distance;
		lookInput = CameraLookInput.CreateDriving();

		if (focus != null)
			SetFocus(focus);
	}

	void OnEnable () {
		lookInput?.Enable();
		pose.SetFadeEnabled(revealThroughToonObjects);
	}

	void OnDisable () {
		pose.SetFadeEnabled(false);
		lookInput?.Disable();
	}

	void OnDestroy () {
		lookInput?.Dispose();
	}

	void LateUpdate () {
		if (focus == null && autoFindLocalPlayer) {
			Transform found = CameraFollowTarget.FindLocalFocus();
			if (found != null)
				SetFocus(found);
		}

		if (focus == null) {
			pose.ClearTarget();
			return;
		}

		gravityAlignment.Update(focusTracker.Point, upAlignmentSpeed, Time.deltaTime);
		Vector3 targetPoint = focus.position + gravityAlignment.Up * focusHeightOffset;
		focusTracker.Update(targetPoint, focusRadius, focusCentering,
			pose.Obstruction, focus, obstructionMask);

		UpdateCinematicState();
		UpdateOrbitYaw();
		UpdatePitch();
		CameraOrbitAngles.WrapYawAndClampPitch(ref orbitAngles, minVerticalAngle, maxVerticalAngle);

		orbitRotation = Quaternion.Euler(orbitAngles);
		float currentDist = Mathf.Lerp(distance, _cinematicTargetDistance, _cinematicWeight);
		pose.Apply(transform, regularCamera, focus, focusTracker.Point,
			gravityAlignment.Rotation * orbitRotation, currentDist, rotationSmoothSpeed,
			obstructionMask, collisionRecoverySpeed, minimumComfortDistance, revealThroughToonObjects);
	}

	public void SetFocus (Transform target) {
		if (target == null) return;

		focus = target;
		pose.ResetBoom();
		focusBody = focus.GetComponent<Rigidbody>();
		gravityAlignment.SnapAt(focus.position);
		focusTracker.Snap(focus.position + gravityAlignment.Up * focusHeightOffset);

		smoothedFocusForward = focus.forward;
		Vector3 localFwd = Quaternion.Inverse(gravityAlignment.Rotation) * smoothedFocusForward;
		Vector2 flatFwd = new Vector2(localFwd.x, localFwd.z);
		if (flatFwd.sqrMagnitude > 0.0001f)
			orbitAngles.y = CameraOrbitAngles.GetAngle(flatFwd.normalized);

		transform.rotation = gravityAlignment.Rotation * (orbitRotation = Quaternion.Euler(orbitAngles));
	}

	void UpdateCinematicState () {
		if (!enableCinematicOrbit) {
			_manualCinematic = false;
			_idleCinematic = false;
			_cinematicWeight = Mathf.MoveTowards(_cinematicWeight, 0f, cinematicTransitionSpeed * Time.deltaTime);
			_cinematicTargetDistance = distance;
			return;
		}

		if (allowKeyToggle && lookInput != null && lookInput.CinematicTogglePressed()) {
			ToggleCinematicMode();
			if (_manualCinematic) _cinematicWaveTimer = 0f;
		}

		float rawSpeed = focusBody != null ? focusBody.linearVelocity.magnitude : 0f;
		Vector2 look = lookInput != null ? lookInput.ReadLook() : Vector2.zero;

		if (rawSpeed < 0.8f && look.sqrMagnitude < 0.01f && !pose.IsObstructed) {
			_idleTimer += Time.deltaTime;
			if (autoCinematicWhenIdle && _idleTimer >= idleTimeToCinematic)
				_idleCinematic = true;
		} else {
			_idleTimer = 0f;
			_idleCinematic = false;
			if (exitCinematicOnMove && rawSpeed > 1.8f)
				_manualCinematic = false;
		}

		bool active = _manualCinematic || _idleCinematic;
		_cinematicWeight = Mathf.MoveTowards(_cinematicWeight, active ? 1f : 0f, cinematicTransitionSpeed * Time.deltaTime);

		if (_cinematicWeight > 0.001f) {
			float targetDist = matchDrivingDistance ? distance : cinematicDistance;
			if (cinematicDistanceBreath > 0f) {
				_cinematicWaveTimer += Time.deltaTime;
				targetDist += Mathf.Cos(_cinematicWaveTimer * cinematicWaveFrequency * Mathf.PI * 2f) * cinematicDistanceBreath;
			}
			_cinematicTargetDistance = targetDist;
		} else {
			_cinematicTargetDistance = distance;
		}
	}

	void UpdateOrbitYaw () {
		if (ManualRotation()) {
			CameraOrbitAngles.WrapYawAndClampPitch(ref orbitAngles, minVerticalAngle, maxVerticalAngle);
			return;
		}

		if (IsCinematicActive || _cinematicWeight > 0.01f) {
			orbitAngles.y += cinematicOrbitSpeed * Time.deltaTime;
			CameraOrbitAngles.WrapYawAndClampPitch(ref orbitAngles, minVerticalAngle, maxVerticalAngle);
			return;
		}

		if (AutomaticRotation())
			CameraOrbitAngles.WrapYawAndClampPitch(ref orbitAngles, minVerticalAngle, maxVerticalAngle);
	}

	bool ManualRotation () {
		if (lookInput == null) return false;
		float x = lookInput.ReadLook().x;
		const float e = 0.001f;
		if (x < -e || x > e) {
			orbitAngles.y += rotationSpeed * Time.unscaledDeltaTime * x;
			lastManualRotationTime = Time.unscaledTime;
			return true;
		}
		return false;
	}

	void UpdatePitch () {
		float rawSpeed = focusBody != null ? focusBody.linearVelocity.magnitude : 0f;
		if (rawSpeed < 1f) rawSpeed = 0f;
		smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed, 3f * Time.deltaTime);
		if (smoothedSpeed < 0.05f) smoothedSpeed = 0f;

		float t = Mathf.Clamp01(smoothedSpeed / speedForFullAngle);
		float drivingPitch = Mathf.Lerp(topDownAngle, behindAngle, t);

		if (_cinematicWeight > 0.001f && !matchDrivingPitch) {
			float targetPitch = cinematicBasePitch;
			if (cinematicPitchWaveAmp > 0f)
				targetPitch += Mathf.Sin(_cinematicWaveTimer * cinematicWaveFrequency * Mathf.PI * 2f) * cinematicPitchWaveAmp;
			float blendedPitch = Mathf.Lerp(drivingPitch, targetPitch, _cinematicWeight);
			orbitAngles.x = Mathf.Lerp(orbitAngles.x, blendedPitch, pitchSmoothSpeed * Time.deltaTime);
		} else {
			orbitAngles.x = Mathf.Lerp(orbitAngles.x, drivingPitch, pitchSmoothSpeed * Time.deltaTime);
		}
	}

	bool AutomaticRotation () {
		if (Time.unscaledTime - lastManualRotationTime < alignDelay)
			return false;

		smoothedFocusForward = Vector3.Slerp(
			smoothedFocusForward, focus.forward, 12f * Time.unscaledDeltaTime);

		Vector3 facingAligned = Quaternion.Inverse(gravityAlignment.Rotation) * smoothedFocusForward;
		Vector2 headingVector = new Vector2(facingAligned.x, facingAligned.z);
		if (headingVector.sqrMagnitude < 0.0001f)
			return false;

		float headingAngle = CameraOrbitAngles.GetAngle(headingVector.normalized);
		float deltaAbs = Mathf.Abs(Mathf.DeltaAngle(orbitAngles.y, headingAngle));
		float rotationChange = rotationSpeed * Time.unscaledDeltaTime;
		if (deltaAbs < alignSmoothRange)
			rotationChange *= deltaAbs / alignSmoothRange;
		else if (180f - deltaAbs < alignSmoothRange)
			rotationChange *= (180f - deltaAbs) / alignSmoothRange;

		orbitAngles.y = Mathf.MoveTowardsAngle(orbitAngles.y, headingAngle, rotationChange);
		return true;
	}
}
