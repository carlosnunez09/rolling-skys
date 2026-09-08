using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game Escape/Pause menu. Supports keyboard Escape and Gamepad Start/Menu button.
/// Allows players to invite friends via the Steam overlay, reset/respawn their car,
/// or disconnect and return cleanly to the Main Menu.
/// </summary>
public class InGameEscMenu : MonoBehaviour {

    public static InGameEscMenu Instance { get; private set; }

    [Header("Menu Containers")]
    [SerializeField] GameObject _menuRoot;
    [SerializeField] RectTransform _panel;

    [Header("Header Elements")]
    [SerializeField] TMP_Text _titleText;
    [SerializeField] TMP_Text _sessionStatusText;

    [Header("Buttons")]
    [SerializeField] Button _resumeButton;
    [SerializeField] Button _inviteButton;
    [SerializeField] TMP_Text _inviteButtonText;
    [SerializeField] Button _respawnButton;
    [SerializeField] Button _mainMenuButton;

    [Header("Footer")]
    [SerializeField] TMP_Text _hintText;

    MovingCar _boundCar;

    /// <summary>True when the ESC menu is actively open on screen.</summary>
    public bool IsOpen => _menuRoot != null && _menuRoot.activeSelf;

    void Awake () {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ResolveUiElements();
        BindButtonEvents();

        // Ensure menu is closed on initialization
        if (_menuRoot != null)
            _menuRoot.SetActive(false);
    }

    void Start () {
        if (_boundCar == null)
            _boundCar = FindLocalPlayerCar();
    }

    void OnDestroy () {
        if (Instance == this)
            Instance = null;

        UnbindButtonEvents();
    }

