using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameNetworkManager : MonoBehaviour {

    public static GameNetworkManager Instance { get; private set; }

    [Header("Network")]
    [SerializeField] ushort _port = 7777;
    [SerializeField] string _serverAddress = "127.0.0.1";
    [SerializeField] string _listenAddress = "0.0.0.0";
    [SerializeField] bool _connectAutomatically;

    [Header("Scene")]
    [SerializeField] string _gameSceneName = "SampleScene";

    [Header("Spawning")]
    [SerializeField] GameObject _carPrefab;
    [SerializeField] Transform[] _spawnPoints;
    [SerializeField] bool _findSpawnPointsByTag = true;

    int  _nextSpawnIndex;
    bool _gameSceneLoaded;
    string _statusMessage = "Offline";
    string _lastTargetAddress = "127.0.0.1:7777";
    readonly HashSet<ulong> _clientsAwaitingSceneSync = new HashSet<ulong>();

    public enum ActiveNetworkMode {
        Offline,
        Solo,
        SteamHost,
        SteamClient,
        DirectHost,
        DirectClient,
        DedicatedServer
    }

    public bool IsListening => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    public bool IsClient    => NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
    public bool IsServer    => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    public ushort Port      => _port;
    public string ServerAddress => _serverAddress;
    public bool ConnectAutomatically => _connectAutomatically;
    public string StatusMessage => _statusMessage;
    public ActiveNetworkMode CurrentMode { get; private set; } = ActiveNetworkMode.Offline;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake () {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Application.runInBackground = true;
        DontDestroyOnLoad(gameObject);
        ApplyCommandLineOverrides();
    }

    void Start () {
        if (NetworkManager.Singleton != null) {
            NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnTransportFailure         += OnTransportFailure;
        } else {
            Debug.LogError("GameNetworkManager: NetworkManager.Singleton is null in Start. " +
                           "Ensure NetworkManager is in the scene and initialises before this script.");
        }

        bool runAsServer = Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("-server", StringComparison.OrdinalIgnoreCase));
        bool runAsHost = Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("-host", StringComparison.OrdinalIgnoreCase));

#if UNITY_SERVER && !UNITY_EDITOR
        StartServer();
#else
        if (runAsServer) {
            StartServer();
        } else if (runAsHost) {
            StartHost();
        } else if (_connectAutomatically) {
            StartClient();
        }
