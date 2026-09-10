using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Controls the Main Menu UI for selecting game modes:
/// - Solo Practice (Local host on 127.0.0.1 with zero network overhead)
/// - Steam Lobby Host & Join (P2P via Steamworks and Valve relay)
/// - Steam Friend Invites (Steam overlay invite dialog)
/// - Direct IP / Dedicated Server (Connecting to custom IP:Port or Edgegap)
/// Full controller and gamepad support with auto-selection and wrap-around navigation.
/// </summary>
public class MainMenuNetworkUI : MonoBehaviour {

    [Header("Buttons")]
    [SerializeField] Button _soloButton;
    [SerializeField] Button _steamHostButton;
    [SerializeField] Button _inviteButton;
    [SerializeField] Button _directConnectButton;
    [SerializeField] Button _disconnectButton;

    [Header("Input & Text")]
    [SerializeField] TMP_InputField _addressInput;
    [SerializeField] TextMeshProUGUI _statusText;
    [SerializeField] TextMeshProUGUI _controllerHintText;

    // Legacy fallback references from original scene
    [SerializeField] Button _hostButton;
    [SerializeField] Button _clientButton;

    GameObject _lastSelected;

    void Awake() {
#if UNITY_SERVER && !UNITY_EDITOR
        gameObject.SetActive(false);
        return;
#else
        ResolveReferences();
        BindButtons();
#endif
    }

    void Start() {
        ApplyAllVisuals();
        EnsureControllerHintText();
        SetupButtonNavigation();

        // Automatically focus primary button on startup for controller navigation
        if (EventSystem.current != null && _soloButton != null) {
            EventSystem.current.firstSelectedGameObject = _soloButton.gameObject;
            EventSystem.current.SetSelectedGameObject(_soloButton.gameObject);
            _lastSelected = _soloButton.gameObject;
        }
    }

    void OnDestroy() {
        UnbindButtons();
    }

    void Update() {
        HandleControllerFocus();
        HandleControllerCancel();
        UpdateControllerHint();
        RefreshState();
    }

