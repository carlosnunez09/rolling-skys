using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

/// <summary>
/// Handles dual-motor rumble / haptic feedback for local player vehicles.
/// Delivers nuanced tactile response for tire slip/drift, mini-turbo charges,
/// jump launches, ground impact shocks, and collisions.
/// </summary>
public class CarHaptics : MonoBehaviour {

	[BoxGroup("Haptic Settings"), SerializeField]
	bool enableHaptics = true;

	[BoxGroup("Haptic Settings"), SerializeField, Range(0f, 1f), Label("Master Intensity")]
	float masterIntensity = 0.85f;

	[BoxGroup("Haptic Settings"), SerializeField, Range(0f, 1f), Label("Drift Rumble Scale")]
	float driftRumbleScale = 0.6f;

	[BoxGroup("Haptic Settings"), SerializeField, Range(0f, 1f), Label("Impact Rumble Scale")]
	float impactRumbleScale = 0.9f;

	float _burstLow;
	float _burstHigh;
	float _burstTimer;

	float _continuousLow;
	float _continuousHigh;

	public bool EnableHaptics {
		get => enableHaptics;
		set {
			enableHaptics = value;
			if (!enableHaptics) ResetHaptics();
		}
	}

	void Update () {
		if (!enableHaptics) return;

		Gamepad pad = Gamepad.current;
		if (pad == null) return;

		float low = _continuousLow;
		float high = _continuousHigh;

		if (_burstTimer > 0f) {
			_burstTimer -= Time.deltaTime;
			low  = Mathf.Max(low,  _burstLow);
			high = Mathf.Max(high, _burstHigh);
			if (_burstTimer <= 0f) {
				_burstLow  = 0f;
				_burstHigh = 0f;
			}
		}

		low  = Mathf.Clamp01(low  * masterIntensity);
		high = Mathf.Clamp01(high * masterIntensity);

		pad.SetMotorSpeeds(low, high);

		// Decay continuous motors for next frame
		_continuousLow  = Mathf.MoveTowards(_continuousLow,  0f, 4f * Time.deltaTime);
		_continuousHigh = Mathf.MoveTowards(_continuousHigh, 0f, 4f * Time.deltaTime);
	}

	void OnDisable () => ResetHaptics();
	void OnDestroy () => ResetHaptics();
	void OnApplicationFocus (bool hasFocus) {
		if (!hasFocus) ResetHaptics();
	}

	public void TriggerImpulse (float lowFrequency, float highFrequency, float duration) {
		if (!enableHaptics) return;
		_burstLow   = Mathf.Max(_burstLow, lowFrequency);
		_burstHigh  = Mathf.Max(_burstHigh, highFrequency);
		_burstTimer = Mathf.Max(_burstTimer, duration);
	}

	public void SetDriftVibration (float lateralSpeed, bool miniTurboReady) {
		if (!enableHaptics) return;
		float slipNorm = Mathf.Clamp01(Mathf.Abs(lateralSpeed) / 12f);
		_continuousLow = Mathf.Max(_continuousLow, slipNorm * driftRumbleScale);

		if (miniTurboReady) {
			_continuousHigh = Mathf.Max(_continuousHigh, 0.4f * masterIntensity);
		}
	}

	public void TriggerJumpLaunch () {
		TriggerImpulse(0.2f, 0.5f, 0.12f);
	}

	public void TriggerLanding (float landingImpactVelocity) {
		float severity = Mathf.Clamp01(Mathf.Abs(landingImpactVelocity) / 16f);
		if (severity > 0.15f) {
			TriggerImpulse(severity * impactRumbleScale, severity * 0.4f * impactRumbleScale, 0.18f);
		}
	}

	public void TriggerMiniTurboBurst () {
		TriggerImpulse(0.7f, 0.95f, 0.28f);
	}

	public void TriggerCollisionShock (float relativeVelocity) {
		float severity = Mathf.Clamp01(relativeVelocity / 20f);
		if (severity > 0.2f) {
			TriggerImpulse(severity * 0.9f, severity * 0.7f, 0.22f);
		}
	}

	public void ResetHaptics () {
		_burstLow       = 0f;
		_burstHigh      = 0f;
		_burstTimer     = 0f;
		_continuousLow  = 0f;
		_continuousHigh = 0f;
		Gamepad.current?.ResetHaptics();
	}
}
