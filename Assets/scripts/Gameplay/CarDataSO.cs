using UnityEngine;

public enum VehicleType {
	Standard,
	Sports,
	HeavyDrifter,
	Glider
}

/// <summary>
/// ScriptableObject defining all vehicle physics, transmission, steering, drift,
/// aerodynamics, suspension, and visual parameters.
/// Can be hot-swapped onto MovingCar at runtime or in the Editor.
/// </summary>
[CreateAssetMenu(fileName = "NewCarData", menuName = "Rolling Skies/Car Data Profile")]
public class CarDataSO : ScriptableObject {

	[Header("Profile Info")]
	public string profileName = "Standard Car";
	[TextArea(2, 4)]
	public string description = "Default balanced handling profile with responsive steering and all-around track grip.";
	public VehicleType vehicleType = VehicleType.Standard;

	[Header("Engine & Speed")]
	[Range(0f, 150f)]  public float maxSpeed = 76.6f;
	[Range(0f, 250f)]  public float topSpeed = 119.3f;
	[Range(0f, 60f)]   public float maxReverseSpeed = 9.1f;
	[Range(0f, 300f)]  public float acceleration = 76.08f;
	[Range(0f, 300f)]  public float brakeForce = 120f;
	[Range(0f, 50f)]   public float coastDeceleration = 2.1f;
	[Range(0f, 100f)]  public float overdriveForce = 26.4f;
	public AnimationCurve torqueCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f);

	[Header("Transmission & Gears")]
	public bool useGears = true;
	[Range(1, 8)] public int gearCount = 6;
	public float[] gearRatios = new float[] { 3.4f, 2.1f, 1.5f, 1.15f, 0.92f, 0.75f };
	[Range(2000f, 9000f)] public float shiftUpRPM = 6800f;
	[Range(1000f, 5000f)] public float shiftDownRPM = 3000f;
	[Range(600f, 1500f)]  public float idleRPM = 900f;
	[Range(5000f, 10000f)] public float redlineRPM = 7500f;
	[Range(0.01f, 0.5f)]  public float shiftDelay = 0.08f;
	[Range(0.5f, 2.0f)]   public float firstGearTorqueBoost = 1.35f;

	[Header("Steering & Wheels")]
	[Range(1f, 30f)]  public float minTurningRadius = 6.0f;
	[Range(0.1f, 2f)] public float wheelSpread = 0.55f; // Half Track Width
	[Range(0.5f, 4f)] public float wheelBase = 1.8f;    // Distance between front & rear axles
	[Range(-2f, 0f)]  public float axleHeightOffset = -0.35f;
	[Range(15f, 60f)] public float maxSteerAngle = 35f; // Visual steering angle lock
	[Range(0.5f, 5f)] public float steerSensitivity = 1.0f;
	[Range(0f, 1f)]   public float speedSteerFalloff = 0.35f; // Reduces steering angle at top speed

	[Header("Grip, Drift & Dynamics")]
	[Range(0f, 1f)]   public float lateralGrip = 0.85f;
	[Range(0f, 0.2f)] public float driftGrip = 0.0f;
	[Range(1f, 4f)]   public float driftYawMultiplier = 1.8f;
	[Range(0f, 60f)]  public float maxDriftAngle = 36f;
	[Range(1f, 30f)]  public float driftAngleRate = 8f;
	[Range(0.1f, 2f)] public float counterSteerAuthority = 1.0f;
	[Range(0f, 40f)]  public float miniTurboImpulse = 11f;
	[Range(0.3f, 3f)] public float miniTurboChargeTime = 1.2f;
	[Range(5f, 60f)]  public float yawInertiaSmoothRate = 22f;

	[Header("Aerodynamics & Downforce")]
	[Range(0f, 150f)] public float downforceStrength = 32f;
	[Range(0f, 60f)]  public float curvatureAdhesion = 18f;
	[Range(0.1f, 5f)] public float minAdhesionVelocity = 0.5f;

	[Header("Air Control & Future Glider")]
	[Range(0f, 180f)] public float airPitchSpeed = 80f;
	[Range(0f, 180f)] public float airYawSpeed = 90f;
	[Range(0f, 180f)] public float airRollSpeed = 65f;
	[Range(0.5f, 15f)] public float airAutoRightSpeed = 4.5f;
	[Range(0.5f, 20f)] public float airAngularDamping = 5f;
	public bool preAlignToLanding = true;
	[Range(1f, 12f)] public float landingProbeDistance = 4.5f;
	public bool canGlide = false;
	[Range(1f, 10f)] public float glideFallSpeed = 3.5f;
	[Range(10f, 80f)] public float glideForwardSpeed = 40f;

	[Header("Ground & Suspension")]
	[Range(0f, 90f)]  public float maxGroundAngle = 70f;
	[Range(0f, 100f)] public float maxSnapSpeed = 50f;
	public float probeDistance = 1.0f;
	public LayerMask probeMask = -1;
	[Range(0.1f, 1f)] public float minSurfaceSpeedMultiplier = 0.35f;
	[Range(1f, 30f)]  public float surfaceTransitionSpeed = 8f;
	[Range(0.5f, 5f)] public float minJumpHeight = 1.2f;
	[Range(1f, 10f)]  public float maxJumpHeight = 4.08f;
	[Range(0.1f, 0.6f)] public float maxJumpHoldDuration = 0.25f;
	[Range(0.05f, 0.5f)] public float jumpCooldown = 0.2f;
	[Range(0.05f, 0.3f)] public float jumpBufferDuration = 0.15f;
	[Range(0f, 720f)] public float maxSlipYawRate = 180f;
	[Range(0f, 10f)]  public float slipRecoveryRate = 3.5f;

	[Header("Skid Marks & Visuals")]
	public Material skidMaterial;
	[Range(0.05f, 0.5f)] public float markWidth = 0.22f;
	[Range(1f, 30f)]  public float fadeTime = 8f;
	[Range(0.02f, 1f)] public float minSegmentLength = 0.12f;
	[Range(0f, 5f)]   public float minSkidLateralSpeed = 0.6f;
	public int maxSkidPoints = 512;
	[Range(0f, 0.05f)] public float skidGroundOffset = 0.018f;

	/// <summary>
	/// Creates an in-memory clone of this profile.
	/// </summary>
	public CarDataSO Clone () {
		CarDataSO copy = Instantiate(this);
		copy.name = $"{this.name}_Clone";
		return copy;
	}
}
