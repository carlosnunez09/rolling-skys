using System.IO;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(CarDataSO))]
[CanEditMultipleObjects]
public class CarDataSOEditor : Editor {

	CarDataSO _profile;
	static int s_SelectedTab = 0;

	static readonly string[] TabLabels = new string[] {
		"WHEELS",
		"ENGINE & GEARS",
		"DRIFT",
		"AERO & FLIGHT",
		"GROUND & JUMP",
		"SKIDS"
	};

	SerializedProperty p_profileName, p_description, p_vehicleType;

	// Engine
	SerializedProperty p_maxSpeed, p_maxReverseSpeed, p_acceleration, p_brakeForce;
	SerializedProperty p_coastDecel, p_torqueCurve, p_topSpeed, p_overdriveForce;

	// Transmission
	SerializedProperty p_useGears, p_gearCount, p_gearRatios, p_shiftUpRPM, p_shiftDownRPM;
	SerializedProperty p_idleRPM, p_redlineRPM, p_shiftDelay, p_firstGearTorqueBoost;

	// Steering & Wheels
	SerializedProperty p_minTurningRadius, p_wheelSpread, p_wheelBase, p_axleHeightOffset;
	SerializedProperty p_maxSteerAngle, p_steerSensitivity, p_speedSteerFalloff;

	// Grip & Drift
	SerializedProperty p_lateralGrip, p_driftGrip, p_driftYawMultiplier, p_maxDriftAngle;
	SerializedProperty p_driftAngleRate, p_counterSteerAuthority, p_miniTurboImpulse;
	SerializedProperty p_miniTurboChargeTime, p_yawInertiaSmoothRate;

	// Downforce & Aero
	SerializedProperty p_downforceStrength, p_curvatureAdhesion, p_minAdhesionVelocity;
	SerializedProperty p_airPitchSpeed, p_airYawSpeed, p_airRollSpeed, p_airAutoRightSpeed;
	SerializedProperty p_airAngularDamping, p_preAlignToLanding, p_landingProbeDistance;
	SerializedProperty p_canGlide, p_glideFallSpeed, p_glideForwardSpeed;

	// Ground & Jump
	SerializedProperty p_maxGroundAngle, p_maxSnapSpeed, p_probeDistance, p_probeMask;
	SerializedProperty p_minSurfaceSpeedMultiplier, p_surfaceTransitionSpeed;
	SerializedProperty p_minJumpHeight, p_maxJumpHeight, p_maxJumpHoldDuration;
	SerializedProperty p_jumpCooldown, p_jumpBufferDuration, p_maxSlipYawRate, p_slipRecoveryRate;

	// Skid Marks
	SerializedProperty p_skidMaterial, p_markWidth, p_fadeTime, p_minSegmentLength;
	SerializedProperty p_minSkidLateralSpeed, p_maxSkidPoints, p_skidGroundOffset;

