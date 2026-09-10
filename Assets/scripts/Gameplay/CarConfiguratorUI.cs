using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// In-depth live car configurator UI.
/// Allows tweaking all 55+ physics and dynamics parameters of CarDataSO in real time
/// with full Gamepad / Controller support (D-Pad navigation, Left/Right slider tuning, LB/RB tab cycling).
/// </summary>
public class CarConfiguratorUI : MonoBehaviour {

    public static CarConfiguratorUI Instance { get; private set; }

    [Header("Runtime State")]
    CarDataSO _profile;
    CarDataSO _initialSnapshot;
    MovingCar _boundCar;
    Action _onCloseCallback;

    int _currentTab = 0;
    readonly string[] _tabNames = new string[] {
        "Engine",
        "Gears",
        "Steering",
        "Grip & Drift",
        "Aero & Air",
        "Suspension",
        "Visuals"
    };

    [Header("UI Structure")]
    GameObject _panelRoot;
    TMP_Text _titleText;
    TMP_Text _tabHintText;
    Transform _tabsBar;
    Transform _contentArea;
    TMP_Text _footerStatusText;

    readonly List<Button> _tabButtons = new List<Button>();
    readonly List<GameObject> _tabPanels = new List<GameObject>();
    readonly List<Selectable> _tabFirstSelectable = new List<Selectable>();

    Button _saveAsNewButton;
    Button _resetButton;
    Button _doneButton;

    GameObject _lastSelected;

    public bool IsOpen => _panelRoot != null && _panelRoot.activeSelf;

