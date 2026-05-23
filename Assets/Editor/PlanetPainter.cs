// =============================================================================
// PlanetPainter.cs  —  Editor Window
// =============================================================================
// An all-in-one scene painter for round planets and ProBuilder platforms.
//
// TABS
// ────
//   Objects   — scatter prefabs on any surface (original behaviour)
//   Grass     — dense prefab scatter with tint, for foliage (original)
//   Surface   — paint vertex colours that drive the PlanetSurface shader
//               (up to 4 texture layers, with opacity + blending controls)
//   Sculpt    — push/pull vertices along their surface normal to add
//               hills, craters, or raised platforms
//
// QUICK-START
// ──────────
//   1. Open via  Tools ▶ Planet Painter.
//   2. Assign a Planet Transform (or any ProBuilder mesh Transform).
//   3. For Surface Paint: add a PlanetSurface component to that object and
//      fill its Layers array with PlanetLayerData ScriptableObjects.
//   4. Click "Activate Brush" and drag over the mesh in the Scene view.
//
// HOW SURFACE PAINTING WORKS
// ──────────────────────────
//   The brush finds all vertices within brush-radius and tweaks their
//   RGBA colour channels.  Each channel maps to one shader layer:
//     R=Layer 0, G=Layer 1, B=Layer 2, A=Layer 3.
//   After each stroke PlanetSurface.CommitColors() flushes the changes
//   back to the mesh so they are saved with the scene/prefab.
//
// HOW SCULPTING WORKS
// ───────────────────
//   Vertices inside the brush radius are displaced along the surface
//   normal (outward from planet centre, or the mesh vertex normal for
//   flat platforms) by a strength value, then normals are recalculated.
//   For ProBuilder meshes the component is refreshed so UV projections
//   stay consistent.  Undo is registered before every stroke.
// =============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class PlanetPainter : EditorWindow
{
    // ── Enums ──────────────────────────────────────────────────────────────────

    enum BrushMode  { ObjectPlacement, GrassEmbedder, SurfacePaint, Sculpt }
    enum PaintState { Idle, Painting, Erasing }
    enum PrefabUpAxis { PlusY, PlusZ, MinusZ, PlusX, MinusX, MinusY }

    static readonly string[] TabLabels =
        { "⬡  Objects", "🌿  Grass", "🎨  Surface", "⛰  Sculpt" };

    BrushMode _mode = BrushMode.ObjectPlacement;

    // ── Planet ─────────────────────────────────────────────────────────────────

    Transform _planet;
    Vector3   PlanetCenter => _planet != null ? _planet.position : Vector3.zero;

    // ── Shared brush ───────────────────────────────────────────────────────────

    float _brushRadius   = 3f;
    float _probeReach    = 600f;
    bool  _brushActive   = false;
    float _slopeLimit    = 60f;
    float _surfaceOffset = 0f;

    // ── Object Placement ───────────────────────────────────────────────────────

    List<GameObject> _objPrefabs   = new List<GameObject>();
    Transform        _objParent;
    float            _objDensity   = 3f;
    bool             _objAlignNorm = true;
    float            _objScaleMin  = 0.8f;
    float            _objScaleMax  = 1.2f;
    float            _objWidthMin  = 0.8f;
    float            _objWidthMax  = 1.2f;
    bool             _objRandYaw   = true;
    float            _objTiltJitter= 0f;
    PrefabUpAxis     _objUpAxis    = PrefabUpAxis.PlusY;
    int              _objLayer     = 0;

    // ── Grass Embedder ─────────────────────────────────────────────────────────

    List<GameObject> _grassPrefabs    = new List<GameObject>();
    Transform        _grassParent;
    float            _grassDensity    = 10f;
    bool             _grassAlignNorm  = true;
    float            _grassScaleMin   = 0.9f;
    float            _grassScaleMax   = 1.1f;
    float            _grassWidthMin   = 0.9f;
    float            _grassWidthMax   = 1.1f;
    float            _grassTiltJitter = 0f;
    PrefabUpAxis     _grassUpAxis     = PrefabUpAxis.PlusY;
    int              _grassLayer      = 0;
    bool             _tintEnabled     = false;
    Color            _tintA           = Color.white;
    Color            _tintB           = new Color(0.75f, 1f, 0.75f, 1f);
    float            _tintVariation   = 1f;
    string           _tintShaderProp  = "_BaseColor";

    // ── Surface Paint ──────────────────────────────────────────────────────────

    /// <summary>Which vertex-colour channel (0=R,1=G,2=B,3=A) is being painted.</summary>
    int   _paintLayer    = 0;

    /// <summary>How strongly the brush affects vertices per stroke pass. 0..1.</summary>
    float _paintOpacity  = 0.5f;

    /// <summary>
    /// Falloff power for the brush — 1=flat (all verts inside radius get full
    /// opacity), values >1 give a smooth gradient edge.
    /// </summary>
    float _paintFalloff  = 2f;

    /// <summary>
    /// When true, painting a channel subtracts from others so they always sum
    /// to 1 (splatmap style).  When false, channels accumulate freely.
    /// </summary>
    bool _paintNormalise = true;

    /// <summary>Cached PlanetSurface on the current target. Re-fetched when target changes.</summary>
    PlanetSurface _surface;

    // ── Sculpt ─────────────────────────────────────────────────────────────────

    /// <summary>Outward displacement per stroke (world units). Negative = push in.</summary>
    float _sculptStrength = 0.3f;

    /// <summary>Laplacian smoothing passes applied after each sculpt stroke (0 = none).</summary>
    int   _sculptSmooth   = 1;

    // ── Render Density ─────────────────────────────────────────────────────────

    enum RenderDensity { Full, Half, Quarter, Off }
    RenderDensity _objRenderDensity   = RenderDensity.Full;
    RenderDensity _grassRenderDensity = RenderDensity.Full;

    // ── State ──────────────────────────────────────────────────────────────────

    PaintState    _state        = PaintState.Idle;
    bool          _eraseMode    = false;
    List<Vector3> _strokePlaced = new List<Vector3>();
    Vector2       _scroll;

    // Foldout state
    bool _foldScale   = true;
    bool _foldFilters = true;
    bool _foldTint    = false;
    bool _foldRay     = false;

    // Mesh subdivision
    int  _subdivIterations = 1;
    bool _subdivReproject  = true;

    // Spacing helpers
    float ObjMinGap   => _brushRadius / Mathf.Max(_objDensity,   0.1f);
    float GrassMinGap => _brushRadius / Mathf.Max(_grassDensity, 0.1f);

    // ──────────────────────────────────────────────────────────────────────────
    // Window lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Planet Painter")]
    static void Open() => GetWindow<PlanetPainter>("Planet Painter");

    void OnEnable()  => SceneView.duringSceneGui += OnSceneGUI;
    void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    // ──────────────────────────────────────────────────────────────────────────
    // Inspector UI
    // ──────────────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawHeader();
        DrawPlanetTarget();
        DrawSeparator();

        var tabStyle = new GUIStyle(EditorStyles.toolbarButton)
            { fontSize = 12, fixedHeight = 30 };
        _mode = (BrushMode)GUILayout.Toolbar((int)_mode, TabLabels, tabStyle);
        EditorGUILayout.Space(6);

        switch (_mode)
        {
            case BrushMode.ObjectPlacement: DrawObjectPanel();  break;
            case BrushMode.GrassEmbedder:   DrawGrassPanel();   break;
            case BrushMode.SurfacePaint:    DrawSurfacePanel(); break;
            case BrushMode.Sculpt:          DrawSculptPanel();  break;
        }

        DrawSeparator();
        DrawBrushControls();
        DrawSeparator();
        DrawStats();
        DrawSeparator();
        DrawMeshTools();
        DrawSeparator();
        DrawDangerZone();

        EditorGUILayout.EndScrollView();
        Repaint();
    }

    // ── Header ─────────────────────────────────────────────────────────────────

    void DrawHeader()
    {
        var bg = new GUIStyle
            { normal = { background = MakeTex(1, 1, new Color(0.11f, 0.11f, 0.16f)) } };
        EditorGUILayout.BeginVertical(bg, GUILayout.Height(40));
        GUILayout.FlexibleSpace();
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 15,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = new Color(0.88f, 0.93f, 1f) }
        };
        GUILayout.Label("🌍  Planet Painter", style);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);
    }

    // ── Planet Target ──────────────────────────────────────────────────────────

    void DrawPlanetTarget()
    {
        DrawSectionHeader("Planet / Surface Target", new Color(0.3f, 0.55f, 0.9f));

        EditorGUI.BeginChangeCheck();
        _planet = (Transform)EditorGUILayout.ObjectField(
            "Target Transform", _planet, typeof(Transform), true);
        if (EditorGUI.EndChangeCheck())
            RefreshSurface();

        if (_planet == null)
        {
            EditorGUILayout.HelpBox(
                "Assign a planet or ProBuilder platform Transform to begin.",
                MessageType.Warning);
        }
        else
        {
            string extra = _surface != null ? "  [PlanetSurface ✓]" : "  [no PlanetSurface]";
            EditorGUILayout.HelpBox(
                $"{_planet.name}   center {PlanetCenter:F1}{extra}",
                MessageType.None);
        }
    }

    // ── Object Panel ───────────────────────────────────────────────────────────

    void DrawObjectPanel()
    {
        var accent = new Color(0.3f, 0.72f, 0.45f);

        DrawSectionHeader("Prefabs", accent);
        DrawPrefabList(_objPrefabs);

        EditorGUILayout.Space(3);
        DrawSectionHeader("Container", accent);
        _objParent = (Transform)EditorGUILayout.ObjectField(
            "Parent", _objParent, typeof(Transform), true);
        if (_objParent == null && GUILayout.Button("Create ObjectContainer"))
            _objParent = CreateContainer("ObjectContainer");

        EditorGUILayout.Space(3);
        DrawSectionHeader("Brush", accent);
        _brushRadius  = EditorGUILayout.Slider("Brush Radius",    _brushRadius,  0.1f, 2000f);
        _objDensity   = EditorGUILayout.Slider("Density",         _objDensity,   1f,   20f);
        _objAlignNorm = EditorGUILayout.Toggle("Align to Normal", _objAlignNorm);
        _objRandYaw   = EditorGUILayout.Toggle("Random Yaw",      _objRandYaw);

        _foldScale = EditorGUILayout.Foldout(_foldScale, "Scale Randomization", true, BoldFoldout());
        if (_foldScale)
        {
            EditorGUI.indentLevel++;
            _objScaleMin   = EditorGUILayout.Slider("Height Min",    _objScaleMin,    0.01f, 5f);
            _objScaleMax   = EditorGUILayout.Slider("Height Max",    _objScaleMax,    _objScaleMin, 5f);
            _objWidthMin   = EditorGUILayout.Slider("Width Min",     _objWidthMin,    0.01f, 5f);
            _objWidthMax   = EditorGUILayout.Slider("Width Max",     _objWidthMax,    _objWidthMin, 5f);
            _objTiltJitter = EditorGUILayout.Slider("Tilt Jitter°",  _objTiltJitter,  0f, 45f);
            _objUpAxis     = (PrefabUpAxis)EditorGUILayout.EnumPopup("Prefab Up Axis", _objUpAxis);
            EditorGUILayout.HelpBox(
                "Which local axis of your prefab points away from the surface.\n" +
                "+Y = Unity default.  +Z = Blender-exported, sideways trees.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        _foldFilters = EditorGUILayout.Foldout(_foldFilters, "Placement Filters", true, BoldFoldout());
        if (_foldFilters)
        {
            EditorGUI.indentLevel++;
            _slopeLimit    = EditorGUILayout.Slider("Max Slope °",    _slopeLimit,    0f,  90f);
            _surfaceOffset = EditorGUILayout.Slider("Surface Offset", _surfaceOffset, -1f, 2f);
            EditorGUI.indentLevel--;
        }

        _foldRay = EditorGUILayout.Foldout(_foldRay, "Raycast Settings", true, BoldFoldout());
        if (_foldRay)
        {
            EditorGUI.indentLevel++;
            _probeReach = EditorGUILayout.FloatField("Probe Reach", _probeReach);
            EditorGUILayout.HelpBox(
                "Must exceed your planet's radius from its centre.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(3);
        _objLayer = EditorGUILayout.LayerField("Object Layer", _objLayer);
    }

    // ── Grass Panel ────────────────────────────────────────────────────────────

    void DrawGrassPanel()
    {
        var accent = new Color(0.4f, 0.78f, 0.3f);

        DrawSectionHeader("Prefabs", accent);
        DrawPrefabList(_grassPrefabs);

        EditorGUILayout.Space(3);
        DrawSectionHeader("Container", accent);
        _grassParent = (Transform)EditorGUILayout.ObjectField(
            "Parent", _grassParent, typeof(Transform), true);
        if (_grassParent == null && GUILayout.Button("Create GrassContainer"))
            _grassParent = CreateContainer("GrassContainer");

        EditorGUILayout.Space(3);
        DrawSectionHeader("Brush", accent);
        _brushRadius    = EditorGUILayout.Slider("Brush Radius",    _brushRadius,   0.1f, 2000f);
        _grassDensity   = EditorGUILayout.Slider("Density",         _grassDensity,  1f,   40f);
        _grassAlignNorm = EditorGUILayout.Toggle("Align to Normal", _grassAlignNorm);

        _foldScale = EditorGUILayout.Foldout(_foldScale, "Scale Randomization", true, BoldFoldout());
        if (_foldScale)
        {
            EditorGUI.indentLevel++;
            _grassScaleMin   = EditorGUILayout.Slider("Height Min",   _grassScaleMin,   0.01f, 3f);
            _grassScaleMax   = EditorGUILayout.Slider("Height Max",   _grassScaleMax,   _grassScaleMin, 3f);
            _grassWidthMin   = EditorGUILayout.Slider("Width Min",    _grassWidthMin,   0.01f, 3f);
            _grassWidthMax   = EditorGUILayout.Slider("Width Max",    _grassWidthMax,   _grassWidthMin, 3f);
            _grassTiltJitter = EditorGUILayout.Slider("Tilt Jitter°", _grassTiltJitter, 0f, 45f);
            _grassUpAxis     = (PrefabUpAxis)EditorGUILayout.EnumPopup("Prefab Up Axis", _grassUpAxis);
            EditorGUILayout.HelpBox(
                "Which local axis of your prefab points away from the surface.\n" +
                "+Y = Unity default.  +Z = Blender-exported, sideways objects.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        _foldFilters = EditorGUILayout.Foldout(_foldFilters, "Placement Filters", true, BoldFoldout());
        if (_foldFilters)
        {
            EditorGUI.indentLevel++;
            _slopeLimit    = EditorGUILayout.Slider("Max Slope °",    _slopeLimit,    0f,  90f);
            _surfaceOffset = EditorGUILayout.Slider("Surface Offset", _surfaceOffset, -1f, 2f);
            EditorGUI.indentLevel--;
        }

        _foldTint = EditorGUILayout.Foldout(_foldTint, "Color Tint", true, BoldFoldout());
        if (_foldTint)
        {
            EditorGUI.indentLevel++;
            _tintEnabled = EditorGUILayout.Toggle("Enable Tint", _tintEnabled);
            if (_tintEnabled)
            {
                _tintA          = EditorGUILayout.ColorField("Tint A",         _tintA);
                _tintB          = EditorGUILayout.ColorField("Tint B",         _tintB);
                _tintVariation  = EditorGUILayout.Slider("Variation",          _tintVariation, 0f, 1f);
                _tintShaderProp = EditorGUILayout.TextField("Shader Property", _tintShaderProp);
                EditorGUILayout.HelpBox(
                    "Common names:  _BaseColor (URP)  |  _Color (built-in)  |  _TintColor (custom)",
                    MessageType.None);
            }
            EditorGUI.indentLevel--;
        }

        _foldRay = EditorGUILayout.Foldout(_foldRay, "Raycast Settings", true, BoldFoldout());
        if (_foldRay)
        {
            EditorGUI.indentLevel++;
            _probeReach = EditorGUILayout.FloatField("Probe Reach", _probeReach);
            EditorGUILayout.HelpBox(
                "Must exceed your planet's radius from its centre.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(3);
        _grassLayer = EditorGUILayout.LayerField("Grass Layer", _grassLayer);
    }

    // ── Surface Paint Panel ────────────────────────────────────────────────────

    void DrawSurfacePanel()
    {
        var accent = new Color(0.85f, 0.6f, 0.2f);

        DrawSectionHeader("PlanetSurface Component", accent);

        if (_surface == null)
        {
            EditorGUILayout.HelpBox(
                "No PlanetSurface component found on the target.\n" +
                "Add one and assign PlanetLayerData assets to its Layers array.",
                MessageType.Warning);

            if (_planet != null && GUILayout.Button("Add PlanetSurface to Target"))
            {
                var ps = Undo.AddComponent<PlanetSurface>(_planet.gameObject);
                ps.Init();
                _surface = ps;
            }
            return;
        }

        // Layer buttons — one per channel, coloured with the layer's editorColor.
        DrawSectionHeader("Layers  (click to select active)", accent);
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < 4; i++)
        {
            PlanetLayerData ld = i < _surface.layers.Length ? _surface.layers[i] : null;
            Color swatch       = ld != null ? ld.editorColor : new Color(0.3f, 0.3f, 0.3f);
            GUI.backgroundColor = i == _paintLayer ? swatch : swatch * 0.55f;
            string label = ld != null ? $"{i}: {ld.layerName}" : $"L{i}\n(empty)";
            var btnStyle = new GUIStyle(GUI.skin.button)
                { fontSize = 10, fontStyle = i == _paintLayer ? FontStyle.Bold : FontStyle.Normal };
            if (GUILayout.Button(label, btnStyle, GUILayout.Height(40)))
                _paintLayer = i;
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        PlanetLayerData selected = _paintLayer < _surface.layers.Length
            ? _surface.layers[_paintLayer] : null;

        if (selected != null)
        {
            EditorGUILayout.HelpBox(
                $"Painting: {selected.layerName}   " +
                $"Tiling: {selected.tiling}   " +
                $"Friction: {selected.frictionMultiplier}",
                MessageType.None);
            if (GUILayout.Button("Ping in Project"))
                EditorGUIUtility.PingObject(selected);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"Layer {_paintLayer} has no PlanetLayerData assigned in PlanetSurface.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(3);
        DrawSectionHeader("Paint Brush", accent);
        _brushRadius    = EditorGUILayout.Slider("Brush Radius",       _brushRadius,   0.5f, 2000f);
        _paintOpacity   = EditorGUILayout.Slider("Opacity",            _paintOpacity,  0.01f, 1f);
        _paintFalloff   = EditorGUILayout.Slider("Falloff",            _paintFalloff,  1f,    8f);
        _paintNormalise = EditorGUILayout.Toggle("Normalise Channels", _paintNormalise);
        EditorGUILayout.HelpBox(
            "Normalise: painting one layer subtracts from others so they sum to 1.\n" +
            "Un-ticked: channels accumulate freely — good for overlap blending.",
            MessageType.None);

        EditorGUILayout.Space(3);
        DrawSectionHeader("Utilities", accent);
        if (GUILayout.Button("Re-push Material Properties"))
        {
            _surface.PushMaterialProperties();
            Debug.Log("[PlanetPainter] Material properties re-pushed.");
        }
        EditorGUILayout.HelpBox(
            "Use after changing layer textures or tiling to update the shader.",
            MessageType.None);

        if (GUILayout.Button("Fill All Vertices with Active Layer"))
            FillAllVertices();
    }

    // ── Sculpt Panel ───────────────────────────────────────────────────────────

    void DrawSculptPanel()
    {
        var accent = new Color(0.7f, 0.45f, 0.2f);

        // ── ProBuilder guard ───────────────────────────────────────────────────
        if (TargetIsProBuilder())
        {
            EditorGUILayout.HelpBox(
                "This mesh is still managed by ProBuilder.\n" +
                "ProBuilder owns its vertex data and will overwrite any sculpt " +
                "edits on the next rebuild. Subdivide modifiers regenerate the " +
                "topology entirely, making this worse.\n\n" +
                "Click the button below to bake it into a plain mesh before sculpting. " +
                "Surface painting (vertex colours) still works on ProBuilder meshes.",
                MessageType.Error);

            EditorGUILayout.Space(4);
            var stripStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize   = 13,
                fontStyle  = FontStyle.Bold,
                fixedHeight = 38,
                normal    = { textColor = Color.white },
                hover     = { textColor = Color.white },
                active    = { textColor = Color.white },
            };
            GUI.backgroundColor = new Color(0.85f, 0.15f, 0.15f);
            if (GUILayout.Button("⚠  Strip ProBuilder & Bake Mesh", stripStyle))
                StripProBuilderFromTarget();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space(4);

            GUI.enabled = false;
        }
        // ──────────────────────────────────────────────────────────────────────

        DrawSectionHeader("Sculpt Brush", accent);
        _brushRadius    = EditorGUILayout.Slider("Brush Radius",     _brushRadius,    0.5f, 2000f);
        _sculptStrength = EditorGUILayout.Slider("Strength",         _sculptStrength, -5f,  5f);
        _sculptSmooth   = EditorGUILayout.IntSlider("Smooth Passes", _sculptSmooth,    0,    5);

        EditorGUILayout.HelpBox(
            "Positive Strength = push outward (raise).\n" +
            "Negative Strength = push inward (lower).\n" +
            "Hold Shift while dragging to flip the direction.",
            MessageType.Info);

        GUI.enabled = true;

        EditorGUILayout.Space(3);
        DrawSectionHeader("Recalculate", accent);
        if (GUILayout.Button("Recalculate Normals on Target"))
        {
            if (_planet != null)
            {
                var mf = _planet.GetComponent<MeshFilter>();
                if (mf?.sharedMesh != null)
                {
                    Undo.RecordObject(mf.sharedMesh, "Recalculate Normals");
                    mf.sharedMesh.RecalculateNormals();
                    mf.sharedMesh.RecalculateTangents();
                    EditorUtility.SetDirty(mf.sharedMesh);
                }
            }
        }
    }

    // ── Brush Controls ─────────────────────────────────────────────────────────

    void DrawBrushControls()
    {
        DrawSectionHeader("Brush Controls", new Color(0.6f, 0.5f, 0.85f));

        bool isErasing  = _state == PaintState.Erasing;
        bool isPainting = _state == PaintState.Painting;

        GUI.backgroundColor = isPainting   ? new Color(0.25f, 0.9f, 0.45f)
                            : isErasing    ? new Color(1f,    0.3f, 0.3f)
                            : _brushActive ? new Color(0.9f, 0.85f, 0.25f)
                            :                Color.white;

        string statusLabel = isPainting   ? "● PAINTING"
                           : isErasing    ? "✕ ERASING"
                           : _brushActive ? "◎ BRUSH ACTIVE" : "Activate Brush";

        var bigBtn = new GUIStyle(GUI.skin.button)
            { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 36 };
        if (GUILayout.Button(statusLabel, bigBtn)) _brushActive = !_brushActive;
        GUI.backgroundColor = Color.white;

        // Paint / Erase toggle (Sculpt uses Shift instead).
        if (_mode != BrushMode.Sculpt)
        {
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = !_eraseMode ? new Color(0.25f, 0.9f, 0.45f) : new Color(0.55f, 0.55f, 0.55f);
            if (GUILayout.Button("🖌  Paint", GUILayout.Height(26))) _eraseMode = false;
            GUI.backgroundColor = _eraseMode  ? new Color(1f, 0.3f, 0.3f)     : new Color(0.55f, 0.55f, 0.55f);
            if (GUILayout.Button("✕  Erase",  GUILayout.Height(26))) _eraseMode = true;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        string hint = _mode == BrushMode.Sculpt
            ? "Drag to sculpt. Hold Shift to invert direction."
            : _eraseMode
                ? "Drag to erase. Hold Shift to paint temporarily."
                : "Drag to paint. Hold Shift to erase temporarily.";

        EditorGUILayout.HelpBox(
            _brushActive ? hint : "Click above to activate the brush.",
            MessageType.Info);
    }

    // ── Stats ──────────────────────────────────────────────────────────────────

    void DrawStats()
    {
        DrawSectionHeader("Scene Stats", new Color(0.5f, 0.5f, 0.55f));
        int objCount   = _objParent   != null ? _objParent.childCount   : 0;
        int grassCount = _grassParent != null ? _grassParent.childCount : 0;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label($"Objects: {objCount}",   BadgeStyle(new Color(0.35f, 0.75f, 0.45f)));
        GUILayout.Label($"Grass:   {grassCount}", BadgeStyle(new Color(0.45f, 0.82f, 0.3f)));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawSectionHeader("Performance / Render", new Color(0.55f, 0.45f, 0.75f));
        DrawRenderDensityRow("Objects", ref _objRenderDensity, _objParent);
        DrawRenderDensityRow("Grass",   ref _grassRenderDensity, _grassParent);
    }

    static readonly string[] RenderDensityLabels = { "Full", "1/2", "1/4", "Off" };

    void DrawRenderDensityRow(string label, ref RenderDensity current, Transform container)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(52));
        for (int i = 0; i < 4; i++)
        {
            var d = (RenderDensity)i;
            GUI.backgroundColor = current == d ? new Color(0.5f, 0.5f, 0.9f) : new Color(0.4f, 0.4f, 0.4f);
            if (GUILayout.Button(RenderDensityLabels[i], GUILayout.Height(22)))
            {
                current = d;
                ApplyRenderDensity(container, d);
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    static void ApplyRenderDensity(Transform container, RenderDensity density)
    {
        if (container == null) return;
        int step = density == RenderDensity.Full    ? 1
                 : density == RenderDensity.Half    ? 2
                 : density == RenderDensity.Quarter ? 4
                 :                                    int.MaxValue;
        int idx = 0;
        foreach (Transform child in container)
        {
            bool on = density != RenderDensity.Off && (idx % step == 0);
            foreach (var r in child.GetComponentsInChildren<Renderer>())
                r.enabled = on;
            idx++;
        }
    }

    // ── Mesh Tools ─────────────────────────────────────────────────────────────

    void DrawMeshTools()
    {
        DrawSectionHeader("Mesh Tools  —  Vertex Resolution", new Color(0.6f, 0.55f, 0.3f));

        if (_planet == null)
        {
            EditorGUILayout.HelpBox("Assign a planet Transform above.", MessageType.Info);
            return;
        }

        var mf   = _planet.GetComponent<MeshFilter>();
        int vNow = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
        int vAfter = vNow;
        for (int i = 0; i < _subdivIterations; i++) vAfter *= 4;

        _subdivIterations = EditorGUILayout.IntSlider("Iterations", _subdivIterations, 1, 3);
        _subdivReproject  = EditorGUILayout.Toggle("Reproject to Sphere", _subdivReproject);

        EditorGUILayout.HelpBox(
            $"Each iteration ×4 verts.  {vNow:N0}  →  {vAfter:N0} vertices after {_subdivIterations} pass(es).\n" +
            "Reproject keeps the sphere round by normalising new midpoints.\n" +
            "Vertex colours are interpolated so existing paint is preserved.",
            MessageType.Info);

        GUI.backgroundColor = new Color(0.45f, 0.75f, 0.45f);
        if (GUILayout.Button("Subdivide Planet Mesh", GUILayout.Height(28)))
        {
            if (mf == null || mf.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("No Mesh", "Planet has no MeshFilter or sharedMesh.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog(
                "Subdivide",
                $"Subdivide {_subdivIterations} time(s)?\n" +
                $"Result: ~{vAfter:N0} vertices.\nThis modifies the asset on disk.",
                "Subdivide", "Cancel"))
            {
                Undo.RecordObject(mf, "Subdivide Mesh");
                Mesh m = mf.sharedMesh;
                for (int i = 0; i < _subdivIterations; i++)
                    m = SubdivideOnce(m);
                mf.sharedMesh = m;
                EditorUtility.SetDirty(mf);
            }
        }
        GUI.backgroundColor = Color.white;
    }

    Mesh SubdivideOnce(Mesh src)
    {
        Vector3[] srcV = src.vertices;
        Color[]   srcC = src.colors;
        Vector2[] srcU = src.uv;
        int[]     srcT = src.triangles;

        // Default vertex colours to Layer 0 (1,0,0,0) if none exist.
        if (srcC == null || srcC.Length != srcV.Length)
        {
            srcC = new Color[srcV.Length];
            for (int i = 0; i < srcC.Length; i++) srcC[i] = new Color(1, 0, 0, 0);
        }
        bool hasUV = srcU != null && srcU.Length == srcV.Length;

        var verts  = new List<Vector3>(srcV);
        var colors = new List<Color>(srcC);
        var uvs    = new List<Vector2>(hasUV ? srcU : new Vector2[srcV.Length]);
        var tris   = new List<int>(srcT.Length * 4);
        var midMap = new Dictionary<long, int>();

        int Midpoint(int a, int b)
        {
            long key = a < b ? ((long)a << 32 | (uint)b) : ((long)b << 32 | (uint)a);
            if (midMap.TryGetValue(key, out int idx)) return idx;

            Vector3 mid = (srcV[a] + srcV[b]) * 0.5f;
            if (_subdivReproject && _planet != null)
            {
                float rA  = (srcV[a] - PlanetCenter).magnitude;
                float rB  = (srcV[b] - PlanetCenter).magnitude;
                mid = PlanetCenter + (mid - PlanetCenter).normalized * ((rA + rB) * 0.5f);
            }

            idx = verts.Count;
            verts.Add(mid);
            colors.Add(Color.Lerp(srcC[a], srcC[b], 0.5f));
            uvs.Add(hasUV ? (srcU[a] + srcU[b]) * 0.5f : Vector2.zero);
            midMap[key] = idx;
            return idx;
        }

        for (int t = 0; t < srcT.Length; t += 3)
        {
            int a = srcT[t], b = srcT[t + 1], c = srcT[t + 2];
            int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);

            tris.AddRange(new[] { a, ab, ca });
            tris.AddRange(new[] { ab, b,  bc });
            tris.AddRange(new[] { ca, bc, c  });
            tris.AddRange(new[] { ab, bc, ca });
        }

        var result = new Mesh { name = src.name };
        result.indexFormat = verts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        result.SetVertices(verts);
        result.SetColors(colors);
        result.SetUVs(0, uvs);
        result.SetTriangles(tris, 0);
        result.RecalculateNormals();
        result.RecalculateBounds();
        return result;
    }

    // ── Danger Zone ────────────────────────────────────────────────────────────

    void DrawDangerZone()
    {
        DrawSectionHeader("Danger Zone", new Color(0.85f, 0.3f, 0.3f));

        if (_mode == BrushMode.SurfacePaint)
        {
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("Reset All Vertex Colours to Layer 0", GUILayout.Height(28)))
            {
                if (_surface != null && EditorUtility.DisplayDialog(
                    "Confirm", "Reset all vertex colours on this mesh to full Layer 0?",
                    "Yes", "Cancel"))
                {
                    Undo.RecordObject(_surface.SharedMesh, "Reset Vertex Colours");
                    _paintLayer = 0;
                    FillAllVertices();
                }
            }
            GUI.backgroundColor = Color.white;
            return;
        }

        Transform activeParent = _mode == BrushMode.ObjectPlacement ? _objParent : _grassParent;
        string    label        = _mode == BrushMode.ObjectPlacement ? "Objects" : "Grass";

        GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
        if (GUILayout.Button($"Clear All {label}", GUILayout.Height(28)))
        {
            if (activeParent == null)
            {
                EditorUtility.DisplayDialog("No Container", "No container assigned.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog(
                "Confirm", $"Delete all {label.ToLower()} under '{activeParent.name}'?",
                "Yes", "Cancel"))
            {
                Undo.RegisterFullObjectHierarchyUndo(activeParent.gameObject, $"Clear {label}");
                for (int i = activeParent.childCount - 1; i >= 0; i--)
                    DestroyImmediate(activeParent.GetChild(i).gameObject);
            }
        }
        GUI.backgroundColor = Color.white;
    }

    // ── Prefab List ────────────────────────────────────────────────────────────

    void DrawPrefabList(List<GameObject> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            var numStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.55f, 0.55f, 0.55f) }
            };
            GUILayout.Label($"{i + 1}", numStyle, GUILayout.Width(18));
            list[i] = (GameObject)EditorGUILayout.ObjectField(list[i], typeof(GameObject), false);
            GUI.backgroundColor = new Color(1f, 0.42f, 0.42f);
            if (GUILayout.Button("✕", GUILayout.Width(24))) { list.RemoveAt(i); i--; }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        GUI.backgroundColor = new Color(0.45f, 0.8f, 0.5f);
        if (GUILayout.Button("+ Add Prefab")) list.Add(null);
        GUI.backgroundColor = Color.white;
    }

    // ── UI Helpers ─────────────────────────────────────────────────────────────

    static void DrawSectionHeader(string title, Color accent)
    {
        EditorGUILayout.Space(2);
        var rect = EditorGUILayout.GetControlRect(false, 20f);
        EditorGUI.DrawRect(rect, new Color(accent.r * 0.28f, accent.g * 0.28f, accent.b * 0.28f, 0.85f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);
        var s = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = 11, normal = { textColor = Color.white } };
        EditorGUI.LabelField(new Rect(rect.x + 8f, rect.y, rect.width, rect.height), title, s);
        EditorGUILayout.Space(2);
    }

    static GUIStyle BoldFoldout() =>
        new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

    static GUIStyle BadgeStyle(Color col) => new GUIStyle(EditorStyles.boldLabel)
    {
        alignment = TextAnchor.MiddleCenter,
        normal    = { textColor = col }
    };

    static void DrawSeparator()
    {
        EditorGUILayout.Space(4);
        EditorGUI.DrawRect(
            EditorGUILayout.GetControlRect(false, 1f),
            new Color(0.22f, 0.22f, 0.22f, 0.9f));
        EditorGUILayout.Space(4);
    }

    static Texture2D MakeTex(int w, int h, Color col)
    {
        var t   = new Texture2D(w, h);
        var pix = new Color[w * h];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        t.SetPixels(pix);
        t.Apply();
        return t;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scene GUI
    // ──────────────────────────────────────────────────────────────────────────

    void OnSceneGUI(SceneView scene)
    {
        if (!_brushActive || _planet == null) return;

        Event e         = Event.current;
        bool  shiftHeld = e.shift;

        Ray         ray     = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        RaycastHit? surface = PlanetSurfaceHit(ray);

        if (surface.HasValue)
        {
            Vector3 pt        = surface.Value.point;
            Vector3 outNormal = (pt - PlanetCenter).normalized;
            Vector3 hitNormal = surface.Value.normal;
            float   slope     = Vector3.Angle(hitNormal, outNormal);
            bool    erasing   = _mode == BrushMode.Sculpt
                ? shiftHeld
                : (_eraseMode ? !shiftHeld : shiftHeld);

            DrawBrushGizmo(pt, outNormal, hitNormal, slope, erasing);
            scene.Repaint();
        }

        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
        {
            if (surface.HasValue)
            {
                Vector3 pt        = surface.Value.point;
                Vector3 outNormal = (pt - PlanetCenter).normalized;
                bool    erasing   = _mode == BrushMode.Sculpt
                    ? shiftHeld
                    : (_eraseMode ? !shiftHeld : shiftHeld);

                switch (_mode)
                {
                    case BrushMode.ObjectPlacement:
                        _state = erasing ? PaintState.Erasing : PaintState.Painting;
                        if (erasing) Erase(pt);
                        else         PlaceObjects(pt, outNormal);
                        break;

                    case BrushMode.GrassEmbedder:
                        _state = erasing ? PaintState.Erasing : PaintState.Painting;
                        if (erasing) Erase(pt);
                        else         PlaceGrass(pt, outNormal);
                        break;

                    case BrushMode.SurfacePaint:
                        _state = PaintState.Painting;
                        PaintSurface(surface.Value, outNormal, erasing);
                        break;

                    case BrushMode.Sculpt:
                        _state = PaintState.Painting;
                        SculptSurface(surface.Value, outNormal, erasing);
                        break;
                }
            }
            e.Use();
        }

        if (e.type == EventType.MouseUp)
        {
            _state = PaintState.Idle;
            _strokePlaced.Clear();
        }
    }

    // ── Gizmo ──────────────────────────────────────────────────────────────────

    void DrawBrushGizmo(Vector3 pt, Vector3 outNormal, Vector3 hitNormal, float slope, bool erasing)
    {
        Color baseCol;
        if (_mode == BrushMode.SurfacePaint && _surface != null)
        {
            PlanetLayerData ld = _paintLayer < _surface.layers.Length
                ? _surface.layers[_paintLayer] : null;
            baseCol = ld != null ? ld.editorColor : new Color(0.85f, 0.6f, 0.2f);
            if (erasing) baseCol = new Color(1f, 0.22f, 0.2f);
        }
        else if (_mode == BrushMode.Sculpt)
        {
            baseCol = erasing ? new Color(0.3f, 0.5f, 1f) : new Color(1f, 0.65f, 0.2f);
        }
        else
        {
            bool slopeFail = slope > _slopeLimit;
            baseCol = erasing   ? new Color(1f,   0.22f, 0.2f)
                    : slopeFail ? new Color(1f,   0.7f,  0.1f)
                    :             new Color(0.2f, 0.92f, 0.42f);
        }

        Vector3 tangent = Vector3.Cross(outNormal, Vector3.up);
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(outNormal, Vector3.forward);
        tangent.Normalize();
        Vector3 biTangent = Vector3.Cross(outNormal, tangent);

        Handles.color = new Color(baseCol.r, baseCol.g, baseCol.b, 0.92f);
        Handles.DrawWireDisc(pt, outNormal, _brushRadius);

        Handles.color = new Color(baseCol.r, baseCol.g, baseCol.b, 0.3f);
        Handles.DrawWireDisc(pt, outNormal, _brushRadius * 0.88f);

        Handles.color = new Color(baseCol.r, baseCol.g, baseCol.b, erasing ? 0.18f : 0.07f);
        Handles.DrawSolidDisc(pt, outNormal, _brushRadius);

        Handles.color = new Color(1f, 1f, 1f, 0.22f);
        Handles.DrawLine(pt - tangent   * _brushRadius, pt + tangent   * _brushRadius);
        Handles.DrawLine(pt - biTangent * _brushRadius, pt + biTangent * _brushRadius);

        float arrowLen = _brushRadius * 0.55f;
        Handles.color = new Color(1f, 0.95f, 0.2f, 0.88f);
        Handles.DrawLine(pt, pt + outNormal * arrowLen);
        Handles.SphereHandleCap(0, pt + outNormal * arrowLen,
            Quaternion.identity, _brushRadius * 0.055f, EventType.Repaint);

        if (Vector3.Angle(hitNormal, outNormal) > 1f)
        {
            Handles.color = new Color(0.35f, 0.65f, 1f, 0.75f);
            Handles.DrawLine(pt, pt + hitNormal * (arrowLen * 0.65f));
            Handles.SphereHandleCap(0, pt + hitNormal * (arrowLen * 0.65f),
                Quaternion.identity, _brushRadius * 0.04f, EventType.Repaint);
        }

        Handles.color = new Color(1f, 1f, 1f, 0.92f);
        Handles.DotHandleCap(0, pt, Quaternion.identity, _brushRadius * 0.038f, EventType.Repaint);

        string modeTag = _mode == BrushMode.SurfacePaint
            ? (erasing ? "ERASE SURFACE" : $"PAINT  L{_paintLayer}")
            : _mode == BrushMode.Sculpt
                ? (erasing ? "SCULPT ▼" : "SCULPT ▲")
                : (erasing ? "ERASE" : (_mode == BrushMode.ObjectPlacement ? "OBJECTS" : "GRASS"));

        var labelStyle = new GUIStyle
        {
            normal    = { textColor = new Color(baseCol.r, baseCol.g, baseCol.b, 0.95f) },
            fontStyle = FontStyle.Bold,
            fontSize  = 11
        };
        Handles.Label(
            pt + outNormal * (_brushRadius * 1.18f) + tangent * (_brushRadius * 0.08f),
            $" {modeTag}  r:{_brushRadius:F1}", labelStyle);
    }

    // ── Spherical Raycast ──────────────────────────────────────────────────────

    RaycastHit? PlanetSurfaceHit(Ray cameraRay)
    {
        RaycastHit[] hits = Physics.RaycastAll(cameraRay, 2000f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (IsPaintedChild(h.collider.transform)) continue;
            return h;
        }
        return null;
    }

    bool IsPaintedChild(Transform t)
    {
        while (t != null)
        {
            if (_objParent   != null && t == _objParent)   return true;
            if (_grassParent != null && t == _grassParent) return true;
            t = t.parent;
        }
        return false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Surface Painting
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds every mesh vertex within brush radius and adjusts its vertex
    /// colour channel for the active layer.  Supports opacity + falloff.
    /// Calls CommitColors() to flush the changes back to the GPU mesh.
    /// </summary>
    void PaintSurface(RaycastHit hit, Vector3 outNormal, bool erasing)
    {
        if (_surface == null) { RefreshSurface(); if (_surface == null) return; }

        Mesh mesh = _surface.SharedMesh;
        if (mesh == null) return;

        Undo.RecordObject(mesh, "Paint Surface");

        Vector3[] verts = mesh.vertices;
        Transform tf    = _planet;

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 worldVert = tf.TransformPoint(verts[i]);
            float   dist      = Vector3.Distance(worldVert, hit.point);
            if (dist > _brushRadius) continue;

            // Falloff: 1 at brush centre, 0 at edge.
            float t     = 1f - Mathf.Pow(dist / _brushRadius, _paintFalloff);
            float delta = t * _paintOpacity;

            Color c = _surface.GetColor(i);

            if (erasing)
            {
                // Remove weight from active channel and return it to Layer 0.
                float removed = Mathf.Min(c[(int)_paintLayer], delta);
                c[(int)_paintLayer] -= removed;
                if (_paintLayer != 0) c[0] += removed;
            }
            else
            {
                // Add to active channel.
                float add = Mathf.Min(delta, 1f - c[(int)_paintLayer]);
                c[(int)_paintLayer] = Mathf.Clamp01(c[(int)_paintLayer] + add);

                // Normalise: redistribute the excess away from the other channels.
                if (_paintNormalise)
                {
                    float excess = c.r + c.g + c.b + c.a - 1f;
                    if (excess > 0f)
                    {
                        float otherSum = 0f;
                        for (int ch = 0; ch < 4; ch++)
                            if (ch != _paintLayer) otherSum += c[ch];

                        for (int ch = 0; ch < 4; ch++)
                        {
                            if (ch == _paintLayer) continue;
                            if (otherSum > 1e-5f)
                                c[ch] = Mathf.Clamp01(c[ch] - excess * (c[ch] / otherSum));
                        }
                    }
                }
            }

            _surface.SetColor(i, c);
        }

        _surface.CommitColors();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Sculpting
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Displaces mesh vertices within the brush radius along each vertex's
    /// object-space normal, then recalculates normals and tangents.
    /// Calls RefreshProBuilderMesh() via reflection for ProBuilder meshes.
    /// </summary>
    void SculptSurface(RaycastHit hit, Vector3 outNormal, bool invert)
    {
        // ProBuilder meshes cannot be sculpted — they own their vertex data
        // and will overwrite any direct edits on the next rebuild.
        if (TargetIsProBuilder())
        {
            Debug.LogWarning(
                "[PlanetPainter] Sculpting is not supported on ProBuilder meshes. " +
                "Use ProBuilder → Strip ProBuilder Scripts before sculpting, or " +
                "use a plain mesh (e.g. GameObject → 3D Object → Sphere).");
            return;
        }

        var mf = _planet.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        Mesh mesh = mf.sharedMesh;
        Undo.RecordObject(mesh, "Sculpt Surface");

        Vector3[] verts   = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Transform tf      = _planet;
        float     sign    = invert ? -1f : 1f;

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 worldVert = tf.TransformPoint(verts[i]);
            float   dist      = Vector3.Distance(worldVert, hit.point);
            if (dist > _brushRadius) continue;

            // Squared falloff: stronger centre, soft edge.
            float t = 1f - (dist / _brushRadius);
            t = t * t;

            // Displace along the object-space vertex normal.
            verts[i] += normals[i] * (sign * _sculptStrength * t * 0.016f);
        }

        if (_sculptSmooth > 0)
            verts = SmoothVertices(mesh, verts, _sculptSmooth);

        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        EditorUtility.SetDirty(mesh);
        RefreshProBuilderMesh(tf.gameObject);
    }

    /// <summary>
    /// Runs N passes of Laplacian vertex smoothing using triangle connectivity.
    /// Each vertex moves 50% toward the average of its triangle neighbours.
    /// </summary>
    static Vector3[] SmoothVertices(Mesh mesh, Vector3[] verts, int passes)
    {
        int[] tris = mesh.triangles;
        for (int p = 0; p < passes; p++)
        {
            var sums   = new Vector3[verts.Length];
            var counts = new int    [verts.Length];

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                sums[a] += verts[b] + verts[c]; counts[a] += 2;
                sums[b] += verts[a] + verts[c]; counts[b] += 2;
                sums[c] += verts[a] + verts[b]; counts[c] += 2;
            }

            var result = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
                result[i] = counts[i] > 0
                    ? Vector3.Lerp(verts[i], sums[i] / counts[i], 0.5f)
                    : verts[i];
            verts = result;
        }
        return verts;
    }

    /// <summary>
    /// Returns true when the current target has a ProBuilderMesh component.
    /// Uses reflection so there is no hard compile-time dependency on ProBuilder.
    /// </summary>
    bool TargetIsProBuilder()
    {
        if (_planet == null) return false;
        var pbType = System.Type.GetType(
            "UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder");
        return pbType != null && _planet.GetComponent(pbType) != null;
    }

    /// <summary>
    /// Bakes the current ProBuilder render mesh into a saved Mesh asset, then
    /// destroys all ProBuilder components so the object becomes a plain mesh.
    /// The baked mesh is saved next to the scene as  Assets/BakedMeshes/&lt;name&gt;.asset.
    /// </summary>
    void StripProBuilderFromTarget()
    {
        if (_planet == null) return;

        var pbType = System.Type.GetType(
            "UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder");
        if (pbType == null)
        {
            Debug.LogWarning("[PlanetPainter] ProBuilder assembly not found.");
            return;
        }

        var mf = _planet.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("[PlanetPainter] No MeshFilter / sharedMesh found on target.");
            return;
        }

        // ── 1. Bake a standalone copy of the current render mesh ───────────────
        Mesh bakedMesh = Object.Instantiate(mf.sharedMesh);
        bakedMesh.name = _planet.name + "_Baked";

        const string folder = "Assets/BakedMeshes";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "BakedMeshes");

        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{folder}/{bakedMesh.name}.asset");
        AssetDatabase.CreateAsset(bakedMesh, assetPath);
        AssetDatabase.SaveAssets();

        // ── 2. Register undo for the GameObject so components can be restored ──
        Undo.RegisterFullObjectHierarchyUndo(_planet.gameObject, "Strip ProBuilder");

        // ── 3. Assign the baked mesh before destroying ProBuilder components ───
        mf.sharedMesh = bakedMesh;

        // ── 4. Destroy all known ProBuilder component types via reflection ──────
        //       Order matters: remove dependents before ProBuilderMesh itself.
        string[] pbComponentTypeNames =
        {
            "UnityEngine.ProBuilder.PolyShape, Unity.ProBuilder",
            "UnityEngine.ProBuilder.BezierShape, Unity.ProBuilder",
            "UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder",
        };

        foreach (string typeName in pbComponentTypeNames)
        {
            var t = System.Type.GetType(typeName);
            if (t == null) continue;
            var comp = _planet.GetComponent(t);
            if (comp != null)
                Undo.DestroyObjectImmediate(comp);
        }

        EditorUtility.SetDirty(_planet.gameObject);
        EditorUtility.SetDirty(mf);

        Debug.Log(
            $"[PlanetPainter] Stripped ProBuilder from '{_planet.name}'. " +
            $"Baked mesh saved to '{assetPath}'.");

        // Refresh state so the guard clears immediately.
        RefreshSurface();
        Repaint();
    }

    /// <summary>
    /// Refreshes the ProBuilder mesh component via reflection so that ProBuilder's
    /// internal data stays in sync with the modified vertices.
    /// Does nothing if the ProBuilder assembly is not present or the component
    /// is missing — no hard compile-time dependency.
    /// </summary>
    static void RefreshProBuilderMesh(GameObject go)
    {
        var pbType = System.Type.GetType(
            "UnityEngine.ProBuilder.ProBuilderMesh, Unity.ProBuilder");
        if (pbType == null) return;

        var comp = go.GetComponent(pbType);
        if (comp == null) return;

        var refresh = pbType.GetMethod("Refresh",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public);
        refresh?.Invoke(comp, null);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Object Placement
    // ──────────────────────────────────────────────────────────────────────────

    void PlaceObjects(Vector3 center, Vector3 outNormal)
    {
        var valid = _objPrefabs.FindAll(p => p != null);
        if (valid.Count == 0) { Debug.LogWarning("PlanetPainter: add at least one prefab."); return; }
        EnsureContainer(ref _objParent, "ObjectContainer");

        int count = Mathf.Max(1, Mathf.RoundToInt(_objDensity));
        for (int i = 0; i < count; i++)
        {
            Vector3     samplePt = SampleSpherePoint(center, outNormal, _brushRadius);
            RaycastHit? hit      = SphericalProbeHit(samplePt, outNormal);
            if (!hit.HasValue) continue;
            if (TooClose(hit.Value.point, ObjMinGap, _objParent)) continue;
            if (Vector3.Angle(hit.Value.normal, outNormal) > _slopeLimit) continue;

            GameObject prefab = valid[Random.Range(0, valid.Count)];
            Quaternion rot    = BuildRotation(hit.Value.normal, outNormal, _objAlignNorm, _objRandYaw, _objTiltJitter, _objUpAxis);
            float      height = Random.Range(_objScaleMin, _objScaleMax);
            float      width  = Random.Range(_objWidthMin, _objWidthMax);

            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _objParent);
            obj.transform.SetPositionAndRotation(
                hit.Value.point + outNormal * _surfaceOffset, rot);
            obj.transform.localScale = new Vector3(width, height, width);
            SetLayerRecursive(obj, _objLayer);

            Undo.RegisterCreatedObjectUndo(obj, "Paint Object");
            _strokePlaced.Add(hit.Value.point);
        }
    }

    // ── Grass Placement ────────────────────────────────────────────────────────

    void PlaceGrass(Vector3 center, Vector3 outNormal)
    {
        var valid = _grassPrefabs.FindAll(p => p != null);
        if (valid.Count == 0) { Debug.LogWarning("PlanetPainter: add at least one grass prefab."); return; }
        EnsureContainer(ref _grassParent, "GrassContainer");

        int count = Mathf.Max(1, Mathf.RoundToInt(_grassDensity));
        for (int i = 0; i < count; i++)
        {
            Vector3     samplePt = SampleSpherePoint(center, outNormal, _brushRadius);
            RaycastHit? hit      = SphericalProbeHit(samplePt, outNormal);
            if (!hit.HasValue) continue;
            if (TooClose(hit.Value.point, GrassMinGap, _grassParent)) continue;
            if (Vector3.Angle(hit.Value.normal, outNormal) > _slopeLimit) continue;

            GameObject prefab = valid[Random.Range(0, valid.Count)];
            Quaternion rot    = BuildRotation(hit.Value.normal, outNormal, _grassAlignNorm, true, _grassTiltJitter, _grassUpAxis);
            float      height = Random.Range(_grassScaleMin, _grassScaleMax);
            float      width  = Random.Range(_grassWidthMin, _grassWidthMax);

            var blade = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _grassParent);
            blade.transform.SetPositionAndRotation(
                hit.Value.point + outNormal * _surfaceOffset, rot);
            blade.transform.localScale = new Vector3(width, height, width);
            SetLayerRecursive(blade, _grassLayer);

            if (_tintEnabled)
                ApplyTint(blade, Color.Lerp(_tintA, _tintB, Random.Range(0f, _tintVariation)));

            Undo.RegisterCreatedObjectUndo(blade, "Paint Grass");
            _strokePlaced.Add(hit.Value.point);
        }
    }

    // ── Erase ──────────────────────────────────────────────────────────────────

    void Erase(Vector3 center)
    {
        Transform activeParent = _mode == BrushMode.ObjectPlacement ? _objParent : _grassParent;
        if (activeParent == null) return;

        var toDelete = new List<GameObject>();
        foreach (Transform child in activeParent)
            if (Vector3.Distance(child.position, center) <= _brushRadius)
                toDelete.Add(child.gameObject);

        foreach (var go in toDelete)
            Undo.DestroyObjectImmediate(go);
    }

    // ── Sphere Helpers ─────────────────────────────────────────────────────────

    Vector3 SampleSpherePoint(Vector3 center, Vector3 outNormal, float radius)
    {
        Vector3 tangent = Vector3.Cross(outNormal, Vector3.up);
        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(outNormal, Vector3.forward);
        tangent.Normalize();
        Vector3 biTangent = Vector3.Cross(outNormal, tangent);
        Vector2 r2 = Random.insideUnitCircle * radius;
        return center + tangent * r2.x + biTangent * r2.y;
    }

    RaycastHit? SphericalProbeHit(Vector3 samplePoint, Vector3 outNormal)
    {
        float        reach = Mathf.Max(_probeReach, 10f);
        RaycastHit[] hits  = Physics.RaycastAll(
            new Ray(samplePoint + outNormal * reach, -outNormal), reach * 2f);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
            if (!IsPaintedChild(h.collider.transform)) return h;
        return null;
    }

    // ── Rotation Builder ───────────────────────────────────────────────────────

    static Vector3 AxisToVector(PrefabUpAxis a)
    {
        switch (a)
        {
            case PrefabUpAxis.PlusY:  return Vector3.up;
            case PrefabUpAxis.MinusY: return Vector3.down;
            case PrefabUpAxis.PlusX:  return Vector3.right;
            case PrefabUpAxis.MinusX: return Vector3.left;
            case PrefabUpAxis.PlusZ:  return Vector3.forward;
            case PrefabUpAxis.MinusZ: return Vector3.back;
            default:                  return Vector3.up;
        }
    }

    static Quaternion BuildRotation(
        Vector3 hitNormal, Vector3 outNormal,
        bool alignToNormal, bool randomYaw, float tiltJitter, PrefabUpAxis prefabUp)
    {
        Vector3    upAxis    = alignToNormal ? hitNormal : outNormal;
        Vector3    prefabUpV = AxisToVector(prefabUp);
        Quaternion rot       = Quaternion.FromToRotation(prefabUpV, upAxis);
        if (randomYaw)      rot = rot * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        if (tiltJitter > 0) rot = rot * Quaternion.Euler(
            Random.Range(-tiltJitter, tiltJitter), 0f,
            Random.Range(-tiltJitter, tiltJitter));
        return rot;
    }

    // ── Proximity check ────────────────────────────────────────────────────────

    bool TooClose(Vector3 point, float minGap, Transform container = null)
    {
        foreach (var p in _strokePlaced)
            if (Vector3.Distance(point, p) < minGap) return true;
        if (container != null)
            foreach (Transform child in container)
                if (Vector3.Distance(child.position, point) < minGap) return true;
        return false;
    }

    // ── Container Helpers ──────────────────────────────────────────────────────

    Transform CreateContainer(string containerName)
    {
        var go = new GameObject(containerName);
        Undo.RegisterCreatedObjectUndo(go, $"Create {containerName}");
        return go.transform;
    }

    void EnsureContainer(ref Transform container, string containerName)
    {
        if (container != null) return;
        container = CreateContainer(containerName);
    }

    // ── Tint ───────────────────────────────────────────────────────────────────

    void ApplyTint(GameObject root, Color tint)
    {
        var mpb = new MaterialPropertyBlock();
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor(_tintShaderProp, tint);
            r.SetPropertyBlock(mpb);
            foreach (var mat in r.sharedMaterials)
                if (mat != null && mat.HasProperty(_tintShaderProp))
                    mat.SetColor(_tintShaderProp, tint);
        }
    }

    // ── Layer ──────────────────────────────────────────────────────────────────

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform)
            SetLayerRecursive(c.gameObject, layer);
    }

    // ── Surface helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Re-fetches the <see cref="PlanetSurface"/> component from the current
    /// target and calls Init() on it so the colour buffer is ready.
    /// </summary>
    void RefreshSurface()
    {
        _surface = _planet != null
            ? _planet.GetComponent<PlanetSurface>()
            : null;

        if (_surface != null) _surface.Init();
    }

    /// <summary>
    /// Flood-fills every vertex of the mesh with the currently selected paint
    /// layer (sets that channel to 1, all others to 0).
    /// </summary>
    void FillAllVertices()
    {
        if (_surface == null) return;
        Mesh mesh = _surface.SharedMesh;
        if (mesh == null) return;

        Undo.RecordObject(mesh, "Fill Vertex Colours");
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            var c = new Color(0f, 0f, 0f, 0f);
            c[(int)_paintLayer] = 1f;
            _surface.SetColor(i, c);
        }
        _surface.CommitColors();
    }
}