	void OnEnable () {
		_profile = (CarDataSO)target;

		p_profileName = serializedObject.FindProperty("profileName");
		p_description = serializedObject.FindProperty("description");
		p_vehicleType = serializedObject.FindProperty("vehicleType");

		p_maxSpeed        = serializedObject.FindProperty("maxSpeed");
		p_maxReverseSpeed = serializedObject.FindProperty("maxReverseSpeed");
		p_acceleration    = serializedObject.FindProperty("acceleration");
		p_brakeForce      = serializedObject.FindProperty("brakeForce");
		p_coastDecel      = serializedObject.FindProperty("coastDeceleration");
		p_torqueCurve     = serializedObject.FindProperty("torqueCurve");
		p_topSpeed        = serializedObject.FindProperty("topSpeed");
		p_overdriveForce  = serializedObject.FindProperty("overdriveForce");

		p_useGears             = serializedObject.FindProperty("useGears");
		p_gearCount            = serializedObject.FindProperty("gearCount");
		p_gearRatios           = serializedObject.FindProperty("gearRatios");
		p_shiftUpRPM           = serializedObject.FindProperty("shiftUpRPM");
		p_shiftDownRPM         = serializedObject.FindProperty("shiftDownRPM");
		p_idleRPM              = serializedObject.FindProperty("idleRPM");
		p_redlineRPM           = serializedObject.FindProperty("redlineRPM");
		p_shiftDelay           = serializedObject.FindProperty("shiftDelay");
		p_firstGearTorqueBoost = serializedObject.FindProperty("firstGearTorqueBoost");

		p_minTurningRadius  = serializedObject.FindProperty("minTurningRadius");
		p_wheelSpread       = serializedObject.FindProperty("wheelSpread");
		p_wheelBase         = serializedObject.FindProperty("wheelBase");
		p_axleHeightOffset  = serializedObject.FindProperty("axleHeightOffset");
		p_maxSteerAngle     = serializedObject.FindProperty("maxSteerAngle");
		p_steerSensitivity  = serializedObject.FindProperty("steerSensitivity");
		p_speedSteerFalloff = serializedObject.FindProperty("speedSteerFalloff");

		p_lateralGrip          = serializedObject.FindProperty("lateralGrip");
		p_driftGrip            = serializedObject.FindProperty("driftGrip");
		p_driftYawMultiplier   = serializedObject.FindProperty("driftYawMultiplier");
		p_maxDriftAngle        = serializedObject.FindProperty("maxDriftAngle");
		p_driftAngleRate       = serializedObject.FindProperty("driftAngleRate");
		p_counterSteerAuthority= serializedObject.FindProperty("counterSteerAuthority");
		p_miniTurboImpulse     = serializedObject.FindProperty("miniTurboImpulse");
		p_miniTurboChargeTime  = serializedObject.FindProperty("miniTurboChargeTime");
		p_yawInertiaSmoothRate = serializedObject.FindProperty("yawInertiaSmoothRate");

		p_downforceStrength   = serializedObject.FindProperty("downforceStrength");
		p_curvatureAdhesion   = serializedObject.FindProperty("curvatureAdhesion");
		p_minAdhesionVelocity = serializedObject.FindProperty("minAdhesionVelocity");
		p_airPitchSpeed       = serializedObject.FindProperty("airPitchSpeed");
		p_airYawSpeed         = serializedObject.FindProperty("airYawSpeed");
		p_airRollSpeed        = serializedObject.FindProperty("airRollSpeed");
		p_airAutoRightSpeed   = serializedObject.FindProperty("airAutoRightSpeed");
		p_airAngularDamping   = serializedObject.FindProperty("airAngularDamping");
		p_preAlignToLanding   = serializedObject.FindProperty("preAlignToLanding");
		p_landingProbeDistance= serializedObject.FindProperty("landingProbeDistance");
		p_canGlide            = serializedObject.FindProperty("canGlide");
		p_glideFallSpeed      = serializedObject.FindProperty("glideFallSpeed");
		p_glideForwardSpeed   = serializedObject.FindProperty("glideForwardSpeed");

		p_maxGroundAngle            = serializedObject.FindProperty("maxGroundAngle");
		p_maxSnapSpeed              = serializedObject.FindProperty("maxSnapSpeed");
		p_probeDistance             = serializedObject.FindProperty("probeDistance");
		p_probeMask                 = serializedObject.FindProperty("probeMask");
		p_minSurfaceSpeedMultiplier = serializedObject.FindProperty("minSurfaceSpeedMultiplier");
		p_surfaceTransitionSpeed    = serializedObject.FindProperty("surfaceTransitionSpeed");
		p_minJumpHeight             = serializedObject.FindProperty("minJumpHeight");
		p_maxJumpHeight             = serializedObject.FindProperty("maxJumpHeight");
		p_maxJumpHoldDuration       = serializedObject.FindProperty("maxJumpHoldDuration");
		p_jumpCooldown              = serializedObject.FindProperty("jumpCooldown");
		p_jumpBufferDuration        = serializedObject.FindProperty("jumpBufferDuration");
		p_maxSlipYawRate            = serializedObject.FindProperty("maxSlipYawRate");
		p_slipRecoveryRate          = serializedObject.FindProperty("slipRecoveryRate");

		p_skidMaterial       = serializedObject.FindProperty("skidMaterial");
		p_markWidth          = serializedObject.FindProperty("markWidth");
		p_fadeTime           = serializedObject.FindProperty("fadeTime");
		p_minSegmentLength   = serializedObject.FindProperty("minSegmentLength");
		p_minSkidLateralSpeed= serializedObject.FindProperty("minSkidLateralSpeed");
		p_maxSkidPoints      = serializedObject.FindProperty("maxSkidPoints");
		p_skidGroundOffset   = serializedObject.FindProperty("skidGroundOffset");
	}

