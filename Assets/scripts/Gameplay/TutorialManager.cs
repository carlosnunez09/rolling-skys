using System;
using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

/// <summary>
/// Guided interactive tutorial demonstrating the core non-Euclidean mechanics of Rolling Skys:
/// basic driving, spherical planetary gravity, ramp/inverted ceiling adhesion,
/// variable jump heights, and counter-steer mini-turbo drifting.
/// </summary>
public class TutorialManager : MonoBehaviour {

	public static TutorialManager Instance { get; private set; }

	public enum TutorialStep {
		NotStarted,
		Accelerate,
		Brake,
		PlanetaryGravity,
		RampsAndInverted,
		VariableJump,
		DriftAndMiniTurbo,
		CourseCheckpoints,
		Completed
	}

	[BoxGroup("Settings"), SerializeField]
	bool startOnAwake = true;

	[BoxGroup("Settings"), SerializeField]
	bool showOverlay = true;

	[BoxGroup("State"), SerializeField, ReadOnly]
	TutorialStep currentStep = TutorialStep.NotStarted;

	[BoxGroup("Target Car"), SerializeField]
	MovingCar car;

	[BoxGroup("Target Car"), SerializeField]
	RaceRuntime raceRuntime;

	// Progress tracking
	float _stepTimer;
	float _stepProgress; // 0 to 1
	bool  _stepCompleted;
	float _transitionTimer;

	// Step-specific tracking
	Vector3 _lastPosition;
	float _accumulatedDistance;
	float _invertedTimer;
	float _airtimeTimer;
	bool  _miniTurboCharged;
	int   _checkpointsAtStart;
	int   _checkpointsPassedInStep;
	float _tutorialStartTime;
	float _tutorialTotalTime;

	// Telemetry stats during tutorial
	float _topSpeedKmh;
	float _totalDriftTime;
	int   _miniTurboCount;
	int   _jumpCount;

	public TutorialStep CurrentStep => currentStep;
	public bool IsActive => currentStep != TutorialStep.NotStarted && currentStep != TutorialStep.Completed;
	public float StepProgress => _stepProgress;

	void Awake () {
		if (Instance != null && Instance != this) {
			Destroy(gameObject);
			return;
		}
		Instance = this;
	}

	void Start () {
		if (startOnAwake) {
			StartTutorial();
		}
	}

	[Button("Start / Restart Tutorial")]
	public void StartTutorial () {
		BindCar();
		currentStep = TutorialStep.Accelerate;
		_stepTimer = 0f;
		_stepProgress = 0f;
		_stepCompleted = false;
		_transitionTimer = 0f;
		_tutorialStartTime = Time.time;
		_topSpeedKmh = 0f;
		_totalDriftTime = 0f;
		_miniTurboCount = 0;
		_jumpCount = 0;
		ResetStepTracking();
	}

	[Button("Skip Current Step")]
	public void SkipStep () {
		AdvanceStep();
	}

	[Button("Stop Tutorial")]
	public void StopTutorial () {
		currentStep = TutorialStep.NotStarted;
	}

