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
	float cinematicOrbitSpeed = 15f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(2f, 50f), Label("Cinematic Distance  m")]
	float cinematicDistance = 8.5f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0f, 85f), Label("Base Pitch  °")]
	float cinematicBasePitch = 18f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0f, 30f), Label("Pitch Wave Amp  °")]
	float cinematicPitchWaveAmp = 6f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0.02f, 2f), Label("Wave Frequency  Hz")]
	float cinematicWaveFrequency = 0.2f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0f, 10f), Label("Distance Breath Amp  m")]
	float cinematicDistanceBreath = 1.0f;

	[BoxGroup("Cinematic Orbit"), SerializeField, Range(0.5f, 10f), Label("Transition Speed")]
	float cinematicTransitionSpeed = 2.5f;

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
		if (!active) _idleCinematic = false;
	}

	public void ToggleCinematicMode () => SetCinematicMode(!IsCinematicActive);

	Vector3 CameraHalfExtends {
		get {
			Vector3 h;
			h.y = regularCamera.nearClipPlane *
			      Mathf.Tan(0.5f * Mathf.Deg2Rad * regularCamera.fieldOfView);
			h.x = h.y * regularCamera.aspect;
			h.z = 0f;
			return h;
		}
	}

	void OnValidate () {
		if (maxVerticalAngle < minVerticalAngle)
			maxVerticalAngle = minVerticalAngle;
	}

	void Awake () {
		regularCamera = GetComponent<Camera>();
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

		if (focus == null) return;

		UpdateGravityAlignment();
		UpdateFocusPoint();

		UpdateCinematicState();

		if (ManualRotation()) {
			ConstrainAngles();
		} else if (_cinematicWeight > 0.95f) {
			orbitAngles.y += cinematicOrbitSpeed * Time.deltaTime;
			ConstrainAngles();
		} else {
			if (AutomaticRotation())
				ConstrainAngles();
			if (_cinematicWeight > 0.001f) {
				orbitAngles.y += cinematicOrbitSpeed * _cinematicWeight * Time.deltaTime;
				ConstrainAngles();
			}
		}

		UpdatePitch();

		orbitRotation = Quaternion.Euler(orbitAngles);
		Quaternion lookRotation = gravityAlignment * orbitRotation;

		// Smooth rotation FIRST, then derive the position from the smoothed rotation.
		Quaternion smoothedRotation = Quaternion.Slerp(
			transform.rotation, lookRotation,
			Mathf.Clamp01(rotationSmoothSpeed * Time.deltaTime));

		float currentDist = Mathf.Lerp(distance, _cinematicTargetDistance, _cinematicWeight);
		Vector3 lookDirection = smoothedRotation * Vector3.forward;
		Vector3 lookPosition  = focusPoint - lookDirection * currentDist;

		// Occlusion — box cast from the focus point toward the camera's near plane.
		Vector3 rectOffset    = lookDirection * regularCamera.nearClipPlane;
		Vector3 rectPosition  = lookPosition + rectOffset;
		Vector3 castFrom      = focusPoint;
		Vector3 castLine      = rectPosition - castFrom;
		float   castDistance  = castLine.magnitude;

		if (castDistance > 0.001f) {
			Vector3 castDirection = castLine / castDistance;
			if (Physics.BoxCast(
				castFrom, CameraHalfExtends, castDirection, out RaycastHit hit,
				smoothedRotation, castDistance, obstructionMask
			)) {
				rectPosition = castFrom + castDirection * hit.distance;
				lookPosition = rectPosition - rectOffset;
			}
		}

		transform.SetPositionAndRotation(lookPosition, smoothedRotation);
	}

	public void SetFocus (Transform target) {
		if (target == null) return;

		focus = target;
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

		transform.localRotation = orbitRotation = Quaternion.Euler(orbitAngles);
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
			_manualCinematic = !_manualCinematic;
			if (_manualCinematic) _cinematicWaveTimer = 0f;
		}

		// Idle check based on focus vehicle speed and look input
		float rawSpeed = focusBody != null ? focusBody.linearVelocity.magnitude : 0f;
		Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

		if (rawSpeed < 0.8f && lookInput.sqrMagnitude < 0.01f) {
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
			_cinematicWaveTimer += Time.deltaTime;
			float distBreath = Mathf.Cos(_cinematicWaveTimer * cinematicWaveFrequency * Mathf.PI * 2f) * cinematicDistanceBreath;
			_cinematicTargetDistance = cinematicDistance + distBreath;
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

		if (_cinematicWeight > 0.001f) {
			float wavePitch = cinematicBasePitch + Mathf.Sin(_cinematicWaveTimer * cinematicWaveFrequency * Mathf.PI * 2f) * cinematicPitchWaveAmp;
			float targetPitch = Mathf.Lerp(drivingPitch, wavePitch, _cinematicWeight);
			orbitAngles.x = Mathf.Lerp(orbitAngles.x, targetPitch, pitchSmoothSpeed * Time.deltaTime);
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
