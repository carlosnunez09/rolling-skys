using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NaughtyAttributes;

/// <summary>
/// HUD component: renders a live globe view of the dominant planet into a
/// RenderTexture and overlays the player position and all track waypoints
/// as UI dots on top of it.
///
/// UI wiring (all optional — missing slots are skipped safely):
///   _mapDisplay         — RawImage that shows the rendered globe
///   _planetNameText     — TextMeshProUGUI label above/below the map
///   _playerDot          — RectTransform (small image) for the player marker
///   _waypointDotPrefab  — prefab with a RectTransform (tiny circle/dot sprite)
///   _dotContainer       — parent RectTransform for spawned waypoint dots
///
/// Camera note:
///   The minimap camera renders ALL layers by default (cullingMask = ~0).
///   If you use a dedicated "MiniMap" layer, set _cullingMask accordingly
///   and move planet objects to that layer to isolate the minimap scene.
/// </summary>
public class minimap : MonoBehaviour {

    //
    // ── References ────────────────────────────────────────────────────────────

    [BoxGroup("References"), SerializeField, Required]
    MovingCar _car;

    // ── UI ────────────────────────────────────────────────────────────────────

    [BoxGroup("UI"), SerializeField]
    RawImage _mapDisplay;

    [BoxGroup("UI"), SerializeField]
    TextMeshProUGUI _planetNameText;

    [BoxGroup("UI"), SerializeField, Label("Player Dot")]
    RectTransform _playerDot;

    [BoxGroup("UI"), SerializeField, Label("Waypoint Dot Prefab")]
    GameObject _waypointDotPrefab;

    [BoxGroup("UI"), SerializeField, Label("Dot Container")]
    RectTransform _dotContainer;

    // Assign any circle sprite (UI > Default Sprite is fine) to make the map round.
    // The script adds a Mask to the direct parent of Map Display at runtime.
    [BoxGroup("UI"), SerializeField, Label("Circle Mask Sprite")]
    Sprite _circleMaskSprite;

    // ── Camera ────────────────────────────────────────────────────────────────

    [BoxGroup("Camera"), SerializeField, Range(10f, 1000f), Label("Orbit Distance")]
    float _orbitDistance = 120f;

    [BoxGroup("Camera"), SerializeField, Range(20f, 120f), Label("Field of View")]
    float _fov = 50f;

    [BoxGroup("Camera"), SerializeField, Range(64, 512), Label("Render Texture Size")]
    int _texSize = 256;

    [BoxGroup("Camera"), SerializeField, Label("Background Color")]
    Color _bgColor = new Color(0.02f, 0.02f, 0.06f, 1f);

    // Tick any layer here to hide it from the minimap (clouds, atmosphere, etc.)
    [BoxGroup("Camera"), SerializeField, Label("Exclude Layers")]
    LayerMask _excludeLayers = 0;

    // ── Stats ─────────────────────────────────────────────────────────────────

    [BoxGroup("Stats"), SerializeField, ReadOnly]
    string _activePlanetName;

    // ── Private State ─────────────────────────────────────────────────────────

    Camera                     _miniCam;
    RenderTexture              _rt;
    Planet                     _currentPlanet;
    readonly List<RectTransform> _dots = new List<RectTransform>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake () {
        SetupCircleMask();
        BuildCamera();
    }

    void SetupCircleMask () {
        if (_circleMaskSprite == null || _mapDisplay == null) return;

        Transform parent = _mapDisplay.transform.parent;
        if (parent == null || parent.GetComponent<Mask>() != null) return;

        // Add a circle Image + Mask to the parent so all children are clipped to the circle.
        var img    = parent.gameObject.AddComponent<Image>();
        img.sprite = _circleMaskSprite;
        img.type   = Image.Type.Simple;
        img.color  = Color.white;

        var mask = parent.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
    }

    void OnDestroy () {
        if (_miniCam) Destroy(_miniCam.gameObject);
        if (_rt)      { _rt.Release(); Destroy(_rt); }
    }

