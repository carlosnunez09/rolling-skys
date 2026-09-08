using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

/// <summary>
/// In-game dev playtest suite and telemetry hub for internal QA and physics tuning.
/// Provides waypoint teleportation, checkpoint reset, god mode, gravity scaling,
/// game speed manipulation, and session telemetry export.
/// </summary>
public class DevPlaytestManager : MonoBehaviour {

	public static DevPlaytestManager Instance { get; private set; }

	[BoxGroup("Dev Suite"), SerializeField]
	bool startVisible = false;

	[BoxGroup("Dev Suite"), SerializeField]
	MovingCar car;

	[BoxGroup("Dev Suite"), SerializeField]
	RaceRuntime raceRuntime;

	[BoxGroup("Dev Suite"), SerializeField]
	OrbitCamera orbitCamera;

	bool _visible;
	int  _activeTab; // 0 = Quick Actions & Teleport, 1 = Physics & Cheats, 2 = Session Telemetry
	readonly string[] _tabNames = { "Actions & Nav", "Physics Cheats", "Session Telemetry" };

	// Cheats State
	bool  _godMode;
	bool  _infiniteBoost;
	float _gravityScale = 1.0f;
	float _timeScale = 1.0f;

	// Telemetry State
	float _sessionStartTime;
	float _topSpeedKmh;
	float _accumulatedSpeed;
	int   _speedSampleCount;
	float _totalDistance;
	float _totalDriftTime;
	int   _miniTurboCount;
	float _totalAirtime;
	int   _jumpCount;
	int   _collisionCount;
	float _maxCollisionImpact;
	Vector3 _prevPos;
	bool    _wasAirborne;
	bool    _wasMiniTurboReady;

	// Waypoint navigation
	int _selectedWaypointIndex;

	public bool IsOpen => _visible;
	public bool GodModeEnabled => _godMode;

	void Awake () {
		if (Instance != null && Instance != this) {
			Destroy(gameObject);
			return;
		}
		Instance = this;
		_visible = startVisible;
		_sessionStartTime = Time.unscaledTime;
	}

	void Start () {
		BindReferences();
		if (car != null) _prevPos = car.transform.position;
	}

