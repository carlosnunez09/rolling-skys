# Rolling Skys — Development Issues & Implementation Roadmap

This document outlines the three core initiatives requested for Rolling Skys: full controller support, an interactive tutorial/mechanics demo, and an internal playtesting dev suite.

---

## Issue #3: Input Buffering for Jump and Actions

- **GitHub Issue**: [#3](https://github.com/carlosnunez09/rolling-skys/issues/3)
- **Status**: Completed (Closed)
- **Priority**: High (P1)
- **Labels**: `input`, `gameplay`
- **Resolution**: Implemented in `MovingCar.cs` (`jumpBufferDuration = 0.18f`, `_jumpBufferTimer`)

### 1. Problem Statement
When approaching the ground from the air, pressing the jump button slightly before touching down would previously be lost or ignored because the vehicle was not yet grounded on that exact frame. This caused unresponsive or missed jump inputs when landing.

### 2. Requirements & Scope
1. **Jump Input Buffer Window**:
   - Cache jump input intent over a configurable buffer window (`0.18s`).
   - Process cached jump upon ground contact within the buffer duration.
2. **Variable Jump Sustain**:
   - Allow tapping for hop and holding for sustained ascent.

---

## Issue #4: Comprehensive Controller & Gamepad Support with Haptics

- **GitHub Issue**: [#4](https://github.com/carlosnunez09/rolling-skys/issues/4)
- **Status**: Completed (Closed)
- **Priority**: Critical (P0)
- **Labels**: `input`, `controller`, `polish`, `gameplay`
- **Resolution**: Implemented in commit `005e672` (`InputDeviceManager.cs`, `CarHaptics.cs`, `MovingCar.cs`)

### 1. Problem Statement
The current vehicle input bindings rely on a single 2D vector for movement where pushing forward on the left stick drives forward and pulling back brakes. This produces an awkward, unintuitive experience on gamepads. Modern racing players expect analog triggers (Right Trigger for throttle, Left Trigger for brake/reverse), stick steering with deadzone calibration, shoulder/face button drifting, jumping, and tactile haptic vibration feedback. In addition, players need a quick respawn/reset button if the vehicle tumbles into space or gets flipped.

### 2. Requirements & Scope
1. **Standard Racing Controller Layout**:
   - **Throttle (Accelerate)**: `<Gamepad>/rightTrigger` (analog 0–1) + `<Keyboard>/w`.
   - **Brake / Reverse**: `<Gamepad>/leftTrigger` (analog 0–1) + `<Keyboard>/s`.
   - **Steering**: `<Gamepad>/leftStick/x` + `<Gamepad>/dpad/left` & `right` + `<Keyboard>/a` & `d`.
   - **Jump / Hop**: `<Gamepad>/buttonSouth` ('A' / Cross) + `<Keyboard>/space`.
   - **Drift / Powerslide**: `<Gamepad>/rightShoulder` (RB), `<Gamepad>/leftShoulder` (LB), `<Gamepad>/buttonEast` ('B' / Circle), `<Gamepad>/buttonWest` ('X' / Square) + `<Keyboard>/leftShift`.
   - **Quick Respawn / Reset**: `<Gamepad>/buttonNorth` ('Y' / Triangle), `<Gamepad>/select` + `<Keyboard>/r`.
   - **Camera Orbit / Look**: `<Gamepad>/rightStick` + Arrow keys.
   - **Cinematic Camera Toggle**: `<Gamepad>/dpad/up`, `<Gamepad>/select` + `<Keyboard>/c`.
2. **Dual-Motor Haptic Feedback (Controller Rumble)**:
   - Drift slip vibration proportional to lateral speed and drift duration.
   - Mini-turbo ready pulse and mini-turbo release burst.
   - Variable jump launch impulse.
   - Ground impact / hard landing shock proportional to vertical landing velocity.
   - Surface hazard vibration (e.g. lava/damage tiles).
3. **Input Device Detection & Dynamic UI Prompts**:
   - Automatically detect active controller (Xbox, PlayStation, Generic, Keyboard/Mouse).
   - Provide glyph/text helpers ("RT" vs "W", "A" vs "Space", etc.) for UI and tutorial prompts.
4. **Respawn & Orientation Recovery**:
   - Automatically uprights the car, aligns to current surface gravity (`CustomGravity.GetUpAxis`), resets velocities, and places the car safely on track.

---

## Issue #5: Interactive Tutorial & Mechanics Demonstration

- **GitHub Issue**: [#5](https://github.com/carlosnunez09/rolling-skys/issues/5)
- **Status**: Completed (Closed)
- **Priority**: High (P1)
- **Labels**: `tutorial`, `demo`, `ui`, `onboarding`
- **Resolution**: Implemented in commit `005e672` (`TutorialManager.cs`)

### 1. Problem Statement
Rolling Skys features advanced, non-Euclidean physics: spherical planetary gravity, inverted ceiling driving, 70° ramps, variable jump sustains, and counter-steer mini-turbo drifting. New and internal testers need an intuitive, guided walkthrough that showcases how each mechanic functions before entering competitive multiplayer races.

### 2. Requirements & Scope
1. **Modular Step-by-Step Tutorial Sequence**:
   - **Step 1 — Basic Acceleration & Braking**: Accelerate to target speed using RT / W, then brake to stop.
   - **Step 2 — Planetary Gravity & Curvature**: Drive along curved spherical planet surface; understand gravity auto-alignment.
   - **Step 3 — Ramps & Inverted Slabs**: Climb a 45°+ ramp onto an upside-down ceiling track, observing seamless gravity handoff.
   - **Step 4 — Variable Jump Heights**: Tap Jump for short hop; hold Jump to clear wide crevasses and gaps.
   - **Step 5 — Power Drift & Mini-Turbo**: Initiate drift into a curve, counter-steer to tighten arc, charge blue mini-turbo sparks, and release for speed boost.
   - **Step 6 — Checkpoint Gates & Race Completion**: Pass through sequential holographic gates to finish the tutorial lap.
2. **HUD Tutorial Overlay**:
   - Clean banner displaying the current objective, dynamic button prompts matching the active device, and animated checkmarks on completion.
3. **Tutorial Completion Fanfare**:
   - Victory celebration with cinematic orbit camera sweep and statistics recap.

---

## Issue #6: Internal Playtest Demo Suite & Telemetry

- **GitHub Issue**: [#6](https://github.com/carlosnunez09/rolling-skys/issues/6)
- **Status**: Completed (Closed)
- **Priority**: High (P1)
- **Labels**: `playtest`, `dev-tools`, `analytics`, `qa`
- **Resolution**: Implemented in commit `005e672` (`DevPlaytestManager.cs`, `CustomGravity.cs`)

### 1. Problem Statement
Internal playtesters need quick feedback loops: resetting to specific track zones without restarting the game, monitoring performance and physics telemetry, testing physics edge cases (god mode, infinite boost, low gravity), and logging playtest data for tuning handling curves.

### 2. Requirements & Scope
1. **In-Game Playtest Overlay & Quick Menu**:
   - Toggle with `F1` or `Gamepad Start / Back / Select`.
   - **Quick Actions**:
     - Respawn to Last Checkpoint.
     - Teleport to Waypoint (1 through N).
     - Restart Lap / Run.
   - **Tuning & Physics Overrides**:
     - God Mode (Immune to hazards / fallouts).
     - Infinite Mini-Turbo / Boost.
     - Gravity Scale Modifier (Earth 1.0x, Moon 0.3x, Heavy 2.0x).
     - Game Speed / Slomo Slider (0.25x to 2.0x).
2. **Session Telemetry & Analytics**:
   - Live display of: Lap Times, Top Speed, Average Speed, Time Drifting, Mini-Turbos Triggered, Airtime, and Impact/Collision count.
   - Playtest summary popup on lap end with option to copy/export session metrics.
3. **Dedicated Dev Demo Playground Integration**:
   - Connects seamlessly with `SampleScene.unity` and standalone test scenes for fast iterative testing.
