using System;
using System.Collections.Generic;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Custom NetworkTransport for Netcode for GameObjects that uses Steamworks P2P networking.
/// Seamlessly traverses NAT and firewalls using Valve's relay network with Steam IDs as endpoints.
/// </summary>
public class SteamP2PTransport : NetworkTransport {

    public override ulong ServerClientId => 0;

    public override bool IsSupported => SteamManager.Initialized;

    [Header("Steam P2P Target")]
    [Tooltip("Target host Steam ID when connecting as client.")]
    public ulong TargetSteamID;

    struct QueuedEvent {
        public NetworkEvent type;
        public ulong clientId;
        public ArraySegment<byte> payload;
        public float receiveTime;
    }

    bool _isRunning;
    bool _isServer;
    ulong _nextClientId = 1;

    readonly Dictionary<ulong, CSteamID> _clientIdToSteamId = new Dictionary<ulong, CSteamID>();
    readonly Dictionary<ulong, ulong> _steamIdToClientId = new Dictionary<ulong, ulong>();
    readonly Queue<QueuedEvent> _eventQueue = new Queue<QueuedEvent>();

    Callback<P2PSessionRequest_t> _p2pSessionRequestCallback;
    Callback<P2PSessionConnectFail_t> _p2pSessionConnectFailCallback;

    public override void Initialize(NetworkManager networkManager = null) {
        // Nothing special required for transport initialization
    }

    public override bool StartServer() {
        if (!SteamManager.Initialized) {
            Debug.LogError("[SteamP2PTransport] Cannot start server: Steam is not initialized.");
            return false;
        }

        Cleanup();
        _isRunning = true;
        _isServer = true;
        _nextClientId = 1;

        RegisterCallbacks();
        Debug.Log("[SteamP2PTransport] Started Steam P2P host listening for connections.");
        return true;
    }

    public override bool StartClient() {
        if (!SteamManager.Initialized) {
            Debug.LogError("[SteamP2PTransport] Cannot start client: Steam is not initialized.");
            return false;
        }

        if (TargetSteamID == 0) {
            Debug.LogError("[SteamP2PTransport] Cannot start client: TargetSteamID is invalid (0).");
            return false;
        }

        Cleanup();
        _isRunning = true;
        _isServer = false;

        RegisterCallbacks();

        // Queue connection event so NGO initiates connection handshake to ServerClientId
        _eventQueue.Enqueue(new QueuedEvent {
            type = NetworkEvent.Connect,
            clientId = ServerClientId,
            payload = default,
            receiveTime = Time.realtimeSinceStartup
        });

        Debug.Log($"[SteamP2PTransport] Started Steam P2P client targeting host Steam ID: {TargetSteamID}");
        return true;
    }

    public override void DisconnectRemoteClient(ulong clientId) {
        if (!_isRunning || !_isServer) return;

        if (_clientIdToSteamId.TryGetValue(clientId, out CSteamID steamId)) {
            // Send disconnect control packet on channel 1
            byte[] disconnectMsg = { 0xFF };
            SteamNetworking.SendP2PPacket(steamId, disconnectMsg, 1, EP2PSend.k_EP2PSendReliable, 1);
            SteamNetworking.CloseP2PSessionWithUser(steamId);

            _clientIdToSteamId.Remove(clientId);
            _steamIdToClientId.Remove(steamId.m_SteamID);

            _eventQueue.Enqueue(new QueuedEvent {
                type = NetworkEvent.Disconnect,
                clientId = clientId,
                payload = default,
                receiveTime = Time.realtimeSinceStartup
            });
            Debug.Log($"[SteamP2PTransport] Disconnected remote client {clientId} ({steamId.m_SteamID}).");
        }
    }

