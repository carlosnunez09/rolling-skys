using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Component that links a ProBuilder / any MeshFilter mesh to up to four
/// <see cref="PlanetLayerData"/> ScriptableObjects and drives the
/// <c>PlanetSurface</c> shader via per-vertex colours.
///
/// Attach this to your planet or any ProBuilder platform that you want to
/// paint with the Planet Painter tool.  One component = one paintable mesh.
///
/// HOW LAYERS MAP TO VERTEX COLOURS
/// ---------------------------------
///   Layer 0  →  vertex colour R
///   Layer 1  →  vertex colour G
///   Layer 2  →  vertex colour B
///   Layer 3  →  vertex colour A
///
/// The shader blends them using triplanar world-space projection, so
/// there are no UV seams even on spheres.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PlanetSurface : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────────────────
    // Public data
    // ──────────────────────────────────────────────────────────────────────────

    [Tooltip("Up to 4 layers.  The index maps to a vertex-colour channel " +
             "(0=R, 1=G, 2=B, 3=A).  Leave slots null to ignore them.")]
    public PlanetLayerData[] layers = new PlanetLayerData[4];

    [Tooltip("Material using the PlanetSurface shader.  " +
             "If null the first material on the MeshRenderer is used.")]
    public Material surfaceMaterial;

    // ──────────────────────────────────────────────────────────────────────────
    // Internal cache
    // ──────────────────────────────────────────────────────────────────────────

    // Cached references, rebuilt on Awake / when requested from the editor.
    MeshFilter   _filter;
    MeshRenderer _renderer;
    Mesh         _mesh;

    // Vertex-colour arrays that mirror the mesh.  The painter writes into
    // these and calls CommitColors() to flush them back to the GPU.
    Color[] _colors;

    // ──────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        Init();
        PushMaterialProperties();
    }

#if UNITY_EDITOR
    // In the editor we push properties whenever the inspector changes.
    void OnValidate() => EditorApplication.delayCall += () =>
    {
        if (this == null) return;     // destroyed while delayed
        Init();
        PushMaterialProperties();
    };
