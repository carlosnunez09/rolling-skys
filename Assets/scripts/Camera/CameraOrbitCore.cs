using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Shared follow-camera core. OrbitCamera (driving) and CinematicOrbitCamera
// (showcase) both run gravity, focus lag, local-player binding, look input, and
// the obstruction boom through these types so those paths cannot drift apart.

/// <summary>
/// Resolves the locally owned car, then an unspawned Solo Practice car.
/// Never locks onto a networked replica.
/// </summary>
public static class CameraFollowTarget {
	public static Transform FindLocalFocus () {
		MovingCar[] cars = UnityEngine.Object.FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude);

		foreach (MovingCar car in cars)
			if (car != null && car.HasLocalControl)
				return car.transform;

		foreach (MovingCar car in cars)
			if (car != null && !car.IsSpawned)
				return car.transform;

		return null;
	}
}

/// <summary>Smooths the camera's up-axis toward <see cref="CustomGravity"/> at a focus point.</summary>
public struct CameraGravityAlignment {
	Quaternion rotation;

	public Quaternion Rotation => rotation;
	public Vector3 Up => rotation * Vector3.up;

	public void Identity () => rotation = Quaternion.identity;

	public void SnapAt (Vector3 position) {
		Vector3 startUp = CustomGravity.GetUpAxis(position);
		rotation = startUp.sqrMagnitude > 0.001f
			? Quaternion.FromToRotation(Vector3.up, startUp)
			: Quaternion.identity;
	}

	public void Update (Vector3 focusPoint, float speedDegPerSec, float dt) {
		Vector3 fromUp = rotation * Vector3.up;
		Vector3 toUp = CustomGravity.GetUpAxis(focusPoint);
		// Zero up-axis (no gravity source) would NaN FromToRotation and stick forever.
		if (toUp.sqrMagnitude < 0.001f) return;

		float dot = Mathf.Clamp(Vector3.Dot(fromUp, toUp), -1f, 1f);
		float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
		float maxAngle = speedDegPerSec * dt;
		Quaternion newAlignment = Quaternion.FromToRotation(fromUp, toUp) * rotation;

		if (float.IsNaN(newAlignment.x) || float.IsNaN(newAlignment.y) ||
		    float.IsNaN(newAlignment.z) || float.IsNaN(newAlignment.w)) return;

		rotation = angle <= maxAngle
			? newAlignment
			: Quaternion.SlerpUnclamped(rotation, newAlignment, maxAngle / angle);
	}
}

/// <summary>
/// Lagged tracking pivot. Caps lag at <see cref="MaxLagMetres"/> and snaps to the
/// car when a wall sits between the pivot and the body so the boom cannot pull
/// through geometry.
/// </summary>
public sealed class CameraFocusTracker {
	public const float MaxLagMetres = 1.5f;

	Vector3 point;

	public Vector3 Point => point;

	public void Snap (Vector3 targetPoint) => point = targetPoint;

	public Vector3 Update (
		Vector3 targetPoint,
		float radius,
		float centering,
		CameraObstructionSolver solver,
		Transform focus,
		LayerMask mask
	) {
		if (radius <= 0f) {
			point = targetPoint;
			return point;
		}

		float dist = Vector3.Distance(targetPoint, point);
		float t = 1f;
		if (dist > 0.01f && centering > 0f)
			t = Mathf.Pow(1f - centering, Time.unscaledDeltaTime);
		if (dist > radius)
			t = Mathf.Min(t, radius / dist);

		point = Vector3.Lerp(targetPoint, point, t);
		point = targetPoint + Vector3.ClampMagnitude(point - targetPoint, MaxLagMetres);

		if (solver != null && solver.IsPathBlocked(targetPoint, point, focus, mask))
			point = targetPoint;

		return point;
	}
}

/// <summary>
/// Look / cinematic-toggle / zoom actions. Bindings match the rest of the project
/// (code-created Input System actions, same as <c>MovingCar</c>) rather than the
/// unused starter InputSystem_Actions asset.
/// </summary>
sealed class CameraLookInput : IDisposable {
	public readonly InputAction Look;
	public readonly InputAction CinematicToggle;
	public readonly InputAction Zoom;

	public static CameraLookInput CreateDriving () {
		InputAction toggle = new InputAction("CinematicToggle", InputActionType.Button);
		toggle.AddBinding("<Keyboard>/c");
		toggle.AddBinding("<Gamepad>/dpad/up");
		return new CameraLookInput(MakeLook("Look"), toggle, null);
	}

