using UnityEngine;

/// <summary>
/// One paintable surface layer for the planet.
///
/// Each layer occupies one vertex-color channel:
///   Layer 0 → vertex R
///   Layer 1 → vertex G
///   Layer 2 → vertex B
///   Layer 3 → vertex A
///
/// The shader blends layers by their channel weight, using triplanar
/// world-space projection so sphere UV seams are never an issue.
///
/// Game-play properties (friction, damage, etc.) live here so you can
/// read them at runtime from PlanetSurface.GetLayerAtPoint().
/// </summary>
[CreateAssetMenu(
    fileName = "New PlanetLayer",
    menuName  = "Planet Painting / Planet Layer Data",
    order     = 0)]
public class PlanetLayerData : ScriptableObject
{
    // ── Visual ─────────────────────────────────────────────────────────────────

    [Header("Identification")]
    [Tooltip("Human-readable label shown in the painter UI.")]
    public string layerName = "Layer";

    [Tooltip("Swatch colour shown in the painter tool buttons.")]
    public Color  editorColor = Color.white;

    // ── Textures ───────────────────────────────────────────────────────────────

    [Header("Textures")]
    [Tooltip("Albedo (RGB) + optional smoothness in A.")]
    public Texture2D albedo;

    [Tooltip("Normal map (DXT5nm / BC5 packed). Leave null for flat normals.")]
    public Texture2D normalMap;

    [Tooltip("Mask map: R=Metallic, G=AO, B=Detail, A=Smoothness. " +
             "Leave null to use the scalar fallbacks below.")]
    public Texture2D maskMap;

    [Header("Brightness")]
    [Tooltip("Multiplies the albedo colour. 1 = original, 2 = double brightness, 0 = black.")]
    [Range(0f, 3f)]
    public float brightness = 1f;

    // ── Tiling / triplanar ─────────────────────────────────────────────────────

    [Header("Triplanar Tiling")]
    [Tooltip("World-space units per texture tile. " +
             "Larger value = fewer, bigger tiles.")]
    public float tiling = 4f;

    [Tooltip("Sharpness of the blend between the three triplanar projections. " +
             "1 = smooth, 8 = very sharp seams.")]
    [Range(1f, 8f)]
    public float triplanarBlend = 4f;

    // ── Surface properties (scalars used when maskMap is null) ────────────────

    [Header("Surface Properties (scalar fallbacks)")]
    [Tooltip("0 = non-metallic, 1 = fully metallic.")]
    [Range(0f, 1f)]
    public float metallic   = 0f;

    [Tooltip("Surface smoothness when no mask map is assigned.")]
    [Range(0f, 1f)]
    public float smoothness = 0.3f;

    [Tooltip("Ambient occlusion intensity (0 = no AO, 1 = full AO baked in).")]
    [Range(0f, 1f)]
    public float ambientOcclusion = 1f;

    // ── Gameplay ───────────────────────────────────────────────────────────────

    [Header("Gameplay Properties")]
    [Tooltip("Physics friction multiplier applied when the player stands on this layer. " +
             "Read at runtime via PlanetSurface.GetLayerAtPoint().")]
    public float frictionMultiplier = 1f;

    [Tooltip("Speed multiplier while traversing this surface (e.g. 0.5 for mud, 1.5 for ice).")]
    public float speedMultiplier    = 1f;

    [Tooltip("Damage per second dealt to the player while on this surface. 0 = safe.")]
    public float damagePerSecond    = 0f;

    [Tooltip("True if the player should be slowed / treated as liquid (swamp, lava, etc.).")]
    public bool  isHazard           = false;

    [Tooltip("Custom string tag you can use for any additional game-logic branching.")]
    public string tag               = "";
}