    void Awake () {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy () {
        if (Instance == this)
            Instance = null;
    }

    void Update () {
        if (!IsOpen) return;

        // Controller / Keyboard Shortcuts
        if (Gamepad.current != null) {
            var pad = Gamepad.current;

            // Close with B / East button or Start button
            if (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) {
                Close();
                return;
            }

            // Tab navigation with bumpers (LB / RB)
            if (pad.leftShoulder.wasPressedThisFrame) {
                SwitchTab((_currentTab - 1 + _tabNames.Length) % _tabNames.Length);
            } else if (pad.rightShoulder.wasPressedThisFrame) {
                SwitchTab((_currentTab + 1) % _tabNames.Length);
            }
        }

        if (Keyboard.current != null) {
            if (Keyboard.current.escapeKey.wasPressedThisFrame) {
                Close();
                return;
            }
            if (Keyboard.current.qKey.wasPressedThisFrame) {
                SwitchTab((_currentTab - 1 + _tabNames.Length) % _tabNames.Length);
            } else if (Keyboard.current.eKey.wasPressedThisFrame) {
                SwitchTab((_currentTab + 1) % _tabNames.Length);
            }
        }

        HandleControllerFocus();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the configurator for the specified profile and car.
    /// Procedurally creates the entire UI if not yet present in scene.
    /// </summary>
    public void Open (CarDataSO profile, MovingCar boundCar, Action onClose = null) {
        _profile = profile;
        _boundCar = boundCar;
        _onCloseCallback = onClose;

        if (_profile != null)
            _initialSnapshot = _profile.Clone();

        EnsureUiHierarchy();

        if (_panelRoot != null)
            _panelRoot.SetActive(true);

        // Keep car inputs disabled during configuration
        _boundCar?.SetInputEnabled(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UpdateTitle();
        SwitchTab(_currentTab);
    }

    public void Close () {
        if (_panelRoot != null)
            _panelRoot.SetActive(false);

        _boundCar?.SetInputEnabled(true);

        Action cb = _onCloseCallback;
        _onCloseCallback = null;
        cb?.Invoke();
    }

    public void SwitchTab (int index) {
        if (index < 0 || index >= _tabPanels.Count) return;
        _currentTab = index;

        for (int i = 0; i < _tabPanels.Count; i++) {
            bool active = (i == _currentTab);
            if (_tabPanels[i] != null)
                _tabPanels[i].SetActive(active);

            if (i < _tabButtons.Count && _tabButtons[i] != null) {
                ColorBlock cb = _tabButtons[i].colors;
                cb.normalColor = active ? new Color(0.12f, 0.55f, 0.85f, 1f) : new Color(0.15f, 0.18f, 0.26f, 0.9f);
                _tabButtons[i].colors = cb;
            }
        }

        if (_tabHintText != null) {
            _tabHintText.text = $"[LB] Prev Tab  •  <color=#1FE0FF><b>{_tabNames[_currentTab]}</b></color> ({_currentTab + 1}/{_tabNames.Length})  •  [RB] Next Tab";
        }

        // Set focus to the first selectable control in this tab
        if (EventSystem.current != null && _currentTab < _tabFirstSelectable.Count) {
            Selectable first = _tabFirstSelectable[_currentTab];
            if (first != null && first.gameObject.activeInHierarchy) {
                EventSystem.current.SetSelectedGameObject(first.gameObject);
                _lastSelected = first.gameObject;
            }
        }
    }

    public void SaveAsNewProfile () {
        if (_profile == null) return;

        CarDataSO newProfile = _profile.Clone();
        string baseName = string.IsNullOrEmpty(_profile.profileName) ? _profile.name : _profile.profileName;
        newProfile.name = $"{baseName} (Tuned)";
        newProfile.profileName = $"{baseName} (Tuned)";
        newProfile.description = $"Custom tune created via in-game car configurator on {DateTime.Now:g}.";

        _profile = newProfile;
        _boundCar?.ApplyProfile(_profile);
        UpdateTitle();

        if (_footerStatusText != null)
            _footerStatusText.text = $"<color=#2EB85C>Saved & Equipped:</color> {newProfile.profileName}";
    }

    public void ResetToDefaults () {
        if (_profile == null || _initialSnapshot == null) return;

        // Copy snapshot values back into active profile
        CopyProfileData(_initialSnapshot, _profile);
        _boundCar?.ApplyProfile(_profile);

        // Rebuild or refresh UI values
        RebuildAllTabs();
        SwitchTab(_currentTab);

        if (_footerStatusText != null)
            _footerStatusText.text = "<color=#FFAA00>Settings reverted to initial snapshot.</color>";
    }

    // ── UI Construction ────────────────────────────────────────────────────────

    void EnsureUiHierarchy () {
        if (_panelRoot != null) {
            RebuildAllTabs();
            return;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) {
            GameObject canvasObj = new GameObject("CarConfiguratorCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 99;
            CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        // Dark backdrop
        GameObject root = new GameObject("CarConfiguratorUI", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.sizeDelta = Vector2.zero;
        Image bgImg = root.GetComponent<Image>();
        bgImg.color = new Color(0.03f, 0.05f, 0.08f, 0.94f);

        // Main Panel (Center)
        GameObject panelObj = new GameObject("MainConfigPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObj.transform.SetParent(root.transform, false);
        RectTransform panelRt = panelObj.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(1000f, 680f);
        Image panelImg = panelObj.GetComponent<Image>();
        panelImg.color = new Color(0.08f, 0.11f, 0.16f, 0.98f);

        // Title Bar
        GameObject titleObj = new GameObject("TitleText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleObj.transform.SetParent(panelObj.transform, false);
        RectTransform titleRt = titleObj.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.anchoredPosition = new Vector2(0f, -16f);
        titleRt.sizeDelta = new Vector2(-40f, 36f);

        _titleText = titleObj.GetComponent<TextMeshProUGUI>();
        _titleText.fontSize = 24;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.color = Color.white;
        _titleText.text = "CAR TUNING & PHYSICS CONFIGURATOR";

        // Tab Bumpers / Subtitle
        GameObject tabHintObj = new GameObject("TabHintText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        tabHintObj.transform.SetParent(panelObj.transform, false);
        RectTransform tabHintRt = tabHintObj.GetComponent<RectTransform>();
        tabHintRt.anchorMin = new Vector2(0f, 1f);
        tabHintRt.anchorMax = new Vector2(1f, 1f);
        tabHintRt.pivot = new Vector2(0.5f, 1f);
        tabHintRt.anchoredPosition = new Vector2(0f, -54f);
        tabHintRt.sizeDelta = new Vector2(-40f, 24f);

        _tabHintText = tabHintObj.GetComponent<TextMeshProUGUI>();
        _tabHintText.fontSize = 14;
        _tabHintText.alignment = TextAlignmentOptions.Center;
        _tabHintText.color = new Color(0.65f, 0.78f, 0.9f);

        // Tab Selector Buttons Bar
        GameObject tabsBarObj = new GameObject("TabsBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(HorizontalLayoutGroup));
        tabsBarObj.transform.SetParent(panelObj.transform, false);
        RectTransform tabsBarRt = tabsBarObj.GetComponent<RectTransform>();
        tabsBarRt.anchorMin = new Vector2(0f, 1f);
        tabsBarRt.anchorMax = new Vector2(1f, 1f);
        tabsBarRt.pivot = new Vector2(0.5f, 1f);
        tabsBarRt.anchoredPosition = new Vector2(0f, -84f);
        tabsBarRt.sizeDelta = new Vector2(-40f, 38f);

        HorizontalLayoutGroup tabsHlg = tabsBarObj.GetComponent<HorizontalLayoutGroup>();
        tabsHlg.spacing = 8;
        tabsHlg.childAlignment = TextAnchor.MiddleCenter;
        tabsHlg.childControlWidth = true;
        tabsHlg.childControlHeight = true;
        _tabsBar = tabsBarObj.transform;

        // Content Area (Scrollable / Tab container)
        GameObject contentAreaObj = new GameObject("ContentArea", typeof(RectTransform), typeof(CanvasRenderer));
        contentAreaObj.transform.SetParent(panelObj.transform, false);
        RectTransform contentRt = contentAreaObj.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 0f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.offsetMin = new Vector2(30f, 85f);
        contentRt.offsetMax = new Vector2(-30f, -135f);
        _contentArea = contentAreaObj.transform;

        // Bottom Action Bar
        GameObject bottomBar = new GameObject("BottomBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(HorizontalLayoutGroup));
        bottomBar.transform.SetParent(panelObj.transform, false);
        RectTransform btmRt = bottomBar.GetComponent<RectTransform>();
        btmRt.anchorMin = new Vector2(0f, 0f);
        btmRt.anchorMax = new Vector2(1f, 0f);
        btmRt.pivot = new Vector2(0.5f, 0f);
        btmRt.anchoredPosition = new Vector2(0f, 34f);
        btmRt.sizeDelta = new Vector2(-60f, 40f);

        HorizontalLayoutGroup btmHlg = bottomBar.GetComponent<HorizontalLayoutGroup>();
        btmHlg.spacing = 16;
        btmHlg.childAlignment = TextAnchor.MiddleCenter;
        btmHlg.childControlWidth = true;
        btmHlg.childControlHeight = true;

        _saveAsNewButton = CreateButton(bottomBar.transform, "SaveNewBtn", "+ Save As New Profile", new Color(0.14f, 0.42f, 0.28f));
        _resetButton = CreateButton(bottomBar.transform, "ResetBtn", "↺ Reset Defaults", new Color(0.48f, 0.28f, 0.12f));
        _doneButton = CreateButton(bottomBar.transform, "DoneBtn", "✔ Done / Back [B]", new Color(0.18f, 0.25f, 0.38f));

        _saveAsNewButton.onClick.AddListener(SaveAsNewProfile);
        _resetButton.onClick.AddListener(ResetToDefaults);
        _doneButton.onClick.AddListener(Close);

        // Footer Hint / Status Bar
        GameObject footerObj = new GameObject("FooterStatusText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        footerObj.transform.SetParent(panelObj.transform, false);
        RectTransform footerRt = footerObj.GetComponent<RectTransform>();
        footerRt.anchorMin = new Vector2(0f, 0f);
        footerRt.anchorMax = new Vector2(1f, 0f);
        footerRt.pivot = new Vector2(0.5f, 0f);
        footerRt.anchoredPosition = new Vector2(0f, 8f);
        footerRt.sizeDelta = new Vector2(-40f, 22f);

        _footerStatusText = footerObj.GetComponent<TextMeshProUGUI>();
        _footerStatusText.fontSize = 12;
        _footerStatusText.alignment = TextAlignmentOptions.Center;
        _footerStatusText.color = new Color(0.55f, 0.65f, 0.75f);
        _footerStatusText.text = "🎮 [D-Pad Up/Down] Select Parameter  •  [D-Pad Left/Right] Adjust Value  •  [LB/RB] Switch Tabs  •  [B] Back";

        _panelRoot = root;

        RebuildAllTabs();
    }

    void RebuildAllTabs () {
        if (_tabsBar == null || _contentArea == null || _profile == null) return;

        // Clear existing tabs
        foreach (Transform child in _tabsBar) Destroy(child.gameObject);
        foreach (Transform child in _contentArea) Destroy(child.gameObject);

        _tabButtons.Clear();
        _tabPanels.Clear();
        _tabFirstSelectable.Clear();

        // Build each tab button & content panel
        for (int i = 0; i < _tabNames.Length; i++) {
            int tabIdx = i;
            string tabName = _tabNames[i];

            // Tab button
            Button tabBtn = CreateButton(_tabsBar, $"Tab_{tabName}", tabName, new Color(0.15f, 0.18f, 0.26f));
            tabBtn.onClick.AddListener(() => SwitchTab(tabIdx));
            _tabButtons.Add(tabBtn);

            // Tab Content Panel
            GameObject panel = new GameObject($"Panel_{tabName}", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(_contentArea, false);
            RectTransform pRt = panel.GetComponent<RectTransform>();
            pRt.anchorMin = Vector2.zero;
            pRt.anchorMax = Vector2.one;
            pRt.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = panel.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.padding = new RectOffset(10, 10, 10, 10);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            _tabPanels.Add(panel);

            // Populate settings rows for this tab
            List<Selectable> rowSelectables = new List<Selectable>();
            PopulateTab(tabIdx, panel.transform, rowSelectables);

            // Store first selectable for controller auto-focus
            _tabFirstSelectable.Add(rowSelectables.Count > 0 ? rowSelectables[0] : tabBtn);

            // Link vertical navigation between rows within the tab, then to bottom buttons
            SetupTabNavigation(tabBtn, rowSelectables);

            panel.SetActive(false);
        }
    }

    void PopulateTab (int tabIdx, Transform parent, List<Selectable> selectables) {
        switch (tabIdx) {
            case 0: // Engine & Speed
                AddSlider(parent, selectables, "Top Speed (Forward)", 0f, 250f, 1f, () => _profile.topSpeed, v => _profile.topSpeed = v, "km/h");
                AddSlider(parent, selectables, "Max Speed (Cruising)", 0f, 150f, 1f, () => _profile.maxSpeed, v => _profile.maxSpeed = v, "km/h");
                AddSlider(parent, selectables, "Max Reverse Speed", 0f, 60f, 1f, () => _profile.maxReverseSpeed, v => _profile.maxReverseSpeed = v, "km/h");
                AddSlider(parent, selectables, "Acceleration Force", 0f, 300f, 2f, () => _profile.acceleration, v => _profile.acceleration = v);
                AddSlider(parent, selectables, "Brake Force", 0f, 300f, 5f, () => _profile.brakeForce, v => _profile.brakeForce = v);
                AddSlider(parent, selectables, "Coast Deceleration", 0f, 50f, 0.5f, () => _profile.coastDeceleration, v => _profile.coastDeceleration = v);
                AddSlider(parent, selectables, "Overdrive Force", 0f, 100f, 1f, () => _profile.overdriveForce, v => _profile.overdriveForce = v);
                break;

            case 1: // Transmission & Gears
                AddToggle(parent, selectables, "Enable Transmission Gears", () => _profile.useGears, v => _profile.useGears = v);
                AddSlider(parent, selectables, "Gear Count", 1f, 8f, 1f, () => _profile.gearCount, v => _profile.gearCount = Mathf.RoundToInt(v), "Gears", true);
                AddSlider(parent, selectables, "Shift Up RPM", 2000f, 9000f, 100f, () => _profile.shiftUpRPM, v => _profile.shiftUpRPM = v, "RPM", true);
                AddSlider(parent, selectables, "Shift Down RPM", 1000f, 5000f, 100f, () => _profile.shiftDownRPM, v => _profile.shiftDownRPM = v, "RPM", true);
                AddSlider(parent, selectables, "Idle RPM", 600f, 1500f, 50f, () => _profile.idleRPM, v => _profile.idleRPM = v, "RPM", true);
                AddSlider(parent, selectables, "Redline RPM", 5000f, 10000f, 100f, () => _profile.redlineRPM, v => _profile.redlineRPM = v, "RPM", true);
                AddSlider(parent, selectables, "Shift Delay Duration", 0.01f, 0.50f, 0.01f, () => _profile.shiftDelay, v => _profile.shiftDelay = v, "s");
                AddSlider(parent, selectables, "1st Gear Torque Boost", 0.50f, 2.00f, 0.05f, () => _profile.firstGearTorqueBoost, v => _profile.firstGearTorqueBoost = v, "x");
                break;

            case 2: // Steering & Wheels
                AddSlider(parent, selectables, "Min Turning Radius", 1.0f, 30.0f, 0.5f, () => _profile.minTurningRadius, v => _profile.minTurningRadius = v, "m");
                AddSlider(parent, selectables, "Max Steering Angle Lock", 15f, 60f, 1f, () => _profile.maxSteerAngle, v => _profile.maxSteerAngle = v, "°", true);
                AddSlider(parent, selectables, "Steering Sensitivity", 0.5f, 5.0f, 0.1f, () => _profile.steerSensitivity, v => _profile.steerSensitivity = v, "x");
                AddSlider(parent, selectables, "Speed Steer Falloff", 0.00f, 1.00f, 0.05f, () => _profile.speedSteerFalloff, v => _profile.speedSteerFalloff = v);
                AddSlider(parent, selectables, "Wheel Spread (Half Track)", 0.10f, 2.00f, 0.05f, () => _profile.wheelSpread, v => _profile.wheelSpread = v, "m");
                AddSlider(parent, selectables, "Wheel Base (Axle Spread)", 0.50f, 4.00f, 0.05f, () => _profile.wheelBase, v => _profile.wheelBase = v, "m");
                AddSlider(parent, selectables, "Axle Height Offset", -2.00f, 0.00f, 0.05f, () => _profile.axleHeightOffset, v => _profile.axleHeightOffset = v, "m");
                break;

            case 3: // Grip & Drift
                AddSlider(parent, selectables, "Lateral Grip (Normal)", 0.00f, 1.00f, 0.02f, () => _profile.lateralGrip, v => _profile.lateralGrip = v);
                AddSlider(parent, selectables, "Drift Grip (Slide)", 0.00f, 0.20f, 0.01f, () => _profile.driftGrip, v => _profile.driftGrip = v);
                AddSlider(parent, selectables, "Drift Yaw Multiplier", 1.0f, 4.0f, 0.1f, () => _profile.driftYawMultiplier, v => _profile.driftYawMultiplier = v, "x");
                AddSlider(parent, selectables, "Max Drift Angle", 0f, 60f, 1f, () => _profile.maxDriftAngle, v => _profile.maxDriftAngle = v, "°", true);
                AddSlider(parent, selectables, "Drift Angle Rate", 1f, 30f, 1f, () => _profile.driftAngleRate, v => _profile.driftAngleRate = v, "°/s", true);
                AddSlider(parent, selectables, "Counter Steer Authority", 0.1f, 2.0f, 0.1f, () => _profile.counterSteerAuthority, v => _profile.counterSteerAuthority = v, "x");
                AddSlider(parent, selectables, "Mini-Turbo Impulse Boost", 0f, 40f, 1f, () => _profile.miniTurboImpulse, v => _profile.miniTurboImpulse = v);
                AddSlider(parent, selectables, "Mini-Turbo Charge Time", 0.3f, 3.0f, 0.1f, () => _profile.miniTurboChargeTime, v => _profile.miniTurboChargeTime = v, "s");
                AddSlider(parent, selectables, "Yaw Inertia Smooth Rate", 5f, 60f, 1f, () => _profile.yawInertiaSmoothRate, v => _profile.yawInertiaSmoothRate = v);
                break;

            case 4: // Aero & Air
                AddSlider(parent, selectables, "Downforce Strength", 0f, 150f, 2f, () => _profile.downforceStrength, v => _profile.downforceStrength = v);
                AddSlider(parent, selectables, "Curvature Adhesion", 0f, 60f, 1f, () => _profile.curvatureAdhesion, v => _profile.curvatureAdhesion = v);
                AddSlider(parent, selectables, "Min Adhesion Velocity", 0.1f, 5.0f, 0.1f, () => _profile.minAdhesionVelocity, v => _profile.minAdhesionVelocity = v, "m/s");
                AddSlider(parent, selectables, "Air Pitch Speed", 0f, 180f, 5f, () => _profile.airPitchSpeed, v => _profile.airPitchSpeed = v, "°/s", true);
                AddSlider(parent, selectables, "Air Yaw Speed", 0f, 180f, 5f, () => _profile.airYawSpeed, v => _profile.airYawSpeed = v, "°/s", true);
                AddSlider(parent, selectables, "Air Roll Speed", 0f, 180f, 5f, () => _profile.airRollSpeed, v => _profile.airRollSpeed = v, "°/s", true);
                AddSlider(parent, selectables, "Air Auto-Righting Speed", 0.5f, 15.0f, 0.5f, () => _profile.airAutoRightSpeed, v => _profile.airAutoRightSpeed = v);
                AddSlider(parent, selectables, "Air Angular Damping", 0.5f, 20.0f, 0.5f, () => _profile.airAngularDamping, v => _profile.airAngularDamping = v);
                AddToggle(parent, selectables, "Can Deploy Glider", () => _profile.canGlide, v => _profile.canGlide = v);
                AddSlider(parent, selectables, "Glide Fall Speed", 1.0f, 10.0f, 0.2f, () => _profile.glideFallSpeed, v => _profile.glideFallSpeed = v, "m/s");
                AddSlider(parent, selectables, "Glide Forward Speed", 10f, 80f, 1f, () => _profile.glideForwardSpeed, v => _profile.glideForwardSpeed = v, "m/s", true);
                break;

            case 5: // Suspension & Ground
                AddSlider(parent, selectables, "Max Ground Surface Angle", 0f, 90f, 2f, () => _profile.maxGroundAngle, v => _profile.maxGroundAngle = v, "°", true);
                AddSlider(parent, selectables, "Max Ground Snap Speed", 0f, 100f, 2f, () => _profile.maxSnapSpeed, v => _profile.maxSnapSpeed = v);
                AddSlider(parent, selectables, "Min Surface Speed Multiplier", 0.10f, 1.00f, 0.05f, () => _profile.minSurfaceSpeedMultiplier, v => _profile.minSurfaceSpeedMultiplier = v);
                AddSlider(parent, selectables, "Surface Transition Speed", 1f, 30f, 1f, () => _profile.surfaceTransitionSpeed, v => _profile.surfaceTransitionSpeed = v);
                AddSlider(parent, selectables, "Min Jump Height", 0.5f, 5.0f, 0.1f, () => _profile.minJumpHeight, v => _profile.minJumpHeight = v, "m");
                AddSlider(parent, selectables, "Max Jump Height", 1.0f, 10.0f, 0.2f, () => _profile.maxJumpHeight, v => _profile.maxJumpHeight = v, "m");
                AddSlider(parent, selectables, "Max Jump Hold Duration", 0.10f, 0.60f, 0.02f, () => _profile.maxJumpHoldDuration, v => _profile.maxJumpHoldDuration = v, "s");
                AddSlider(parent, selectables, "Jump Cooldown Delay", 0.05f, 0.50f, 0.02f, () => _profile.jumpCooldown, v => _profile.jumpCooldown = v, "s");
                AddSlider(parent, selectables, "Max Slip Yaw Rate", 0f, 720f, 10f, () => _profile.maxSlipYawRate, v => _profile.maxSlipYawRate = v, "°/s", true);
                AddSlider(parent, selectables, "Slip Recovery Rate", 0.0f, 10.0f, 0.2f, () => _profile.slipRecoveryRate, v => _profile.slipRecoveryRate = v);
                break;

            case 6: // Visuals & Skid Marks
                AddSlider(parent, selectables, "Skid Mark Width", 0.05f, 0.50f, 0.01f, () => _profile.markWidth, v => _profile.markWidth = v, "m");
                AddSlider(parent, selectables, "Skid Fade Out Time", 1.0f, 30.0f, 0.5f, () => _profile.fadeTime, v => _profile.fadeTime = v, "s");
                AddSlider(parent, selectables, "Min Skid Segment Length", 0.02f, 1.00f, 0.02f, () => _profile.minSegmentLength, v => _profile.minSegmentLength = v, "m");
                AddSlider(parent, selectables, "Min Skid Lateral Speed", 0.0f, 5.0f, 0.1f, () => _profile.minSkidLateralSpeed, v => _profile.minSkidLateralSpeed = v, "m/s");
                AddSlider(parent, selectables, "Skid Ground Height Offset", 0.000f, 0.050f, 0.002f, () => _profile.skidGroundOffset, v => _profile.skidGroundOffset = v, "m");
                break;
        }
    }

    // ── Row Helper Widgets ─────────────────────────────────────────────────────

    void AddSlider (Transform parent, List<Selectable> selectables, string label, float min, float max, float step,
                    Func<float> getter, Action<float> setter, string unit = "", bool isInteger = false) {

        GameObject row = new GameObject($"Row_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        RectTransform rRt = row.GetComponent<RectTransform>();
        rRt.sizeDelta = new Vector2(940f, 32f);

        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;

        // Label
        GameObject lblObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lblObj.transform.SetParent(row.transform, false);
        lblObj.GetComponent<RectTransform>().sizeDelta = new Vector2(250f, 30f);
        TextMeshProUGUI lbl = lblObj.GetComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 14;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.color = new Color(0.85f, 0.88f, 0.95f);

        // Minus Button [-]
        Button minusBtn = CreateSmallButton(row.transform, "Minus", "-", new Color(0.2f, 0.24f, 0.32f), 30f);

        // Slider
        GameObject sliderObj = new GameObject("Slider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Slider));
        sliderObj.transform.SetParent(row.transform, false);
        RectTransform sRt = sliderObj.GetComponent<RectTransform>();
        sRt.sizeDelta = new Vector2(400f, 26f);

        Slider slider = sliderObj.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = isInteger;

        // Slider Background
        GameObject sBg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        sBg.transform.SetParent(sliderObj.transform, false);
        RectTransform bgRt = sBg.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.35f);
        bgRt.anchorMax = new Vector2(1f, 0.65f);
        bgRt.sizeDelta = Vector2.zero;
        sBg.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.22f);

        // Fill Area
        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.35f);
        faRt.anchorMax = new Vector2(1f, 0.65f);
        faRt.offsetMin = new Vector2(5f, 0f);
        faRt.offsetMax = new Vector2(-5f, 0f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fRt = fill.GetComponent<RectTransform>();
        fRt.sizeDelta = Vector2.zero;
        Image fillImg = fill.GetComponent<Image>();
        fillImg.color = new Color(0.18f, 0.65f, 0.92f);
        slider.fillRect = fRt;

        // Handle Area
        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderObj.transform, false);
        RectTransform haRt = handleArea.GetComponent<RectTransform>();
        haRt.anchorMin = Vector2.zero;
        haRt.anchorMax = Vector2.one;
        haRt.offsetMin = new Vector2(10f, 0f);
        haRt.offsetMax = new Vector2(-10f, 0f);

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform hRt = handle.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(18f, 22f);
        Image hImg = handle.GetComponent<Image>();
        hImg.color = Color.white;
        slider.handleRect = hRt;
        slider.targetGraphic = hImg;

        // Slider Colors
        ColorBlock scb = slider.colors;
        scb.normalColor = Color.white;
        scb.highlightedColor = new Color(0.2f, 0.85f, 1f);
        scb.selectedColor = new Color(0.12f, 0.92f, 1f);
        scb.pressedColor = new Color(0.1f, 0.5f, 0.8f);
        slider.colors = scb;

        // Value Label
        GameObject valObj = new GameObject("ValueText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        valObj.transform.SetParent(row.transform, false);
        valObj.GetComponent<RectTransform>().sizeDelta = new Vector2(110f, 30f);
        TextMeshProUGUI valText = valObj.GetComponent<TextMeshProUGUI>();
        valText.fontSize = 14;
        valText.alignment = TextAlignmentOptions.MidlineRight;
        valText.color = new Color(0.3f, 0.92f, 1f);

        // Plus Button [+]
        Button plusBtn = CreateSmallButton(row.transform, "Plus", "+", new Color(0.2f, 0.24f, 0.32f), 30f);

        // Value formatter
        Action updateValText = () => {
            float val = getter();
            if (isInteger)
                valText.text = $"{Mathf.RoundToInt(val)} {unit}".Trim();
            else if (step < 0.05f)
                valText.text = $"{val:F3} {unit}".Trim();
            else if (step < 0.5f)
                valText.text = $"{val:F2} {unit}".Trim();
            else
                valText.text = $"{val:F1} {unit}".Trim();
        };

        // Initialize slider value without triggering extra events
        slider.SetValueWithoutNotify(getter());
        updateValText();

        slider.onValueChanged.AddListener(v => {
            setter(v);
            _boundCar?.ApplyProfile(_profile);
            updateValText();
        });

        minusBtn.onClick.AddListener(() => {
            slider.value = Mathf.Max(min, slider.value - step);
        });

        plusBtn.onClick.AddListener(() => {
            slider.value = Mathf.Min(max, slider.value + step);
        });

        // The Slider is the primary selectable for Gamepad vertical navigation
        selectables.Add(slider);
    }

    void AddToggle (Transform parent, List<Selectable> selectables, string label, Func<bool> getter, Action<bool> setter) {
        GameObject row = new GameObject($"Toggle_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        RectTransform rRt = row.GetComponent<RectTransform>();
        rRt.sizeDelta = new Vector2(940f, 34f);

        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 15;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;

        // Label
        GameObject lblObj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        lblObj.transform.SetParent(row.transform, false);
        lblObj.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 30f);
        TextMeshProUGUI lbl = lblObj.GetComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 14;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lbl.color = new Color(0.85f, 0.88f, 0.95f);

        // Toggle Button
        Button toggleBtn = CreateButton(row.transform, "ToggleBtn", "ENABLED", new Color(0.14f, 0.42f, 0.28f));
        toggleBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(200f, 32f);
        TMP_Text btnText = toggleBtn.GetComponentInChildren<TMP_Text>(true);

        Action refreshToggleState = () => {
            bool state = getter();
            if (btnText != null)
                btnText.text = state ? "<color=#55FF88>ENABLED [A]</color>" : "<color=#FF6666>DISABLED [A]</color>";

            ColorBlock cb = toggleBtn.colors;
            cb.normalColor = state ? new Color(0.14f, 0.38f, 0.25f) : new Color(0.38f, 0.16f, 0.16f);
            cb.highlightedColor = new Color(0.2f, 0.8f, 1f);
            cb.selectedColor = new Color(0.15f, 0.9f, 1f);
            toggleBtn.colors = cb;
        };

        refreshToggleState();

        toggleBtn.onClick.AddListener(() => {
            setter(!getter());
            _boundCar?.ApplyProfile(_profile);
            refreshToggleState();
        });

        selectables.Add(toggleBtn);
    }

    void SetupTabNavigation (Button tabBtn, List<Selectable> rowSelectables) {
        if (rowSelectables == null || rowSelectables.Count == 0) return;

        for (int i = 0; i < rowSelectables.Count; i++) {
            Selectable curr = rowSelectables[i];
            Selectable up = (i == 0) ? (Selectable)tabBtn : rowSelectables[i - 1];
            Selectable down = (i == rowSelectables.Count - 1) ? (Selectable)_doneButton : rowSelectables[i + 1];

            Navigation nav = curr.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = up;
            nav.selectOnDown = down;
            curr.navigation = nav;
        }

        // Link tab button downwards to first row
        if (tabBtn != null) {
            Navigation tNav = tabBtn.navigation;
            tNav.mode = Navigation.Mode.Explicit;
            tNav.selectOnDown = rowSelectables[0];
            tabBtn.navigation = tNav;
        }
    }

    void HandleControllerFocus () {
        if (EventSystem.current == null) return;

        GameObject curr = EventSystem.current.currentSelectedGameObject;
        if (curr != null && curr.activeInHierarchy) {
            _lastSelected = curr;
            return;
        }

        bool hasNav = false;
        if (Gamepad.current != null) {
            var pad = Gamepad.current;
            hasNav = pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
                     pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame ||
                     pad.buttonSouth.wasPressedThisFrame ||
                     Mathf.Abs(pad.leftStick.y.ReadValue()) > 0.35f ||
                     Mathf.Abs(pad.leftStick.x.ReadValue()) > 0.35f;
        }

        if (Keyboard.current != null) {
            var kb = Keyboard.current;
            hasNav |= kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                      kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
        }

        if (hasNav) {
            GameObject target = (_lastSelected != null && _lastSelected.activeInHierarchy)
                ? _lastSelected
                : (_currentTab < _tabFirstSelectable.Count && _tabFirstSelectable[_currentTab] != null
                    ? _tabFirstSelectable[_currentTab].gameObject
                    : (_doneButton != null ? _doneButton.gameObject : null));

            if (target != null)
                EventSystem.current.SetSelectedGameObject(target);
        }
    }

    void UpdateTitle () {
        if (_titleText != null && _profile != null) {
            string name = string.IsNullOrEmpty(_profile.profileName) ? _profile.name : _profile.profileName;
            _titleText.text = $"CAR TUNER: <color=#1FE0FF>{name}</color> <size=16>({_profile.vehicleType})</size>";
        }
    }

    void CopyProfileData (CarDataSO src, CarDataSO dst) {
        if (src == null || dst == null) return;

        dst.maxSpeed                  = src.maxSpeed;
        dst.topSpeed                  = src.topSpeed;
        dst.maxReverseSpeed           = src.maxReverseSpeed;
        dst.acceleration              = src.acceleration;
        dst.brakeForce                = src.brakeForce;
        dst.coastDeceleration         = src.coastDeceleration;
        dst.overdriveForce            = src.overdriveForce;

        dst.useGears                  = src.useGears;
        dst.gearCount                 = src.gearCount;
        dst.shiftUpRPM                = src.shiftUpRPM;
        dst.shiftDownRPM              = src.shiftDownRPM;
        dst.idleRPM                   = src.idleRPM;
        dst.redlineRPM                = src.redlineRPM;
        dst.shiftDelay                = src.shiftDelay;
        dst.firstGearTorqueBoost      = src.firstGearTorqueBoost;

        dst.minTurningRadius          = src.minTurningRadius;
        dst.wheelSpread               = src.wheelSpread;
        dst.wheelBase                 = src.wheelBase;
        dst.axleHeightOffset          = src.axleHeightOffset;
        dst.maxSteerAngle             = src.maxSteerAngle;
        dst.steerSensitivity          = src.steerSensitivity;
        dst.speedSteerFalloff         = src.speedSteerFalloff;

        dst.lateralGrip               = src.lateralGrip;
        dst.driftGrip                 = src.driftGrip;
        dst.driftYawMultiplier        = src.driftYawMultiplier;
        dst.maxDriftAngle             = src.maxDriftAngle;
        dst.driftAngleRate            = src.driftAngleRate;
        dst.counterSteerAuthority     = src.counterSteerAuthority;
        dst.miniTurboImpulse          = src.miniTurboImpulse;
        dst.miniTurboChargeTime       = src.miniTurboChargeTime;
        dst.yawInertiaSmoothRate      = src.yawInertiaSmoothRate;

        dst.downforceStrength         = src.downforceStrength;
        dst.curvatureAdhesion         = src.curvatureAdhesion;
        dst.minAdhesionVelocity       = src.minAdhesionVelocity;

        dst.airPitchSpeed             = src.airPitchSpeed;
        dst.airYawSpeed               = src.airYawSpeed;
        dst.airRollSpeed              = src.airRollSpeed;
        dst.airAutoRightSpeed         = src.airAutoRightSpeed;
        dst.airAngularDamping         = src.airAngularDamping;
        dst.preAlignToLanding         = src.preAlignToLanding;
        dst.landingProbeDistance      = src.landingProbeDistance;
        dst.canGlide                  = src.canGlide;
        dst.glideFallSpeed            = src.glideFallSpeed;
        dst.glideForwardSpeed         = src.glideForwardSpeed;

        dst.maxGroundAngle            = src.maxGroundAngle;
        dst.maxSnapSpeed              = src.maxSnapSpeed;
        dst.minSurfaceSpeedMultiplier = src.minSurfaceSpeedMultiplier;
        dst.surfaceTransitionSpeed    = src.surfaceTransitionSpeed;
        dst.minJumpHeight             = src.minJumpHeight;
        dst.maxJumpHeight             = src.maxJumpHeight;
        dst.maxJumpHoldDuration       = src.maxJumpHoldDuration;
        dst.jumpCooldown              = src.jumpCooldown;
        dst.jumpBufferDuration        = src.jumpBufferDuration;
        dst.maxSlipYawRate            = src.maxSlipYawRate;
        dst.slipRecoveryRate          = src.slipRecoveryRate;

        dst.markWidth                 = src.markWidth;
        dst.fadeTime                  = src.fadeTime;
        dst.minSegmentLength          = src.minSegmentLength;
        dst.minSkidLateralSpeed       = src.minSkidLateralSpeed;
        dst.maxSkidPoints             = src.maxSkidPoints;
        dst.skidGroundOffset          = src.skidGroundOffset;
    }

    Button CreateButton (Transform parent, string name, string label, Color bgColor) {
        GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160f, 36f);

        Image img = btnObj.GetComponent<Image>();
        img.color = bgColor;

        Button btn = btnObj.GetComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = bgColor;
        colors.highlightedColor = new Color(0.2f, 0.78f, 1f, 1f);
        colors.selectedColor    = new Color(0.12f, 0.88f, 1f, 1f);
        colors.pressedColor     = bgColor * 0.75f;
        btn.colors = colors;

        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return btn;
    }

    Button CreateSmallButton (Transform parent, string name, string label, Color bgColor, float size) {
        GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(size, size);

        Image img = btnObj.GetComponent<Image>();
        img.color = bgColor;

        Button btn = btnObj.GetComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = bgColor;
        colors.highlightedColor = new Color(0.2f, 0.78f, 1f, 1f);
        colors.selectedColor    = new Color(0.12f, 0.88f, 1f, 1f);
        colors.pressedColor     = bgColor * 0.75f;
        btn.colors = colors;

        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return btn;
    }
}
