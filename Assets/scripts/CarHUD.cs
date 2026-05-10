using UnityEngine;
using TMPro;
using NaughtyAttributes;

/// Attach this to any GameObject in your scene (e.g. a "HUD" empty).
/// Drag your car's MovingCar component into the Car slot, then wire up
/// whichever TextMeshProUGUI fields you want — any left empty are skipped.
public class CarHUD : MonoBehaviour {

	// ── Car Reference ─────────────────────────────────────────────────

	[BoxGroup("Car"), SerializeField, Required]
	MovingCar car;

	// ── Primary Gauges ────────────────────────────────────────────────

	[BoxGroup("Primary"), SerializeField, Label("Speed")]
	TextMeshProUGUI speedText;

	[BoxGroup("Primary"), SerializeField, Label("Acceleration")]
	TextMeshProUGUI accelerationText;

	[BoxGroup("Primary"), SerializeField, Label("Direction  (FWD / REV)")]
	TextMeshProUGUI directionText;

	// ── Handling ──────────────────────────────────────────────────────

	[BoxGroup("Handling"), SerializeField, Label("Yaw Rate")]
	TextMeshProUGUI yawRateText;

	[BoxGroup("Handling"), SerializeField, Label("Lateral Speed  (drift)")]
	TextMeshProUGUI lateralSpeedText;

	[BoxGroup("Handling"), SerializeField, Label("Landing Slip")]
	TextMeshProUGUI landingSlipText;

	[BoxGroup("Handling"), SerializeField, Label("Drifting")]
	TextMeshProUGUI driftingText;

	// ── World / Gravity ───────────────────────────────────────────────

	[BoxGroup("World"), SerializeField, Label("Gravity Source")]
	TextMeshProUGUI gravitySourceText;

	[BoxGroup("World"), SerializeField, Label("Gravity Strength  m/s²")]
	TextMeshProUGUI gravityStrengthText;

	[BoxGroup("World"), SerializeField, Label("Grounded")]
	TextMeshProUGUI groundedText;

	[BoxGroup("World"), SerializeField, Label("Ground Angle  °")]
	TextMeshProUGUI groundAngleText;

	// ── Debug Block ───────────────────────────────────────────────────
	// One TMP that shows everything at once — useful during development.

	[BoxGroup("Debug Block"), SerializeField, Label("All Stats Text")]
	TextMeshProUGUI debugBlock;

	// ─────────────────────────────────────────────────────────────────

	void LateUpdate () {
		if (car == null) return;

		Set(speedText,         $"{car.Speed:F1} m/s");
		Set(accelerationText,  $"{car.Acceleration:F1} m/s²");
		Set(directionText,     car.ForwardSpeed >= 0f ? "FWD" : "REV");
		Set(yawRateText,       $"{car.YawRate:F1} °/s");
		Set(lateralSpeedText,  $"{car.LateralSpeed:F2} m/s");
		Set(landingSlipText,   $"{car.LandingSlip * 100f:F0} %");
		Set(driftingText,      car.IsDrifting ? "DRIFT" : "—");
		Set(gravitySourceText, car.GravitySource);
		Set(gravityStrengthText, $"{car.GravityStrength:F2} m/s²");
		Set(groundedText,      car.IsGrounded ? "Grounded" : "Airborne");
		Set(groundAngleText,   $"{car.GroundAngle:F1} °");

		if (debugBlock != null) {
			debugBlock.text =
				$"Speed        {car.Speed:F1} m/s\n" +
				$"Fwd Speed    {car.ForwardSpeed:F1} m/s\n" +
				$"Lat Speed    {car.LateralSpeed:F2} m/s\n" +
				$"Accel        {car.Acceleration:F1} m/s²\n" +
				$"Yaw Rate     {car.YawRate:F1} °/s\n" +
				$"Yaw Accel    {car.YawAcceleration:F1} °/s²\n" +
				$"Grounded     {(car.IsGrounded ? "Yes" : "No")}\n" +
				$"Drifting     {(car.IsDrifting ? "Yes" : "No")}\n" +
				$"Land Slip    {car.LandingSlip * 100f:F0} %\n" +
				$"Ground Ang   {car.GroundAngle:F1} °\n" +
				$"Gravity      {car.GravityStrength:F2} m/s²\n" +
				$"Grav Source  {car.GravitySource}\n" +
				$"RB Below     {(car.RigidbodyBelow ? "Yes" : "No")}";
		}
	}

	static void Set (TextMeshProUGUI field, string value) {
		if (field != null) field.text = value;
	}
}