    void ResolveReferences() {
        // Auto-detect buttons from children if not explicitly linked in the Inspector
        if (_soloButton == null)
            _soloButton = FindChildButton("SoloButton");

        if (_steamHostButton == null)
            _steamHostButton = FindChildButton("SteamHostButton") ?? _hostButton;

        if (_inviteButton == null)
            _inviteButton = FindChildButton("InviteButton") ?? FindChildButton("InviteFriendsButton");

        if (_directConnectButton == null)
            _directConnectButton = FindChildButton("DirectConnectButton") ?? _clientButton;

        if (_disconnectButton == null)
            _disconnectButton = FindChildButton("DisconnectButton");

        if (_addressInput == null)
            _addressInput = GetComponentInChildren<TMP_InputField>(true);

        if (_statusText == null)
            _statusText = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    Button FindChildButton(string childName) {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<Button>() : null;
    }

    void BindButtons() {
        _soloButton?.onClick.AddListener(OnSoloClicked);
        _steamHostButton?.onClick.AddListener(OnSteamHostClicked);
        _inviteButton?.onClick.AddListener(OnInviteClicked);
        _directConnectButton?.onClick.AddListener(OnDirectConnectClicked);
        _disconnectButton?.onClick.AddListener(OnDisconnectClicked);

        // If _hostButton was not assigned to _steamHostButton, also listen to it
        if (_hostButton != null && _hostButton != _steamHostButton && _hostButton != _soloButton) {
            _hostButton.onClick.AddListener(OnHostButtonClicked);
        }
        if (_clientButton != null && _clientButton != _directConnectButton) {
            _clientButton.onClick.AddListener(OnDirectConnectClicked);
        }
    }

    void UnbindButtons() {
        _soloButton?.onClick.RemoveListener(OnSoloClicked);
        _steamHostButton?.onClick.RemoveListener(OnSteamHostClicked);
        _inviteButton?.onClick.RemoveListener(OnInviteClicked);
        _directConnectButton?.onClick.RemoveListener(OnDirectConnectClicked);
        _disconnectButton?.onClick.RemoveListener(OnDisconnectClicked);

        if (_hostButton != null) _hostButton.onClick.RemoveListener(OnHostButtonClicked);
        if (_clientButton != null) _clientButton.onClick.RemoveListener(OnDirectConnectClicked);
    }

    // ── Button Handlers ────────────────────────────────────────────────────────

    public void OnSoloClicked() {
        Debug.Log("[MainMenuUI] Starting Solo Practice mode...");
        GameNetworkManager.Instance?.StartSoloHost();
    }

    public void OnSteamHostClicked() {
        if (!SteamManager.Initialized) {
            Debug.LogWarning("[MainMenuUI] Steam is not initialized; falling back to direct host.");
            GameNetworkManager.Instance?.StartHost();
            return;
        }

        Debug.Log("[MainMenuUI] Creating Steam lobby...");
        if (SteamLobbyManager.Instance != null) {
            SteamLobbyManager.Instance.CreateLobby(friendsOnly: true, maxMembers: 8);
        } else {
            GameNetworkManager.Instance?.StartSteamHost();
        }
    }

    public void OnInviteClicked() {
        if (SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby) {
            SteamLobbyManager.Instance.OpenInviteOverlay();
        } else {
            Debug.LogWarning("[MainMenuUI] Cannot invite: not in a Steam lobby.");
        }
    }

    public void OnDirectConnectClicked() {
        string endpoint = _addressInput != null && !string.IsNullOrWhiteSpace(_addressInput.text)
            ? _addressInput.text.Trim()
            : (GameNetworkManager.Instance != null ? GameNetworkManager.Instance.ServerAddress : "127.0.0.1:7777");

        Debug.Log($"[MainMenuUI] Connecting to direct IP endpoint: {endpoint}");
        GameNetworkManager.Instance?.StartDirectClient(endpoint);
    }

    public void OnHostButtonClicked() {
        // Fallback for default HostButton: if Steam is active, host Steam lobby; otherwise start direct host
        if (SteamManager.Initialized) {
            OnSteamHostClicked();
        } else {
            GameNetworkManager.Instance?.StartHost();
        }
    }

    public void OnDisconnectClicked() {
        Debug.Log("[MainMenuUI] Disconnecting / canceling...");
        GameNetworkManager.Instance?.Shutdown();
    }

    // ── UI State Refresh ───────────────────────────────────────────────────────

    void RefreshState() {
        NetworkManager manager = NetworkManager.Singleton;
        bool isListening = manager != null && manager.IsListening;
        bool steamReady = SteamManager.Initialized;
        bool inSteamLobby = SteamLobbyManager.Instance != null && SteamLobbyManager.Instance.IsInLobby;

        if (_soloButton != null) _soloButton.interactable = !isListening;
        if (_steamHostButton != null) _steamHostButton.interactable = !isListening;
        if (_directConnectButton != null) _directConnectButton.interactable = !isListening;
        if (_hostButton != null) _hostButton.interactable = !isListening;
        if (_clientButton != null) _clientButton.interactable = !isListening;

        // Invite button is only active when hosting/in a Steam lobby
        if (_inviteButton != null) {
            _inviteButton.interactable = inSteamLobby;
            _inviteButton.gameObject.SetActive(inSteamLobby || steamReady);
        }

        if (_disconnectButton != null) {
            _disconnectButton.interactable = isListening || inSteamLobby;
        }

        if (_statusText == null) return;

        if (manager == null) {
            _statusText.text = "NetworkManager missing";
        } else if (isListening) {
            if (GameNetworkManager.Instance != null) {
                _statusText.text = inSteamLobby
                    ? $"{GameNetworkManager.Instance.StatusMessage} ({SteamLobbyManager.Instance.MemberCount} in lobby)"
                    : GameNetworkManager.Instance.StatusMessage;
            } else {
                _statusText.text = manager.IsHost ? "Hosting" : (manager.IsServer ? "Server" : "Connected");
            }
        } else {
            if (steamReady) {
                _statusText.text = inSteamLobby
                    ? $"In Steam Lobby ({SteamLobbyManager.Instance.MemberCount} players)"
                    : "Ready | Steam Online";
            } else {
                _statusText.text = "Offline | Solo & Direct IP ready (Steam not running)";
            }
        }

        SetupButtonNavigation();
    }

    // ── Controller & Navigation Helpers ────────────────────────────────────────

    void HandleControllerCancel() {
        bool bPressed = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        bool escPressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        if (bPressed || escPressed) {
            if (_addressInput != null && _addressInput.isFocused) {
                _addressInput.DeactivateInputField();
                if (EventSystem.current != null) {
                    Selectable target = (_directConnectButton != null && _directConnectButton.interactable)
                        ? _directConnectButton
                        : (Selectable)_soloButton;
                    if (target != null) EventSystem.current.SetSelectedGameObject(target.gameObject);
                }
            } else if (_disconnectButton != null && _disconnectButton.interactable && _disconnectButton.gameObject.activeInHierarchy) {
                OnDisconnectClicked();
            }
        }
    }

    void HandleControllerFocus() {
        if (EventSystem.current == null) return;

        GameObject current = EventSystem.current.currentSelectedGameObject;
        if (current != null && current.activeInHierarchy) {
            _lastSelected = current;
            return;
        }

        bool hasNavInput = false;
        if (Gamepad.current != null) {
            var pad = Gamepad.current;
            hasNavInput = pad.dpad.up.wasPressedThisFrame ||
                          pad.dpad.down.wasPressedThisFrame ||
                          pad.dpad.left.wasPressedThisFrame ||
                          pad.dpad.right.wasPressedThisFrame ||
                          pad.buttonSouth.wasPressedThisFrame ||
                          Mathf.Abs(pad.leftStick.y.ReadValue()) > 0.35f ||
                          Mathf.Abs(pad.leftStick.x.ReadValue()) > 0.35f;
        }
        if (Keyboard.current != null) {
            var kb = Keyboard.current;
            hasNavInput |= kb.upArrowKey.wasPressedThisFrame ||
                           kb.downArrowKey.wasPressedThisFrame ||
                           kb.wKey.wasPressedThisFrame ||
                           kb.sKey.wasPressedThisFrame;
        }

        if (hasNavInput) {
            GameObject target = (_lastSelected != null && _lastSelected.activeInHierarchy)
                ? _lastSelected
                : (_soloButton != null ? _soloButton.gameObject : null);

            if (target != null) {
                EventSystem.current.SetSelectedGameObject(target);
            }
        }
    }

    void ApplyAllVisuals() {
        ApplySelectableColors(_soloButton);
        ApplySelectableColors(_steamHostButton);
        ApplySelectableColors(_hostButton);
        ApplySelectableColors(_directConnectButton);
        ApplySelectableColors(_clientButton);
        ApplySelectableColors(_inviteButton);
        ApplySelectableColors(_disconnectButton);
        ApplySelectableColors(_addressInput);
    }

    void ApplySelectableColors(Selectable selectable) {
        if (selectable == null) return;
        ColorBlock colors = selectable.colors;
        colors.highlightedColor = new Color(0.18f, 0.78f, 1f, 1f); // Vibrant light blue / cyan
        colors.selectedColor    = new Color(0.12f, 0.88f, 1f, 1f); // Vibrant cyan glow
        colors.pressedColor     = new Color(0.08f, 0.45f, 0.75f, 1f);
        selectable.colors       = colors;
    }

    void SetupButtonNavigation() {
        List<Selectable> selectables = new List<Selectable>();

        if (_soloButton != null && _soloButton.gameObject.activeInHierarchy && _soloButton.interactable)
            selectables.Add(_soloButton);

        Selectable host = (_steamHostButton != null && _steamHostButton.gameObject.activeInHierarchy && _steamHostButton.interactable)
            ? _steamHostButton
            : (_hostButton != null && _hostButton.gameObject.activeInHierarchy && _hostButton.interactable ? _hostButton : null);
        if (host != null && !selectables.Contains(host))
            selectables.Add(host);

        Selectable client = (_directConnectButton != null && _directConnectButton.gameObject.activeInHierarchy && _directConnectButton.interactable)
            ? _directConnectButton
            : (_clientButton != null && _clientButton.gameObject.activeInHierarchy && _clientButton.interactable ? _clientButton : null);
        if (client != null && !selectables.Contains(client))
            selectables.Add(client);

        if (_addressInput != null && _addressInput.gameObject.activeInHierarchy && _addressInput.interactable)
            selectables.Add(_addressInput);

        if (_inviteButton != null && _inviteButton.gameObject.activeInHierarchy && _inviteButton.interactable)
            selectables.Add(_inviteButton);

        if (_disconnectButton != null && _disconnectButton.gameObject.activeInHierarchy && _disconnectButton.interactable)
            selectables.Add(_disconnectButton);

        if (selectables.Count <= 1) return;

        for (int i = 0; i < selectables.Count; i++) {
            Selectable current = selectables[i];
            Selectable up = selectables[(i - 1 + selectables.Count) % selectables.Count];
            Selectable down = selectables[(i + 1) % selectables.Count];

            Navigation nav = current.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = up;
            nav.selectOnDown = down;
            current.navigation = nav;
        }
    }

    void EnsureControllerHintText() {
        if (_controllerHintText != null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        Transform existing = canvas.transform.Find("ControllerHintText");
        if (existing != null) {
            _controllerHintText = existing.GetComponent<TextMeshProUGUI>();
            UpdateControllerHint();
            return;
        }

        GameObject hintObj = new GameObject("ControllerHintText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        hintObj.transform.SetParent(canvas.transform, false);

        RectTransform rt = hintObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 18f);
        rt.sizeDelta = new Vector2(-40f, 32f);

        _controllerHintText = hintObj.GetComponent<TextMeshProUGUI>();
        _controllerHintText.fontSize = 14;
        _controllerHintText.alignment = TextAlignmentOptions.Center;
        _controllerHintText.color = new Color(0.6f, 0.7f, 0.8f, 0.95f);
        UpdateControllerHint();
    }

    void UpdateControllerHint() {
        if (_controllerHintText == null) return;
        bool isPad = Gamepad.current != null;
        if (isPad) {
            _controllerHintText.text = "<color=#1FE0FF>🎮 Controller:</color> [D-Pad / Left Stick] Navigate   [A] Select   [B] Back / Disconnect";
        } else {
            _controllerHintText.text = "<color=#8E9CAE>⌨ Keyboard:</color> [Arrows / WASD] Navigate   [Enter / Space] Select   [Esc] Cancel";
        }
    }
}
