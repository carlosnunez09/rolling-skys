using System;
using Steamworks;
using UnityEngine;

/// <summary>
/// Manages Steam Lobbies and Friend Invites using Steamworks.NET.
/// Bridges Steam lobby events directly to GameNetworkManager and SteamP2PTransport.
/// </summary>
public class SteamLobbyManager : MonoBehaviour {

    public static SteamLobbyManager Instance { get; private set; }

    public static event Action<string> OnStatusChanged;
    public static event Action<int> OnMemberCountChanged;

    public CSteamID CurrentLobbyID { get; private set; }
    public bool IsInLobby => CurrentLobbyID.IsValid() && CurrentLobbyID.m_SteamID != 0;
    public bool IsHosting { get; private set; }
    public int MemberCount { get; private set; }

    Callback<LobbyCreated_t> _lobbyCreatedCallback;
    Callback<GameLobbyJoinRequested_t> _gameLobbyJoinRequestedCallback;
    Callback<LobbyEnter_t> _lobbyEnterCallback;
    Callback<LobbyChatUpdate_t> _lobbyChatUpdateCallback;

    void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start() {
        if (!SteamManager.Initialized) {
            Debug.LogWarning("[SteamLobbyManager] Steam is not initialized. Steam lobbies will be unavailable.");
            return;
        }

        _lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        _gameLobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
        _lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
        _lobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);