#endif

    // ──────────────────────────────────────────────────────────────────────────
    // Initialisation
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches or rebuilds the internal mesh + colour buffer references.
    /// Safe to call multiple times.
    /// </summary>
    public void Init()
    {
        _filter   = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();
        _mesh     = _filter.sharedMesh;

        if (_mesh == null) return;

        // Ensure we have a colour array matching the current vertex count.
        int vcount = _mesh.vertexCount;
        if (_colors == null || _colors.Length != vcount)
        {
            Color[] existing = _mesh.colors;
            if (existing != null && existing.Length == vcount)
                _colors = existing;
            else
            {
                // Default: full red channel = Layer 0 dominates everywhere.
                _colors = new Color[vcount];
                for (int i = 0; i < vcount; i++)
                    _colors[i] = new Color(1f, 0f, 0f, 0f);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Vertex colour API used by the painter
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current colour of the given vertex.
    /// </summary>
    public Color GetColor(int vertexIndex) => _colors[vertexIndex];

    /// <summary>
    /// Overwrites the colour of a single vertex in the working buffer.
    /// Call <see cref="CommitColors"/> after batching all vertex writes.
    /// </summary>
    public void SetColor(int vertexIndex, Color color)
    {
        if (_colors == null || vertexIndex >= _colors.Length) return;
        _colors[vertexIndex] = color;
    }

    /// <summary>
    /// Flushes the working colour buffer back to the mesh on the GPU.
    /// Also marks the mesh as dirty so Unity serialises the change.
    /// </summary>
    public void CommitColors()
    {
        if (_mesh == null || _colors == null) return;
        _mesh.colors = _colors;
        _mesh.UploadMeshData(false);

#if UNITY_EDITOR
        EditorUtility.SetDirty(_mesh);
#endif
    }

    /// <summary>
    /// Returns the raw vertex-colour array (read-only view — do not cache).
    /// </summary>
    public IReadOnlyList<Color> Colors => _colors;

    /// <summary>
    /// Exposes the shared mesh so the sculpt brush can modify vertices.
    /// </summary>
    public Mesh SharedMesh => _mesh;

    // ──────────────────────────────────────────────────────────────────────────
    // Shader material synchronisation
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes all <see cref="PlanetLayerData"/> properties into the
    /// <see cref="surfaceMaterial"/> (or the first MeshRenderer material).
    ///
    /// Called automatically on Awake/OnValidate, and from the painter when
    /// the layer list changes.
    ///
    /// Property naming convention in the shader:
    ///   _LayerN_Albedo, _LayerN_Normal, _LayerN_Mask,
    ///   _LayerN_Tiling, _LayerN_TriBlend,
    ///   _LayerN_Metallic, _LayerN_Smoothness, _LayerN_AO
    /// where N is 0..3.
    /// </summary>
    public void PushMaterialProperties()
    {
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();

        Material mat = surfaceMaterial;
        if (mat == null && _renderer != null && _renderer.sharedMaterials.Length > 0)
            mat = _renderer.sharedMaterials[0];
        if (mat == null) return;

        for (int i = 0; i < 4; i++)
        {
            PlanetLayerData layer = (i < layers.Length) ? layers[i] : null;

            // Texture slots (safe: assigning null clears the slot in URP).
            SetMatTex(mat, $"_Layer{i}Albedo",  layer?.albedo);
            SetMatTex(mat, $"_Layer{i}Normal",  layer?.normalMap);
            SetMatTex(mat, $"_Layer{i}Mask",    layer?.maskMap);

            // Scalars.
            mat.SetFloat($"_Layer{i}Tiling",     layer?.tiling            ?? 4f);
            mat.SetFloat($"_Layer{i}TriBlend",   layer?.triplanarBlend    ?? 4f);
            mat.SetFloat($"_Layer{i}Metallic",    layer?.metallic          ?? 0f);
            mat.SetFloat($"_Layer{i}Smoothness", layer?.smoothness        ?? 0.3f);
            mat.SetFloat($"_Layer{i}AO",         layer?.ambientOcclusion  ?? 1f);
            mat.SetFloat($"_Layer{i}Brightness", layer?.brightness        ?? 1f);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Gameplay query
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the dominant <see cref="PlanetLayerData"/> at a world-space
    /// point by finding the nearest mesh vertex and reading its colour weights.
    ///
    /// Usage (e.g. in a player controller):
    /// <code>
    ///   if (Physics.Raycast(ray, out hit))
    ///   {
    ///       var surf = hit.collider.GetComponent&lt;PlanetSurface&gt;();
    ///       PlanetLayerData layer = surf?.GetLayerAtPoint(hit.point);
    ///       float speed = layer != null ? layer.speedMultiplier : 1f;
    ///   }
    /// </code>
    /// </summary>
    /// <param name="worldPoint">World-space position of the query.</param>
    /// <returns>The layer whose vertex-colour channel is strongest, or null.</returns>
    public PlanetLayerData GetLayerAtPoint(Vector3 worldPoint)
    {
        if (_mesh == null || _colors == null) return null;

        // Find the nearest vertex to the world-space point.
        Vector3[] verts    = _mesh.vertices;
        int       bestIdx  = 0;
        float     bestDist = float.MaxValue;

        for (int i = 0; i < verts.Length; i++)
        {
            float d = Vector3.SqrMagnitude(transform.TransformPoint(verts[i]) - worldPoint);
            if (d < bestDist) { bestDist = d; bestIdx = i; }
        }

        Color c = _colors[bestIdx];
        float[] weights = { c.r, c.g, c.b, c.a };

        int   dominantChannel = 0;
        float dominantWeight  = weights[0];
        for (int i = 1; i < 4; i++)
            if (weights[i] > dominantWeight) { dominantWeight = weights[i]; dominantChannel = i; }

        if (dominantChannel < layers.Length)
            return layers[dominantChannel];

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    static void SetMatTex(Material mat, string prop, Texture2D tex)
    {
        if (mat.HasProperty(prop))
            mat.SetTexture(prop, tex);
    }
}
