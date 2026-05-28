using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuNetworkUI : MonoBehaviour {

    [SerializeField] TMP_InputField _addressInput;
    [SerializeField] Button _hostButton;
    [SerializeField] Button _clientButton;
    [SerializeField] Button _disconnectButton;
    [SerializeField] TextMeshProUGUI _statusText;

    void Awake () {
#if UNITY_SERVER && !UNITY_EDITOR
        gameObject.SetActive(false);
        return;
#else
        _hostButton?.onClick.AddListener(StartHost);
        _clientButton?.onClick.AddListener(StartClient);
        _disconnectButton?.onClick.AddListener(Disconnect);

        if (_addressInput != null && string.IsNullOrWhiteSpace(_addressInput.text))
            _addressInput.text = "127.0.0.1:7777";
#endif
    }

    void OnDestroy () {
        _hostButton?.onClick.RemoveListener(StartHost);
        _clientButton?.onClick.RemoveListener(StartClient);
        _disconnectButton?.onClick.RemoveListener(Disconnect);
    }

    void Update () {
        RefreshState();
    }

    public void StartHost () {
        GameNetworkManager.Instance?.StartHost();
    }

    public void StartClient () {
        string address = _addressInput != null ? _addressInput.text : "127.0.0.1:7777";
        GameNetworkManager.Instance?.StartClient(address);
    }

    public void Disconnect () {
        GameNetworkManager.Instance?.Shutdown();
    }

    void RefreshState () {
        NetworkManager manager = NetworkManager.Singleton;
        bool listening = manager != null && manager.IsListening;

        if (_hostButton != null) _hostButton.interactable = !listening;
        if (_clientButton != null) _clientButton.interactable = !listening;
        if (_disconnectButton != null) _disconnectButton.interactable = listening;

        if (_statusText == null) return;

        if (manager == null) {
            _statusText.text = "NetworkManager missing";
        } else if (GameNetworkManager.Instance != null) {
            _statusText.text = GameNetworkManager.Instance.StatusMessage;
        } else if (!manager.IsListening) {
            ushort port = GameNetworkManager.Instance != null ? GameNetworkManager.Instance.Port : (ushort)7777;
            _statusText.text = $"Offline - port {port}";
        } else if (manager.IsHost) {
            _statusText.text = "Hosting";
        } else if (manager.IsServer) {
            _statusText.text = "Server";
        } else if (manager.IsClient) {
            _statusText.text = "Client connected";
        }
    }
}