	public static CameraLookInput CreateCinematic () {
		InputAction zoom = new InputAction("CinematicZoom", InputActionType.Value);
		zoom.AddBinding("<Mouse>/scroll/y");
		zoom.AddCompositeBinding("1DAxis")
			.With("Positive", "<Gamepad>/rightTrigger")
			.With("Negative", "<Gamepad>/leftTrigger");
		return new CameraLookInput(MakeLook("CinematicLook"), null, zoom);
	}

	static InputAction MakeLook (string name) {
		InputAction look = new InputAction(name, InputActionType.Value);
		look.AddCompositeBinding("2DVector")
			.With("Up",    "<Keyboard>/upArrow")
			.With("Down",  "<Keyboard>/downArrow")
			.With("Left",  "<Keyboard>/leftArrow")
			.With("Right", "<Keyboard>/rightArrow");
		look.AddBinding("<Gamepad>/rightStick");
		return look;
	}

	CameraLookInput (InputAction look, InputAction toggle, InputAction zoom) {
		Look = look;
		CinematicToggle = toggle;
		Zoom = zoom;
	}

	public Vector2 ReadLook () => Look != null ? Look.ReadValue<Vector2>() : Vector2.zero;

	public bool CinematicTogglePressed () =>
		CinematicToggle != null && CinematicToggle.WasPressedThisFrame();

	public float ReadZoom () => Zoom != null ? Zoom.ReadValue<float>() : 0f;

	public void Enable () {
		Look?.Enable();
		CinematicToggle?.Enable();
		Zoom?.Enable();
	}

	public void Disable () {
		Look?.Disable();
		CinematicToggle?.Disable();
		Zoom?.Disable();
	}

	public void Dispose () {
		Look?.Dispose();
		CinematicToggle?.Dispose();
		Zoom?.Dispose();
	}
}

/// <summary>
/// Single follow/orbit pose step: smooth the look rotation first, then place the
/// camera along that heading with the shared obstruction boom and toon cutaway.
/// </summary>
public sealed class FollowCameraPose {
	readonly CameraObstructionSolver obstruction = new CameraObstructionSolver();
	CameraOcclusionFade fade;

	public CameraObstructionSolver Obstruction => obstruction;
	public bool IsObstructed => obstruction.IsObstructed;

	public void Bind (GameObject cameraObject) {
		if (cameraObject == null) return;
		fade = cameraObject.GetComponent<CameraOcclusionFade>();
		if (fade == null)
			fade = cameraObject.AddComponent<CameraOcclusionFade>();
	}

	public void ResetBoom () => obstruction.Reset();

	public void ClearTarget () {
		if (fade != null)
			fade.SetTarget(null, Vector3.zero);
	}

	public void SetFadeEnabled (bool enabled) {
		if (fade != null)
			fade.enabled = enabled;
	}

	public void Apply (
		Transform cameraTransform,
		Camera camera,
		Transform focus,
		Vector3 focusPoint,
		Quaternion desiredRotation,
		float distance,
		float rotationSmoothSpeed,
		LayerMask obstructionMask,
		float recoverySpeed,
		float comfortDistance,
		bool revealThroughToonObjects
	) {
		// Smooth rotation first, then derive position from the smoothed heading so
		// sub-frame flicker cannot desync the boom from the view ray.
		Quaternion smoothedRotation = Quaternion.Slerp(
			cameraTransform.rotation, desiredRotation,
			1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));

		Vector3 lookDirection = smoothedRotation * Vector3.forward;
		Vector3 lookPosition = obstruction.Resolve(
			camera, focus, focusPoint, -lookDirection, distance,
			obstructionMask, recoverySpeed, comfortDistance, revealThroughToonObjects);

		if (fade != null) {
			fade.enabled = revealThroughToonObjects;
			if (revealThroughToonObjects)
				fade.SetTarget(focus, focus.position);
		}

		cameraTransform.SetPositionAndRotation(lookPosition, smoothedRotation);
	}
}

static class CameraOrbitAngles {
	public static void WrapYaw (ref Vector2 orbitAngles) {
		if (orbitAngles.y < 0f) orbitAngles.y += 360f;
		else if (orbitAngles.y >= 360f) orbitAngles.y -= 360f;
	}

	public static void WrapYawAndClampPitch (ref Vector2 orbitAngles, float minPitch, float maxPitch) {
		orbitAngles.x = Mathf.Clamp(orbitAngles.x, minPitch, maxPitch);
		WrapYaw(ref orbitAngles);
	}

	public static float GetAngle (Vector2 direction) {
		float angle = Mathf.Acos(direction.y) * Mathf.Rad2Deg;
		return direction.x < 0f ? 360f - angle : angle;
	}
}
