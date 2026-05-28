using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

#if !UNITY_SERVER || UNITY_EDITOR
using Steamworks;
#endif

[RequireComponent(typeof(MovingCar))]
public class NetworkCarController : NetworkBehaviour {

    [SerializeField] TextMeshPro _nameTag; // world-space label above car
    [SerializeField] bool _useSteamIdentity;

    readonly NetworkVariable<ulong> _steamId = new NetworkVariable<ulong>();
    readonly NetworkVariable<FixedString64Bytes> _displayName = new NetworkVariable<FixedString64Bytes>();

    public override void OnNetworkSpawn () {
        _steamId.OnValueChanged += OnSteamIdChanged;
        _displayName.OnValueChanged += OnDisplayNameChanged;

        if (IsOwner) {
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
        string displayName = $"Player {NetworkManager.Singleton.LocalClientId}";

        if (_useSteamIdentity && SteamManager.Initialized) {
            try {
                steamId = SteamUser.GetSteamID().m_SteamID;

                string personaName = SteamFriends.GetPersonaName();
                if (!string.IsNullOrWhiteSpace(personaName))
                    displayName = personaName;
            } catch (System.Exception e) {
                Debug.Log($"NetworkCarController: Steam identity unavailable, using Netcode fallback. {e.Message}");
            }
        } else if (_useSteamIdentity) {
            Debug.Log("NetworkCarController: Steam is not initialized, using Netcode fallback identity.");
        }

        SubmitIdentityServerRpc(steamId, new FixedString64Bytes(displayName));
    }
#endif

    void RefreshNameTag () {
        if (_nameTag == null) return;

#if !UNITY_SERVER || UNITY_EDITOR
        _nameTag.text = _displayName.Value.Length > 0
            ? _displayName.Value.ToString()
            : $"Player {OwnerClientId}";
        _nameTag.gameObject.SetActive(!IsOwner); // hide your own name tag
#endif
    }
}