	void BindCar () {
		if (car != null && car.HasLocalControl) return;

		MovingCar[] cars = FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude);
		foreach (var c in cars) {
			if (c != null && c.HasLocalControl) {
				car = c;
				break;
			}
		}
		if (raceRuntime == null) {
			raceRuntime = FindAnyObjectByType<RaceRuntime>();
		}
		if (car != null) {
			_lastPosition = car.transform.position;
		}
	}

	void Update () {
		// Hotkey toggle (T or Gamepad D-Pad Left)
		if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame) {
			if (IsActive) StopTutorial();
			else StartTutorial();
		}
		if (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame) {
			if (IsActive) StopTutorial();
			else StartTutorial();
		}

		if (currentStep == TutorialStep.NotStarted) return;

		if (car == null) {
			BindCar();
			if (car == null) return;
		}

		// Update global training metrics
		float currentKmh = car.Speed * 3.6f;
		if (currentKmh > _topSpeedKmh) _topSpeedKmh = currentKmh;
		if (car.IsDrifting) _totalDriftTime += Time.deltaTime;

		if (_stepCompleted) {
			_transitionTimer -= Time.deltaTime;
			if (_transitionTimer <= 0f) {
				AdvanceStep();
			}
			return;
		}

		_stepTimer += Time.deltaTime;
		UpdateCurrentStep();
	}

	void ResetStepTracking () {
		_stepTimer = 0f;
		_stepProgress = 0f;
		_stepCompleted = false;
		_transitionTimer = 0f;
		if (car != null) {
			_lastPosition = car.transform.position;
		}
		_accumulatedDistance = 0f;
		_invertedTimer = 0f;
		_airtimeTimer = 0f;
		_miniTurboCharged = false;
		_checkpointsPassedInStep = 0;

		if (raceRuntime != null && car != null) {
			foreach (var r in raceRuntime.Racers) {
				if (r != null && r.Car == car) {
					_checkpointsAtStart = r.CheckpointsCrossed;
					break;
				}
			}
		}
	}

	void CompleteStep () {
		_stepCompleted = true;
		_stepProgress = 1f;
		_transitionTimer = 1.3f;
	}

	void AdvanceStep () {
		switch (currentStep) {
			case TutorialStep.Accelerate:
				currentStep = TutorialStep.Brake;
				break;
			case TutorialStep.Brake:
				currentStep = TutorialStep.PlanetaryGravity;
				break;
			case TutorialStep.PlanetaryGravity:
				currentStep = TutorialStep.RampsAndInverted;
				break;
			case TutorialStep.RampsAndInverted:
				currentStep = TutorialStep.VariableJump;
				break;
			case TutorialStep.VariableJump:
				currentStep = TutorialStep.DriftAndMiniTurbo;
				break;
			case TutorialStep.DriftAndMiniTurbo:
				currentStep = TutorialStep.CourseCheckpoints;
				break;
			case TutorialStep.CourseCheckpoints:
				currentStep = TutorialStep.Completed;
				_tutorialTotalTime = Time.time - _tutorialStartTime;
				FindAnyObjectByType<OrbitCamera>()?.SetCinematicMode(true);
				break;
			case TutorialStep.Completed:
				currentStep = TutorialStep.NotStarted;
				break;
		}
		ResetStepTracking();
	}

	void UpdateCurrentStep () {
		float kmh = car.Speed * 3.6f;

		switch (currentStep) {
			case TutorialStep.Accelerate: {
				// Target: Reach 35 km/h
				_stepProgress = Mathf.Clamp01(kmh / 35f);
				if (kmh >= 35f) {
					CompleteStep();
				}
				break;
			}

			case TutorialStep.Brake: {
				// Target: Stop (< 3 km/h) after having reached at least some speed
				if (_stepTimer < 0.4f && kmh < 10f) {
					_stepProgress = 0f;
				} else {
					_stepProgress = Mathf.Clamp01(1f - (kmh / 25f));
					if (kmh <= 2.5f && _stepTimer > 0.8f) {
						CompleteStep();
					}
				}
				break;
			}

			case TutorialStep.PlanetaryGravity: {
				// Target: Drive 50 meters along the curved surface
				if (car.IsGrounded) {
					float deltaDist = Vector3.Distance(car.transform.position, _lastPosition);
					if (deltaDist < 5f) _accumulatedDistance += deltaDist;
				}
				_lastPosition = car.transform.position;
				_stepProgress = Mathf.Clamp01(_accumulatedDistance / 50f);
				if (_accumulatedDistance >= 50f) {
					CompleteStep();
				}
				break;
			}

			case TutorialStep.RampsAndInverted: {
				// Target: Drive onto a ramp or inverted track (steep angle > 28° or upside down)
				Vector3 up = CustomGravity.GetUpAxis(car.transform.position);
				float worldUpDot = Vector3.Dot(up, Vector3.up);
				bool isSteepOrInverted = car.GroundAngle > 28f || worldUpDot < 0.3f;

				if (isSteepOrInverted && car.IsGrounded && kmh > 5f) {
					_invertedTimer += Time.deltaTime;
					_stepProgress = Mathf.Clamp01(_invertedTimer / 1.5f);
					if (_invertedTimer >= 1.5f) {
						CompleteStep();
					}
				} else {
					_invertedTimer = Mathf.MoveTowards(_invertedTimer, 0f, Time.deltaTime * 0.5f);
					_stepProgress = Mathf.Clamp01(_invertedTimer / 1.5f);
				}
				break;
			}

			case TutorialStep.VariableJump: {
				// Target: Jump and sustain in air for >= 0.45s
				if (!car.IsGrounded) {
					_airtimeTimer += Time.deltaTime;
					_stepProgress = Mathf.Clamp01(_airtimeTimer / 0.45f);
					if (_airtimeTimer >= 0.45f) {
						_jumpCount++;
						CompleteStep();
					}
				} else {
					_airtimeTimer = 0f;
				}
				break;
			}

			case TutorialStep.DriftAndMiniTurbo: {
				// Target: Charge mini-turbo and fire burst
				if (car.MiniTurboReady) {
					_miniTurboCharged = true;
				}
				if (!_miniTurboCharged) {
					_stepProgress = car.MiniTurboChargeRatio * 0.7f;
				} else {
					_stepProgress = 0.7f + 0.3f;
					// When exiting drift with mini turbo charged, MovingCar fires the boost!
					if (!car.IsDrifting && kmh > 15f) {
						_miniTurboCount++;
						CompleteStep();
					}
				}
				break;
			}

			case TutorialStep.CourseCheckpoints: {
				// Target: Pass 3 checkpoints in race path, or drive 120m
				if (raceRuntime != null && car != null) {
					foreach (var r in raceRuntime.Racers) {
						if (r != null && r.Car == car) {
							_checkpointsPassedInStep = r.CheckpointsCrossed - _checkpointsAtStart;
							break;
						}
					}
				}

				float deltaDist = Vector3.Distance(car.transform.position, _lastPosition);
				if (deltaDist < 5f) _accumulatedDistance += deltaDist;
				_lastPosition = car.transform.position;

				float cpProgress = Mathf.Clamp01(_checkpointsPassedInStep / 3f);
				float distProgress = Mathf.Clamp01(_accumulatedDistance / 120f);
				_stepProgress = Mathf.Max(cpProgress, distProgress);

				if (_checkpointsPassedInStep >= 3 || _accumulatedDistance >= 120f) {
					CompleteStep();
				}
				break;
			}

			case TutorialStep.Completed:
				break;
		}
	}

	// ── OnGUI HUD Overlay ─────────────────────────────────────────────

	GUIStyle _cardStyle;
	GUIStyle _titleStyle;
	GUIStyle _bodyStyle;
	GUIStyle _badgeStyle;
	GUIStyle _statusStyle;
	GUIStyle _btnStyle;

	void EnsureStyles () {
		if (_cardStyle != null) return;

		_cardStyle = new GUIStyle(GUI.skin.box);
		_titleStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 14,
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleCenter
		};
		_titleStyle.normal.textColor = new Color(0.35f, 0.9f, 0.78f);

		_bodyStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 13,
			alignment = TextAnchor.MiddleCenter,
			wordWrap = true
		};
		_bodyStyle.normal.textColor = Color.white;

		_badgeStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 12,
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleCenter
		};
		_badgeStyle.normal.textColor = new Color(1f, 0.85f, 0.25f);

		_statusStyle = new GUIStyle(GUI.skin.label) {
			fontSize = 13,
			fontStyle = FontStyle.Bold,
			alignment = TextAnchor.MiddleCenter
		};
		_statusStyle.normal.textColor = new Color(0.4f, 1f, 0.5f);

		_btnStyle = new GUIStyle(GUI.skin.button) {
			fontSize = 11,
			fontStyle = FontStyle.Bold
		};
	}

	void OnGUI () {
		if (!showOverlay || currentStep == TutorialStep.NotStarted) return;
		EnsureStyles();

		Matrix4x4 prevMatrix = GUI.matrix;
		Color prevColor = GUI.color;

		float scale = Mathf.Min(1.3f, Mathf.Min(Screen.width / 960f, Screen.height / 700f));
		GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);

		try {
			if (currentStep == TutorialStep.Completed) {
				DrawVictoryPanel();
			} else {
				DrawStepPanel();
			}
		} finally {
			GUI.matrix = prevMatrix;
			GUI.color = prevColor;
		}
	}

	void DrawStepPanel () {
		float w = 520f;
		float h = 130f;
		float x = ((Screen.width / Mathf.Min(1.3f, Mathf.Min(Screen.width / 960f, Screen.height / 700f))) - w) * 0.5f;
		float y = 20f;

		// Card Background
		FillRect(new Rect(x, y, w, h), new Color(0.04f, 0.07f, 0.11f, 0.94f));
		FillRect(new Rect(x, y, w, 3), new Color(0.35f, 0.9f, 0.78f)); // Cyan accent bar

		// Header / Step Counter
		int stepIndex = (int)currentStep;
		const int totalSteps = 6;
		string header = $"TRAINING SIMULATOR  •  STEP {stepIndex} OF {totalSteps}";
		GUI.Label(new Rect(x + 10, y + 8, w - 20, 20), header, _titleStyle);

		// Objective Description & Dynamic Button Prompts
		string objective = GetStepDescription();
		GUI.Label(new Rect(x + 20, y + 32, w - 40, 42), objective, _bodyStyle);

		// Progress Bar
		float barW = w - 160f;
		float barH = 10f;
		float barX = x + 30f;
		float barY = y + 82f;
		FillRect(new Rect(barX, barY, barW, barH), new Color(0.12f, 0.16f, 0.22f));
		Color fillCol = _stepCompleted ? new Color(0.35f, 1f, 0.5f) : new Color(0.35f, 0.9f, 0.78f);
		FillRect(new Rect(barX, barY, barW * _stepProgress, barH), fillCol);

		// Percentage / Status
		string progressText = _stepCompleted ? "✓ COMPLETED!" : $"{(_stepProgress * 100f):F0}%";
		GUI.Label(new Rect(barX + barW + 12, barY - 4, 80, 20), progressText, _stepCompleted ? _statusStyle : _badgeStyle);

		// Skip button
		string skipLabel = Gamepad.current != null ? "Skip [Back]" : "Skip [Tab]";
		if (GUI.Button(new Rect(x + w - 95, y + 100, 85, 22), skipLabel, _btnStyle)) {
			SkipStep();
		}
		if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) {
			SkipStep();
		}
		if (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame) {
			SkipStep();
		}
	}

	void DrawVictoryPanel () {
		float w = 540f;
		float h = 260f;
		float x = ((Screen.width / Mathf.Min(1.3f, Mathf.Min(Screen.width / 960f, Screen.height / 700f))) - w) * 0.5f;
		float y = 50f;

		FillRect(new Rect(x, y, w, h), new Color(0.04f, 0.07f, 0.11f, 0.96f));
		FillRect(new Rect(x, y, w, 4), new Color(1f, 0.8f, 0.2f)); // Gold accent

		GUI.Label(new Rect(x + 10, y + 16, w - 20, 26), "★ TRAINING COMPLETE ★", _titleStyle);
		GUI.Label(new Rect(x + 20, y + 46, w - 40, 24), "You have mastered the non-Euclidean skies!", _bodyStyle);

		// Stats
		float statsY = y + 80f;
		DrawStatRow(x + 50, statsY + 0,  "Total Training Time", $"{_tutorialTotalTime:F1}s");
		DrawStatRow(x + 50, statsY + 26, "Top Speed Achieved",  $"{_topSpeedKmh:F1} km/h");
		DrawStatRow(x + 50, statsY + 52, "Total Time Drifting", $"{_totalDriftTime:F1}s");
		DrawStatRow(x + 50, statsY + 78, "Mini-Turbos Fired",   $"{_miniTurboCount}");

		// Action Buttons
		string restartLabel = Gamepad.current != null ? "Restart [Y]" : "Restart Tutorial";
		string freeDriveLabel = Gamepad.current != null ? "Free Drive [A]" : "Free Drive / Race";

		if (GUI.Button(new Rect(x + 60, y + 210, 180, 34), restartLabel, _btnStyle)) {
			StartTutorial();
		}
		if (GUI.Button(new Rect(x + 280, y + 210, 180, 34), freeDriveLabel, _btnStyle)) {
			StopTutorial();
			FindAnyObjectByType<OrbitCamera>()?.SetCinematicMode(false);
		}

		if (Gamepad.current != null) {
			if (Gamepad.current.buttonSouth.wasPressedThisFrame) {
				StopTutorial();
				FindAnyObjectByType<OrbitCamera>()?.SetCinematicMode(false);
			} else if (Gamepad.current.buttonNorth.wasPressedThisFrame) {
				StartTutorial();
			}
		}
	}

	void DrawStatRow (float rowX, float rowY, string label, string val) {
		GUI.Label(new Rect(rowX, rowY, 240, 22), label, _bodyStyle);
		GUI.Label(new Rect(rowX + 260, rowY, 160, 22), val, _badgeStyle);
	}

	string GetStepDescription () {
		string gasKey   = InputDeviceManager.Prompt(GameAction.Throttle);
		string brakeKey = InputDeviceManager.Prompt(GameAction.Brake);
		string jumpKey  = InputDeviceManager.Prompt(GameAction.Jump);
		string driftKey = InputDeviceManager.Prompt(GameAction.Drift);

		switch (currentStep) {
			case TutorialStep.Accelerate:
				return $"Press [{gasKey}] to Accelerate forward and reach 35 km/h.";
			case TutorialStep.Brake:
				return $"Press [{brakeKey}] to Brake and bring your car to a complete stop.";
			case TutorialStep.PlanetaryGravity:
				return $"Drive forward along the curved planet. Gravity automatically aligns your vehicle!";
			case TutorialStep.RampsAndInverted:
				return $"Climb up the steep ramp onto the inverted ceiling track. Curvature adhesion keeps you pinned!";
			case TutorialStep.VariableJump:
				return $"Tap [{jumpKey}] for a short hop, or HOLD [{jumpKey}] for maximum airtime!";
			case TutorialStep.DriftAndMiniTurbo:
				return $"Hold [{driftKey}] while turning to Drift! Counter-steer to charge your Mini-Turbo, then release!";
			case TutorialStep.CourseCheckpoints:
				return $"Follow the track and pass through 3 checkpoint gates to complete your training lap.";
			default:
				return "";
		}
	}

	static void FillRect (Rect rect, Color color) {
		Color prev = GUI.color;
		GUI.color = color;
		GUI.DrawTexture(rect, Texture2D.whiteTexture);
		GUI.color = prev;
	}
}
