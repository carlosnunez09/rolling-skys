using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuNetworkUI : MonoBehaviour {

    [SerializeField] Button _hostButton;
    [SerializeField] Button _serverButton;
    [SerializeField] Button _clientButton;
    [SerializeField] Button _disconnectButton;
    [SerializeField] TMP_InputField _addressInput;
    [SerializeField] TextMeshProUGUI _statusText;

    void Awake () {
#if UNITY_SERVER
        gameObject.SetActive(false);
        return;
#else
        _hostButton?.onClick.AddListener(StartHost);
        _serverButton?.onClick.AddListener(StartServer);
        _clientButton?.onClick.AddListener(StartClient);
        _disconnectButton?.onClick.AddListener(Disconnect);
#endif
    }

    void Start () {
#if !UNITY_SERVER
        if (_addressInput != null && string.IsNullOrWhiteSpace(_addressInput.text)) {
            if (GameNetworkManager.Instance != null)
                _addressInput.text = $"{GameNetworkManager.Instance.ServerAddress}:{GameNetworkManager.Instance.Port}";
            else
                _addressInput.text = "127.0.0.1:7777";
        }
#endif
    }

    void OnDestroy () {
        _hostButton?.onClick.RemoveListener(StartHost);
        _serverButton?.onClick.RemoveListener(StartServer);
        _clientButton?.onClick.RemoveListener(StartClient);
        _disconnectButton?.onClick.RemoveListener(Disconnect);
    }

    void Update () {
        RefreshState();
    }

    public void StartHost () {
        GameNetworkManager.Instance?.StartHost();
    }

    public void StartServer () {
        GameNetworkManager.Instance?.StartServer();
    }

    public void StartClient () {
        if (GameNetworkManager.Instance == null) return;

        string endpoint = _addressInput != null ? _addressInput.text : null;
        if (!string.IsNullOrWhiteSpace(endpoint))
            GameNetworkManager.Instance.StartClient(endpoint.Trim());
        else
            GameNetworkManager.Instance.StartClient();
    }

    public void Disconnect () {
        GameNetworkManager.Instance?.Shutdown();
    }

    void RefreshState () {
        NetworkManager manager = NetworkManager.Singleton;
        bool listening = manager != null && manager.IsListening;

        if (_hostButton != null) _hostButton.interactable = !listening;
        if (_serverButton != null) _serverButton.interactable = !listening;
        if (_clientButton != null) _clientButton.interactable = !listening;
        if (_disconnectButton != null) _disconnectButton.interactable = listening;
        if (_addressInput != null) _addressInput.interactable = !listening;

        if (_statusText == null) return;

        if (manager == null) {
            _statusText.text = "NetworkManager missing";
            return;
        }

        if (GameNetworkManager.Instance != null) {
            _statusText.text = GameNetworkManager.Instance.StatusMessage;
            return;
        }

        if (!manager.IsListening) {
            _statusText.text = "Offline - port 7777";
        } else if (manager.IsHost) {
            _statusText.text = "Hosting";
        } else if (manager.IsServer) {
            _statusText.text = "Server";
        } else if (manager.IsClient) {
            _statusText.text = "Client connected";
        }
    }
}
