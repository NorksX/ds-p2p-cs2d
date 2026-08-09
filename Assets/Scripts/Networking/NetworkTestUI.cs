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
        if (NetworkManager.Instance != null)
        {
            bool success = NetworkManager.Instance.HostLobby();
            
            if (success)
            {
                Debug.Log("Hosting lobby!");
                ShowLobbyPanel();
            }
        }
        else
        {
            Debug.LogError("NetworkManager not found!");
        }
    }
    
    public void OnJoinButtonClicked()
    {
        if (NetworkManager.Instance != null)
        {
            string address = hostAddressInput?.text ?? "127.0.0.1";
            bool success = NetworkManager.Instance.JoinLobby(address, 7777);
            
            if (success)
            {
                Debug.Log($"Connecting to {address}:7777...");
                // Don't switch UI yet! Wait for connection to actually happen or set a "Connecting..." state
                // But for simplicity, we switch, and if it fails, CheckConnectionState will revert it
                ShowLobbyPanel(); 
            }
        }
        else
        {
            Debug.LogError("NetworkManager not found!");
        }
    }
    
    public void OnLeaveButtonClicked()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.LeaveLobby();
            
            // Show connection panel, hide lobby panel
            if (connectionPanel != null)
                connectionPanel.SetActive(true);
            
            if (lobbyPanel != null)
                lobbyPanel.SetActive(false);
        }
    }
    
    private void UpdateStatus()
    {
        if (statusText == null || NetworkManager.Instance == null)
            return;
        
        string status = $"State: {NetworkManager.Instance.State}\n";
        status += $"Role: {(NetworkManager.Instance.IsHost ? "HOST" : "CLIENT")}\n";
        status += $"Connected Peers: {NetworkManager.Instance.ConnectedPeers.Count}";
        
        // Add a prominent warning if host with no peers
        if (NetworkManager.Instance.IsHost && NetworkManager.Instance.ConnectedPeers.Count == 0)
        {
            status += "\n\nWAITING FOR PLAYERS...";
        }
        
        statusText.text = status;
    }
}
