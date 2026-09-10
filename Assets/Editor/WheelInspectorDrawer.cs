using UnityEngine;
using UnityEditor;

/// <summary>
/// Interactive 2D Top-Down Vehicle Schematic and Wheel Geometry Drawer.
/// Renders track width, wheelbase, steering lock, turning radius, and dynamic wheel rotation
/// in real-time inside the Unity Inspector.
/// </summary>
public static class WheelInspectorDrawer {

	static float s_PreviewAngle = 0f;
	static bool s_LiveFollow = true;

	public static void Draw (
		float wheelSpread,
		float wheelBase,
		float maxSteerAngle,
		float minTurningRadius,
		MovingCar liveCar = null
	) {
		// ── Determine Active Angle ─────────────────────────────────────────
		float activeAngle = s_PreviewAngle;
		bool isPlayMode = Application.isPlaying && liveCar != null;

		if (isPlayMode && s_LiveFollow) {
			activeAngle = liveCar.CurrentSteerAngle;
		}

		// Clamp to max steer lock
		activeAngle = Mathf.Clamp(activeAngle, -maxSteerAngle, maxSteerAngle);

		// ── Header & Mode Controls ────────────────────────────────────────
		EditorGUILayout.BeginVertical(EditorStyles.helpBox);

		EditorGUILayout.BeginHorizontal();
		GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) {
			fontSize = 12,
			normal = { textColor = new Color(0.22f, 0.74f, 0.97f) }
		};
		EditorGUILayout.LabelField("2D WHEEL GEOMETRY & STEERING INSPECTOR", titleStyle);

		if (isPlayMode) {
			s_LiveFollow = GUILayout.Toggle(s_LiveFollow, "Follow Live Car", "Button", GUILayout.Width(110));
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space(2);

		// ── Interactive Steer Slider ──────────────────────────────────────
		EditorGUI.BeginDisabledGroup(isPlayMode && s_LiveFollow);
		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField($"Steer Preview: {activeAngle:+0.0;-0.0;0.0}°", GUILayout.Width(130));

		if (GUILayout.Button("-Lock", GUILayout.Width(50))) {
			s_PreviewAngle = -maxSteerAngle;
			activeAngle = -maxSteerAngle;
		}

		s_PreviewAngle = EditorGUILayout.Slider(activeAngle, -maxSteerAngle, maxSteerAngle);
		activeAngle = s_PreviewAngle;

		if (GUILayout.Button("+Lock", GUILayout.Width(50))) {
			s_PreviewAngle = maxSteerAngle;
			activeAngle = maxSteerAngle;
		}

		if (GUILayout.Button("Center", GUILayout.Width(55))) {
			s_PreviewAngle = 0f;
			activeAngle = 0f;
		}
		EditorGUILayout.EndHorizontal();
		EditorGUI.EndDisabledGroup();

		EditorGUILayout.Space(4);

		// ── Canvas Allocation ─────────────────────────────────────────────
		float canvasHeight = 230f;
		Rect canvasRect = GUILayoutUtility.GetRect(10f, 1000f, canvasHeight, canvasHeight);

		if (Event.current.type == EventType.Repaint) {
			DrawSchematic(canvasRect, wheelSpread, wheelBase, maxSteerAngle, minTurningRadius, activeAngle);
		}

		// ── Metrics Readout Grid ──────────────────────────────────────────
		EditorGUILayout.Space(4);
		DrawMetricsBar(wheelSpread, wheelBase, maxSteerAngle, minTurningRadius, activeAngle);

		EditorGUILayout.EndVertical();
	}

