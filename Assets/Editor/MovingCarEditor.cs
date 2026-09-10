using System.IO;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MovingCar))]
[CanEditMultipleObjects]
public class MovingCarEditor : Editor {

	MovingCar _car;
	static int s_SelectedTab = 0;

	static readonly string[] TabLabels = new string[] {
		"WHEELS",
		"ENGINE & GEARS",
		"DRIFT",
		"AERO & FLIGHT",
		"GROUND & JUMP",
		"SKIDS",
		"TELEMETRY"
	};

	// ── Serialized Properties ──────────────────────────────────────────
	SerializedProperty p_offlineMode;
	SerializedProperty p_carProfile;

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
		_car = (MovingCar)target;

		p_offlineMode = serializedObject.FindProperty("_offlineSceneTestMode");
		p_carProfile  = serializedObject.FindProperty("carProfile");

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

		DrawHeaderBanner();
		DrawProfileSyncBar();

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
			case 6:
				DrawTelemetryTab();
				break;
		}

		serializedObject.ApplyModifiedProperties();

		// Repaint continuously in play mode so telemetry and preview stay butter smooth
		if (Application.isPlaying) {
			Repaint();
		}
	}

	// ── Header Banner ──────────────────────────────────────────────────
	void DrawHeaderBanner () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);

		EditorGUILayout.BeginHorizontal();
		GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) {
			fontSize = 13,
			normal = { textColor = new Color(0.22f, 0.74f, 0.97f) }
		};
		EditorGUILayout.LabelField("ROLLING SKIES // VEHICLE DYNAMICS", titleStyle);

		// Status Badge
		if (Application.isPlaying) {
			Color badgeColor = _car.IsGrounded ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.25f, 0.8f, 1f);
			if (_car.IsDrifting) badgeColor = new Color(1f, 0.6f, 0.1f);

			GUIStyle badgeStyle = new GUIStyle(EditorStyles.miniButton) {
				fontStyle = FontStyle.Bold,
				normal = { textColor = badgeColor }
			};
			string status = _car.IsDrifting ? "DRIFTING" : (_car.IsGrounded ? "GROUNDED" : "AIRBORNE");
			GUILayout.Label($"LIVE: {status}", badgeStyle, GUILayout.Width(110));
		}
		else {
			GUIStyle badgeStyle = new GUIStyle(EditorStyles.miniLabel) {
				alignment = TextAnchor.MiddleRight,
				normal = { textColor = new Color(0.6f, 0.68f, 0.76f) }
			};
			GUILayout.Label("EDIT MODE", badgeStyle, GUILayout.Width(80));
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.EndVertical();
	}

	// ── Profile Sync Bar ───────────────────────────────────────────────
	void DrawProfileSyncBar () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);

		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.PropertyField(p_carProfile, new GUIContent("Active Profile"));

		if (GUILayout.Button("New Asset", GUILayout.Width(80))) {
			CreateNewProfileAsset();
		}
		EditorGUILayout.EndHorizontal();

		CarDataSO activeProfile = (CarDataSO)p_carProfile.objectReferenceValue;
		if (activeProfile != null) {
			EditorGUILayout.BeginHorizontal();
			GUIStyle hintStyle = new GUIStyle(EditorStyles.miniLabel) {
				normal = { textColor = new Color(0.7f, 0.85f, 0.95f) }
			};
			EditorGUILayout.LabelField($"Profile: {activeProfile.profileName} ({activeProfile.vehicleType})", hintStyle);

			if (GUILayout.Button("Apply To Car", GUILayout.Width(100))) {
				Undo.RecordObject(_car, "Apply Car Profile");
				_car.ApplyProfile(activeProfile);
				EditorUtility.SetDirty(_car);
			}

			if (GUILayout.Button("Save To Profile", GUILayout.Width(100))) {
				Undo.RecordObject(activeProfile, "Export To Car Profile");
				_car.ExportToProfile(activeProfile);
				EditorUtility.SetDirty(activeProfile);
				AssetDatabase.SaveAssets();
			}
			EditorGUILayout.EndHorizontal();
		}
		else {
			EditorGUILayout.HelpBox("Assign a CarDataSO profile to hot-swap vehicle behavior, or export current settings.", MessageType.Info);
		}

		EditorGUILayout.EndVertical();
	}

	// ── Tab 0: Wheel & Steering ────────────────────────────────────────
	void DrawWheelTab () {
		// Embed interactive 2D schematic drawer
		WheelInspectorDrawer.Draw(
			p_wheelSpread.floatValue,
			p_wheelBase.floatValue,
			p_maxSteerAngle.floatValue,
			p_minTurningRadius.floatValue,
			_car
		);

		EditorGUILayout.Space(6);
		EditorGUILayout.LabelField("STEERING GEOMETRY PARAMETERS", EditorStyles.boldLabel);

		EditorGUILayout.PropertyField(p_wheelSpread, new GUIContent("Half Track Width (Spread)", "Lateral distance from car center to wheel hub (meters)"));
		EditorGUILayout.PropertyField(p_wheelBase, new GUIContent("Wheelbase (m)", "Distance between front and rear axles (meters)"));
		EditorGUILayout.PropertyField(p_axleHeightOffset, new GUIContent("Axle Height Offset", "Vertical offset from pivot to wheel contact level"));
		EditorGUILayout.PropertyField(p_minTurningRadius, new GUIContent("Min Turning Radius (m)", "Clamps maximum steering curvature at full lock"));
		EditorGUILayout.PropertyField(p_maxSteerAngle, new GUIContent("Max Steer Angle (°)", "Physical steering angle lock of the front wheels"));
		EditorGUILayout.PropertyField(p_steerSensitivity, new GUIContent("Steer Sensitivity", "Steering response multiplier"));
		EditorGUILayout.PropertyField(p_speedSteerFalloff, new GUIContent("Speed Steer Falloff", "Reduces steering sensitivity at high speeds for stability"));
	}

	// ── Tab 1: Engine & Gears ──────────────────────────────────────────
	void DrawEngineGearTab () {
		// Transmission Section
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
			EditorGUILayout.HelpBox("Transmission disabled: Operating in direct-drive mode without gear shifts.", MessageType.None);
		}
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		// Engine Power Section
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("ENGINE POWER & ACCELERATION", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_acceleration, new GUIContent("Acceleration (m/s²)"));
		EditorGUILayout.PropertyField(p_brakeForce, new GUIContent("Brake Force (m/s²)"));
		EditorGUILayout.PropertyField(p_coastDecel, new GUIContent("Coast Deceleration"));
		EditorGUILayout.PropertyField(p_torqueCurve, new GUIContent("Torque Curve (RPM/Speed vs Power)"));
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		// Speed & Overdrive Section
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("SPEED LIMITS & OVERDRIVE", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_maxSpeed, new GUIContent("Max Cruise Speed (m/s)"));
		EditorGUILayout.PropertyField(p_topSpeed, new GUIContent("Top Speed Cap (Overdrive)"));
		EditorGUILayout.PropertyField(p_maxReverseSpeed, new GUIContent("Max Reverse Speed (m/s)"));
		EditorGUILayout.PropertyField(p_overdriveForce, new GUIContent("Overdrive Push Force"));
		EditorGUILayout.EndVertical();
	}

	// ── Tab 2: Drift & Handling ────────────────────────────────────────
	void DrawDriftTab () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("LATERAL GRIP & OVERSTEER DYNAMICS", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_lateralGrip, new GUIContent("Normal Lateral Grip (0 = Ice, 1 = Rail)"));
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

	// ── Tab 3: Aero & Flight ───────────────────────────────────────────
	void DrawAeroTab () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("DOWNFORCE & CURVATURE ADHESION", EditorStyles.boldLabel);
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

	// ── Tab 4: Ground & Jump ───────────────────────────────────────────
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

	// ── Tab 5: Skid Marks ──────────────────────────────────────────────
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

	// ── Tab 6: Telemetry & Testing ─────────────────────────────────────
	void DrawTelemetryTab () {
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("DEVELOPMENT PLAYTEST CONTROLS", EditorStyles.boldLabel);
		EditorGUILayout.PropertyField(p_offlineMode, new GUIContent("Offline Scene Test Mode"));
		EditorGUILayout.EndVertical();

		EditorGUILayout.Space(4);

		if (!Application.isPlaying) {
			EditorGUILayout.HelpBox("Enter Play Mode to view real-time engine telemetry, gear shifting, RPM gauges, and surface adhesion.", MessageType.Info);
			return;
		}

		EditorGUILayout.BeginVertical(EditorStyles.helpBox);
		EditorGUILayout.LabelField("LIVE ENGINE & TRANSMISSION TELEMETRY", EditorStyles.boldLabel);

		// Speedometer bar
		float speedRatio = _car.MaxSpeed > 0f ? Mathf.Clamp01(_car.Speed / _car.MaxSpeed) : 0f;
		Rect speedBarRect = GUILayoutUtility.GetRect(18f, 20f);
		EditorGUI.ProgressBar(speedBarRect, speedRatio, $"Speed: {_car.Speed:F1} m/s  ({_car.Speed * 3.6f:F0} km/h) / {_car.MaxSpeed:F0} m/s");

		EditorGUILayout.Space(2);

		// Tachometer bar
		float rpmNorm = _car.RPMNormalized;
		Color tachCol = rpmNorm > 0.88f ? new Color(1f, 0.25f, 0.25f) : (rpmNorm > 0.7f ? new Color(1f, 0.8f, 0.2f) : new Color(0.2f, 0.85f, 1f));
		Rect rpmBarRect = GUILayoutUtility.GetRect(18f, 20f);
		EditorGUI.ProgressBar(rpmBarRect, rpmNorm, $"RPM: {_car.RPM:F0}  [Gear: {_car.GearName}]");

		EditorGUILayout.Space(4);

		// Readout Grid
		EditorGUILayout.BeginHorizontal();
		DrawTelemetryCell("FORWARD SPD", $"{_car.ForwardSpeed:+0.0;-0.0;0.0} m/s");
		DrawTelemetryCell("LATERAL SPD", $"{_car.LateralSpeed:+0.0;-0.0;0.0} m/s");
		DrawTelemetryCell("DRIFT ANGLE", $"{_car.DriftAngle:F1}°");
		DrawTelemetryCell("TURBO CHARGE", $"{_car.MiniTurboChargeRatio:P0}");
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space(2);

		EditorGUILayout.BeginHorizontal();
		DrawTelemetryCell("GROUND NORMAL", $"{_car.ContactNormal}");
		DrawTelemetryCell("DOWNFORCE", $"{_car.Downforce:F1} N");
		DrawTelemetryCell("GROUND ANGLE", $"{_car.GroundAngle:F1}°");
		DrawTelemetryCell("SURFACE FRICTION", $"{_car.GroundFriction:F2}x");
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.EndVertical();
	}

	void DrawTelemetryCell (string title, string val) {
		EditorGUILayout.BeginVertical(EditorStyles.textArea);
		GUIStyle tStyle = new GUIStyle(EditorStyles.miniLabel) {
			fontSize = 8,
			normal = { textColor = new Color(0.6f, 0.68f, 0.76f) }
		};
		GUIStyle vStyle = new GUIStyle(EditorStyles.boldLabel) {
			fontSize = 10,
			normal = { textColor = new Color(0.9f, 0.95f, 1f) }
		};
		EditorGUILayout.LabelField(title, tStyle);
		EditorGUILayout.LabelField(val, vStyle);
		EditorGUILayout.EndVertical();
	}

	void CreateNewProfileAsset () {
		string dir = "Assets/Cars";
		if (!Directory.Exists(dir)) {
			Directory.CreateDirectory(dir);
			AssetDatabase.Refresh();
		}

		string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/NewCarProfile.asset");
		CarDataSO newProfile = ScriptableObject.CreateInstance<CarDataSO>();
		newProfile.profileName = _car.gameObject.name;

		_car.ExportToProfile(newProfile);

		AssetDatabase.CreateAsset(newProfile, path);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		p_carProfile.objectReferenceValue = newProfile;
		serializedObject.ApplyModifiedProperties();

		Selection.activeObject = newProfile;
		EditorGUIUtility.PingObject(newProfile);
	}
}
