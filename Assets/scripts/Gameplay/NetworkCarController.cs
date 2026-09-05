using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

#if !UNITY_SERVER || UNITY_EDITOR
using Steamworks;
#endif

[RequireComponent(typeof(MovingCar))]
public class NetworkCarController : NetworkBehaviour {

    [SerializeField] TMP_Text _nameTag; // local label under this car prefab
    [SerializeField] CarNameTagBillboard _billboard;
    [SerializeField] string _offlineDisplayName = "Player";
    [SerializeField] bool _useSteamIdentity;

    MovingCar _car;

    readonly NetworkVariable<ulong> _steamId = new NetworkVariable<ulong>();
    readonly NetworkVariable<FixedString64Bytes> _displayName = new NetworkVariable<FixedString64Bytes>();

    void Awake () {
        _car = GetComponent<MovingCar>();
        ResolveNameTag();
    }

    void Start () {
        if (_car != null && _car.OfflineSceneTestActive)
            RefreshNameTag();
    }

    public override void OnNetworkSpawn () {
        ResolveNameTag();

        _steamId.OnValueChanged += OnSteamIdChanged;
        _displayName.OnValueChanged += OnDisplayNameChanged;

        if (_car != null && _car.HasLocalControl) {
#if !UNITY_SERVER || UNITY_EDITOR
            SubmitLocalIdentity();
#endif
        }

        RefreshNameTag();
    }

    public override void OnNetworkDespawn () {
        _steamId.OnValueChanged -= OnSteamIdChanged;
        _displayName.OnValueChanged -= OnDisplayNameChanged;
    }

    void OnSteamIdChanged (ulong previousValue, ulong newValue) => RefreshNameTag();

    void OnDisplayNameChanged (FixedString64Bytes previousValue, FixedString64Bytes newValue) => RefreshNameTag();

    void ResolveNameTag () {
        if (_billboard == null)
            _billboard = GetComponentInChildren<CarNameTagBillboard>(true);

        if (_nameTag != null) return;

        Transform searchRoot = transform.parent != null ? transform.parent : transform;
        TMP_Text[] childLabels = searchRoot.GetComponentsInChildren<TMP_Text>(true);

        foreach (TMP_Text childLabel in childLabels) {
            string childName = childLabel.gameObject.name;
            if (childName.IndexOf("playername", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                childName.IndexOf("nametag", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                childName.IndexOf("nametab", System.StringComparison.OrdinalIgnoreCase) >= 0) {
                _nameTag = childLabel;
                return;
            }
        }

        if (childLabels.Length > 0)
            _nameTag = childLabels[0];
    }

    [ServerRpc]
    void SubmitIdentityServerRpc (ulong steamId, FixedString64Bytes displayName, ServerRpcParams rpcParams = default) {
        _steamId.Value = steamId;
        _displayName.Value = displayName.Length > 0
            ? displayName
            : new FixedString64Bytes($"Player {rpcParams.Receive.SenderClientId}");
    }

#if !UNITY_SERVER || UNITY_EDITOR
    void SubmitLocalIdentity () {
        ulong steamId = 0;
        ulong localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        string displayName = ResolveLocalDisplayName($"Player {localClientId}");

        if (_useSteamIdentity && SteamManager.Initialized) {
            try {
                steamId = SteamUser.GetSteamID().m_SteamID;
            } catch (System.Exception e) {
                Debug.Log($"NetworkCarController: Steam ID unavailable, using Netcode fallback. {e.Message}");
            }
        }

        SubmitIdentityServerRpc(steamId, new FixedString64Bytes(displayName));
    }

    string ResolveLocalDisplayName (string fallbackName) {
        string displayName = !string.IsNullOrWhiteSpace(_offlineDisplayName)
            ? _offlineDisplayName
            : fallbackName;

        if (_useSteamIdentity && SteamManager.Initialized) {
            try {
                string personaName = SteamFriends.GetPersonaName();
                if (!string.IsNullOrWhiteSpace(personaName))
                    displayName = personaName;
            } catch (System.Exception e) {
                Debug.Log($"NetworkCarController: Steam identity unavailable, using Netcode fallback. {e.Message}");
            }
        } else if (_useSteamIdentity) {
            Debug.Log("NetworkCarController: Steam is not initialized, using Netcode fallback identity.");
        }

        return displayName;
    }
#endif

    void RefreshNameTag () {
        ResolveNameTag();

#if !UNITY_SERVER || UNITY_EDITOR
        bool isLocalCar = _car != null && _car.HasLocalControl;
        string displayName = _displayName.Value.Length > 0
            ? _displayName.Value.ToString()
            : isLocalCar
                ? ResolveLocalDisplayName("Player")
                : $"Player {OwnerClientId}";

        if (_nameTag != null) {
            _nameTag.text = displayName;
            _nameTag.gameObject.SetActive(!isLocalCar); // hide your own name tag
        }

        if (_billboard != null)
            _billboard.SetText(displayName);
#endif
    }
}
