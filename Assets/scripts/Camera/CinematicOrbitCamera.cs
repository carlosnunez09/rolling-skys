using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

/// <summary>
/// Dedicated cinematic orbit camera designed for vehicle showcases, track panoramas,
/// intro countdown sequences, victory screens, and photo mode in Rolling Skys.
///
/// Features:
///  - Arbitrary & spherical gravity alignment via CustomGravity (works seamlessly on planets, walls, loops, and ceilings).
///  - Continuous smooth panoramic orbiting with adjustable speed and direction.
///  - Sinusoidal elevation tilt wave (dynamic drone / crane sweep).
///  - Distance breathing (subtle dolly zoom / breathing effect).
///  - Smooth target tracking with configurable lag radius and centering damping.
///  - Obstruction avoidance with camera near-plane box casting to prevent clipping into terrain.
///  - Optional cinematic telephoto FOV easing.
///  - Optional interactive manual rotation and zoom override.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CinematicOrbitCamera : MonoBehaviour {

	// ── Target & Focus ─────────────────────────────────────────────────

	[BoxGroup("Target"), SerializeField]
	Transform target = default;

	[BoxGroup("Target"), SerializeField]
	bool autoFindPlayer = true;

	[BoxGroup("Target"), SerializeField, Label("Target Offset")]
	Vector3 targetOffset = new Vector3(0f, 0.8f, 0f);

	[BoxGroup("Target"), SerializeField, Range(0f, 10f), Label("Follow Lag Radius")]
	float focusLagRadius = 2.5f;

	[BoxGroup("Target"), SerializeField, Range(0f, 1f), Label("Centering Damping")]
	float focusCentering = 0.5f;

	// ── Orbit Dynamics ─────────────────────────────────────────────────

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(-90f, 90f), Label("Orbit Speed  °/s")]
	float orbitSpeed = 16f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(2f, 50f), Label("Distance  m")]
	float distance = 8.5f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0f, 85f), Label("Base Elevation  °")]
	float baseElevationAngle = 18f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0f, 30f), Label("Elevation Wave Amp  °")]
	float elevationWaveAmplitude = 7f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0.02f, 2f), Label("Elevation Wave Freq  Hz")]
	float elevationWaveFrequency = 0.18f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0f, 10f), Label("Distance Breath Amp  m")]
	float distanceBreathAmplitude = 1.0f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0.02f, 2f), Label("Distance Breath Freq  Hz")]
	float distanceBreathFrequency = 0.12f;

	// ── Alignment & Smoothing ──────────────────────────────────────────

	[BoxGroup("Smoothing"), SerializeField, Range(1f, 60f), Label("Rotation Smooth Speed")]
	float rotationSmoothSpeed = 12f;

	[BoxGroup("Smoothing"), SerializeField, Label("Align To Surface Gravity")]
	bool useGravityAlignment = true;

	[BoxGroup("Smoothing"), SerializeField, Min(10f), Label("Up Alignment Speed  °/s")]
	float upAlignmentSpeed = 240f;

	// ── Field of View & Lens ───────────────────────────────────────────

	[BoxGroup("Lens"), SerializeField, Label("Apply Cinematic FOV")]
	bool applyCinematicFOV = true;

	[BoxGroup("Lens"), SerializeField, Range(20f, 90f), Label("Cinematic FOV  °")]
	float cinematicFOV = 50f;

	[BoxGroup("Lens"), SerializeField, Range(0.5f, 10f), Label("FOV Smooth Speed")]
	float fovSmoothSpeed = 2.5f;

	// ── Occlusion & Obstruction ────────────────────────────────────────

	[BoxGroup("Occlusion"), SerializeField]
	LayerMask obstructionMask = -1;

	// ── Interactive Controls ───────────────────────────────────────────

	[BoxGroup("Interactive"), SerializeField, Label("Allow Manual Look / Zoom")]
	bool allowInteractiveControl = true;

	[BoxGroup("Interactive"), SerializeField, Range(10f, 180f), Label("Manual Rotate Speed  °/s")]
	float manualRotateSpeed = 90f;

	[BoxGroup("Interactive"), SerializeField, Range(1f, 20f), Label("Zoom Speed  m/s")]
	float zoomSpeed = 6f;

	// ── Runtime State ──────────────────────────────────────────────────

	Camera targetCamera;
	Vector3 focusPoint;
	Vector2 orbitAngles = new Vector2(18f, 0f);
	float waveTimer;
	float currentDistance;
	float defaultFOV;
	bool isPaused;
	Quaternion gravityAlignment = Quaternion.identity;

	InputAction lookAction;
	InputAction zoomAction;

	Vector3 CameraHalfExtends {
		get {
			Vector3 h;
			h.y = targetCamera.nearClipPlane * Mathf.Tan(0.5f * Mathf.Deg2Rad * targetCamera.fieldOfView);
			h.x = h.y * targetCamera.aspect;
			h.z = 0f;
			return h;
		}
	}

	public Transform Target => target;
	public float CurrentOrbitAngle => orbitAngles.y;
	public bool IsPaused => isPaused;

	void Awake () {
		targetCamera = GetComponent<Camera>();
		if (targetCamera != null) {
			defaultFOV = targetCamera.fieldOfView;
		}

		currentDistance = distance;
		orbitAngles.x = baseElevationAngle;

		lookAction = new InputAction("CinematicLook", InputActionType.Value);
		lookAction.AddCompositeBinding("2DVector")
			.With("Up",    "<Keyboard>/upArrow")
			.With("Down",  "<Keyboard>/downArrow")
			.With("Left",  "<Keyboard>/leftArrow")
			.With("Right", "<Keyboard>/rightArrow");
		lookAction.AddBinding("<Gamepad>/rightStick");

		zoomAction = new InputAction("CinematicZoom", InputActionType.Value);
		zoomAction.AddBinding("<Mouse>/scroll/y");
		zoomAction.AddCompositeBinding("1DAxis")
			.With("Positive", "<Gamepad>/rightTrigger")
			.With("Negative", "<Gamepad>/leftTrigger");

		if (target != null)
			SetTarget(target);
	}

	void OnEnable () {
		lookAction?.Enable();
		zoomAction?.Enable();
	}

	void OnDisable () {
		lookAction?.Disable();
		zoomAction?.Disable();
	}

	void OnDestroy () {
		lookAction?.Dispose();
		zoomAction?.Dispose();
	}

	void LateUpdate () {
		if (target == null && autoFindPlayer)
			TryFindPlayerTarget();

		if (target == null) return;

		UpdateGravityAlignment();
		UpdateFocusPoint();
		UpdateOrbitMovement();
		UpdateFOV();

		// Calculate desired orientation and position
		Quaternion orbitRotation = Quaternion.Euler(orbitAngles.x, orbitAngles.y, 0f);
		Quaternion lookRotation  = gravityAlignment * orbitRotation;

		Quaternion smoothedRotation = Quaternion.Slerp(
			transform.rotation, lookRotation,
			Mathf.Clamp01(rotationSmoothSpeed * Time.deltaTime));

		Vector3 lookDirection = smoothedRotation * Vector3.forward;
		Vector3 lookPosition  = focusPoint - lookDirection * currentDistance;

		// Occlusion avoidance with camera near-plane box cast
		Vector3 rectOffset   = lookDirection * targetCamera.nearClipPlane;
		Vector3 rectPosition = lookPosition + rectOffset;
		Vector3 castFrom     = focusPoint;
		Vector3 castLine     = rectPosition - castFrom;
		float   castDistance = castLine.magnitude;

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

	void UpdateOrbitMovement () {
		if (!isPaused) {
			// Continuous panoramic orbit
			orbitAngles.y += orbitSpeed * Time.deltaTime;
			if (orbitAngles.y >= 360f) orbitAngles.y -= 360f;
			else if (orbitAngles.y < 0f) orbitAngles.y += 360f;

			// Elevation & distance breathing waves
			waveTimer += Time.deltaTime;
			float targetElevation = baseElevationAngle + Mathf.Sin(waveTimer * elevationWaveFrequency * Mathf.PI * 2f) * elevationWaveAmplitude;
			orbitAngles.x = Mathf.Lerp(orbitAngles.x, targetElevation, 4f * Time.deltaTime);

			float targetDist = distance + Mathf.Cos(waveTimer * distanceBreathFrequency * Mathf.PI * 2f) * distanceBreathAmplitude;
			currentDistance = Mathf.Lerp(currentDistance, targetDist, 3f * Time.deltaTime);
		}

		// Optional interactive look / zoom override
		if (allowInteractiveControl) {
			Vector2 look = lookAction.ReadValue<Vector2>();
			if (Mathf.Abs(look.x) > 0.01f) {
				orbitAngles.y += look.x * manualRotateSpeed * Time.unscaledDeltaTime;
			}
			if (Mathf.Abs(look.y) > 0.01f) {
				orbitAngles.x = Mathf.Clamp(orbitAngles.x - look.y * manualRotateSpeed * 0.5f * Time.unscaledDeltaTime, 0f, 85f);
			}

			float zoomVal = zoomAction.ReadValue<float>();
			if (Mathf.Abs(zoomVal) > 0.01f) {
				distance = Mathf.Clamp(distance - Mathf.Sign(zoomVal) * zoomSpeed * Time.unscaledDeltaTime, 2f, 50f);
			}
		}
	}

	void UpdateFocusPoint () {
		Vector3 worldOffset = gravityAlignment * targetOffset;
		Vector3 targetPoint = target.position + worldOffset;

		if (focusLagRadius > 0f) {
			float dist = Vector3.Distance(targetPoint, focusPoint);
			float t = 1f;
			if (dist > 0.01f && focusCentering > 0f)
				t = Mathf.Pow(1f - focusCentering, Time.unscaledDeltaTime);
			if (dist > focusLagRadius)
				t = Mathf.Min(t, focusLagRadius / dist);
			focusPoint = Vector3.Lerp(targetPoint, focusPoint, t);
		} else {
			focusPoint = targetPoint;
		}
	}

	void UpdateGravityAlignment () {
		if (!useGravityAlignment) {
			gravityAlignment = Quaternion.identity;
			return;
		}

		Vector3 fromUp = gravityAlignment * Vector3.up;
		Vector3 toUp   = CustomGravity.GetUpAxis(focusPoint);

		if (toUp.sqrMagnitude < 0.001f) return;

		float dot      = Mathf.Clamp(Vector3.Dot(fromUp, toUp), -1f, 1f);
		float angle    = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float maxAngle = upAlignmentSpeed * Time.deltaTime;

		Quaternion newAlignment = Quaternion.FromToRotation(fromUp, toUp) * gravityAlignment;
		if (float.IsNaN(newAlignment.x) || float.IsNaN(newAlignment.y) ||
		    float.IsNaN(newAlignment.z) || float.IsNaN(newAlignment.w)) return;

		gravityAlignment = angle <= maxAngle
			? newAlignment
			: Quaternion.SlerpUnclamped(gravityAlignment, newAlignment, maxAngle / angle);
	}

	void UpdateFOV () {
		if (targetCamera == null) return;

		float targetFov = applyCinematicFOV ? cinematicFOV : defaultFOV;
		targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, targetFov, fovSmoothSpeed * Time.deltaTime);
	}

	void TryFindPlayerTarget () {
		var cars = FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude);
		foreach (MovingCar car in cars) {
			if (car != null && car.HasLocalControl) {
				SetTarget(car.transform);
				return;
			}
		}
		foreach (MovingCar car in cars) {
			if (car != null && !car.IsSpawned) {
				SetTarget(car.transform);
				return;
			}
		}
	}

	// ── Public API ─────────────────────────────────────────────────────

	public void SetTarget (Transform newTarget) {
		if (newTarget == null) return;
		target = newTarget;
		focusPoint = target.position + (gravityAlignment * targetOffset);

		if (useGravityAlignment) {
			Vector3 startUp = CustomGravity.GetUpAxis(focusPoint);
			gravityAlignment = startUp.sqrMagnitude > 0.001f
				? Quaternion.FromToRotation(Vector3.up, startUp)
				: Quaternion.identity;
		}
	}

	public void SetOrbitSpeed (float speed)       => orbitSpeed = speed;
	public void SetDistance (float newDistance)   => distance = Mathf.Max(newDistance, 1f);
	public void SetElevation (float angle)        => baseElevationAngle = Mathf.Clamp(angle, 0f, 85f);
	public void PauseOrbit ()                     => isPaused = true;
	public void ResumeOrbit ()                    => isPaused = false;
	public void TogglePause ()                    => isPaused = !isPaused;
}
