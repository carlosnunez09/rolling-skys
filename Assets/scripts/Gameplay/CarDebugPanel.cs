using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Runtime-only telemetry view. Samples at 10 Hz to keep values readable.</summary>
public sealed class CarDebugPanel {
    static readonly Color Background = new Color(0.035f, 0.055f, 0.085f, 0.96f);
    static readonly Color Accent = new Color(0.35f, 0.9f, 0.78f);
    static readonly string[] Tabs = { "Driving", "Surface", "Race" };
    readonly string[,] values = new string[3, 12];
    readonly string[][] labels = {
        new[] { "Forward", "Lateral", "Acceleration", "Downforce", "RPM", "Torque", "Turn rate", "Drift angle", "Turbo charge", "Landing slip", "Grip", "Heading" },
        new[] { "Surface", "Speed multiplier", "Hazard", "Gravity source", "Gravity strength", "Ground angle" },
        new[] { "Track", "Phase", "Position", "Completed laps", "Checkpoints", "Race time" }
    };
    GUIStyle titleStyle, captionStyle, labelStyle, valueStyle, speedStyle, tabStyle;
    bool visible = true;
    int selectedTab;
    float nextSample;
    string speed = "--", state = "WAITING FOR LOCAL CAR";
    bool hazard;
    float speedRatio, turboRatio;

    public void Tick (MovingCar car, RaceRuntime race) {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
            visible = !visible;
        if (Time.unscaledTime < nextSample) return;
        nextSample = Time.unscaledTime + 0.1f;
        if (car == null) {
            speed = "--";
            state = "WAITING FOR LOCAL CAR";
            hazard = false;
            speedRatio = turboRatio = 0f;
            for (int t = 0; t < labels.Length; t++)
                for (int i = 0; i < labels[t].Length; i++) values[t, i] = "--";
            return;
        }

        speed = (car.Speed * 3.6f).ToString("F0");
        state = !car.IsGrounded ? "AIRBORNE" : car.IsDrifting ? "DRIFTING" : "GROUNDED";
        hazard = car.IsOnHazard;
        speedRatio = Mathf.Clamp01(car.SpeedRatio);
        turboRatio = Mathf.Clamp01(car.MiniTurboChargeRatio);
        values[0, 0] = $"{car.ForwardSpeed * 3.6f:F1} km/h";
        values[0, 1] = $"{car.LateralSpeed:F2} m/s";
        values[0, 2] = $"{car.Acceleration:F1} m/s²";
        values[0, 3] = $"{car.Downforce:F1} m/s²";
        values[0, 4] = $"{car.RPM:F0}";
        values[0, 5] = $"{car.TorqueRatio:P0}";
        values[0, 6] = $"{car.YawRate:F1} °/s";
        values[0, 7] = $"{car.DriftAngle:F1}°";
        values[0, 8] = car.MiniTurboReady ? "READY" : $"{car.MiniTurboChargeRatio:P0}";
        values[0, 9] = $"{car.LandingSlip:P0}";
        values[0, 10] = $"{car.GroundFriction:P0}";
        values[0, 11] = $"{car.transform.eulerAngles.y:F0}°";
        values[1, 0] = car.SurfaceName;
        values[1, 1] = $"{car.SurfaceSpeedMultiplier:P0}";
        values[1, 2] = hazard ? "DANGER" : "Clear";
        values[1, 3] = car.GravitySource;
        values[1, 4] = $"{car.GravityStrength:F2} m/s²";
        values[1, 5] = $"{car.GroundAngle:F1}°";
        values[2, 0] = race != null && !string.IsNullOrEmpty(race.ActiveTrackName) ? race.ActiveTrackName : "Free drive";
        values[2, 1] = race != null ? race.Phase.ToString() : "No race";
        for (int i = 2; i < 6; i++) values[2, i] = "--";
        if (race == null || race.Racers == null) return;
        foreach (var racer in race.Racers) {
            if (racer == null || racer.Car != car) continue;
            values[2, 2] = racer.Position.ToString();
            values[2, 3] = racer.LapCount.ToString();
            values[2, 4] = racer.CheckpointsCrossed.ToString();
            values[2, 5] = $"{(int)(racer.RaceTime / 60f)}:{racer.RaceTime % 60f:00.000}";
            break;
        }
    }

