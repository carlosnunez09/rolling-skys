using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NaughtyAttributes;

/// <summary>
/// Full instrument-panel HUD for the player car.
/// Wire up the car, the race runtime, and any UI elements you want active.
/// Anything left null is safely skipped at runtime.
/// </summary>
public class CarHUD : MonoBehaviour {

    // ── References ────────────────────────────────────────────────────

    [BoxGroup("Car"), SerializeField, Required]
    MovingCar car;

    [BoxGroup("Race"), SerializeField]
    RaceRuntime raceRuntime;

    // ── Primary Gauges — Text ─────────────────────────────────────────

    [BoxGroup("Speed Text"), SerializeField, Label("Speed  (km/h)")]
    TextMeshProUGUI speedText;

    [BoxGroup("Speed Text"), SerializeField, Label("Speed Unit Label")]
    TextMeshProUGUI speedUnitText;

    [BoxGroup("Speed Text"), SerializeField, Label("Direction  (FWD / REV)")]
    TextMeshProUGUI directionText;

    [BoxGroup("Speed Text"), SerializeField, Label("Acceleration  m/s²")]
    TextMeshProUGUI accelerationText;

    // ── Speedometer Dial ──────────────────────────────────────────────

    [BoxGroup("Speedometer Dial"), SerializeField, Label("Dial Fill Image")]
    [Tooltip("A UI Image set to Filled / Radial 360.  " +
             "Fill Amount is driven by the current speed ratio.")]
    Image speedDialFill;

    [BoxGroup("Speedometer Dial"), SerializeField, Label("Dial Needle")]
    [Tooltip("Transform of the needle sprite.  " +
             "Its Z rotation is mapped from min to max angle.")]
    RectTransform speedNeedle;

    [BoxGroup("Speedometer Dial"), SerializeField, Label("Needle Min Angle °")]
    float needleMinAngle = 130f;

    [BoxGroup("Speedometer Dial"), SerializeField, Label("Needle Max Angle °")]
    float needleMaxAngle = -130f;

    [BoxGroup("Speedometer Dial"), SerializeField, Label("Max Speed Label")]
    TextMeshProUGUI maxSpeedLabel;

    // ── Torque Curve Graph ────────────────────────────────────────────

    [BoxGroup("Torque Graph"), SerializeField, Label("Graph Raw Image")]
    [Tooltip("RawImage whose texture is replaced at runtime with the drawn curve.")]
    RawImage torqueGraphImage;

    [BoxGroup("Torque Graph"), SerializeField, Label("Current Torque Marker")]
    [Tooltip("Small UI image that slides horizontally to show current torque on the graph.")]
    RectTransform torqueMarker;

    [BoxGroup("Torque Graph"), SerializeField, Label("Torque Value Text")]
    TextMeshProUGUI torqueValueText;

    [BoxGroup("Torque Graph"), SerializeField, Label("RPM Text")]
    TextMeshProUGUI rpmText;

    [BoxGroup("Torque Graph"), SerializeField, Label("Graph Width px")]
    int graphWidth  = 256;

    [BoxGroup("Torque Graph"), SerializeField, Label("Graph Height px")]
    int graphHeight = 96;

    [BoxGroup("Torque Graph"), SerializeField, Label("Graph BG Color")]
    Color graphBgColor  = new Color(0.07f, 0.07f, 0.09f, 1f);

    [BoxGroup("Torque Graph"), SerializeField, Label("Curve Color")]
    Color graphLineColor = new Color(0.2f, 0.9f, 0.4f, 1f);

    [BoxGroup("Torque Graph"), SerializeField, Label("Marker Color")]
    Color markerLineColor = new Color(1f, 0.8f, 0.1f, 1f);

    // ── Handling ──────────────────────────────────────────────────────

    [BoxGroup("Handling"), SerializeField, Label("Yaw Rate  °/s")]
    TextMeshProUGUI yawRateText;

    [BoxGroup("Handling"), SerializeField, Label("Turn Rate Bar")]
    [Tooltip("Slider or Image (filled) that shows yaw rate magnitude 0–180 °/s.")]
    Slider turnRateBar;

    [BoxGroup("Handling"), SerializeField, Label("Max Yaw For Bar  °/s")]
    float maxYawForBar = 180f;

    [BoxGroup("Handling"), SerializeField, Label("Lateral Speed  m/s (drift)")]
    TextMeshProUGUI lateralSpeedText;