        Debug.Log("[SteamLobbyManager] Initialized Steam lobby callbacks.");
    }

    void OnDestroy() {
        _lobbyCreatedCallback?.Dispose();
        _lobbyCreatedCallback = null;
        _gameLobbyJoinRequestedCallback?.Dispose();
        _gameLobbyJoinRequestedCallback = null;
        _lobbyEnterCallback?.Dispose();
        _lobbyEnterCallback = null;
        _lobbyChatUpdateCallback?.Dispose();
        _lobbyChatUpdateCallback = null;

        LeaveLobby();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Create a Steam lobby (Friends-only by default).</summary>
    public void CreateLobby(bool friendsOnly = true, int maxMembers = 8) {
        if (!SteamManager.Initialized) {
            Debug.LogError("[SteamLobbyManager] Cannot create lobby: Steam is not initialized.");
            OnStatusChanged?.Invoke("Steam not initialized");
            return;
        }

        LeaveLobby();

        ELobbyType lobbyType = friendsOnly ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic;
        Debug.Log($"[SteamLobbyManager] Requesting lobby creation (type: {lobbyType}, max: {maxMembers})...");
        OnStatusChanged?.Invoke("Creating Steam lobby...");
        SteamMatchmaking.CreateLobby(lobbyType, maxMembers);
    }

    /// <summary>Join an existing Steam lobby.</summary>
    public void JoinLobby(CSteamID lobbyId) {
        if (!SteamManager.Initialized) {
            Debug.LogError("[SteamLobbyManager] Cannot join lobby: Steam is not initialized.");
            return;
        }

        LeaveLobby();
        Debug.Log($"[SteamLobbyManager] Joining lobby {lobbyId.m_SteamID}...");
        OnStatusChanged?.Invoke("Joining Steam lobby...");
        SteamMatchmaking.JoinLobby(lobbyId);
    }

    /// <summary>Leave current lobby and reset state.</summary>
    public void LeaveLobby() {
        if (IsInLobby) {
            Debug.Log($"[SteamLobbyManager] Leaving lobby {CurrentLobbyID.m_SteamID}.");
            SteamMatchmaking.LeaveLobby(CurrentLobbyID);
        }

        CurrentLobbyID = CSteamID.Nil;
        IsHosting = false;
        MemberCount = 0;
        OnMemberCountChanged?.Invoke(0);
    }

    /// <summary>Open the Steam overlay friends invite dialog for this lobby.</summary>
    public void OpenInviteOverlay() {
        if (!SteamManager.Initialized) {
            Debug.LogWarning("[SteamLobbyManager] Steam not initialized.");
            return;
        }

        if (IsInLobby) {
            Debug.Log($"[SteamLobbyManager] Opening Steam invite overlay for lobby {CurrentLobbyID.m_SteamID}.");
            SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobbyID);
        } else {
            Debug.LogWarning("[SteamLobbyManager] Cannot invite: not in a lobby.");
        }
    }

    // ── Steam Callbacks ────────────────────────────────────────────────────────

    void OnLobbyCreated(LobbyCreated_t callback) {
        if (callback.m_eResult != EResult.k_EResultOK) {
            Debug.LogError($"[SteamLobbyManager] Failed to create lobby. Result: {callback.m_eResult}");
            OnStatusChanged?.Invoke($"Lobby creation failed ({callback.m_eResult})");
            IsHosting = false;
            return;
        }

        CurrentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);
        IsHosting = true;
        MemberCount = 1;

        // Set metadata on the lobby so peers can find host info
        CSteamID mySteamId = SteamUser.GetSteamID();
        SteamMatchmaking.SetLobbyData(CurrentLobbyID, "HostSteamID", mySteamId.m_SteamID.ToString());
        SteamMatchmaking.SetLobbyData(CurrentLobbyID, "HostName", SteamFriends.GetPersonaName());
        SteamMatchmaking.SetLobbyData(CurrentLobbyID, "GameName", "RollingSkys");

        Debug.Log($"[SteamLobbyManager] Lobby created successfully: {CurrentLobbyID.m_SteamID}, Host: {mySteamId.m_SteamID} ({SteamFriends.GetPersonaName()})");
        OnStatusChanged?.Invoke($"Hosting Steam Lobby ({SteamFriends.GetPersonaName()})");
        OnMemberCountChanged?.Invoke(MemberCount);

        // Start local Netcode Host with Steam P2P transport
        if (GameNetworkManager.Instance != null) {
            GameNetworkManager.Instance.StartSteamHost();
        } else {
            Debug.LogError("[SteamLobbyManager] GameNetworkManager.Instance is null when starting host.");
        }
    }

    void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback) {
        Debug.Log($"[SteamLobbyManager] Join requested from Steam friend/invite for lobby {callback.m_steamIDLobby.m_SteamID}. Joining...");
        JoinLobby(callback.m_steamIDLobby);
    }

    void OnLobbyEnter(LobbyEnter_t callback) {
        CurrentLobbyID = new CSteamID(callback.m_ulSteamIDLobby);

        if (callback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) {
            Debug.LogError($"[SteamLobbyManager] Failed to enter lobby: {callback.m_EChatRoomEnterResponse}");
            OnStatusChanged?.Invoke("Failed to enter lobby");
            LeaveLobby();
            return;
        }

        MemberCount = SteamMatchmaking.GetNumLobbyMembers(CurrentLobbyID);
        OnMemberCountChanged?.Invoke(MemberCount);

        // If we created this lobby, OnLobbyCreated already handled starting the host
        if (IsHosting) return;

        // Client side: extract host Steam ID
        string hostSteamIdStr = SteamMatchmaking.GetLobbyData(CurrentLobbyID, "HostSteamID");
        string hostName = SteamMatchmaking.GetLobbyData(CurrentLobbyID, "HostName");

        if (ulong.TryParse(hostSteamIdStr, out ulong hostSteamId)) {
            Debug.Log($"[SteamLobbyManager] Entered lobby of {hostName} ({hostSteamId}). Starting Netcode Steam client...");
            OnStatusChanged?.Invoke($"Connecting to {hostName}...");

            if (GameNetworkManager.Instance != null) {
                GameNetworkManager.Instance.StartSteamClient(hostSteamId);
            } else {
                Debug.LogError("[SteamLobbyManager] GameNetworkManager.Instance is null when starting client.");
            }
        } else {
            Debug.LogError($"[SteamLobbyManager] Lobby {CurrentLobbyID.m_SteamID} has invalid or missing HostSteamID '{hostSteamIdStr}'.");
            OnStatusChanged?.Invoke("Lobby missing host data");
        }
    }

    void OnLobbyChatUpdate(LobbyChatUpdate_t callback) {
        if (!IsInLobby) return;

        MemberCount = SteamMatchmaking.GetNumLobbyMembers(CurrentLobbyID);
        Debug.Log($"[SteamLobbyManager] Lobby chat update: {MemberCount} total players.");
        OnMemberCountChanged?.Invoke(MemberCount);
    }
}