	static void DrawSchematic (
		Rect rect,
		float wheelSpread,
		float wheelBase,
		float maxSteerAngle,
		float minTurningRadius,
		float steerAngle
	) {
		// 1. Background slate & subtle border
		EditorGUI.DrawRect(rect, new Color(0.08f, 0.10f, 0.13f, 1f));

		// Border lines
		Handles.color = new Color(0.20f, 0.25f, 0.32f, 1f);
		Handles.DrawLine(new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin));
		Handles.DrawLine(new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax));
		Handles.DrawLine(new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax));
		Handles.DrawLine(new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMin, rect.yMin));

		// 2. Blueprint Grid
		Handles.color = new Color(0.14f, 0.18f, 0.24f, 0.5f);
		for (float x = rect.xMin + 25f; x < rect.xMax; x += 25f)
			Handles.DrawLine(new Vector3(x, rect.yMin), new Vector3(x, rect.yMax));
		for (float y = rect.yMin + 25f; y < rect.yMax; y += 25f)
			Handles.DrawLine(new Vector3(rect.xMin, y), new Vector3(rect.xMax, y));

		// Center of canvas
		Vector2 center = rect.center;

		// 3. Scaling
		// Safe defaults if zeroes passed
		float trackWidth = Mathf.Max(wheelSpread * 2f, 0.4f);
		float wheelbaseM = Mathf.Max(wheelBase, 0.8f);

		// Vehicle should occupy ~55% of canvas height
		float targetH = rect.height * 0.55f;
		float ppm = Mathf.Clamp(targetH / wheelbaseM, 40f, 95f);

		float halfTrackPx = wheelSpread * ppm;
		float halfWheelbasePx = (wheelBase * 0.5f) * ppm;

		// Axle Centers (Y-up is negative in GUI space: front is UP = negative Y)
		float frontAxleY = center.y - halfWheelbasePx;
		float rearAxleY  = center.y + halfWheelbasePx;

		// Centerline
		Handles.color = new Color(0.3f, 0.4f, 0.55f, 0.35f);
		Handles.DrawLine(new Vector3(center.x, rect.yMin + 10f), new Vector3(center.x, rect.yMax - 10f));

		// 4. Turning Path Arc
		float absAngle = Mathf.Abs(steerAngle);
		if (absAngle > 0.5f) {
			float rad = absAngle * Mathf.Deg2Rad;
			float turnRadiusM = wheelBase / Mathf.Sin(rad);
			float turnRadiusPx = turnRadiusM * ppm;
			float steerSign = Mathf.Sign(steerAngle);

			// Instantaneous Center of Rotation (ICR) lies on rear axle line
			Vector2 icr = new Vector2(center.x + steerSign * turnRadiusPx, rearAxleY);

			// Draw arc from front bumper
			Handles.color = new Color(0.22f, 0.74f, 0.97f, 0.35f);
			Vector3 arcStart = new Vector3(center.x, frontAxleY, 0f) - new Vector3(icr.x, icr.y, 0f);
			float arcSweep = steerSign * -35f;
			Handles.DrawWireArc(new Vector3(icr.x, icr.y, 0f), Vector3.forward, arcStart.normalized, arcSweep, arcStart.magnitude);

			// Small ICR indicator
			if (rect.Contains(icr)) {
				Handles.color = new Color(0.98f, 0.75f, 0.15f, 0.8f);
				Handles.DrawSolidDisc(new Vector3(icr.x, icr.y, 0f), Vector3.forward, 3f);
			}
		}

		// 5. Chassis Body
		float chassisWidth = Mathf.Max(halfTrackPx * 1.6f, 34f);
		float chassisLength = (wheelbaseM * ppm) + 40f;
		Rect chassisRect = new Rect(center.x - chassisWidth * 0.5f, center.y - chassisLength * 0.5f, chassisWidth, chassisLength);

		// Body silhouette
		EditorGUI.DrawRect(chassisRect, new Color(0.14f, 0.18f, 0.24f, 0.95f));
		Handles.color = new Color(0.28f, 0.37f, 0.48f, 1f);
		Handles.DrawPolyLine(
			new Vector3(chassisRect.xMin, chassisRect.yMin),
			new Vector3(chassisRect.xMax, chassisRect.yMin),
			new Vector3(chassisRect.xMax, chassisRect.yMax),
			new Vector3(chassisRect.xMin, chassisRect.yMax),
			new Vector3(chassisRect.xMin, chassisRect.yMin)
		);

		// Front Hood Chevron (Pointing forward)
		Handles.color = new Color(0.22f, 0.74f, 0.97f, 0.9f);
		float chevW = chassisWidth * 0.32f;
		float chevTipY = chassisRect.yMin + 8f;
		float chevBaseY = chassisRect.yMin + 22f;
		Handles.DrawPolyLine(
			new Vector3(center.x - chevW, chevBaseY),
			new Vector3(center.x, chevTipY),
			new Vector3(center.x + chevW, chevBaseY)
		);

		// 6. Axle Lines
		Handles.color = new Color(0.45f, 0.55f, 0.68f, 0.85f);
		// Front axle line
		Handles.DrawLine(new Vector3(center.x - halfTrackPx, frontAxleY), new Vector3(center.x + halfTrackPx, frontAxleY));
		// Rear axle line
		Handles.DrawLine(new Vector3(center.x - halfTrackPx, rearAxleY), new Vector3(center.x + halfTrackPx, rearAxleY));

		// Center of Mass marker
		Handles.color = new Color(0.98f, 0.75f, 0.15f, 0.95f);
		Handles.DrawWireDisc(new Vector3(center.x, center.y, 0f), Vector3.forward, 5f);
		Handles.DrawLine(new Vector3(center.x - 8f, center.y), new Vector3(center.x + 8f, center.y));
		Handles.DrawLine(new Vector3(center.x, center.y - 8f), new Vector3(center.x, center.y + 8f));

		// 7. Wheels
		Vector2 tireSize = new Vector2(13f, 32f);

		// Rear Wheels (Static 0°)
		Vector2 rlPos = new Vector2(center.x - halfTrackPx, rearAxleY);
		Vector2 rrPos = new Vector2(center.x + halfTrackPx, rearAxleY);
		DrawTire(rlPos, tireSize, 0f, false);
		DrawTire(rrPos, tireSize, 0f, false);

		// Front Wheels (Rotated by steerAngle)
		Vector2 flPos = new Vector2(center.x - halfTrackPx, frontAxleY);
		Vector2 frPos = new Vector2(center.x + halfTrackPx, frontAxleY);
		DrawTire(flPos, tireSize, steerAngle, true);
		DrawTire(frPos, tireSize, steerAngle, true);

		// 8. Dimension Lines & Readouts
		DrawDimensionLines(center, halfTrackPx, halfWheelbasePx, frontAxleY, rearAxleY, trackWidth, wheelbaseM, rect);
	}

	static void DrawTire (Vector2 center, Vector2 size, float angle, bool isFront) {
		Matrix4x4 prevMatrix = GUI.matrix;
		GUIUtility.RotateAroundPivot(angle, center);

		Rect tireRect = new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);

		// Rubber body
		EditorGUI.DrawRect(tireRect, new Color(0.04f, 0.05f, 0.06f, 1f));

		// Rim / Tread border
		Color rimColor = isFront ? new Color(0.22f, 0.74f, 0.97f, 0.9f) : new Color(0.6f, 0.7f, 0.82f, 0.8f);
		Handles.color = rimColor;
		Handles.DrawPolyLine(
			new Vector3(tireRect.xMin, tireRect.yMin),
			new Vector3(tireRect.xMax, tireRect.yMin),
			new Vector3(tireRect.xMax, tireRect.yMax),
			new Vector3(tireRect.xMin, tireRect.yMax),
			new Vector3(tireRect.xMin, tireRect.yMin)
		);

		// Tire center groove
		Handles.color = new Color(0.3f, 0.35f, 0.42f, 0.7f);
		Handles.DrawLine(new Vector3(center.x, tireRect.yMin + 3f), new Vector3(center.x, tireRect.yMax - 3f));

		// Front tire heading arrow
		if (isFront) {
			Handles.color = new Color(0.22f, 0.74f, 0.97f, 0.85f);
			Handles.DrawLine(new Vector3(center.x, tireRect.yMin), new Vector3(center.x, tireRect.yMin - 12f));
			Handles.DrawLine(new Vector3(center.x, tireRect.yMin - 12f), new Vector3(center.x - 3f, tireRect.yMin - 7f));
			Handles.DrawLine(new Vector3(center.x, tireRect.yMin - 12f), new Vector3(center.x + 3f, tireRect.yMin - 7f));
		}

		GUI.matrix = prevMatrix;
	}

	static void DrawDimensionLines (
		Vector2 center,
		float halfTrackPx,
		float halfWheelbasePx,
		float frontAxleY,
		float rearAxleY,
		float trackWidthM,
		float wheelbaseM,
		Rect canvasRect
	) {
		GUIStyle dimStyle = new GUIStyle(EditorStyles.miniLabel) {
			fontSize = 9,
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleCenter,
			normal = { textColor = new Color(0.85f, 0.92f, 1f, 0.95f) }
		};

		// ── Track Width Dimension Line (Drawn across rear axle) ───────────
		float dimTrackY = rearAxleY + 30f;
		if (dimTrackY < canvasRect.yMax - 14f) {
			Handles.color = new Color(0.38f, 0.72f, 1f, 0.7f);
			float leftX = center.x - halfTrackPx;
			float rightX = center.x + halfTrackPx;

			// Horizontal line
			Handles.DrawLine(new Vector3(leftX, dimTrackY), new Vector3(rightX, dimTrackY));
			// End caps
			Handles.DrawLine(new Vector3(leftX, dimTrackY - 4f), new Vector3(leftX, dimTrackY + 4f));
			Handles.DrawLine(new Vector3(rightX, dimTrackY - 4f), new Vector3(rightX, dimTrackY + 4f));

			// Label
			Rect trackLabelRect = new Rect(center.x - 45f, dimTrackY - 12f, 90f, 14f);
			EditorGUI.DrawRect(trackLabelRect, new Color(0.06f, 0.08f, 0.11f, 0.85f));
			GUI.Label(trackLabelRect, $"Track {trackWidthM:F2}m", dimStyle);
		}

		// ── Wheelbase Dimension Line (Drawn along right side) ─────────────
		float dimWbX = center.x + halfTrackPx + 36f;
		if (dimWbX < canvasRect.xMax - 45f) {
			Handles.color = new Color(0.38f, 0.72f, 1f, 0.7f);

			// Vertical line
			Handles.DrawLine(new Vector3(dimWbX, frontAxleY), new Vector3(dimWbX, rearAxleY));
			// End caps
			Handles.DrawLine(new Vector3(dimWbX - 4f, frontAxleY), new Vector3(dimWbX + 4f, frontAxleY));
			Handles.DrawLine(new Vector3(dimWbX - 4f, rearAxleY), new Vector3(dimWbX + 4f, rearAxleY));

			// Label
			Rect wbLabelRect = new Rect(dimWbX + 4f, center.y - 8f, 80f, 16f);
			EditorGUI.DrawRect(wbLabelRect, new Color(0.06f, 0.08f, 0.11f, 0.85f));
			GUI.Label(wbLabelRect, $"WB {wheelbaseM:F2}m", dimStyle);
		}
	}

	static void DrawMetricsBar (
		float wheelSpread,
		float wheelBase,
		float maxSteerAngle,
		float minTurningRadius,
		float currentAngle
	) {
		float trackWidth = wheelSpread * 2f;
		float absAngle = Mathf.Abs(currentAngle);
		float estRadius = absAngle > 0.5f ? (wheelBase / Mathf.Sin(absAngle * Mathf.Deg2Rad)) : float.PositiveInfinity;
		string estRadiusText = float.IsInfinity(estRadius) ? "∞ (Straight)" : $"{estRadius:F1} m";

		EditorGUILayout.BeginHorizontal();

		DrawMetricCard("TRACK WIDTH", $"{trackWidth:F2} m", new Color(0.38f, 0.72f, 1f));
		DrawMetricCard("WHEELBASE", $"{wheelBase:F2} m", new Color(0.38f, 0.72f, 1f));
		DrawMetricCard("STEER LOCK", $"±{maxSteerAngle:F0}°", new Color(0.98f, 0.75f, 0.15f));
		DrawMetricCard("MIN RADIUS", $"{minTurningRadius:F1} m", new Color(0.34f, 0.8f, 0.44f));
		DrawMetricCard("EST. RADIUS", estRadiusText, new Color(0.22f, 0.74f, 0.97f));

		EditorGUILayout.EndHorizontal();
	}

	static void DrawMetricCard (string label, string value, Color accent) {
		EditorGUILayout.BeginVertical(EditorStyles.textArea, GUILayout.MinWidth(65));
		GUIStyle lblStyle = new GUIStyle(EditorStyles.miniLabel) {
			fontSize = 8,
			alignment = TextAnchor.MiddleCenter,
			normal = { textColor = new Color(0.6f, 0.68f, 0.76f) }
		};
		GUIStyle valStyle = new GUIStyle(EditorStyles.boldLabel) {
			fontSize = 11,
			alignment = TextAnchor.MiddleCenter,
			normal = { textColor = accent }
		};
		EditorGUILayout.LabelField(label, lblStyle);
		EditorGUILayout.LabelField(value, valStyle);
		EditorGUILayout.EndVertical();
	}
}
