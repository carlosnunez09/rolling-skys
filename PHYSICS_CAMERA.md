# Physics and camera changes

The vehicle remains an arcade controller: custom gravity, surface grip, drift charging,
air steering and boost pads retain their existing tuning. This pass corrects stability
and collision problems rather than replacing the driving model.

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

## Camera settings

Both orbit cameras use a shared sphere sweep that encloses the near-plane corners.
It ignores triggers and the followed car, contracts immediately at obstacles, and
returns outward gradually. Crowded casts fall back to a complete query.

On `OrbitCamera`, under **Distance**:

- **Collision Recovery Speed**: default 4; higher values restore distance faster.
- **Minimum Comfort Distance**: default 3 metres. This is permitted through supported
  toon obstacles only; unsupported obstacles retain hard camera collision.
- **Reveal Through Toon Objects**: enables the cutaway. Disable for collision-only behavior.

The camera's tracking lag is capped at 1.5 metres and resets when a wall separates the
tracking pivot from the car. Idle orbiting stops while obstructed. Pitch limits are
applied after pitch updates, and target assignment respects arbitrary gravity.

`CameraOcclusionFade` is added at runtime. Its **Reveal Radius** (default 1.4 metres)
and **Edge Softness** (default 0.35) control the opening. Add it to the camera in Edit
mode if you want to serialize different values.

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

Run **Tools > Rolling Skys > Check Physics and Camera** in Edit mode for 20 regression
checks (grip, braking, gravity, collision filtering, crowded casts and shader compilation).
The checks create and remove temporary objects in an additive scene.

`PhysicsCameraChecks.RenderCutawayCheck()` renders an obstructed toon target with and
without the opening and verifies that the target becomes visible and camera globals
are restored. Preview images are written to `Temp/CameraChecks`.

A six-second live scene smoke check exercised acceleration, turning, braking and
coasting: peak speed 21.30 m/s, displacement 45.60 m, finite velocity throughout.
No full race, multiplayer session, or exhaustive track traversal was completed.
Driving feel on individual tracks still needs hands-on tuning.