    public override void DisconnectLocalClient() {
        if (!_isRunning) return;

        if (!_isServer && TargetSteamID != 0) {
            CSteamID target = new CSteamID(TargetSteamID);
            byte[] disconnectMsg = { 0xFF };
            SteamNetworking.SendP2PPacket(target, disconnectMsg, 1, EP2PSend.k_EP2PSendReliable, 1);
            SteamNetworking.CloseP2PSessionWithUser(target);

            _eventQueue.Enqueue(new QueuedEvent {
                type = NetworkEvent.Disconnect,
                clientId = ServerClientId,
                payload = default,
                receiveTime = Time.realtimeSinceStartup
            });
            Debug.Log("[SteamP2PTransport] Disconnected local client from Steam host.");
        }
    }

    public override ulong GetCurrentRtt(ulong clientId) {
        return 0; // Steam classic P2P doesn't expose raw RTT via this API
    }

    public override void Shutdown() {
        if (!_isRunning) return;

        Debug.Log("[SteamP2PTransport] Shutting down Steam P2P transport.");
        _isRunning = false;

        if (_isServer) {
            foreach (var kvp in _clientIdToSteamId) {
                SteamNetworking.CloseP2PSessionWithUser(kvp.Value);
            }
        } else if (TargetSteamID != 0) {
            SteamNetworking.CloseP2PSessionWithUser(new CSteamID(TargetSteamID));
        }

        Cleanup();
        DisposeCallbacks();
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery) {
        if (!_isRunning || !SteamManager.Initialized) return;

        CSteamID targetSteamId;
        if (_isServer) {
            if (clientId == ServerClientId) return; // Can't send to self
            if (!_clientIdToSteamId.TryGetValue(clientId, out targetSteamId)) {
                Debug.LogWarning($"[SteamP2PTransport] Attempted to send to unknown client {clientId}.");
                return;
            }
        } else {
            targetSteamId = new CSteamID(TargetSteamID);
        }

        EP2PSend sendType = (networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced)
                            && payload.Count <= 1200
            ? EP2PSend.k_EP2PSendUnreliableNoDelay
            : EP2PSend.k_EP2PSendReliable;

        byte[] buffer;
        if (payload.Offset == 0 && payload.Count == payload.Array.Length) {
            buffer = payload.Array;
        } else {
            buffer = new byte[payload.Count];
            Buffer.BlockCopy(payload.Array, payload.Offset, buffer, 0, payload.Count);
        }

        SteamNetworking.SendP2PPacket(targetSteamId, buffer, (uint)payload.Count, sendType, 0);
    }

    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime) {
        clientId = 0;
        payload = default;
        receiveTime = Time.realtimeSinceStartup;

        if (!_isRunning || !SteamManager.Initialized) {
            return NetworkEvent.Nothing;
        }

        // 1. Process queued events (Connect / Disconnect / previous frames)
        if (_eventQueue.Count > 0) {
            QueuedEvent evt = _eventQueue.Dequeue();
            clientId = evt.clientId;
            payload = evt.payload;
            receiveTime = evt.receiveTime;
            return evt.type;
        }

        // 2. Poll Control channel (channel 1) for disconnect notifications
        while (SteamNetworking.IsP2PPacketAvailable(out uint ctrlSize, 1)) {
            byte[] ctrlBuf = new byte[ctrlSize];
            if (SteamNetworking.ReadP2PPacket(ctrlBuf, ctrlSize, out uint _, out CSteamID senderId, 1)) {
                if (_isServer && _steamIdToClientId.TryGetValue(senderId.m_SteamID, out ulong senderClientId)) {
                    _steamIdToClientId.Remove(senderId.m_SteamID);
                    _clientIdToSteamId.Remove(senderClientId);
                    SteamNetworking.CloseP2PSessionWithUser(senderId);

                    clientId = senderClientId;
                    return NetworkEvent.Disconnect;
                }

                if (!_isServer && senderId.m_SteamID == TargetSteamID) {
                    SteamNetworking.CloseP2PSessionWithUser(senderId);
                    clientId = ServerClientId;
                    return NetworkEvent.Disconnect;
                }
            }
        }

        // 3. Poll Game Data channel (channel 0)
        while (SteamNetworking.IsP2PPacketAvailable(out uint dataSize, 0)) {
            byte[] dataBuf = new byte[dataSize];
            if (SteamNetworking.ReadP2PPacket(dataBuf, dataSize, out uint bytesRead, out CSteamID senderId, 0)) {
                if (_isServer) {
                    if (!_steamIdToClientId.TryGetValue(senderId.m_SteamID, out ulong assignedClientId)) {
                        assignedClientId = _nextClientId++;
                        _steamIdToClientId[senderId.m_SteamID] = assignedClientId;
                        _clientIdToSteamId[assignedClientId] = senderId;

                        // Queue data payload immediately after connect event
                        _eventQueue.Enqueue(new QueuedEvent {
                            type = NetworkEvent.Data,
                            clientId = assignedClientId,
                            payload = new ArraySegment<byte>(dataBuf, 0, (int)bytesRead),
                            receiveTime = receiveTime
                        });

                        clientId = assignedClientId;
                        return NetworkEvent.Connect;
                    }

                    clientId = assignedClientId;
                    payload = new ArraySegment<byte>(dataBuf, 0, (int)bytesRead);
                    return NetworkEvent.Data;
                } else {
                    if (senderId.m_SteamID != TargetSteamID) {
                        // Ignore packets from unexpected senders
                        continue;
                    }

                    clientId = ServerClientId;
                    payload = new ArraySegment<byte>(dataBuf, 0, (int)bytesRead);
                    return NetworkEvent.Data;
                }
            }
        }

        return NetworkEvent.Nothing;
    }

    void OnP2PSessionRequest(P2PSessionRequest_t callback) {
        if (!_isRunning || !_isServer) return;

        Debug.Log($"[SteamP2PTransport] Accepting incoming P2P session from {callback.m_steamIDRemote.m_SteamID}");
        SteamNetworking.AcceptP2PSessionWithUser(callback.m_steamIDRemote);
    }

    void OnP2PSessionConnectFail(P2PSessionConnectFail_t callback) {
        if (!_isRunning) return;

        Debug.LogWarning($"[SteamP2PTransport] P2P connection failed with {callback.m_steamIDRemote.m_SteamID}. Error code: {callback.m_eP2PSessionError}");

        if (_isServer && _steamIdToClientId.TryGetValue(callback.m_steamIDRemote.m_SteamID, out ulong clientId)) {
            _steamIdToClientId.Remove(callback.m_steamIDRemote.m_SteamID);
            _clientIdToSteamId.Remove(clientId);
            SteamNetworking.CloseP2PSessionWithUser(callback.m_steamIDRemote);

            _eventQueue.Enqueue(new QueuedEvent {
                type = NetworkEvent.Disconnect,
                clientId = clientId,
                payload = default,
                receiveTime = Time.realtimeSinceStartup
            });
        } else if (!_isServer && callback.m_steamIDRemote.m_SteamID == TargetSteamID) {
            SteamNetworking.CloseP2PSessionWithUser(callback.m_steamIDRemote);
            _eventQueue.Enqueue(new QueuedEvent {
                type = NetworkEvent.Disconnect,
                clientId = ServerClientId,
                payload = default,
                receiveTime = Time.realtimeSinceStartup
            });
        }
    }

    void RegisterCallbacks() {
        DisposeCallbacks();
        _p2pSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
        _p2pSessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);
    }

    void DisposeCallbacks() {
        _p2pSessionRequestCallback?.Dispose();
        _p2pSessionRequestCallback = null;
        _p2pSessionConnectFailCallback?.Dispose();
        _p2pSessionConnectFailCallback = null;
    }

    void Cleanup() {
        _clientIdToSteamId.Clear();
        _steamIdToClientId.Clear();
        _eventQueue.Clear();
    }

    void OnDestroy() {
        Shutdown();
    }
}
