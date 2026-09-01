using UnityEngine;
using TMPro;

/// <summary>
/// Handles UI transitions for networking lobby
/// </summary>
public class NetworkTestUI : MonoBehaviour
{
    [Header("Lobby Panels")]
    [SerializeField] private GameObject connectionPanel;
    [SerializeField] private GameObject lobbyPanel;
    
    [Header("UI Elements")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_InputField hostAddressInput;

    [Tooltip("Used when the address field has no explicit :port")]
    [SerializeField] private int defaultPort = 7777;

    [Tooltip("Port to host on. Leave empty for the default.")]
    [SerializeField] private TMP_InputField hostPortInput;

    [SerializeField] private ServerBrowserUI serverBrowser;
    
    private void Start()
    {
        if (hostAddressInput != null)
        {
            hostAddressInput.text = "127.0.0.1"; // localhost for testing
        }
        
        // Subscribe to events
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnPeerJoined += HandlePeerJoined;
            NetworkManager.Instance.OnPeerLeft += HandlePeerLeft;
        }
        
        // Initial UI state
        ShowConnectionPanel();
        
        UpdateStatus();
    }
    
    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnPeerJoined -= HandlePeerJoined;
            NetworkManager.Instance.OnPeerLeft -= HandlePeerLeft;
        }
    }
    
    private void HandlePeerJoined(PeerInfo info)
    {
        // If we just joined a lobby (and we are not host), ensure we show lobby panel
        if (!NetworkManager.Instance.IsHost)
        {
            ShowLobbyPanel();
        }
        UpdateStatus();
    }
    
    private void HandlePeerLeft(PeerInfo info)
    {
        // If we got disconnected (and are now fully disconnected), show connection panel
        if (NetworkManager.Instance.State == ConnectionState.Disconnected)
        {
            ShowConnectionPanel();
        }
        UpdateStatus();
    }
    
    private void ShowConnectionPanel()
    {
        if (connectionPanel != null) connectionPanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }
    
    private void ShowLobbyPanel()
    {
        if (connectionPanel != null) connectionPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }
    
    // Call this from Update to handle cases where state changes without event (e.g. connection failure)
    private void CheckConnectionState()
    {
        if (NetworkManager.Instance == null) return;
        
        // If we think we are in lobby/game but NetworkManager says disconnected, reset UI
        if ((!connectionPanel.activeSelf) && NetworkManager.Instance.State == ConnectionState.Disconnected)
        {
            ShowConnectionPanel();
        }
    }
    
    private void Update()
    {
        CheckConnectionState();
        UpdateStatus();
    }
    
    public void OnHostButtonClicked()
    {
        if (NetworkManager.Instance == null)
        {
            Debug.LogError("NetworkManager not found!");
            return;
        }

        int port = defaultPort;

        // Lets one machine host several instances - the second cannot reuse the first's port.
        if (hostPortInput != null && !string.IsNullOrWhiteSpace(hostPortInput.text))
        {
            if (!int.TryParse(hostPortInput.text.Trim(), out port) || port <= 0 || port > 65535)
            {
                Debug.LogWarning($"[NetworkTestUI] '{hostPortInput.text}' is not a valid port");
                return;
            }
        }

        if (NetworkManager.Instance.HostLobby(port))
        {
            Debug.Log($"Hosting lobby on {port}!");
            ShowLobbyPanel();
        }
    }
    
    /// <summary>Join button now opens the browser rather than dialling a guessed endpoint.</summary>
    public void OnOpenServerBrowserClicked()
    {
        if (serverBrowser != null)
            serverBrowser.Open();
        else
            OnJoinButtonClicked();
    }

    public void OnJoinButtonClicked()
    {
        if (NetworkManager.Instance == null)
        {
            Debug.LogError("NetworkManager not found!");
            return;
        }

        ParseEndpoint(hostAddressInput?.text, out string address, out int port);

        bool success = NetworkManager.Instance.JoinLobby(address, port);

        if (success)
        {
            Debug.Log($"Connecting to {address}:{port}...");
            // Don't switch UI yet! Wait for connection to actually happen or set a "Connecting..." state
            // But for simplicity, we switch, and if it fails, CheckConnectionState will revert it
            ShowLobbyPanel();
        }
    }

    // Accepts "ip" or "ip:port". Typing the port matters over a Hamachi-style virtual LAN,
    // where broadcast discovery is unreliable.
    private void ParseEndpoint(string raw, out string address, out int port)
    {
        address = string.IsNullOrWhiteSpace(raw) ? "127.0.0.1" : raw.Trim();
        port = defaultPort;

        int separator = address.LastIndexOf(':');
        if (separator <= 0)
            return;

        string portText = address.Substring(separator + 1);

        if (int.TryParse(portText, out int parsed) && parsed > 0 && parsed <= 65535)
        {
            port = parsed;
            address = address.Substring(0, separator);
        }
        else
        {
            Debug.LogWarning($"[NetworkTestUI] Could not read a port from '{raw}', using {defaultPort}");
        }
    }
    
    private void UpdateStatus()
    {
        if (statusText == null || NetworkManager.Instance == null)
            return;

        string status = $"State: {NetworkManager.Instance.State}\n";
        status += $"Role: {(NetworkManager.Instance.IsHost ? "HOST" : "CLIENT")}\n";
        status += $"Connected Peers: {NetworkManager.Instance.ConnectedPeers.Count}";

        if (NetworkManager.Instance.IsHost)
            status += $"\nHosting on port {NetworkManager.Instance.LocalPort}";

        // Add a prominent warning if host with no peers
        if (NetworkManager.Instance.IsHost && NetworkManager.Instance.ConnectedPeers.Count == 0)
        {
            status += "\n\nWAITING FOR PLAYERS...";
        }

        statusText.text = status;
    }
}