    void Update () {
        // Toggle menu via Keyboard Escape or Gamepad Start
        bool escPressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool startPressed = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;

        if (escPressed || startPressed) {
            ToggleMenu();
            return;
        }

        if (IsOpen) {
            // Refresh session status while menu is visible
            RefreshSessionStatus();
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void BindToCar (MovingCar car) {
        _boundCar = car;
    }

    public void ToggleMenu () {
        if (IsOpen)
            CloseMenu();
        else
            OpenMenu();
    }

    public void OpenMenu () {
        if (_menuRoot == null) {
            EnsureFallbackUi();
            if (_menuRoot == null) return;
        }

        if (_boundCar == null)
            _boundCar = FindLocalPlayerCar();

        // Mute vehicle input while in pause menu
        _boundCar?.SetInputEnabled(false);

        // Unlock and reveal mouse cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        _menuRoot.SetActive(true);
        RefreshSessionStatus();

        // Focus resume button for gamepad / keyboard navigation
        if (EventSystem.current != null && _resumeButton != null) {
            EventSystem.current.SetSelectedGameObject(_resumeButton.gameObject);
        }
    }

    public void CloseMenu () {
        if (_menuRoot != null)
            _menuRoot.SetActive(false);

        // Re-enable vehicle input
        if (_boundCar == null)
            _boundCar = FindLocalPlayerCar();

        _boundCar?.SetInputEnabled(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ── Button Handlers ────────────────────────────────────────────────────────

    public void OnResumeClicked () {
        CloseMenu();
    }

    public void OnInviteClicked () {
        if (SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby) {
            Debug.Log("[InGameEscMenu] Opening Steam invite overlay for lobby...");
            SteamLobbyManager.Instance.OpenInviteOverlay();
        } else if (SteamManager.Initialized) {
            Debug.Log("[InGameEscMenu] Not currently in a Steam lobby. Hosting a friend lobby now...");
            if (_sessionStatusText != null)
                _sessionStatusText.text = "Creating Steam Lobby...";

            SteamLobbyManager.Instance?.CreateLobby(friendsOnly: true, maxMembers: 8);
        } else {
            Debug.LogWarning("[InGameEscMenu] Cannot invite friends: Steam is offline.");
            if (_sessionStatusText != null)
                _sessionStatusText.text = "Steam is offline. Launch via Steam to invite friends.";
        }
    }

    public void OnRespawnClicked () {
        if (_boundCar == null)
            _boundCar = FindLocalPlayerCar();

        if (_boundCar != null) {
            Debug.Log("[InGameEscMenu] Respawning player car...");
            _boundCar.Respawn();
        }

        CloseMenu();
    }

    public void OnMainMenuClicked () {
        CloseMenu();

        if (GameNetworkManager.Instance != null) {
            Debug.Log("[InGameEscMenu] Shutting down session and returning to Main Menu...");
            GameNetworkManager.Instance.Shutdown();
        } else {
            Debug.Log("[InGameEscMenu] Loading MainMenu scene...");
            SceneManager.LoadScene("MainMenu");
        }
    }

    // ── UI Helpers ─────────────────────────────────────────────────────────────

    void RefreshSessionStatus () {
        if (_sessionStatusText == null) return;

        bool steamReady = SteamManager.Initialized;
        bool inLobby = SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby;

        if (inLobby) {
            int count = SteamLobbyManager.Instance.MemberCount;
            _sessionStatusText.text = $"<color=#1FB8D1>Steam Friends Lobby</color> • {count} Racer{(count == 1 ? "" : "s")}";
            if (_inviteButton != null) _inviteButton.interactable = true;
            if (_inviteButtonText != null) _inviteButtonText.text = "Invite Friends (Steam)";
        } else if (GameNetworkManager.Instance != null) {
            switch (GameNetworkManager.Instance.CurrentMode) {
                case GameNetworkManager.ActiveNetworkMode.Solo:
                    _sessionStatusText.text = "<color=#2EB85C>Solo Practice Mode</color>";
                    ConfigureInviteButton(steamReady);
                    break;
                case GameNetworkManager.ActiveNetworkMode.DirectClient:
                case GameNetworkManager.ActiveNetworkMode.DedicatedServer:
                    _sessionStatusText.text = $"<color=#E0A800>Server:</color> {GameNetworkManager.Instance.ServerAddress}";
                    if (_inviteButton != null) _inviteButton.interactable = false;
                    if (_inviteButtonText != null) _inviteButtonText.text = "Direct Server Session";
                    break;
                default:
                    _sessionStatusText.text = GameNetworkManager.Instance.StatusMessage;
                    ConfigureInviteButton(steamReady);
                    break;
            }
        } else {
            _sessionStatusText.text = "<color=#8E9CAE>Offline Editor Playtest</color>";
            ConfigureInviteButton(steamReady);
        }
    }

    void ConfigureInviteButton (bool steamReady) {
        if (_inviteButton == null) return;

        if (steamReady) {
            _inviteButton.interactable = true;
            if (_inviteButtonText != null) _inviteButtonText.text = "Host Steam Lobby & Invite";
        } else {
            _inviteButton.interactable = false;
            if (_inviteButtonText != null) _inviteButtonText.text = "Steam Offline";
        }
    }

    MovingCar FindLocalPlayerCar () {
        MovingCar[] cars = FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude);
        MovingCar fallback = null;

        for (int i = 0; i < cars.Length; i++) {
            MovingCar car = cars[i];
            if (car == null) continue;
            if (car.HasLocalControl) return car;
            if (fallback == null) fallback = car;
        }

        return fallback;
    }

    void ResolveUiElements () {
        if (_menuRoot == null) {
            Transform menuTransform = transform.Find("EscMenu") ?? transform.Find("PauseMenu");
            if (menuTransform != null)
                _menuRoot = menuTransform.gameObject;
            else if (gameObject.name == "EscMenu" || gameObject.name == "PauseMenu")
                _menuRoot = gameObject;
        }

        if (_menuRoot == null) return;

        if (_panel == null)
            _panel = _menuRoot.GetComponent<RectTransform>();

        if (_titleText == null)
            _titleText = FindChildText(_menuRoot.transform, "TitleText") ?? FindChildText(_menuRoot.transform, "Title");

        if (_sessionStatusText == null)
            _sessionStatusText = FindChildText(_menuRoot.transform, "StatusText") ?? FindChildText(_menuRoot.transform, "SessionText");

        if (_resumeButton == null)
            _resumeButton = FindChildButton(_menuRoot.transform, "ResumeButton");

        if (_inviteButton == null)
            _inviteButton = FindChildButton(_menuRoot.transform, "InviteButton");

        if (_inviteButton != null && _inviteButtonText == null)
            _inviteButtonText = _inviteButton.GetComponentInChildren<TMP_Text>(true);

        if (_respawnButton == null)
            _respawnButton = FindChildButton(_menuRoot.transform, "RespawnButton");

        if (_mainMenuButton == null)
            _mainMenuButton = FindChildButton(_menuRoot.transform, "MainMenuButton") ?? FindChildButton(_menuRoot.transform, "QuitButton");

        if (_hintText == null)
            _hintText = FindChildText(_menuRoot.transform, "HintText");
    }

    void BindButtonEvents () {
        _resumeButton?.onClick.AddListener(OnResumeClicked);
        _inviteButton?.onClick.AddListener(OnInviteClicked);
        _respawnButton?.onClick.AddListener(OnRespawnClicked);
        _mainMenuButton?.onClick.AddListener(OnMainMenuClicked);
    }

    void UnbindButtonEvents () {
        if (_resumeButton != null) _resumeButton.onClick.RemoveListener(OnResumeClicked);
        if (_inviteButton != null) _inviteButton.onClick.RemoveListener(OnInviteClicked);
        if (_respawnButton != null) _respawnButton.onClick.RemoveListener(OnRespawnClicked);
        if (_mainMenuButton != null) _mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
    }

    Button FindChildButton (Transform root, string childName) {
        Transform child = root.Find(childName);
        if (child != null) return child.GetComponent<Button>();
        foreach (Button b in root.GetComponentsInChildren<Button>(true)) {
            if (b.gameObject.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                return b;
        }
        return null;
    }

    TMP_Text FindChildText (Transform root, string childName) {
        Transform child = root.Find(childName);
        if (child != null) return child.GetComponent<TMP_Text>();
        foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true)) {
            if (t.gameObject.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return null;
    }

    /// <summary>
    /// Procedural fallback UI generation in case EscMenu has no visual prefab in scene.
    /// Ensures ESC key ALWAYS presents a working, interactive menu.
    /// </summary>
    void EnsureFallbackUi () {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject root = new GameObject("EscMenu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);

        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.sizeDelta = Vector2.zero;

        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0.04f, 0.06f, 0.1f, 0.88f); // Dark translucent backdrop

        // Panel
        GameObject panelObj = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
        panelObj.transform.SetParent(root.transform, false);
        RectTransform panelRt = panelObj.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(460f, 480f);

        Image panelImg = panelObj.GetComponent<Image>();
        panelImg.color = new Color(0.09f, 0.12f, 0.18f, 0.98f);

        VerticalLayoutGroup vlg = panelObj.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(36, 36, 32, 32);
        vlg.spacing = 14;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        // Title
        GameObject titleObj = new GameObject("TitleText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleObj.transform.SetParent(panelObj.transform, false);
        TextMeshProUGUI title = titleObj.GetComponent<TextMeshProUGUI>();
        title.text = "PAUSED";
        title.fontSize = 32;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        titleObj.GetComponent<RectTransform>().sizeDelta = new Vector2(380f, 44f);

        // Status
        GameObject statusObj = new GameObject("StatusText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        statusObj.transform.SetParent(panelObj.transform, false);
        TextMeshProUGUI status = statusObj.GetComponent<TextMeshProUGUI>();
        status.text = "Rolling Skys";
        status.fontSize = 16;
        status.alignment = TextAlignmentOptions.Center;
        status.color = new Color(0.7f, 0.8f, 0.9f);
        statusObj.GetComponent<RectTransform>().sizeDelta = new Vector2(380f, 28f);

        // Buttons
        Button resumeBtn = CreateButton(panelObj.transform, "ResumeButton", "Resume", new Color(0.18f, 0.22f, 0.32f));
        Button inviteBtn = CreateButton(panelObj.transform, "InviteButton", "Invite Friends (Steam)", new Color(0.1f, 0.48f, 0.65f));
        Button respawnBtn = CreateButton(panelObj.transform, "RespawnButton", "Reset Car (Respawn)", new Color(0.18f, 0.22f, 0.32f));
        Button quitBtn = CreateButton(panelObj.transform, "MainMenuButton", "Leave to Main Menu", new Color(0.6f, 0.2f, 0.2f));

        // Footer Hint
        GameObject hintObj = new GameObject("HintText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        hintObj.transform.SetParent(panelObj.transform, false);
        TextMeshProUGUI hint = hintObj.GetComponent<TextMeshProUGUI>();
        hint.text = "Press ESC or START to resume";
        hint.fontSize = 13;
        hint.alignment = TextAlignmentOptions.Center;
        hint.color = new Color(0.5f, 0.55f, 0.65f);
        hintObj.GetComponent<RectTransform>().sizeDelta = new Vector2(380f, 24f);

        _menuRoot = root;
        _panel = panelRt;
        _titleText = title;
        _sessionStatusText = status;
        _resumeButton = resumeBtn;
        _inviteButton = inviteBtn;
        _inviteButtonText = inviteBtn.GetComponentInChildren<TMP_Text>(true);
        _respawnButton = respawnBtn;
        _mainMenuButton = quitBtn;
        _hintText = hint;

        BindButtonEvents();
    }

    Button CreateButton (Transform parent, string name, string label, Color bgColor) {
        GameObject btnObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent, false);

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(380f, 48f);

        Image img = btnObj.GetComponent<Image>();
        img.color = bgColor;

        Button btn = btnObj.GetComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = bgColor;
        colors.highlightedColor = bgColor * 1.25f;
        colors.pressedColor = bgColor * 0.8f;
        colors.selectedColor = bgColor * 1.15f;
        btn.colors = colors;

        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(btnObj.transform, false);
        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = Vector2.zero;

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 18;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return btn;
    }
}