    [BoxGroup("Handling"), SerializeField, Label("Landing Slip  %")]
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

    // ── Race Panel ────────────────────────────────────────────────────

    [BoxGroup("Race Panel"), SerializeField, Label("Track Name")]
    TextMeshProUGUI trackNameText;

    [BoxGroup("Race Panel"), SerializeField, Label("Planet Name")]
    TextMeshProUGUI planetNameText;

    [BoxGroup("Race Panel"), SerializeField, Label("Lap")]
    TextMeshProUGUI lapText;

    [BoxGroup("Race Panel"), SerializeField, Label("Checkpoint")]
    TextMeshProUGUI checkpointText;

    [BoxGroup("Race Panel"), SerializeField, Label("Race Time")]
    TextMeshProUGUI raceTimeText;

    [BoxGroup("Race Panel"), SerializeField, Label("Position")]
    TextMeshProUGUI positionText;

    // ── Debug Block ───────────────────────────────────────────────────

    [BoxGroup("Debug Block"), SerializeField, Label("All Stats Block")]
    TextMeshProUGUI debugBlock;

    // ── Private State ─────────────────────────────────────────────────

    Texture2D _torqueGraphTex;
    bool      _graphDirty = true;
    AnimationCurve _lastCurve;

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Start () {
        if (speedUnitText != null) speedUnitText.text = "km/h";
        BuildTorqueGraph();
    }

    void LateUpdate () {
        if (car == null) return;
        UpdateSpeedGauge();
        UpdateTorqueGraph();
        UpdateHandling();
        UpdateWorld();
        UpdateRacePanel();
        UpdateDebugBlock();
    }

    // ── Speed Gauge ───────────────────────────────────────────────────