	void BindReferences () {
		if (car == null || !car.HasLocalControl) {
			MovingCar[] cars = FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude);
			foreach (var c in cars) {
				if (c != null && c.HasLocalControl) {
					car = c;
					break;
				}
			}
		}
		if (raceRuntime == null) raceRuntime = FindAnyObjectByType<RaceRuntime>();
		if (orbitCamera == null) orbitCamera = FindAnyObjectByType<OrbitCamera>();
	}

	void Update () {
		// Toggle hotkeys: F1 or Gamepad Start/Options/Menu
		if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) {
			ToggleMenu();
		}
		if (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame) {
			ToggleMenu();
		}

		if (car == null) {
			BindReferences();
			if (car == null) return;
		}

		UpdateTelemetry();
		ApplyPhysicsCheats();
	}

	public void ToggleMenu () {
		_visible = !_visible;
	}

	void UpdateTelemetry () {
		float dt = Time.deltaTime;
		float kmh = car.Speed * 3.6f;

		if (kmh > _topSpeedKmh) _topSpeedKmh = kmh;
		_accumulatedSpeed += kmh;
		_speedSampleCount++;

		if (car.IsGrounded) {
			float d = Vector3.Distance(car.transform.position, _prevPos);
			if (d < 50f) _totalDistance += d;
		}
		_prevPos = car.transform.position;

		if (car.IsDrifting && car.IsGrounded) {
			_totalDriftTime += dt;
		}

		// Mini-turbo burst detection
		if (_wasMiniTurboReady && !car.MiniTurboReady && !car.IsDrifting && kmh > 15f) {
			_miniTurboCount++;
		}
		_wasMiniTurboReady = car.MiniTurboReady;

		// Airtime & Jump detection
		bool isAir = !car.IsGrounded;
		if (isAir) {
			_totalAirtime += dt;
			if (!_wasAirborne && car.IsJumping) _jumpCount++;
		}
		_wasAirborne = isAir;
	}

	void ApplyPhysicsCheats () {
		// God Mode: auto-catch abyss fallouts
		if (_godMode && car != null) {
			Vector3 carPos = car.transform.position;
			// If fallen far from world center or moving excessively fast away
			if (carPos.sqrMagnitude > 4000000f || carPos.y < -1500f) {
				car.Respawn();
			}
		}

		// Infinite Boost
		if (_infiniteBoost && car != null && car.HasLocalControl) {
			var body = car.GetComponent<Rigidbody>();
			if (body != null && Keyboard.current != null && Keyboard.current.spaceKey.isPressed) {
				body.AddForce(car.transform.forward * 25f, ForceMode.Acceleration);
			}
		}
	}

	public void RecordCollision (float impactVelocity) {
		_collisionCount++;
		if (impactVelocity > _maxCollisionImpact) _maxCollisionImpact = impactVelocity;
	}

	// ── Teleportation & Checkpoint Controls ───────────────────────────

	public void RespawnAtTrack () {
		if (car != null) car.Respawn();
	}

	public void TeleportToWaypoint (int index) {
		if (car == null || raceRuntime == null || raceRuntime.ActivePath == null) return;
		var path = raceRuntime.ActivePath;
		if (index < 0 || index >= path.Count) return;

		var wp = path.GetWaypoint(index);
		if (wp == null) return;

		var body = car.GetComponent<Rigidbody>();
		Vector3 up = CustomGravity.GetUpAxis(wp.transform.position);
		Vector3 fwd = Vector3.ProjectOnPlane(wp.transform.forward, up);
		if (fwd.sqrMagnitude < 0.001f) fwd = wp.transform.forward;

		car.transform.position = wp.transform.position + up * 1.5f;
		car.transform.rotation = Quaternion.LookRotation(fwd.normalized, up);
		if (body != null) {
			body.position = car.transform.position;
			body.rotation = car.transform.rotation;
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
		}
	}

	public void SetGravityScale (float scale) {
		_gravityScale = scale;
		CustomGravity.GlobalGravityScale = scale;
	}

	public void SetTimeScale (float scale) {
		_timeScale = scale;
		Time.timeScale = scale;
	}

	public void ResetTelemetry () {
		_sessionStartTime = Time.unscaledTime;
		_topSpeedKmh = 0f;
		_accumulatedSpeed = 0f;
		_speedSampleCount = 0;
		_totalDistance = 0f;
		_totalDriftTime = 0f;
		_miniTurboCount = 0;
		_totalAirtime = 0f;
		_jumpCount = 0;
		_collisionCount = 0;
		_maxCollisionImpact = 0f;
	}

	public string GenerateTelemetryReport () {
		var sb = new StringBuilder();
		float sessionTime = Time.unscaledTime - _sessionStartTime;
		float avgSpeed = _speedSampleCount > 0 ? (_accumulatedSpeed / _speedSampleCount) : 0f;

		sb.AppendLine("### Rolling Skys — Playtest Session Telemetry");
		sb.AppendLine($"- **Session Duration**: {sessionTime:F1}s ({(int)(sessionTime / 60)}m {sessionTime % 60:F0}s)");
		sb.AppendLine($"- **Top Speed**: {_topSpeedKmh:F1} km/h");
		sb.AppendLine($"- **Average Speed**: {avgSpeed:F1} km/h");
		sb.AppendLine($"- **Distance Covered**: {_totalDistance:F1} m");
		sb.AppendLine($"- **Drift Time**: {_totalDriftTime:F1} s");
		sb.AppendLine($"- **Mini-Turbos Triggered**: {_miniTurboCount}");
		sb.AppendLine($"- **Jumps Performed**: {_jumpCount}");
		sb.AppendLine($"- **Total Airtime**: {_totalAirtime:F1} s");
		sb.AppendLine($"- **Collisions Logged**: {_collisionCount}");
		string surfaceName = car != null ? car.SurfaceName : "None";
		string gravSource = car != null ? car.GravitySource : "None";
		sb.AppendLine($"- **Active Surface**: {surfaceName}");
		sb.AppendLine($"- **Active Gravity Source**: {gravSource}");
		return sb.ToString();
	}

	// ── OnGUI Dev Window ──────────────────────────────────────────────

	GUIStyle _winBgStyle, _headerStyle, _subHeaderStyle, _btnTabStyle, _btnActionStyle, _labelStyle, _valStyle;

	void EnsureStyles () {
		if (_winBgStyle != null) return;
		_winBgStyle = new GUIStyle(GUI.skin.box);
		_headerStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 15,
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleLeft
		};
		_headerStyle.normal.textColor = new Color(0.35f, 0.9f, 0.78f);

		_subHeaderStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 12,
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleLeft
		};
		_subHeaderStyle.normal.textColor = new Color(1f, 0.82f, 0.25f);

		_btnTabStyle = new GUIStyle(GUI.skin.button) {
			fontSize = 12,
			fontStyle = FontStyle.Bold
		};

		_btnActionStyle = new GUIStyle(GUI.skin.button) {
			fontSize = 12
		};

		_labelStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 12
		};
		_labelStyle.normal.textColor = new Color(0.85f, 0.88f, 0.92f);

		_valStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 12,
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleRight
		};
		_valStyle.normal.textColor = Color.white;
	}

	void OnGUI () {
		if (!_visible) return;
		EnsureStyles();

		Matrix4x4 prevMat = GUI.matrix;
		Color prevCol = GUI.color;

		float scale = Mathf.Min(1.2f, Mathf.Min(Screen.width / 950f, Screen.height / 700f));
		GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);

		try {
			float w = 480f;
			float h = 420f;
			float x = 20f;
			float y = 50f;

			// Window Background
			FillRect(new Rect(x, y, w, h), new Color(0.035f, 0.055f, 0.08f, 0.96f));
			FillRect(new Rect(x, y, w, 4), new Color(0.35f, 0.9f, 0.78f)); // Cyan header accent

			// Title Bar
			GUI.Label(new Rect(x + 16, y + 10, 260, 24), "PLAYTEST DEV SUITE", _headerStyle);
			string toggleHint = $"[{InputDeviceManager.Prompt(GameAction.PlaytestMenu)}] Close";
			if (GUI.Button(new Rect(x + w - 105, y + 10, 90, 24), toggleHint, _btnActionStyle)) {
				ToggleMenu();
			}

			// Tabs
			float tabW = (w - 32) / _tabNames.Length;
			for (int t = 0; t < _tabNames.Length; t++) {
				Rect tabRect = new Rect(x + 16 + t * tabW, y + 42, tabW - 4, 28);
				if (t == _activeTab) {
					FillRect(tabRect, new Color(0.12f, 0.28f, 0.29f));
				}
				if (GUI.Button(tabRect, _tabNames[t], _btnTabStyle)) {
					_activeTab = t;
				}
			}

			// Content based on tab
			Rect contentRect = new Rect(x + 16, y + 78, w - 32, h - 90);
			switch (_activeTab) {
				case 0: DrawQuickActionsTab(contentRect); break;
				case 1: DrawPhysicsCheatsTab(contentRect); break;
				case 2: DrawTelemetryTab(contentRect); break;
			}

		} finally {
			GUI.matrix = prevMat;
			GUI.color = prevCol;
		}
	}

	void DrawQuickActionsTab (Rect rect) {
		float cy = rect.y;

		GUI.Label(new Rect(rect.x, cy, rect.width, 20), "VEHICLE RECOVERY & RESET", _subHeaderStyle);
		cy += 24;

		if (GUI.Button(new Rect(rect.x, cy, 210, 30), "Respawn Upright [R]", _btnActionStyle)) {
			RespawnAtTrack();
		}
		if (GUI.Button(new Rect(rect.x + 220, cy, 210, 30), "Restart Lap / Run", _btnActionStyle)) {
			if (raceRuntime != null && raceRuntime.ActivePath != null) raceRuntime.StartRaceManual(raceRuntime.ActivePath, raceRuntime.ActiveTrackName);
			else RespawnAtTrack();
		}
		cy += 40;

		GUI.Label(new Rect(rect.x, cy, rect.width, 20), "WAYPOINT TELEPORTATION", _subHeaderStyle);
		cy += 24;

		int wpCount = (raceRuntime != null && raceRuntime.ActivePath != null) ? raceRuntime.ActivePath.Count : 0;
		if (wpCount > 0) {
			GUI.Label(new Rect(rect.x, cy + 4, 180, 22), $"Target Waypoint: #{_selectedWaypointIndex + 1} / {wpCount}", _labelStyle);

			if (GUI.Button(new Rect(rect.x + 200, cy, 40, 26), "◀", _btnActionStyle)) {
				_selectedWaypointIndex = Mathf.Max(0, _selectedWaypointIndex - 1);
			}
			if (GUI.Button(new Rect(rect.x + 245, cy, 40, 26), "▶", _btnActionStyle)) {
				_selectedWaypointIndex = Mathf.Min(wpCount - 1, _selectedWaypointIndex + 1);
			}
			if (GUI.Button(new Rect(rect.x + 295, cy, 135, 26), "Teleport Now", _btnActionStyle)) {
				TeleportToWaypoint(_selectedWaypointIndex);
			}
		} else {
			GUI.Label(new Rect(rect.x, cy, rect.width, 22), "No active WaypointPath found in scene.", _labelStyle);
		}
		cy += 44;

		GUI.Label(new Rect(rect.x, cy, rect.width, 20), "CAMERA & MODES", _subHeaderStyle);
		cy += 24;

		bool isCinematic = orbitCamera != null && orbitCamera.IsCinematicActive;
		string camBtnLabel = isCinematic ? "Disable Cinematic Cam [C]" : "Enable Cinematic Cam [C]";
		if (GUI.Button(new Rect(rect.x, cy, 210, 30), camBtnLabel, _btnActionStyle)) {
			orbitCamera?.ToggleCinematicMode();
		}

		var tut = TutorialManager.Instance;
		bool tutActive = tut != null && tut.IsActive;
		string tutBtnLabel = tutActive ? "Stop Tutorial [T]" : "Start Tutorial [T]";
		if (GUI.Button(new Rect(rect.x + 220, cy, 210, 30), tutBtnLabel, _btnActionStyle)) {
			if (tutActive) tut.StopTutorial();
			else tut.StartTutorial();
		}
	}

	void DrawPhysicsCheatsTab (Rect rect) {
		float cy = rect.y;

		GUI.Label(new Rect(rect.x, cy, rect.width, 20), "GAMEPLAY TOGGLES", _subHeaderStyle);
		cy += 24;

		bool newGod = GUI.Toggle(new Rect(rect.x, cy, 200, 24), _godMode, " God Mode (Auto Catch / Void Safe)");
		if (newGod != _godMode) _godMode = newGod;

		bool newBoost = GUI.Toggle(new Rect(rect.x + 220, cy, 210, 24), _infiniteBoost, " Infinite Boost (Hold Space)");
		if (newBoost != _infiniteBoost) _infiniteBoost = newBoost;
		cy += 36;

		GUI.Label(new Rect(rect.x, cy, rect.width, 20), $"GLOBAL GRAVITY SCALE: {_gravityScale:F2}x", _subHeaderStyle);
		cy += 24;

		float newGrav = GUI.HorizontalSlider(new Rect(rect.x, cy, 280, 20), _gravityScale, 0.05f, 3.0f);
		if (Mathf.Abs(newGrav - _gravityScale) > 0.01f) {
			SetGravityScale(newGrav);
		}

		if (GUI.Button(new Rect(rect.x + 295, cy - 4, 65, 24), "Moon (0.3)", _btnActionStyle)) SetGravityScale(0.3f);
		if (GUI.Button(new Rect(rect.x + 365, cy - 4, 65, 24), "Earth (1.0)", _btnActionStyle)) SetGravityScale(1.0f);
		cy += 36;

		GUI.Label(new Rect(rect.x, cy, rect.width, 20), $"TIME SCALE (SLOMO): {_timeScale:F2}x", _subHeaderStyle);
		cy += 24;

		float newTime = GUI.HorizontalSlider(new Rect(rect.x, cy, 280, 20), _timeScale, 0.1f, 2.0f);
		if (Mathf.Abs(newTime - _timeScale) > 0.01f) {
			SetTimeScale(newTime);
		}

		if (GUI.Button(new Rect(rect.x + 295, cy - 4, 65, 24), "0.5x Slow", _btnActionStyle)) SetTimeScale(0.5f);
		if (GUI.Button(new Rect(rect.x + 365, cy - 4, 65, 24), "1.0x Norm", _btnActionStyle)) SetTimeScale(1.0f);
	}

	void DrawTelemetryTab (Rect rect) {
		float cy = rect.y;
		float sessionTime = Time.unscaledTime - _sessionStartTime;
		float avgSpeed = _speedSampleCount > 0 ? (_accumulatedSpeed / _speedSampleCount) : 0f;

		DrawMetricRow(rect.x, cy, "Session Time", $"{sessionTime:F1} s"); cy += 22;
		DrawMetricRow(rect.x, cy, "Top Speed", $"{_topSpeedKmh:F1} km/h"); cy += 22;
		DrawMetricRow(rect.x, cy, "Average Speed", $"{avgSpeed:F1} km/h"); cy += 22;
		DrawMetricRow(rect.x, cy, "Distance Covered", $"{_totalDistance:F1} m"); cy += 22;
		DrawMetricRow(rect.x, cy, "Drift Time", $"{_totalDriftTime:F1} s"); cy += 22;
		DrawMetricRow(rect.x, cy, "Mini-Turbos Triggered", $"{_miniTurboCount}"); cy += 22;
		DrawMetricRow(rect.x, cy, "Airtime / Jumps", $"{_totalAirtime:F1}s  ({_jumpCount} jumps)"); cy += 22;
		DrawMetricRow(rect.x, cy, "Gravity Source", car != null ? car.GravitySource : "None"); cy += 22;
		DrawMetricRow(rect.x, cy, "Surface Layer", car != null ? car.SurfaceName : "None"); cy += 28;

		if (GUI.Button(new Rect(rect.x, cy, 210, 30), "Copy Report to Clipboard", _btnActionStyle)) {
			GUIUtility.systemCopyBuffer = GenerateTelemetryReport();
		}
		if (GUI.Button(new Rect(rect.x + 225, cy, 205, 30), "Reset Telemetry Counters", _btnActionStyle)) {
			ResetTelemetry();
		}
	}

	void DrawMetricRow (float x, float y, string label, string val) {
		GUI.Label(new Rect(x, y, 220, 20), label, _labelStyle);
		GUI.Label(new Rect(x + 220, y, 210, 20), val, _valStyle);
	}

	static void FillRect (Rect rect, Color color) {
		Color prev = GUI.color;
		GUI.color = color;
		GUI.DrawTexture(rect, Texture2D.whiteTexture);
		GUI.color = prev;
	}
}