	public override void OnInspectorGUI () {
		serializedObject.Update();

		DrawProfileHeader();

		EditorGUILayout.Space(6);
		s_SelectedTab = GUILayout.Toolbar(s_SelectedTab, TabLabels, GUILayout.Height(26));
		EditorGUILayout.Space(4);

		switch (s_SelectedTab) {
			case 0:
				DrawWheelTab();
				break;
			case 1:
				DrawEngineGearTab();
				break;
			case 2:
				DrawDriftTab();
				break;
			case 3:
				DrawAeroTab();
				break;
			case 4:
				DrawGroundJumpTab();
				break;
			case 5:
				DrawSkidTab();
				break;
		}

		serializedObject.ApplyModifiedProperties();
	}

	void DrawProfileHeader () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);

		EditorGUILayout.BeginHorizontal();
		GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) {
			fontSize = 13,
			normal = { textColor = new Color(0.22f, 0.74f, 0.97f) }
		};
		EditorGUILayout.LabelField("CAR DATA PROFILE // ASSET", titleStyle);

		if (GUILayout.Button("Clone Profile", GUILayout.Width(95))) {
			CloneCurrentProfile();
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space(4);
		EditorGUILayout.PropertyField(p_profileName, new GUIContent("Display Name"));
		EditorGUILayout.PropertyField(p_vehicleType, new GUIContent("Vehicle Class"));
		EditorGUILayout.PropertyField(p_description, new GUIContent("Profile Bio"));

		EditorGUILayout.Space(2);
		// Action to apply to selected scene car if any
		MovingCar selectedCar = Selection.activeGameObject?.GetComponent<MovingCar>();
		if (selectedCar != null) {
			if (GUILayout.Button($"Apply This Profile to '{selectedCar.gameObject.name}' in Scene", GUILayout.Height(22))) {
				Undo.RecordObject(selectedCar, "Apply Profile To Car");
				selectedCar.ApplyProfile(_profile);
				EditorUtility.SetDirty(selectedCar);
			}
		}

		EditorGUILayout.EndVertical();
	}

	void DrawWheelTab () {
		WheelInspectorDrawer.Draw(
			p_wheelSpread.floatValue,
			p_wheelBase.floatValue,
			p_maxSteerAngle.floatValue,
			p_minTurningRadius.floatValue
		);

		EditorGUILayout.Space(6);
		EditorGUILayout.LabelField("STEERING GEOMETRY PARAMETERS", EditorStyles.boldLabel);

		EditorGUILayout.PropertyField(p_wheelSpread, new GUIContent("Half Track Width (Spread)"));
		EditorGUILayout.PropertyField(p_wheelBase, new GUIContent("Wheelbase (m)"));
		EditorGUILayout.PropertyField(p_axleHeightOffset, new GUIContent("Axle Height Offset"));
		EditorGUILayout.PropertyField(p_minTurningRadius, new GUIContent("Min Turning Radius (m)"));
		EditorGUILayout.PropertyField(p_maxSteerAngle, new GUIContent("Max Steer Angle (°)"));
		EditorGUILayout.PropertyField(p_steerSensitivity, new GUIContent("Steer Sensitivity"));
		EditorGUILayout.PropertyField(p_speedSteerFalloff, new GUIContent("Speed Steer Falloff"));
	}

	void DrawEngineGearTab () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("TRANSMISSION & GEAR RATIOS", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_useGears, new GUIContent("Simulate Gear Transmission"));

		if (p_useGears.boolValue) {
			EditorGUILayout.PropertyField(p_gearCount, new GUIContent("Gear Count"));
			EditorGUILayout.PropertyField(p_gearRatios, new GUIContent("Gear Ratios"), true);

			EditorGUILayout.Space(4);
			EditorGUILayout.LabelField("Shift Points & RPM Ranges", EditorStyles.miniBoldLabel);
			EditorGUILayout.PropertyField(p_shiftUpRPM, new GUIContent("Shift Up RPM"));
			EditorGUILayout.PropertyField(p_shiftDownRPM, new GUIContent("Shift Down RPM"));
			EditorGUILayout.PropertyField(p_idleRPM, new GUIContent("Idle RPM"));
			EditorGUILayout.PropertyField(p_redlineRPM, new GUIContent("Redline RPM"));
			EditorGUILayout.PropertyField(p_shiftDelay, new GUIContent("Shift Delay (s)"));
			EditorGUILayout.PropertyField(p_firstGearTorqueBoost, new GUIContent("1st Gear Torque Boost"));
		}
		else {
			EditorGUILayout.HelpBox("Direct drive (single speed) active.", MessageType.None);
		}
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("ENGINE POWER & ACCELERATION", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_acceleration, new GUIContent("Acceleration (m/s²)"));
		EditorGUILayout.PropertyField(p_brakeForce, new GUIContent("Brake Force (m/s²)"));
		EditorGUILayout.PropertyField(p_coastDecel, new GUIContent("Coast Deceleration"));
		EditorGUILayout.PropertyField(p_torqueCurve, new GUIContent("Torque Curve"));
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("SPEED LIMITS & OVERDRIVE", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_maxSpeed, new GUIContent("Max Cruise Speed (m/s)"));
		EditorGUILayout.PropertyField(p_topSpeed, new GUIContent("Top Speed Cap (Overdrive)"));
		EditorGUILayout.PropertyField(p_maxReverseSpeed, new GUIContent("Max Reverse Speed (m/s)"));
		EditorGUILayout.PropertyField(p_overdriveForce, new GUIContent("Overdrive Push Force"));
		EditorGUILayout.EndVertical();
	}

	void DrawDriftTab () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("LATERAL GRIP & OVERSTEER DYNAMICS", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_lateralGrip, new GUIContent("Normal Lateral Grip"));
		EditorGUILayout.PropertyField(p_driftGrip, new GUIContent("Drift Lateral Grip"));
		EditorGUILayout.PropertyField(p_driftYawMultiplier, new GUIContent("Drift Oversteer Multiplier"));
		EditorGUILayout.PropertyField(p_maxDriftAngle, new GUIContent("Max Drift Angle (°)"));
		EditorGUILayout.PropertyField(p_driftAngleRate, new GUIContent("Drift Angle Transition Rate"));
		EditorGUILayout.PropertyField(p_counterSteerAuthority, new GUIContent("Counter-Steer Authority"));
		EditorGUILayout.PropertyField(p_yawInertiaSmoothRate, new GUIContent("Yaw Inertia Smooth Rate"));
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("MINI-TURBO DRIFT CHARGE", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_miniTurboChargeTime, new GUIContent("Charge Time (s)"));
		EditorGUILayout.PropertyField(p_miniTurboImpulse, new GUIContent("Release Impulse (m/s)"));
		EditorGUILayout.EndVertical();
	}

	void DrawAeroTab () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("DOWNFORCE & TRACK ADHESION", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_downforceStrength, new GUIContent("Downforce Strength"));
		EditorGUILayout.PropertyField(p_curvatureAdhesion, new GUIContent("Curvature Track Adhesion"));
		EditorGUILayout.PropertyField(p_minAdhesionVelocity, new GUIContent("Min Adhesion Velocity (m/s)"));
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("AERIAL ROTATION & ATTITUDE CONTROL", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_airPitchSpeed, new GUIContent("Air Pitch Speed (°/s)"));
		EditorGUILayout.PropertyField(p_airYawSpeed, new GUIContent("Air Yaw Speed (°/s)"));
		EditorGUILayout.PropertyField(p_airRollSpeed, new GUIContent("Air Roll Speed (°/s)"));
		EditorGUILayout.PropertyField(p_airAutoRightSpeed, new GUIContent("Air Auto-Righting Speed"));
		EditorGUILayout.PropertyField(p_airAngularDamping, new GUIContent("Air Angular Damping"));
		EditorGUILayout.PropertyField(p_preAlignToLanding, new GUIContent("Pre-Align To Ground"));
		EditorGUILayout.PropertyField(p_landingProbeDistance, new GUIContent("Landing Probe Range (m)"));
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("FUTURE VEHICLE HOOK: GLIDER FLIGHT", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_canGlide, new GUIContent("Enable Glider Wings"));
		if (p_canGlide.boolValue) {
			EditorGUILayout.PropertyField(p_glideFallSpeed, new GUIContent("Glide Fall Speed (m/s)"));
			EditorGUILayout.PropertyField(p_glideForwardSpeed, new GUIContent("Glide Forward Speed (m/s)"));
		}
		EditorGUILayout.EndVertical();
	}

	void DrawGroundJumpTab () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("GROUND DETECTION & ADAPTIVE SNAPPING", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_maxGroundAngle, new GUIContent("Max Ground Slope Angle (°)"));
		EditorGUILayout.PropertyField(p_maxSnapSpeed, new GUIContent("Max Snap Speed (m/s)"));
		EditorGUILayout.PropertyField(p_probeDistance, new GUIContent("Ground Probe Distance (m)"));
		EditorGUILayout.PropertyField(p_probeMask, new GUIContent("Probe LayerMask"));
		EditorGUILayout.PropertyField(p_minSurfaceSpeedMultiplier, new GUIContent("Min Surface Speed Multiplier"));
		EditorGUILayout.PropertyField(p_surfaceTransitionSpeed, new GUIContent("Surface Transition Rate"));
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("JUMP MECHANICS & RECOVERY", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_minJumpHeight, new GUIContent("Min Jump Height (m)"));
		EditorGUILayout.PropertyField(p_maxJumpHeight, new GUIContent("Max Jump Height (m)"));
		EditorGUILayout.PropertyField(p_maxJumpHoldDuration, new GUIContent("Max Jump Hold Duration (s)"));
		EditorGUILayout.PropertyField(p_jumpCooldown, new GUIContent("Jump Cooldown (s)"));
		EditorGUILayout.PropertyField(p_jumpBufferDuration, new GUIContent("Jump Buffer (s)"));
		EditorGUILayout.PropertyField(p_maxSlipYawRate, new GUIContent("Max Landing Slip Yaw Rate"));
		EditorGUILayout.PropertyField(p_slipRecoveryRate, new GUIContent("Slip Recovery Rate"));
		EditorGUILayout.EndVertical();
	}

	void DrawSkidTab () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("SKID MARK GENERATION & VISUALS", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_skidMaterial, new GUIContent("Skid Mesh Material"));
		EditorGUILayout.PropertyField(p_markWidth, new GUIContent("Mark Width (m)"));
		EditorGUILayout.PropertyField(p_fadeTime, new GUIContent("Mark Fade Time (s)"));
		EditorGUILayout.PropertyField(p_minSegmentLength, new GUIContent("Min Segment Length (m)"));
		EditorGUILayout.PropertyField(p_minSkidLateralSpeed, new GUIContent("Min Skid Lateral Speed (m/s)"));
		EditorGUILayout.PropertyField(p_maxSkidPoints, new GUIContent("Max Skid Points Buffer"));
		EditorGUILayout.PropertyField(p_skidGroundOffset, new GUIContent("Skid Surface Lift (Offset)"));
		EditorGUILayout.EndVertical();
	}

	void CloneCurrentProfile () {
		string originalPath = AssetDatabase.GetAssetPath(_profile);
		string dir = Path.GetDirectoryName(originalPath);
		string filename = Path.GetFileNameWithoutExtension(originalPath);
		string newPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{filename}_Copy.asset");

		CarDataSO clone = Instantiate(_profile);
		clone.profileName = $"{_profile.profileName} (Copy)";
		AssetDatabase.CreateAsset(clone, newPath);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Selection.activeObject = clone;
		EditorGUIUtility.PingObject(clone);
	}
}
