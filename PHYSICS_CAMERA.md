# Physics and camera

The vehicle remains an arcade controller: custom gravity, surface grip, drift charging,
air steering and boost pads retain their existing tuning. Physics notes below are the
current driving-model contract; camera notes describe the follow-camera rewrite.

## Vehicle and gravity

- Lateral grip preserves its 50 Hz tuning at other physics tick rates. High surface
  friction cannot reverse lateral velocity, and zero grip actually behaves like ice.
- Braking respects analog input; coasting cannot overshoot zero velocity in a tick.
- Engine speed is recomputed after steering, and ground snapping updates the cached
  velocity used by subsequent forces. Drift speed limiting uses the ground plane.
- Ground and landing probes ignore triggers. The car uses speculative continuous
  collision detection, including when it becomes a kinematic network replica.
- Zero-gravity flight retains the last valid vehicle up-axis.
- Adaptive box room gravity points toward the correct nearest wall. Single-face road
  gravity respects its rectangular footprint (the same unscaled dimensions used by
  the existing box calculations).
- Gravity fallback selection respects priority before strength; negative-strength
  sources keep the correct force direction during blending.
- Boost pads only drive the locally controlled car. Their prediction no longer adds
  world gravity in regions where the driving controller experiences zero gravity.

## Camera architecture

Gameplay follow, idle/cinematic orbit, and the unused showcase camera share one pose
path. Do not stack `OrbitCamera` and `CinematicOrbitCamera` on the same GameObject.

```
LateUpdate (after interpolated rigidbodies)
  CameraFollowTarget        local owner, else unspawned Solo Practice car
  CameraGravityAlignment    CustomGravity up at the lagged pivot
  CameraFocusTracker        lag radius, 1.5 m cap, snap if a wall splits the lag
  yaw / pitch / cinematic   driving-only or showcase-only intent
  FollowCameraPose          smooth rotation, then obstruction boom + cutaway
```

| Type | Role |
| --- | --- |
| `OrbitCamera` | Driving camera in SampleScene / Main Camera. Speed pitch, auto-yaw, idle and `SetCinematicMode` used by `RaceRuntime`, `TutorialManager`, and `DevPlaytestManager`. |
| `CinematicOrbitCamera` | Showcase / photo-mode only (FOV, zoom, pause). Same boom and lag rules. Gameplay cinematics stay on `OrbitCamera`. |
| `FollowCameraPose` | Single apply step: rotation first, then sphere-cast boom, then `CameraOcclusionFade`. |
| `CameraObstructionSolver` | Near-plane sphere sweep. Ignores triggers and the followed car. Contracts immediately, recovers with **Collision Recovery Speed**. Crowded hits fall back to `SphereCastAll`. |
| `CameraOcclusionFade` | Per-camera toon cutaway globals. Added at runtime by `FollowCameraPose.Bind`. |

### Wiring

- `MovingCar` / `LocalPlayerCanvasBinder` call `RaceRuntime.SetPlayerCar`, which calls `OrbitCamera.SetFocus`.
- If the inspector focus is empty, `OrbitCamera` auto-finds `MovingCar.HasLocalControl`, then an unspawned offline car. Networked replicas are never followed.
- Pose runs in `LateUpdate` so it sees interpolated `Rigidbody` poses. Look centering and manual yaw use unscaled time; cinematic orbit and boom recovery use scaled time.

### Distance (both cameras)

- **Collision Recovery Speed**: default 4; higher values restore distance faster.
- **Minimum Comfort Distance**: default 3 metres. Permitted through supported toon
  obstacles only; unsupported obstacles retain hard camera collision.
- **Reveal Through Toon Objects**: enables the cutaway. Disable for collision-only behavior.

Tracking lag is capped at 1.5 metres and resets when a wall separates the pivot from
the car. Idle orbiting on `OrbitCamera` stops while the boom is obstructed. Pitch
limits are applied after pitch updates. Target assignment respects arbitrary gravity.

`CameraOcclusionFade` **Reveal Radius** (default 1.4 metres) and **Edge Softness**
(default 0.35) control the opening. Add the component in Edit mode to serialize
different values.

Cinematic toggle is **C** / **D-Pad Up** (`GameAction.CinematicCamera`). Gamepad
Select is not bound here so it does not fight respawn or the playtest menu.

## Toon cutaway

`Assets/Art/Toon/Shaders/CameraCutaway.hlsl` adds a dithered opening only along the
camera-to-player line. Existing material textures, toon lighting and outlines remain.
Physics colliders and shadow casting are unchanged. Terrain behind the player remains
visible; the player's own renderers are exempt. Shader globals are set and cleared per
camera so the minimap and other views do not inherit the opening.

Integrated into the existing **toon**, **PlanetSurface**, **GrassToon**, and **leavesShader**
shaders, including toon outlines and PlanetSurface's depth pass. There is no material
replacement or conversion to transparent blending. Other shaders need the same include
and clipping call in their visible/depth passes to support this effect. Water, clouds,
atmosphere and unrelated third-party shaders are not modified.

Comfort-distance collision relief requires a renderer on the collider's GameObject
and compatible materials in every slot. Unsupported geometry still uses hard collision.

## Verification

Run **Tools > Rolling Skys > Check Physics and Camera** in Edit mode for 22 regression
checks (grip, braking, gravity, collision filtering, focus lag, crowded casts and
shader compilation). The checks create and remove temporary objects in an additive scene.

`PhysicsCameraChecks.RenderCutawayCheck()` renders an obstructed toon target with and
without the opening and verifies that the target becomes visible and camera globals
are restored. Preview images are written to `Temp/CameraChecks`.

Play **Solo Practice** or **SampleScene** and confirm the follow camera locks onto the
local car, contracts at walls, and restores distance after the obstacle. Toggle
cinematic with C / D-Pad Up; countdown and finish still call `SetCinematicMode`.
