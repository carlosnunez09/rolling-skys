using UnityEngine;
using NaughtyAttributes;

/// <summary>
/// Dedicated showcase / photo-mode orbit. Gameplay sequences (countdown, finish,
/// tutorial, idle) use <see cref="OrbitCamera.SetCinematicMode"/> on the driving
/// camera — do not stack this component on the same GameObject as
/// <see cref="OrbitCamera"/>.
///
/// Shares gravity, focus lag, local-player binding, and the obstruction/cutaway
/// boom with the driving camera via <see cref="FollowCameraPose"/>.
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
	float orbitSpeed = 6f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(2f, 50f), Label("Distance  m")]
	float distance = 15f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0f, 85f), Label("Base Elevation  °")]
	float baseElevationAngle = 20f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0f, 30f), Label("Elevation Wave Amp  °")]
	float elevationWaveAmplitude = 0f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0.02f, 2f), Label("Elevation Wave Freq  Hz")]
	float elevationWaveFrequency = 0.18f;

	[BoxGroup("Orbit Dynamics"), SerializeField, Range(0f, 10f), Label("Distance Breath Amp  m")]
	float distanceBreathAmplitude = 0f;

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

	[BoxGroup("Occlusion"), SerializeField, Min(0.1f)]
	float collisionRecoverySpeed = 4f;

	[BoxGroup("Occlusion"), SerializeField, Min(0.5f)]
	float minimumComfortDistance = 3f;

	[BoxGroup("Occlusion"), SerializeField]
	bool revealThroughToonObjects = true;

	readonly FollowCameraPose pose = new FollowCameraPose();
	readonly CameraFocusTracker focusTracker = new CameraFocusTracker();
	CameraGravityAlignment gravityAlignment;
	CameraLookInput lookInput;

	// ── Interactive Controls ───────────────────────────────────────────

	[BoxGroup("Interactive"), SerializeField, Label("Allow Manual Look / Zoom")]
	bool allowInteractiveControl = true;

	[BoxGroup("Interactive"), SerializeField, Range(10f, 180f), Label("Manual Rotate Speed  °/s")]
	float manualRotateSpeed = 90f;

	[BoxGroup("Interactive"), SerializeField, Range(1f, 20f), Label("Zoom Speed  m/s")]
	float zoomSpeed = 6f;

	Camera targetCamera;
	Vector2 orbitAngles = new Vector2(18f, 0f);
	float waveTimer;
	float currentDistance;
	float defaultFOV;
	bool isPaused;

	public Transform Target => target;
	public float CurrentOrbitAngle => orbitAngles.y;
	public bool IsPaused => isPaused;

	void Awake () {
		gravityAlignment.Identity();
		targetCamera = GetComponent<Camera>();
		pose.Bind(gameObject);
		if (targetCamera != null)
			defaultFOV = targetCamera.fieldOfView;

		currentDistance = distance;
		orbitAngles.x = baseElevationAngle;
		lookInput = CameraLookInput.CreateCinematic();

		if (target != null)
			SetTarget(target);
	}

	void OnEnable () {
		OrbitCamera driving = GetComponent<OrbitCamera>();
		if (driving != null && driving.enabled) {
			Debug.LogWarning(
				"CinematicOrbitCamera is on the same GameObject as OrbitCamera; disabling the showcase camera. Use OrbitCamera.SetCinematicMode for gameplay cinematics.",
				this);
			enabled = false;
			return;
		}

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
		if (target == null && autoFindPlayer) {
			Transform found = CameraFollowTarget.FindLocalFocus();
			if (found != null)
				SetTarget(found);
		}

		if (target == null) {
			pose.ClearTarget();
			return;
		}

		UpdateGravityAlignment();
		Vector3 worldOffset = gravityAlignment.Rotation * targetOffset;
		focusTracker.Update(target.position + worldOffset, focusLagRadius, focusCentering,
			pose.Obstruction, target, obstructionMask);
		UpdateOrbitMovement();
		UpdateFOV();

		Quaternion lookRotation = gravityAlignment.Rotation * Quaternion.Euler(orbitAngles.x, orbitAngles.y, 0f);
		pose.Apply(transform, targetCamera, target, focusTracker.Point,
			lookRotation, currentDistance, rotationSmoothSpeed,
			obstructionMask, collisionRecoverySpeed, minimumComfortDistance, revealThroughToonObjects);
	}

	void UpdateOrbitMovement () {
		if (!isPaused) {
			orbitAngles.y += orbitSpeed * Time.deltaTime;
			CameraOrbitAngles.WrapYaw(ref orbitAngles);

			if (elevationWaveAmplitude > 0f) {
				waveTimer += Time.deltaTime;
				float targetElevation = baseElevationAngle
					+ Mathf.Sin(waveTimer * elevationWaveFrequency * Mathf.PI * 2f) * elevationWaveAmplitude;
				orbitAngles.x = Mathf.Lerp(orbitAngles.x, targetElevation, 4f * Time.deltaTime);
			} else {
				orbitAngles.x = baseElevationAngle;
			}

			if (distanceBreathAmplitude > 0f) {
				float targetDist = distance
					+ Mathf.Cos(waveTimer * distanceBreathFrequency * Mathf.PI * 2f) * distanceBreathAmplitude;
				currentDistance = Mathf.Lerp(currentDistance, targetDist, 3f * Time.deltaTime);
			} else {
				currentDistance = distance;
			}
		}

		if (!allowInteractiveControl || lookInput == null)
			return;

		Vector2 look = lookInput.ReadLook();
		if (Mathf.Abs(look.x) > 0.01f)
			orbitAngles.y += look.x * manualRotateSpeed * Time.unscaledDeltaTime;
		if (Mathf.Abs(look.y) > 0.01f)
			orbitAngles.x = Mathf.Clamp(orbitAngles.x - look.y * manualRotateSpeed * 0.5f * Time.unscaledDeltaTime, 0f, 85f);

		float zoomVal = lookInput.ReadZoom();
		if (Mathf.Abs(zoomVal) > 0.01f)
			distance = Mathf.Clamp(distance - Mathf.Sign(zoomVal) * zoomSpeed * Time.unscaledDeltaTime, 2f, 50f);
	}

	void UpdateGravityAlignment () {
		if (!useGravityAlignment) {
			gravityAlignment.Identity();
			return;
		}

		gravityAlignment.Update(focusTracker.Point, upAlignmentSpeed, Time.deltaTime);
	}

	void UpdateFOV () {
		if (targetCamera == null) return;
		float targetFov = applyCinematicFOV ? cinematicFOV : defaultFOV;
		targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, targetFov, fovSmoothSpeed * Time.deltaTime);
	}

	public void SetTarget (Transform newTarget) {
		if (newTarget == null) return;
		target = newTarget;
		pose.ResetBoom();

		Vector3 start = target.position + gravityAlignment.Rotation * targetOffset;
		if (useGravityAlignment)
			gravityAlignment.SnapAt(start);
		else
			gravityAlignment.Identity();

		focusTracker.Snap(start);
	}

	public void SetOrbitSpeed (float speed) => orbitSpeed = speed;
	public void SetDistance (float newDistance) => distance = Mathf.Max(newDistance, 1f);
	public void SetElevation (float angle) => baseElevationAngle = Mathf.Clamp(angle, 0f, 85f);
	public void PauseOrbit () => isPaused = true;
	public void ResumeOrbit () => isPaused = false;
	public void TogglePause () => isPaused = !isPaused;
}
