using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

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

    readonly CameraObstructionSolver obstructionSolver = new CameraObstructionSolver();
    CameraOcclusionFade occlusionFade;

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

	// Final-stage rotation damping — kills sub-frame flicker without visible lag.
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
	Vector3 smoothedFocusForward;   // initialised from focus.forward in Awake

	InputAction lookAction;
	InputAction cinematicToggleAction;

	Vector3 focusPoint, previousFocusPoint;

	Vector2 orbitAngles = new Vector2(45f, 0f);

	float lastManualRotationTime;

	Quaternion gravityAlignment = Quaternion.identity;  // overwritten in Awake

	Quaternion orbitRotation;

	// Cinematic Orbit Runtime State
	bool _manualCinematic;
	bool _idleCinematic;
	float _idleTimer;
	float _cinematicWeight;
	float _cinematicWaveTimer;
	float _cinematicTargetDistance;

	public bool IsCinematicActive => enableCinematicOrbit && (_manualCinematic || _idleCinematic);
	public float CinematicWeight  => _cinematicWeight;

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
		regularCamera = GetComponent<Camera>();
        occlusionFade = GetComponent<CameraOcclusionFade>();
        if (occlusionFade == null) occlusionFade = gameObject.AddComponent<CameraOcclusionFade>();
        occlusionFade.enabled = revealThroughToonObjects;
		_cinematicTargetDistance = distance;

		lookAction = new InputAction("Look", InputActionType.Value);
		lookAction.AddCompositeBinding("2DVector")
			.With("Up",    "<Keyboard>/upArrow")
			.With("Down",  "<Keyboard>/downArrow")
			.With("Left",  "<Keyboard>/leftArrow")
			.With("Right", "<Keyboard>/rightArrow");
		lookAction.AddBinding("<Gamepad>/rightStick");

		cinematicToggleAction = new InputAction("CinematicToggle", InputActionType.Button);
		cinematicToggleAction.AddBinding("<Keyboard>/c");
		cinematicToggleAction.AddBinding("<Gamepad>/dpad/up");
		cinematicToggleAction.AddBinding("<Gamepad>/select");

		if (focus != null)
			SetFocus(focus);
	}

	void OnEnable () {
		lookAction?.Enable();
		cinematicToggleAction?.Enable();
	}

	void OnDisable () {
        if (occlusionFade != null) occlusionFade.enabled = false;
		lookAction?.Disable();
		cinematicToggleAction?.Disable();
	}

	void OnDestroy () {
		lookAction?.Dispose();
		cinematicToggleAction?.Dispose();
	}

	void LateUpdate () {
		if (focus == null && autoFindLocalPlayer)
			TryAssignLocalPlayerFocus();

		if (focus == null) {
            occlusionFade.SetTarget(null, Vector3.zero);
            return;
        }

		UpdateGravityAlignment();
		UpdateFocusPoint();

		UpdateCinematicState();

		if (ManualRotation()) {
			ConstrainAngles();
		} else if (IsCinematicActive || _cinematicWeight > 0.01f) {
			orbitAngles.y += cinematicOrbitSpeed * Time.deltaTime;
			ConstrainAngles();
		} else if (AutomaticRotation()) {
			ConstrainAngles();
		}

		UpdatePitch();
        ConstrainAngles();

		orbitRotation = Quaternion.Euler(orbitAngles);
		Quaternion lookRotation = gravityAlignment * orbitRotation;

		// Smooth rotation FIRST, then derive the position from the smoothed rotation.
		Quaternion smoothedRotation = Quaternion.Slerp(
			transform.rotation, lookRotation,
			1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));

		float currentDist = Mathf.Lerp(distance, _cinematicTargetDistance, _cinematicWeight);
		Vector3 lookDirection = smoothedRotation * Vector3.forward;
		Vector3 lookPosition  = focusPoint - lookDirection * currentDist;

        lookPosition = obstructionSolver.Resolve(regularCamera, focus, focusPoint, -lookDirection,
            currentDist, obstructionMask, collisionRecoverySpeed, minimumComfortDistance, revealThroughToonObjects);
        occlusionFade.enabled = revealThroughToonObjects;
        if (revealThroughToonObjects) occlusionFade.SetTarget(focus, focus.position);

		transform.SetPositionAndRotation(lookPosition, smoothedRotation);
	}

	public void SetFocus (Transform target) {
		if (target == null) return;

		focus = target;
        obstructionSolver.Reset();
		focusBody  = focus.GetComponent<Rigidbody>();
		Vector3 startUp = CustomGravity.GetUpAxis(focus.position);
		gravityAlignment = startUp.sqrMagnitude > 0.001f
			? Quaternion.FromToRotation(Vector3.up, startUp)
			: Quaternion.identity;

		focusPoint = previousFocusPoint = focus.position + (gravityAlignment * Vector3.up) * focusHeightOffset;

		smoothedFocusForward = focus.forward;

		Vector3 localFwd = Quaternion.Inverse(gravityAlignment) * smoothedFocusForward;
		Vector2 flatFwd  = new Vector2(localFwd.x, localFwd.z);
		if (flatFwd.sqrMagnitude > 0.0001f)
			orbitAngles.y = GetAngle(flatFwd.normalized);

		transform.rotation = gravityAlignment * (orbitRotation = Quaternion.Euler(orbitAngles));
	}

	void TryAssignLocalPlayerFocus () {
		var cars = FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude);

		foreach (MovingCar car in cars) {
			if (car != null && car.HasLocalControl) {
				SetFocus(car.transform);
				return;
			}
		}

		foreach (MovingCar car in cars) {
			if (car != null && !car.IsSpawned) {
				SetFocus(car.transform);
				return;
			}
		}
	}

	void UpdateGravityAlignment () {
		Vector3 fromUp = gravityAlignment * Vector3.up;
		Vector3 toUp   = CustomGravity.GetUpAxis(focusPoint);

		// Guard: zero up-axis (no gravity source active) would produce NaN in
		// FromToRotation and corrupt the alignment permanently.
		if (toUp.sqrMagnitude < 0.001f) return;

		float dot      = Mathf.Clamp(Vector3.Dot(fromUp, toUp), -1f, 1f);
		float angle    = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float maxAngle = upAlignmentSpeed * Time.deltaTime;

		Quaternion newAlignment = Quaternion.FromToRotation(fromUp, toUp) * gravityAlignment;

		// Guard against a NaN result from degenerate input.
		if (float.IsNaN(newAlignment.x) || float.IsNaN(newAlignment.y) ||
		    float.IsNaN(newAlignment.z) || float.IsNaN(newAlignment.w)) return;

		gravityAlignment = angle <= maxAngle
			? newAlignment
			: Quaternion.SlerpUnclamped(gravityAlignment, newAlignment, maxAngle / angle);
	}

	void UpdateFocusPoint () {
		previousFocusPoint = focusPoint;
		Vector3 targetPoint = focus.position + (gravityAlignment * Vector3.up) * focusHeightOffset;
		if (focusRadius > 0f) {
			float dist = Vector3.Distance(targetPoint, focusPoint);
			float t = 1f;
			if (dist > 0.01f && focusCentering > 0f)
				t = Mathf.Pow(1f - focusCentering, Time.unscaledDeltaTime);
			if (dist > focusRadius)
				t = Mathf.Min(t, focusRadius / dist);
			focusPoint = Vector3.Lerp(targetPoint, focusPoint, t);
            // Lag must not drag the tracking pivot through a wall or leave it far behind a fast car.
            focusPoint = targetPoint + Vector3.ClampMagnitude(focusPoint - targetPoint, 1.5f);
            if (obstructionSolver.IsPathBlocked(targetPoint, focusPoint, focus, obstructionMask))
                focusPoint = targetPoint;
		} else {
			focusPoint = targetPoint;
		}
	}

	void UpdateCinematicState () {
		if (!enableCinematicOrbit) {
			_manualCinematic = false;
			_idleCinematic   = false;
			_cinematicWeight = Mathf.MoveTowards(_cinematicWeight, 0f, cinematicTransitionSpeed * Time.deltaTime);
			_cinematicTargetDistance = distance;
			return;
		}

		// Key toggle
		if (allowKeyToggle && cinematicToggleAction != null && cinematicToggleAction.WasPressedThisFrame()) {
			ToggleCinematicMode();
			if (_manualCinematic) _cinematicWaveTimer = 0f;
		}

		// Idle check based on focus vehicle speed and look input
		float rawSpeed = focusBody != null ? focusBody.linearVelocity.magnitude : 0f;
		Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

		if (rawSpeed < 0.8f && lookInput.sqrMagnitude < 0.01f && !obstructionSolver.IsObstructed) {
			_idleTimer += Time.deltaTime;
			if (autoCinematicWhenIdle && _idleTimer >= idleTimeToCinematic) {
				_idleCinematic = true;
			}
		} else {
			_idleTimer = 0f;
			_idleCinematic = false;
			if (exitCinematicOnMove && rawSpeed > 1.8f) {
				_manualCinematic = false;
			}
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

	bool ManualRotation () {
		float x = lookAction.ReadValue<Vector2>().x;
		const float e = 0.001f;
		if (x < -e || x > e) {
			orbitAngles.y         += rotationSpeed * Time.unscaledDeltaTime * x;
			lastManualRotationTime = Time.unscaledTime;
			return true;
		}
		return false;
	}

	void UpdatePitch () {
		float rawSpeed = focusBody != null ? focusBody.linearVelocity.magnitude : 0f;
		// Dead-zone: physics solver noise up to ~0.5 m/s when stopped.
		if (rawSpeed < 1f) rawSpeed = 0f;
		smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed, 3f * Time.deltaTime);
		if (smoothedSpeed < 0.05f) smoothedSpeed = 0f;

		float t           = Mathf.Clamp01(smoothedSpeed / speedForFullAngle);
		float drivingPitch = Mathf.Lerp(topDownAngle, behindAngle, t);

		if (_cinematicWeight > 0.001f && !matchDrivingPitch) {
			float targetPitch = cinematicBasePitch;
			if (cinematicPitchWaveAmp > 0f) {
				targetPitch += Mathf.Sin(_cinematicWaveTimer * cinematicWaveFrequency * Mathf.PI * 2f) * cinematicPitchWaveAmp;
			}
			float blendedPitch = Mathf.Lerp(drivingPitch, targetPitch, _cinematicWeight);
			orbitAngles.x = Mathf.Lerp(orbitAngles.x, blendedPitch, pitchSmoothSpeed * Time.deltaTime);
		} else {
			orbitAngles.x = Mathf.Lerp(orbitAngles.x, drivingPitch, pitchSmoothSpeed * Time.deltaTime);
		}
	}

	bool AutomaticRotation () {
		if (Time.unscaledTime - lastManualRotationTime < alignDelay)
			return false;

		// Smooth the car's facing direction so discrete physics rotations don't
		// jitter the auto-yaw tracking.
		smoothedFocusForward = Vector3.Slerp(
			smoothedFocusForward, focus.forward, 12f * Time.unscaledDeltaTime);

		Vector3 facingAligned = Quaternion.Inverse(gravityAlignment) * smoothedFocusForward;
		Vector2 headingVector = new Vector2(facingAligned.x, facingAligned.z);

		if (headingVector.sqrMagnitude < 0.0001f)
			return false;

		float headingAngle   = GetAngle(headingVector.normalized);
		float deltaAbs       = Mathf.Abs(Mathf.DeltaAngle(orbitAngles.y, headingAngle));
		float rotationChange = rotationSpeed * Time.unscaledDeltaTime;
		if (deltaAbs < alignSmoothRange)
			rotationChange *= deltaAbs / alignSmoothRange;
		else if (180f - deltaAbs < alignSmoothRange)
			rotationChange *= (180f - deltaAbs) / alignSmoothRange;

		orbitAngles.y = Mathf.MoveTowardsAngle(orbitAngles.y, headingAngle, rotationChange);
		return true;
	}

	void ConstrainAngles () {
		orbitAngles.x = Mathf.Clamp(orbitAngles.x, minVerticalAngle, maxVerticalAngle);
		if      (orbitAngles.y <   0f) orbitAngles.y += 360f;
		else if (orbitAngles.y >= 360f) orbitAngles.y -= 360f;
	}

	static float GetAngle (Vector2 direction) {
		float angle = Mathf.Acos(direction.y) * Mathf.Rad2Deg;
		return direction.x < 0f ? 360f - angle : angle;
	}
}