    void LateUpdate () {
        if (_car == null) return;

        Planet dominant = PlanetRegistry.GetDominant(_car.transform.position);

        if (dominant != _currentPlanet) {
            _currentPlanet    = dominant;
            _activePlanetName = dominant != null ? dominant.PlanetName : "—";

            if (_planetNameText) _planetNameText.text = _activePlanetName;

            RebuildDots();
        }

        if (_currentPlanet == null) {
            _miniCam.enabled = false;
            return;
        }

        _miniCam.enabled = true;
        PositionCamera();
        UpdatePlayerDot();
        UpdateWaypointDots();
    }

    // ── Camera Setup ──────────────────────────────────────────────────────────

    void BuildCamera () {
        var go = new GameObject("PlanetMiniMapCamera");
        go.transform.SetParent(null);

        _miniCam                 = go.AddComponent<Camera>();
        _miniCam.clearFlags      = CameraClearFlags.SolidColor;
        _miniCam.backgroundColor = _bgColor;
        _miniCam.cullingMask     = ~_excludeLayers.value;
        _miniCam.fieldOfView     = _fov;
        _miniCam.nearClipPlane   = 0.5f;
        _miniCam.farClipPlane    = 5000f;
        // Render before the main camera so it doesn't appear on top of the scene.
        _miniCam.depth           = -10;
        _miniCam.enabled         = false;

        _rt                      = new RenderTexture(_texSize, _texSize, 16);
        _miniCam.targetTexture   = _rt;

        if (_mapDisplay) _mapDisplay.texture = _rt;
    }

    // ── Per-Frame Updates ─────────────────────────────────────────────────────

    void PositionCamera () {
        Vector3 center    = _currentPlanet.transform.position;
        Vector3 playerDir = (_car.transform.position - center);
        if (playerDir.sqrMagnitude < 0.001f) playerDir = Vector3.up;

        // Look at the planet from directly above the player's position on its surface.
        _miniCam.transform.position = center + playerDir.normalized * _orbitDistance;
        _miniCam.transform.LookAt(center, _car.transform.up);
    }

    void UpdatePlayerDot () {
        if (_playerDot == null || _mapDisplay == null) return;

        Vector3 vp      = _miniCam.WorldToViewportPoint(_car.transform.position);
        bool    visible = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;

        _playerDot.gameObject.SetActive(visible);
        if (!visible) return;

        Rect r = _mapDisplay.rectTransform.rect;
        _playerDot.anchoredPosition = new Vector2(
            (vp.x - 0.5f) * r.width,
            (vp.y - 0.5f) * r.height);
    }

    // ── Dot Management ────────────────────────────────────────────────────────

    void RebuildDots () {
        foreach (var d in _dots)
            if (d) Destroy(d.gameObject);
        _dots.Clear();

        if (_currentPlanet == null || _waypointDotPrefab == null || _dotContainer == null) return;

        foreach (var path in _currentPlanet.Tracks) {
            if (path == null) continue;
            foreach (var wp in path.Waypoints) {
                if (!wp) continue;
                var go = Instantiate(_waypointDotPrefab, _dotContainer);
                var rt = go.GetComponent<RectTransform>();
                if (rt) _dots.Add(rt);
            }
        }
    }

    void UpdateWaypointDots () {
        if (_currentPlanet == null || _mapDisplay == null) return;

        int idx = 0;
        Rect r  = _mapDisplay.rectTransform.rect;

        foreach (var path in _currentPlanet.Tracks) {
            if (path == null) continue;
            foreach (var wp in path.Waypoints) {
                if (!wp || idx >= _dots.Count) continue;

                var  dot     = _dots[idx++];
                if (!dot) continue;

                Vector3 vp      = _miniCam.WorldToViewportPoint(wp.transform.position);
                bool    visible = vp.z > 0f;

                dot.gameObject.SetActive(visible);
                if (!visible) continue;

                dot.anchoredPosition = new Vector2(
                    (vp.x - 0.5f) * r.width,
                    (vp.y - 0.5f) * r.height);
            }
        }
    }
}
