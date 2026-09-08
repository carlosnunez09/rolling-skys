using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum InputDeviceType {
	KeyboardMouse,
	XboxController,
	PlayStationController,
	GenericGamepad
}

public enum GameAction {
	Throttle,
	Brake,
	Steer,
	Jump,
	Drift,
	Respawn,
	CinematicCamera,
	PlaytestMenu
}

/// <summary>
/// Detects active input hardware (Keyboard/Mouse, Xbox, PlayStation, Generic Gamepad)
/// and dynamically provides contextual button prompt strings and glyph labels for HUD,
/// tutorials, and dev overlays.
/// </summary>
public class InputDeviceManager : MonoBehaviour {

	public static InputDeviceManager Instance { get; private set; }

	public static event Action<InputDeviceType> OnDeviceChanged;

	[SerializeField] InputDeviceType currentDevice = InputDeviceType.KeyboardMouse;

	public InputDeviceType CurrentDevice => currentDevice;
	public bool IsGamepad => currentDevice != InputDeviceType.KeyboardMouse;

	void Awake () {
		if (Instance != null && Instance != this) {
			Destroy(gameObject);
			return;
		}
		Instance = this;
		DontDestroyOnLoad(gameObject);
		DetectInitialDevice();
	}

	void Update () {
		DetectActiveInput();
	}

	void DetectInitialDevice () {
		if (Gamepad.current != null) {
			currentDevice = ClassifyGamepad(Gamepad.current);
		} else {
			currentDevice = InputDeviceType.KeyboardMouse;
		}
	}

	void DetectActiveInput () {
		// Detect keyboard or mouse activity
		if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) {
			SetDevice(InputDeviceType.KeyboardMouse);
			return;
		}
		if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame ||
		                             Mouse.current.rightButton.wasPressedThisFrame ||
		                             Mouse.current.delta.ReadValue().sqrMagnitude > 4f)) {
			SetDevice(InputDeviceType.KeyboardMouse);
			return;
		}

		// Detect gamepad activity
		Gamepad pad = Gamepad.current;
		if (pad != null) {
			if (pad.allControls.Count > 0) {
				foreach (var control in pad.allControls) {
					if (control is UnityEngine.InputSystem.Controls.ButtonControl button && button.wasPressedThisFrame) {
						SetDevice(ClassifyGamepad(pad));
						return;
					}
					if (control is UnityEngine.InputSystem.Controls.Vector2Control vec && vec.ReadValue().sqrMagnitude > 0.15f) {
						SetDevice(ClassifyGamepad(pad));
						return;
					}
					if (control is UnityEngine.InputSystem.Controls.AxisControl axis && Mathf.Abs(axis.ReadValue()) > 0.2f) {
						SetDevice(ClassifyGamepad(pad));
						return;
					}
				}
			}
		}
	}

	InputDeviceType ClassifyGamepad (Gamepad pad) {
		if (pad == null) return InputDeviceType.GenericGamepad;
		string layout = pad.layout.ToLowerInvariant();
		string name   = pad.displayName.ToLowerInvariant();

		if (layout.Contains("dualshock") || layout.Contains("dualsense") || name.Contains("playstation") || name.Contains("sony") || name.Contains("wireless controller")) {
			return InputDeviceType.PlayStationController;
		}
		if (layout.Contains("xinput") || layout.Contains("xbox") || name.Contains("xbox") || name.Contains("microsoft")) {
			return InputDeviceType.XboxController;
		}
		return InputDeviceType.GenericGamepad;
	}

	void SetDevice (InputDeviceType newDevice) {
		if (currentDevice != newDevice) {
			currentDevice = newDevice;
			OnDeviceChanged?.Invoke(newDevice);
		}
	}

	/// <summary>
	/// Returns the human-readable prompt string for the requested action based on the active controller.
	/// </summary>
	public string GetActionPrompt (GameAction action) {
		switch (action) {
			case GameAction.Throttle:
				switch (currentDevice) {
					case InputDeviceType.PlayStationController: return "R2";
					case InputDeviceType.XboxController:        return "RT";
					case InputDeviceType.GenericGamepad:        return "RT / R2";
					default:                                    return "W / Up";
				}

			case GameAction.Brake:
				switch (currentDevice) {
					case InputDeviceType.PlayStationController: return "L2";
					case InputDeviceType.XboxController:        return "LT";
					case InputDeviceType.GenericGamepad:        return "LT / L2";
					default:                                    return "S / Down";
				}

			case GameAction.Steer:
				switch (currentDevice) {
					case InputDeviceType.PlayStationController: return "Left Stick";
					case InputDeviceType.XboxController:        return "Left Stick";
					case InputDeviceType.GenericGamepad:        return "Left Stick";
					default:                                    return "A / D";
				}

			case GameAction.Jump:
				switch (currentDevice) {
					case InputDeviceType.PlayStationController: return "Cross (X)";
					case InputDeviceType.XboxController:        return "A";
					case InputDeviceType.GenericGamepad:        return "A / Cross";
					default:                                    return "Space";
				}

			case GameAction.Drift:
				switch (currentDevice) {
					case InputDeviceType.PlayStationController: return "R1 / L1 / Circle";
					case InputDeviceType.XboxController:        return "RB / LB / B";
					case InputDeviceType.GenericGamepad:        return "RB / LB / B";
					default:                                    return "Left Shift";
				}

			case GameAction.Respawn:
				switch (currentDevice) {
					case InputDeviceType.PlayStationController: return "Triangle";
					case InputDeviceType.XboxController:        return "Y";
					case InputDeviceType.GenericGamepad:        return "Y / Triangle";
					default:                                    return "R";
				}

			case GameAction.CinematicCamera:
				switch (currentDevice) {
					case InputDeviceType.PlayStationController: return "D-Pad Up";
					case InputDeviceType.XboxController:        return "D-Pad Up";
					case InputDeviceType.GenericGamepad:        return "D-Pad Up";
					default:                                    return "C";
				}

			case GameAction.PlaytestMenu:
				switch (currentDevice) {
					case InputDeviceType.PlayStationController: return "Options / Share";
					case InputDeviceType.XboxController:        return "Menu / View";
					case InputDeviceType.GenericGamepad:        return "Start / Select";
					default:                                    return "F1";
				}

			default:
				return "";
		}
	}

	/// <summary>
	/// Helper function to retrieve a prompt from anywhere without an explicit manager reference.
	/// </summary>
	public static string Prompt (GameAction action) {
		if (Instance != null)
			return Instance.GetActionPrompt(action);

		// Fallback detection
		bool hasPad = Gamepad.current != null;
		switch (action) {
			case GameAction.Throttle:        return hasPad ? "RT" : "W";
			case GameAction.Brake:           return hasPad ? "LT" : "S";
			case GameAction.Steer:           return hasPad ? "Left Stick" : "A/D";
			case GameAction.Jump:            return hasPad ? "A" : "Space";
			case GameAction.Drift:           return hasPad ? "RB / Shift" : "Shift";
			case GameAction.Respawn:         return hasPad ? "Y" : "R";
			case GameAction.CinematicCamera: return hasPad ? "D-Pad Up" : "C";
			case GameAction.PlaytestMenu:    return hasPad ? "Start" : "F1";
			default:                         return "";
		}
	}
}