    public void Draw () {
        EnsureStyles();
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        float scale = Mathf.Min(1.25f, Mathf.Min(Screen.width / 900f, Screen.height / 650f));
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
        try {
            if (!visible) {
                Fill(new Rect(16, 16, 184, 32), Background);
                if (GUI.Button(new Rect(16, 16, 184, 32), "F3  /  Show telemetry", tabStyle)) visible = true;
                return;
            }
            const float x = 16, y = 16, width = 344;
            float height = 218 + labels[selectedTab].Length * 28;
            Fill(new Rect(x, y, width, height), Background);
            Fill(new Rect(x, y, 3, height), Accent);
            GUI.Label(new Rect(x + 18, y + 14, 220, 25), "VEHICLE TELEMETRY", titleStyle);
            if (GUI.Button(new Rect(x + 270, y + 10, 60, 30), "F3  Hide", tabStyle)) visible = false;
            GUI.Label(new Rect(x + 18, y + 41, 300, 22), hazard ? state + "  /  HAZARD" : state, captionStyle);
            GUI.Label(new Rect(x + 16, y + 65, 160, 56), speed, speedStyle);
            GUI.Label(new Rect(x + 180, y + 91, 130, 24), "km/h", captionStyle);
            Fill(new Rect(x + 18, y + 128, 308, 4), new Color(0.15f, 0.2f, 0.25f));
            Fill(new Rect(x + 18, y + 128, 308 * speedRatio, 4), Accent);
            for (int t = 0; t < Tabs.Length; t++) {
                Rect rect = new Rect(x + 18 + t * 104, y + 147, 100, 30);
                if (t == selectedTab) Fill(rect, new Color(0.12f, 0.28f, 0.29f));
                if (GUI.Button(rect, Tabs[t], tabStyle)) selectedTab = t;
            }
            for (int i = 0; i < labels[selectedTab].Length; i++) {
                float rowY = y + 188 + i * 28;
                if (i % 2 == 0) Fill(new Rect(x + 12, rowY, 320, 28), new Color(1f, 1f, 1f, 0.035f));
                GUI.Label(new Rect(x + 20, rowY + 3, 132, 22), labels[selectedTab][i], labelStyle);
                string value = values[selectedTab, i] ?? "--";
                GUI.Label(new Rect(x + 154, rowY + 3, 170, 22), new GUIContent(value, value), valueStyle);
            }
            // A persistent charge strip gives drift tuning a quick visual reference.
            Fill(new Rect(x + 18, y + height - 10, 308 * turboRatio, 3), new Color(1f, 0.72f, 0.3f));
        } finally {
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }

    void EnsureStyles () {
        if (titleStyle != null) return;
        titleStyle = Style(14, Color.white, FontStyle.Bold);
        captionStyle = Style(12, Accent);
        labelStyle = Style(13, new Color(0.63f, 0.7f, 0.77f));
        valueStyle = Style(13, new Color(0.92f, 0.95f, 0.98f));
        valueStyle.alignment = TextAnchor.MiddleRight;
        speedStyle = Style(46, Color.white, FontStyle.Bold);
        tabStyle = Style(12, Color.white);
        tabStyle.alignment = TextAnchor.MiddleCenter;
    }

    static GUIStyle Style (int size, Color color, FontStyle weight = FontStyle.Normal) {
        var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight, richText = false, clipping = TextClipping.Clip };
        style.normal.textColor = color;
        return style;
    }

    static void Fill (Rect rect, Color color) {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
