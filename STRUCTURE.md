# Project Structure — Rolling Skys

Unity 6 project. All game content lives under `Assets/`. Third-party assets are isolated under `Assets/ThirdParty/` and should not be edited.

---

## Assets/

```
Assets/
├── Art/                    Custom shaders, materials, textures, and prefabs
├── Prefabs/                Assembled game object prefabs
├── ScriptableObjects/      ScriptableObject data asset instances
├── Scripts/                All C# game code
├── Scenes/                 Unity scene files
├── Settings/               Unity project settings assets (URP, Input, etc.)
├── Terrain/                Terrain layers, brushes, and terrain materials
├── ThirdParty/             Unmodified third-party and purchased asset packs
├── blends/                 Blender source files (not imported by Unity)
├── Editor/                 Unity Editor-generated state (not committed)
├── StreamingAssets/        Runtime data — MCP Python server
├── TextMesh Pro/           Unity TMP package resources (auto-generated)
└── TutorialInfo/           Unity starter template readme (can be deleted)
```

---

## Art/

Each environment feature gets its own folder with a consistent internal layout:

```
Art/
├── Environment/
│   ├── Clouds/
│   │   ├── Materials/      VolumetricCloud.mat, wolldstd.mat
│   │   └── Shaders/        VolumetricCloud.shader
│   ├── Grass/
│   │   ├── GrassBlade.asset    GPU grass mesh asset
│   │   ├── Materials/      Custom_GrassToon.mat
│   │   ├── Prefabs/        Grass0, Grass1, grassc1, Weed1V2
│   │   ├── Shaders/        GrassToon.shader
│   │   └── Textures/       Billboard masks and alpha textures
│   ├── Trees/
│   │   ├── Materials/      leavemat.mat, tree material variants
│   │   ├── Prefabs/        Tree4_High.prefab
│   │   ├── Shaders/        leavesShader.shader
│   │   └── Textures/       BirchClump.png, LeafClump1.png
│   └── Water/              (placeholder — shaders/materials to be added)
│       ├── Materials/
│       └── Shaders/
├── Planet/
│   ├── BakedMeshes/        Sphere_Baked.asset (pre-baked mesh for planet surface)
│   ├── Materials/          worldstd.mat, skyblock.mat, PlanetSurface materials
│   └── Shaders/            PlanetAtmosphere.shader, PlanetSurface.shader
├── Toon/
│   ├── Materials/          red.mat, toontesting.mat
│   └── Shaders/            toon.shader
└── UI/
    └── Materials/          WaypointArrow.mat
```

**Convention:** every Art subfolder that has both a shader and materials keeps them in `Shaders/` and `Materials/` sub-subfolders. Textures used exclusively by one feature stay in `Textures/` alongside it.

---

## Scripts/

```
Scripts/
├── Camera/
│   └── OrbitCamera.cs          Third-person orbit camera for sphere/car
├── Gameplay/
│   ├── BoostPad.cs             Trigger that applies a speed impulse
│   ├── CarHUD.cs               HUD display for race state
│   ├── GrassTrampler.cs        Bends GPU grass on contact
│   ├── MovingCar.cs            Spherical-world car controller
│   ├── MovingSphere.cs         Spherical-world ball controller
│   ├── OrbitSpawner.cs         Spawns objects in orbit around a sphere
│   ├── RaceRuntime.cs          Race lap/checkpoint state machine
│   ├── RaceStartTrigger.cs     Starts a race on player entry
│   ├── SurfaceFriction.cs      Per-surface friction override
│   ├── Waypoint.cs             Single waypoint node
│   └── WaypointPath.cs         Ordered list of waypoints for race routes
├── Gravity/
│   ├── AdaptiveGravitySource.cs    Unified gravity source (replaces separate sphere/box/plane)
│   ├── CustomGravity.cs            Applies gravity from all active sources
│   ├── CustomGravityRigidbody.cs   Rigidbody that opts in to custom gravity
│   ├── GravityBox.cs               Box-shaped gravity volume
│   ├── GravityCar.cs               Car-specific gravity alignment
│   ├── GravityPlane.cs             Infinite plane gravity source
│   ├── GravitySource.cs            Base class / interface for gravity sources
│   └── GravitySphere.cs            Spherical planet gravity source
└── Planet/
    ├── PlanetLayerData.cs      ScriptableObject definition for a paint layer
    └── PlanetSurface.cs        Runtime planet surface paint system
```

---

## Prefabs/

```
Prefabs/
└── Environment/
    ├── Asteroid.prefab         Floating obstacle
    ├── CloudSphere.prefab      Cloud shell around a planet
    └── track1.prefab           Race track geometry
```

---

## ScriptableObjects/

```
ScriptableObjects/
├── NewTrackArea.asset          Track area data instance
└── Planet/
    ├── New PlanetLayer.asset       Planet surface layer definition
    └── New PlanetLayer 1.asset     Planet surface layer definition (variant)
```

---

## Terrain/

```
Terrain/
├── Brushes/        New Brush.brush — custom terrain painting brush
├── Layers/         main.terrainlayer, New Terrain Layer.terrainlayer
└── Materials/      accentgrid.mat, testworld_paintable.mat, red.mat
```

---

## ThirdParty/

Do not edit files in this folder. Update by replacing the entire pack.

```
ThirdParty/
├── catlikecodingsorce/         Catlike Coding "Complex Gravity" reference project
├── CompiledAssets/             Foliage and prop models + textures (FBX/PNG)
├── HandpaintedGrassTextures/   Handpainted grass & ground texture pack (Asset Store)
├── NaughtyAttributes/          Inspector attribute library
└── Redcode/                    Redcode Paths spline library
```

---

## Settings/

Unity-managed project settings assets: URP renderer, pipeline asset, input actions. Edit via Project Settings window, not directly.

---

## Naming Conventions

| Type | Convention | Example |
|---|---|---|
| Folders | PascalCase | `Environment`, `Gameplay` |
| Shaders | PascalCase | `GrassToon.shader` |
| Materials | descriptive lowercase | `leavemat.mat`, `worldstd.mat` |
| Scripts | PascalCase | `MovingCar.cs` |
| Prefabs | PascalCase | `Tree4_High.prefab` |
| Textures | descriptive with suffix | `GrassMask0.png` |

---

## Adding New Art

1. Create a subfolder under `Art/Environment/` (or `Art/Planet/`, etc.) matching the feature name.
2. Add `Shaders/`, `Materials/`, and `Textures/` subfolders as needed.
3. If the feature spawns prefabs, add a `Prefabs/` subfolder here (for tight feature coupling) or use the top-level `Prefabs/Environment/` (for shared/reusable objects).
