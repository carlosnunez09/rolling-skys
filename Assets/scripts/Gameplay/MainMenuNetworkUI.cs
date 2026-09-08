using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the Main Menu UI for selecting game modes:
/// - Solo Practice (Local host on 127.0.0.1 with zero network overhead)
/// - Steam Lobby Host & Join (P2P via Steamworks and Valve relay)
/// - Steam Friend Invites (Steam overlay invite dialog)
/// - Direct IP / Dedicated Server (Connecting to custom IP:Port or Edgegap)
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

    // Legacy fallback references from original scene
    [SerializeField] Button _hostButton;
    [SerializeField] Button _clientButton;

    void Awake() {
#if UNITY_SERVER && !UNITY_EDITOR
        gameObject.SetActive(false);
        return;
#else
        ResolveReferences();
        BindButtons();
#endif
    }

    void OnDestroy() {
        UnbindButtons();
    }

    void Update() {
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
    }
}
