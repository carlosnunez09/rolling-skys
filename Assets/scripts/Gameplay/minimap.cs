using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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

    [BoxGroup("References"), SerializeField]
    RaceRuntime _race;

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

    [BoxGroup("UI"), SerializeField, Label("Dot Color (Pre-Race)")]
    Color _dotPreRaceColor = Color.white;

    [BoxGroup("UI"), SerializeField, Label("Dot Color (Racing)")]
    Color _dotRacingColor = new Color(1f, 0.75f, 0f, 1f);

    [BoxGroup("UI"), SerializeField, Range(0f, 1f), Label("Dot Alpha")]
    float _dotAlpha = 0.75f;


    // Assign any circle sprite (UI > Default Sprite is fine) to make the map round.
    // The script adds a Mask to the direct parent of Map Display at runtime.
    [BoxGroup("UI"), SerializeField, Label("Circle Mask Sprite")]
    Sprite _circleMaskSprite;

    // ── Camera ────────────────────────────────────────────────────────────────

    [BoxGroup("Camera"), SerializeField, Range(10f, 1000f), Label("Orbit Distance")]
    float _orbitDistance = 120f;

    [BoxGroup("Camera"), SerializeField, Min(10f), Label("Ortho Size A")]
    float _orthoSizeA = 730f;

    [BoxGroup("Camera"), SerializeField, Min(10f), Label("Ortho Size B")]
    float _orthoSizeB = 400f;

    [BoxGroup("Camera"), SerializeField, Range(0.5f, 20f), Label("Zoom Lerp Speed")]
    float _zoomLerpSpeed = 3f;

    [BoxGroup("Camera"), SerializeField, Range(64, 512), Label("Render Texture Size")]
    int _texSize = 256;

    [BoxGroup("Camera"), SerializeField, Label("Background Color")]
    Color _bgColor = new Color(0.02f, 0.02f, 0.06f, 1f);

    // Tick any layer here to hide it from the minimap (clouds, atmosphere, etc.)
    [BoxGroup("Camera"), SerializeField, Label("Exclude Layers")]
    LayerMask _excludeLayers = 0;

    [BoxGroup("Camera"), SerializeField, Range(1f, 20f), Label("Rotation Smoothing")]
    float _rotSmoothing = 5f;

    // ── Stats ─────────────────────────────────────────────────────────────────

    [BoxGroup("Stats"), SerializeField, ReadOnly]
    string _activePlanetName;

    // ── Private State ─────────────────────────────────────────────────────────

    Camera                       _miniCam;
    RenderTexture                _rt;
    Planet                       _currentPlanet;
    readonly List<RectTransform> _dots = new List<RectTransform>();

    Quaternion            _smoothedCamRot;
    Vector3               _smoothedCamPos;
    Vector3               _smoothedCamUp;
    bool                  _camInitialized;
    RaceRuntime.RacePhase _lastPhase = RaceRuntime.RacePhase.WaitingToStart;

    float _currentOrthoSize;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake () {
        SetupCircleMask();
        BuildCamera();
    }

    void SetupCircleMask () {
        if (_circleMaskSprite == null || _mapDisplay == null) return;

        Transform parent = _mapDisplay.transform.parent;
        if (parent == null || parent.GetComponent<Mask>() != null) return;

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
        }

        if (_currentPlanet == null) {
            _miniCam.enabled = false;
            return;
        }

        // Detect race phase changes to rebuild/recolour dots
        if (_race != null) {
            RaceRuntime.RacePhase phase = _race.Phase;
            if (phase != _lastPhase) {
                if (phase == RaceRuntime.RacePhase.Countdown)
                    RebuildDots();
                else if (phase == RaceRuntime.RacePhase.WaitingToStart)
                    ClearDots();

                UpdateDotColors();
                _lastPhase = phase;
            }
        }

        _miniCam.enabled = true;
        PositionCamera();
        UpdateOrthoZoom();
        UpdatePlayerDot();
        UpdateWaypointDots();
    }

    // ── Camera Setup ──────────────────────────────────────────────────────────

    void BuildCamera () {
        var go = new GameObject("PlanetMiniMapCamera");
        go.transform.SetParent(null);

        _currentOrthoSize = _orthoSizeA;

        _miniCam                   = go.AddComponent<Camera>();
        _miniCam.clearFlags        = CameraClearFlags.SolidColor;
        _miniCam.backgroundColor   = _bgColor;
        _miniCam.orthographic      = true;
        _miniCam.orthographicSize  = _orthoSizeA;
        _miniCam.nearClipPlane     = 0.5f;
        _miniCam.farClipPlane      = 5000f;
        _miniCam.depth             = -10;
        _miniCam.enabled           = false;

        // Build culling mask: start from the inspector exclusions, then also strip the Cloude layer.
        int mask        = ~_excludeLayers.value;
        int cloudeLayer = LayerMask.NameToLayer("cloude");
        if (cloudeLayer >= 0) mask &= ~(1 << cloudeLayer);
        _miniCam.cullingMask = mask;

        // Disable shadow rendering for this camera via URP camera data.
        var urpData = go.AddComponent<UniversalAdditionalCameraData>();
        urpData.renderShadows = false;

        _rt                      = new RenderTexture(_texSize, _texSize, 16);
        _miniCam.targetTexture   = _rt;

        if (_mapDisplay) _mapDisplay.texture = _rt;
    }

    // ── Per-Frame Updates ─────────────────────────────────────────────────────

    void PositionCamera () {
        Vector3 center    = _currentPlanet.transform.position;
        Vector3 playerDir = (_car.transform.position - center);
        if (playerDir.sqrMagnitude < 0.001f) playerDir = Vector3.up;
        playerDir = playerDir.normalized;

        Vector3 targetCamPos = center + playerDir * _orbitDistance;
        Vector3 rawCamUp     = _car.transform.forward;
        if (rawCamUp.sqrMagnitude < 0.001f) rawCamUp = Vector3.up;

        if (!_camInitialized) {
            _smoothedCamPos = targetCamPos;
            _smoothedCamUp  = rawCamUp;
            _smoothedCamRot = Quaternion.LookRotation(center - targetCamPos, rawCamUp);
            _camInitialized = true;
        } else {
            float t = _rotSmoothing * Time.deltaTime;

            // Smooth position separately — eliminates physics-jitter shake.
            _smoothedCamPos = Vector3.Lerp(_smoothedCamPos, targetCamPos, t);

            // Smooth the up vector before passing it to LookRotation so that
            // rapid steering/bouncing can't spike the target rotation.
            _smoothedCamUp = Vector3.Slerp(_smoothedCamUp, rawCamUp, t);

            Quaternion targetRot = Quaternion.LookRotation(center - _smoothedCamPos, _smoothedCamUp);
            _smoothedCamRot = Quaternion.Slerp(_smoothedCamRot, targetRot, t);
        }

        _miniCam.transform.SetPositionAndRotation(_smoothedCamPos, _smoothedCamRot);
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

    void UpdateOrthoZoom () {
        float speedRatio      = _car.MaxSpeed > 0f ? Mathf.Clamp01(_car.Speed / _car.MaxSpeed) : 0f;
        float targetSize      = Mathf.Lerp(_orthoSizeA, _orthoSizeB, speedRatio);
        _currentOrthoSize     = Mathf.Lerp(_currentOrthoSize, targetSize, _zoomLerpSpeed * Time.deltaTime);
        _miniCam.orthographicSize = _currentOrthoSize;
    }

    // ── Dot Management ────────────────────────────────────────────────────────

    void RebuildDots () {
        ClearDots();

        WaypointPath path = _race != null ? _race.ActivePath : null;
        if (path == null || _waypointDotPrefab == null || _dotContainer == null) return;

        foreach (var wp in path.Waypoints) {
            if (!wp) continue;
            var go = Instantiate(_waypointDotPrefab, _dotContainer);
            var rt = go.GetComponent<RectTransform>();
            if (rt) _dots.Add(rt);
        }
    }

    void ClearDots () {
        foreach (var d in _dots)
            if (d) Destroy(d.gameObject);
        _dots.Clear();
    }

    void UpdateDotColors () {
        bool  racing  = _race != null && _race.Phase == RaceRuntime.RacePhase.Racing;
        Color base_   = racing ? _dotRacingColor : _dotPreRaceColor;
        Color col     = new Color(base_.r, base_.g, base_.b, base_.a * _dotAlpha);
        foreach (var dot in _dots) {
            if (!dot) continue;
            var img = dot.GetComponent<Image>();
            if (img) img.color = col;
        }
    }

    void UpdateWaypointDots () {
        if (_mapDisplay == null || _dots.Count == 0) return;

        WaypointPath path = _race != null ? _race.ActivePath : null;
        if (path == null) return;

        // Plane through the planet centre, normal = car's surface normal.
        // A waypoint's signed distance from this plane tells us which side it's on.
        Vector3 planetCenter = _currentPlanet.transform.position;
        Vector3 planeNormal  = (_car.transform.position - planetCenter).normalized;

        bool  racing = _race != null && _race.Phase == RaceRuntime.RacePhase.Racing;
        Color base_  = racing ? _dotRacingColor : _dotPreRaceColor;

        int  idx = 0;
        Rect r   = _mapDisplay.rectTransform.rect;

        foreach (var wp in path.Waypoints) {
            if (!wp || idx >= _dots.Count) continue;

            var dot = _dots[idx++];
            if (!dot) continue;

            // Signed distance from the car's equatorial plane.
            // Positive = same side as the car, negative = opposite side.
            float signedDist  = Vector3.Dot(wp.transform.position - planetCenter, planeNormal);
            float planeAlpha  = Mathf.Clamp01(signedDist / _currentPlanet.DotFadeDistance);

            if (planeAlpha <= 0f) { dot.gameObject.SetActive(false); continue; }

            Vector3 vp = _miniCam.WorldToViewportPoint(wp.transform.position);
            dot.gameObject.SetActive(vp.z > 0f);
            if (vp.z <= 0f) continue;

            dot.anchoredPosition = new Vector2(
                (vp.x - 0.5f) * r.width,
                (vp.y - 0.5f) * r.height);

            var img = dot.GetComponent<Image>();
            if (img) img.color = new Color(base_.r, base_.g, base_.b,
                                           base_.a * _dotAlpha * planeAlpha);
        }
    }
}