#endif
    }

    void OnDestroy () {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnTransportFailure         -= OnTransportFailure;
        UnsubscribeSceneEvents();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Start in Solo Practice mode (local host, 127.0.0.1, no external network needed).</summary>
    public void StartSoloHost () {
        if (!CanStartNetwork()) return;
        if (!ConfigureUnityTransport("127.0.0.1", _port, "127.0.0.1")) return;
        bool started = NetworkManager.Singleton.StartHost();
        Debug.Log($"GameNetworkManager: Solo Host started: {started}, scene: {_gameSceneName}");
        if (!started) return;
        CurrentMode = ActiveNetworkMode.Solo;
        _statusMessage = "Solo Practice";
        SubscribeSceneEvents();
        NetworkManager.Singleton.SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
    }

    /// <summary>Start as host for Steam lobby co-op using Steam P2P relay.</summary>
    public void StartSteamHost () {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) {
            NetworkManager.Singleton.Shutdown();
        }
        if (!CanStartNetwork()) return;
        SteamP2PTransport transport = GetOrCreateSteamTransport();
        if (transport == null) {
            _statusMessage = "Steam transport unavailable";
            return;
        }
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        bool started = NetworkManager.Singleton.StartHost();
        Debug.Log($"GameNetworkManager: Steam Host started: {started}, scene: {_gameSceneName}");
        if (!started) return;
        CurrentMode = ActiveNetworkMode.SteamHost;
        _statusMessage = "Hosting Steam Lobby";
        SubscribeSceneEvents();
        NetworkManager.Singleton.SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
    }

    /// <summary>Connect to a Steam host by Steam ID.</summary>
    public void StartSteamClient (ulong hostSteamId) {
        if (!CanStartNetwork()) return;
        SteamP2PTransport transport = GetOrCreateSteamTransport();
        if (transport == null) {
            _statusMessage = "Steam transport unavailable";
            return;
        }
        transport.TargetSteamID = hostSteamId;
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        bool started = NetworkManager.Singleton.StartClient();
        CurrentMode = ActiveNetworkMode.SteamClient;
        _statusMessage = started ? "Connecting via Steam..." : "Failed to connect to Steam host";
        Debug.Log($"GameNetworkManager: Steam Client connect started: {started}, target: {hostSteamId}");
    }

    /// <summary>Connect to a server/host at the given endpoint (IP:Port).</summary>
    public void StartDirectClient (string endpoint) {
        if (!CanStartNetwork()) return;
        if (!TryParseEndpoint(endpoint, out string targetAddress, out ushort targetPort)) {
            _statusMessage = $"Invalid server address: {endpoint}";
            Debug.LogError($"GameNetworkManager: Invalid server endpoint '{endpoint}'.");
            return;
        }

        _lastTargetAddress = $"{targetAddress}:{targetPort}";
        if (!ConfigureUnityTransport(targetAddress, targetPort, null)) return;
        bool started = NetworkManager.Singleton.StartClient();
        CurrentMode = ActiveNetworkMode.DirectClient;
        _statusMessage = started
            ? $"Connecting to {targetAddress}:{targetPort}"
            : $"Failed to start client for {targetAddress}:{targetPort}";
        Debug.Log($"GameNetworkManager: Direct Client connect started: {started}, address: {targetAddress}, port: {targetPort}");
    }

    /// <summary>Start as direct IP host and load the game scene for all clients.</summary>
    public void StartHost () {
        if (!CanStartNetwork()) return;
        if (!ConfigureUnityTransport("127.0.0.1", _port, _listenAddress)) return;
        bool started = NetworkManager.Singleton.StartHost();
        Debug.Log($"GameNetworkManager: Host start result: {started}, port: {_port}, listen: {_listenAddress}, scene: {_gameSceneName}");
        if (!started) return;
        CurrentMode = ActiveNetworkMode.DirectHost;
        _statusMessage = $"Hosting on port {_port}";
        SubscribeSceneEvents();
        NetworkManager.Singleton.SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
    }

    /// <summary>Start as dedicated server and load the game scene.</summary>
    public void StartServer () {
        if (!CanStartNetwork()) return;
        if (!ConfigureUnityTransport("127.0.0.1", _port, _listenAddress)) return;
        bool started = NetworkManager.Singleton.StartServer();
        Debug.Log($"GameNetworkManager: Server start result: {started}, port: {_port}, listen: {_listenAddress}, scene: {_gameSceneName}");
        if (!started) return;
        CurrentMode = ActiveNetworkMode.DedicatedServer;
        _statusMessage = $"Server listening on {_listenAddress}:{_port}";
        SubscribeSceneEvents();
        NetworkManager.Singleton.SceneManager.LoadScene(_gameSceneName, LoadSceneMode.Single);
    }

    /// <summary>Connect to the configured host. The server will push the game scene.</summary>
    public void StartClient () {
        StartDirectClient(BuildConfiguredEndpoint());
    }

    /// <summary>Disconnect and reset session state so a new session can be started.</summary>
    public void Shutdown () {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        SteamLobbyManager.Instance?.LeaveLobby();

        _gameSceneLoaded = false;
        _spawnPoints     = null;    // force re-cache from next game scene
        _nextSpawnIndex  = 0;
        CurrentMode      = ActiveNetworkMode.Offline;
        _statusMessage   = "Offline";
        _clientsAwaitingSceneSync.Clear();

        if (SceneManager.GetActiveScene().name != "MainMenu") {
            SceneManager.LoadScene("MainMenu");
        }
    }

    // ── Scene event handling ───────────────────────────────────────────────────

    void SubscribeSceneEvents () {
        if (NetworkManager.Singleton?.SceneManager == null) return;
        UnsubscribeSceneEvents();
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
        NetworkManager.Singleton.SceneManager.OnSynchronizeComplete += OnClientSceneSynchronized;
    }

    void UnsubscribeSceneEvents () {
        if (NetworkManager.Singleton?.SceneManager == null) return;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
        NetworkManager.Singleton.SceneManager.OnSynchronizeComplete -= OnClientSceneSynchronized;
    }

    /// <summary>
    /// Called once all clients have finished loading the game scene.
    /// This is the correct place to spawn player objects — the scene is guaranteed
    /// to be fully loaded on every peer before we touch it.
    /// </summary>
    void OnSceneLoadCompleted (string sceneName, LoadSceneMode loadSceneMode,
                               List<ulong> clientsCompleted, List<ulong> clientsTimedOut) {
        if (sceneName != _gameSceneName) return;

        _gameSceneLoaded = true;
        CacheSpawnPoints();

        // Spawn a car for every client that connected before the scene finished loading
        // (includes the host itself).
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds) {
            if (!ClientHasPlayerObject(clientId))
                SpawnPlayerCar(clientId);
            _clientsAwaitingSceneSync.Remove(clientId);
        }

        RebuildRaceRuntime();
    }

    void OnClientSceneSynchronized (ulong clientId) {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (!_gameSceneLoaded) return;
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;

        _clientsAwaitingSceneSync.Remove(clientId);

        if (!ClientHasPlayerObject(clientId))
            SpawnPlayerCar(clientId);

        RebuildRaceRuntime();
        Debug.Log($"GameNetworkManager: Client synchronized and spawned: {clientId}");
    }

    // ── Network callbacks ──────────────────────────────────────────────────────

    void OnClientConnected (ulong clientId) {
        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.LocalClientId == clientId) {
            switch (CurrentMode) {
                case ActiveNetworkMode.Solo:
                    _statusMessage = "Solo Practice";
                    break;
                case ActiveNetworkMode.SteamHost:
                    _statusMessage = "Hosting Steam Lobby";
                    break;
                case ActiveNetworkMode.SteamClient:
                    _statusMessage = "Connected via Steam";
                    break;
                case ActiveNetworkMode.DirectHost:
                    _statusMessage = $"Hosting on port {_port}";
                    break;
                case ActiveNetworkMode.DedicatedServer:
                    _statusMessage = $"Server listening on {_listenAddress}:{_port}";
                    break;
                default:
                    _statusMessage = NetworkManager.Singleton.IsHost ? $"Hosting on port {_port}" : "Connected";
                    break;
            }
        }

        Debug.Log($"GameNetworkManager: Client connected: {clientId}");

        if (!NetworkManager.Singleton.IsServer) return;

        // If the game scene hasn't loaded yet, OnSceneLoadCompleted will handle the spawn.
        if (!_gameSceneLoaded) return;

        if (NetworkManager.Singleton.NetworkConfig.EnableSceneManagement) {
            _clientsAwaitingSceneSync.Add(clientId);
            Debug.Log($"GameNetworkManager: Client {clientId} connected; waiting for scene sync before spawning.");
            return;
        }

        if (!ClientHasPlayerObject(clientId))
            SpawnPlayerCar(clientId);

        RebuildRaceRuntime();
    }

    void OnClientDisconnected (ulong clientId) {
        if (NetworkManager.Singleton == null) return;

        string reason = NetworkManager.Singleton.DisconnectReason;
        if (NetworkManager.Singleton.LocalClientId == clientId) {
            bool maxAttemptsReached = !string.IsNullOrWhiteSpace(reason)
                                      && reason.IndexOf("MaxConnectionAttempts", StringComparison.OrdinalIgnoreCase) >= 0;
            _statusMessage = maxAttemptsReached
                ? $"Connection failed: no server responded at {_lastTargetAddress}"
                : string.IsNullOrWhiteSpace(reason) ? "Disconnected" : $"Disconnected: {reason}";
            CurrentMode = ActiveNetworkMode.Offline;

            if (SceneManager.GetActiveScene().name != "MainMenu") {
                SceneManager.LoadScene("MainMenu");
            }
        }

        Debug.LogWarning($"GameNetworkManager: Client disconnected: {clientId}. Reason: {reason}");
        _clientsAwaitingSceneSync.Remove(clientId);

        if (!NetworkManager.Singleton.IsServer) return;
        RebuildRaceRuntime();
    }

    void OnTransportFailure () {
        string reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : "";
        _statusMessage = string.IsNullOrWhiteSpace(reason) ? "Connection failed" : $"Connection failed: {reason}";
        Debug.LogWarning($"GameNetworkManager: Transport failure. {reason}");
    }

    // ── Spawning ───────────────────────────────────────────────────────────────

    void SpawnPlayerCar (ulong clientId) {
        if (_carPrefab == null) {
            Debug.LogError("GameNetworkManager: no car prefab assigned.");
            return;
        }

        if (!_carPrefab.TryGetComponent(out NetworkObject _)) {
            Debug.LogError($"Car prefab '{_carPrefab.name}' needs a NetworkObject component.");
            return;
        }

        Transform spawnPoint  = GetNextSpawnPoint();
        Vector3   spawnPos    = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRot   = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        Debug.Log(spawnPoint != null
            ? $"GameNetworkManager: Spawning client {clientId} at {spawnPoint.name} ({spawnPos})."
            : $"GameNetworkManager: No spawn point found for client {clientId}; using world origin.");

        GameObject   playerCar     = Instantiate(_carPrefab, spawnPos, spawnRot);
        NetworkObject networkObject = playerCar.GetComponent<NetworkObject>();
        networkObject.SpawnAsPlayerObject(clientId, destroyWithScene: true);
    }

    Transform GetNextSpawnPoint () {
        CacheSpawnPoints();

        if (_spawnPoints == null || _spawnPoints.Length == 0)
            return null;

        Transform spawnPoint = _spawnPoints[_nextSpawnIndex % _spawnPoints.Length];
        _nextSpawnIndex++;
        return spawnPoint;
    }

    void CacheSpawnPoints () {
        if (!_findSpawnPointsByTag || (_spawnPoints != null && _spawnPoints.Length > 0)) return;

        var spawnPoints = new List<Transform>();
        var seen = new HashSet<Transform>();

        try {
            foreach (GameObject taggedObject in GameObject.FindGameObjectsWithTag("SpawnPoint"))
                AddSpawnPoint(taggedObject.transform, spawnPoints, seen);
        } catch (UnityException) {
            // The name-based fallback below still works even if the tag is missing.
        }

        foreach (Transform transform in FindObjectsByType<Transform>(FindObjectsInactive.Exclude)) {
            if (IsNamedSpawnPoint(transform.name))
                AddSpawnPoint(transform, spawnPoints, seen);
        }

        spawnPoints.Sort(CompareSpawnPoints);
        _spawnPoints = spawnPoints.ToArray();

        if (_spawnPoints.Length == 0)
            Debug.LogWarning("GameNetworkManager: No spawn points found. Expected objects named SpawnPoint_01, SpawnPoint_02, etc.");
        else
            Debug.Log($"GameNetworkManager: Cached {_spawnPoints.Length} spawn point(s): {string.Join(", ", Array.ConvertAll(_spawnPoints, point => point.name))}");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    static void AddSpawnPoint (Transform spawnPoint, List<Transform> spawnPoints, HashSet<Transform> seen) {
        if (spawnPoint == null || !seen.Add(spawnPoint)) return;
        spawnPoints.Add(spawnPoint);
    }

    static bool IsNamedSpawnPoint (string objectName) =>
        TryGetSpawnPointIndex(objectName, out _);

    static int CompareSpawnPoints (Transform a, Transform b) {
        bool aHasIndex = TryGetSpawnPointIndex(a.name, out int aIndex);
        bool bHasIndex = TryGetSpawnPointIndex(b.name, out int bIndex);

        if (aHasIndex && bHasIndex && aIndex != bIndex)
            return aIndex.CompareTo(bIndex);
        if (aHasIndex != bHasIndex)
            return aHasIndex ? -1 : 1;

        return string.CompareOrdinal(a.name, b.name);
    }

    static bool TryGetSpawnPointIndex (string objectName, out int index) {
        const string prefix = "SpawnPoint_";
        index = 0;

        if (string.IsNullOrWhiteSpace(objectName)
            || !objectName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string suffix = objectName.Substring(prefix.Length);
        return int.TryParse(suffix, out index);
    }

    bool ClientHasPlayerObject (ulong clientId) =>
        NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)
        && client.PlayerObject != null;

    void RebuildRaceRuntime () {
        FindAnyObjectByType<RaceRuntime>()?.RebuildRacerList();
    }

    bool CanStartNetwork () {
        if (NetworkManager.Singleton == null) {
            Debug.LogError("GameNetworkManager: No NetworkManager found.");
            return false;
        }
        if (NetworkManager.Singleton.IsListening) {
            Debug.LogWarning("GameNetworkManager: NetworkManager is already running.");
            return false;
        }
        return true;
    }

    bool ConfigureUnityTransport (string address, ushort port, string listenAddress) {
        UnityTransport transport = GetOrCreateUnityTransport();
        if (transport == null) {
            Debug.LogError("GameNetworkManager: NetworkManager needs a UnityTransport component.");
            _statusMessage = "Network transport missing";
            return false;
        }
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        transport.SetConnectionData(true, address, port, listenAddress);
        return true;
    }

    UnityTransport GetOrCreateUnityTransport () {
        if (NetworkManager.Singleton == null) return null;
        if (!NetworkManager.Singleton.TryGetComponent(out UnityTransport transport)) {
            transport = NetworkManager.Singleton.gameObject.AddComponent<UnityTransport>();
        }
        return transport;
    }

    SteamP2PTransport GetOrCreateSteamTransport () {
        if (NetworkManager.Singleton == null) return null;
        if (!NetworkManager.Singleton.TryGetComponent(out SteamP2PTransport transport)) {
            transport = NetworkManager.Singleton.gameObject.AddComponent<SteamP2PTransport>();
        }
        return transport;
    }

    bool TryParseEndpoint (string endpoint, out string address, out ushort port) {
        string value = string.IsNullOrWhiteSpace(endpoint) ? "127.0.0.1" : endpoint.Trim();
        address = value;
        port = _port;

        if (value.StartsWith("[", StringComparison.Ordinal)) {
            int closingBracket = value.IndexOf(']');
            if (closingBracket <= 1) return false;

            address = value.Substring(1, closingBracket - 1).Trim();
            if (value.Length == closingBracket + 1)
                return !string.IsNullOrWhiteSpace(address);

            if (value[closingBracket + 1] != ':') return false;
            return ushort.TryParse(value.Substring(closingBracket + 2), out port)
                   && !string.IsNullOrWhiteSpace(address);
        }

        int separatorIndex = value.LastIndexOf(':');
        if (separatorIndex > -1 && value.IndexOf(':') == separatorIndex) {
            address = value.Substring(0, separatorIndex).Trim();
            return ushort.TryParse(value.Substring(separatorIndex + 1), out port)
                   && !string.IsNullOrWhiteSpace(address);
        }

        return !string.IsNullOrWhiteSpace(address);
    }

    void ApplyCommandLineOverrides () {
        if (ushort.TryParse(Environment.GetEnvironmentVariable("ARBITRIUM_PORT_GAME_PORT_INTERNAL"), out ushort edgegapPort))
            _port = edgegapPort;

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) {
            if (args[i].Equals("-port", StringComparison.OrdinalIgnoreCase)
                && ushort.TryParse(args[i + 1], out ushort port))
                _port = port;

            if (args[i].Equals("-listen", StringComparison.OrdinalIgnoreCase))
                _listenAddress = args[i + 1];

            if (args[i].Equals("-address", StringComparison.OrdinalIgnoreCase))
                _serverAddress = args[i + 1];

            if (args[i].Equals("-scene", StringComparison.OrdinalIgnoreCase))
                _gameSceneName = args[i + 1];
        }
    }

    string BuildConfiguredEndpoint () {
        string address = string.IsNullOrWhiteSpace(_serverAddress) ? "127.0.0.1" : _serverAddress.Trim();
        return $"{address}:{_port}";
    }
}