    void UpdateSpeedGauge () {
        float kmh = car.Speed * 3.6f;

        Set(speedText, $"{kmh:F0}");
        Set(directionText,    car.ForwardSpeed >= 0f ? "FWD" : "REV");
        Set(accelerationText, $"{car.Acceleration:+0.0;-0.0} m/s²");
        Set(maxSpeedLabel,    $"{car.MaxSpeed * 3.6f:F0}");

        float ratio = car.SpeedRatio;

        if (speedDialFill != null)
            speedDialFill.fillAmount = ratio;

        if (speedNeedle != null) {
            float angle = Mathf.Lerp(needleMinAngle, needleMaxAngle, ratio);
            speedNeedle.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }

    // ── Torque Curve Graph ────────────────────────────────────────────

    void BuildTorqueGraph () {
        if (torqueGraphImage == null) return;

        _torqueGraphTex = new Texture2D(graphWidth, graphHeight, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        RedrawGraph();
        torqueGraphImage.texture = _torqueGraphTex;
        _lastCurve = car != null ? car.TorqueCurve : null;
    }

    void RedrawGraph () {
        if (_torqueGraphTex == null || car == null) return;

        var pixels = new Color[graphWidth * graphHeight];

        // Background
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = graphBgColor;

        // Curve — sample N points
        for (int x = 0; x < graphWidth; x++) {
            float t   = x / (float)(graphWidth - 1);
            float val = car.TorqueCurve.Evaluate(t);
            int   yPx = Mathf.RoundToInt(val * (graphHeight - 2));

            // Draw a thick line (3 px) for visibility
            for (int dy = -1; dy <= 1; dy++) {
                int py = Mathf.Clamp(yPx + dy, 0, graphHeight - 1);
                pixels[py * graphWidth + x] = graphLineColor;
            }
        }

        _torqueGraphTex.SetPixels(pixels);
        _torqueGraphTex.Apply();
        _graphDirty = false;
    }

    void UpdateTorqueGraph () {
        if (car == null) return;

        // Rebuild the static curve image if the curve changed (editor-side tuning)
        if (_graphDirty || _torqueGraphTex == null)
            BuildTorqueGraph();

        float torque = car.TorqueRatio;
        float rpm    = car.RPM;

        Set(torqueValueText, $"Torque  {torque * 100f:F0}%");
        Set(rpmText,         $"RPM  {rpm:F0}");

        // Slide the marker along the graph width to the current speed-ratio position
        if (torqueMarker != null && torqueGraphImage != null) {
            float ratio   = car.SpeedRatio;
            var   imgRect = torqueGraphImage.rectTransform.rect;
            float xPos    = Mathf.Lerp(imgRect.xMin, imgRect.xMax, ratio);
            torqueMarker.anchoredPosition = new Vector2(xPos, torqueMarker.anchoredPosition.y);
        }
    }

    // ── Handling ──────────────────────────────────────────────────────

    void UpdateHandling () {
        float absYaw = Mathf.Abs(car.YawRate);
        Set(yawRateText,      $"Turn  {car.YawRate:+0.0;-0.0} °/s");
        Set(lateralSpeedText, $"Lat  {car.LateralSpeed:F2} m/s");
        Set(landingSlipText,  $"Slip  {car.LandingSlip * 100f:F0}%");
        Set(driftingText,     car.IsDrifting ? "DRIFT" : "—");

        if (turnRateBar != null)
            turnRateBar.value = maxYawForBar > 0f
                ? Mathf.Clamp01(absYaw / maxYawForBar)
                : 0f;
    }

    // ── World ─────────────────────────────────────────────────────────

    void UpdateWorld () {
        Set(gravitySourceText,   car.GravitySource);
        Set(gravityStrengthText, $"G  {car.GravityStrength:F2} m/s²");
        Set(groundedText,        car.IsGrounded ? "Grounded" : "Airborne");
        Set(groundAngleText,     $"Angle  {car.GroundAngle:F1}°");
    }

    // ── Race Panel ────────────────────────────────────────────────────

    void UpdateRacePanel () {
        if (raceRuntime == null) return;

        Set(trackNameText,  !string.IsNullOrEmpty(raceRuntime.ActiveTrackName) ? raceRuntime.ActiveTrackName : "—");
        // Planet name removed — a track can span multiple planets.

        var player = PlayerRacerState();
        if (player == null) return;

        int totalLaps = raceRuntime.Racers.Count > 0 ? 1 : 1; // placeholder; lap total lives in RaceRuntime
        Set(lapText,        $"Lap  {player.LapCount + 1}");
        Set(checkpointText, $"CP  {player.CheckpointsCrossed}");
        Set(raceTimeText,   FormatTime(player.RaceTime));
        Set(positionText,   Ordinal(player.Position));
    }

    // ── Debug Block ───────────────────────────────────────────────────

    void UpdateDebugBlock () {
        if (debugBlock == null) return;

        float kmh    = car.Speed * 3.6f;
        float torque = car.TorqueRatio * 100f;
        float rpm    = car.RPM;
        string raceLine = "—";

        var player = PlayerRacerState();
        if (player != null)
            raceLine = $"T {FormatTime(player.RaceTime)}  Lap {player.LapCount}  Pos {Ordinal(player.Position)}";

        debugBlock.text =
            $"Speed        {kmh:F1} km/h\n"          +
            $"Fwd Speed    {car.ForwardSpeed * 3.6f:F1} km/h\n" +
            $"Lat Speed    {car.LateralSpeed:F2} m/s\n"         +
            $"Accel        {car.Acceleration:+0.0;-0.0} m/s²\n" +
            $"Turn Rate    {car.YawRate:+0.0;-0.0} °/s\n"        +
            $"Torque       {torque:F0} %\n"            +
            $"RPM          {rpm:F0}\n"                 +
            $"Grounded     {(car.IsGrounded ? "Yes" : "No")}\n" +
            $"Drifting     {(car.IsDrifting ? "Yes" : "No")}\n" +
            $"Land Slip    {car.LandingSlip * 100f:F0}%\n"       +
            $"Ground Ang   {car.GroundAngle:F1}°\n"   +
            $"Gravity      {car.GravityStrength:F2} m/s²\n"      +
            $"Grav Source  {car.GravitySource}\n"      +
            $"Race         {raceLine}";
    }

    // ── Helpers ───────────────────────────────────────────────────────

    RaceRuntime.RacerState PlayerRacerState () {
        if (raceRuntime == null) return null;
        foreach (var r in raceRuntime.Racers)
            if (r.Car == car) return r;
        return raceRuntime.Racers.Count > 0 ? raceRuntime.Racers[0] : null;
    }

    static void Set (TextMeshProUGUI field, string value) {
        if (field != null) field.text = value;
    }

    static string FormatTime (float t) {
        int   mins = (int)(t / 60f);
        float secs = t % 60f;
        return $"{mins}:{secs:00.000}";
    }

    static string Ordinal (int n) => n switch {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th"
    };
}
