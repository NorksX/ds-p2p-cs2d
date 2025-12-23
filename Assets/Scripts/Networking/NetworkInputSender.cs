using UnityEngine;
using LiteNetLib;

/// <summary>
/// Sends local player inputs to the host every tick
/// Attach to local player
/// </summary>
public class NetworkInputSender : MonoBehaviour
{
    private NetworkedPlayer networkedPlayer;
    private LocalInputBuffer inputBuffer;
    
    private void Awake()
    {
        networkedPlayer = GetComponent<NetworkedPlayer>();
        inputBuffer = GetComponent<LocalInputBuffer>();
    }
    
    private void Start()
    {
        // Disable on remote players - they don't send inputs
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.Log($"[NetworkInputSender] Disabling on REMOTE player {networkedPlayer.playerId}");
            this.enabled = false;
            return;
        }
        
        Debug.Log("[NetworkInputSender] Enabled for LOCAL player");
        
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick += HandleTick;
        }
    }
    
    private void OnDestroy()
    {
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick -= HandleTick;
        }
    }
    
    private void HandleTick(int tick)
    {
        // Comprehensive null checks
        if (NetworkManager.Instance == null || networkedPlayer == null || inputBuffer == null)
            return;
            
        // Only send inputs if we're a client (not host)
        // Host simulates locally, doesn't need to send to itself
        if (NetworkManager.Instance.IsHost)
            return;
        
        if (!networkedPlayer.isLocalPlayer)
            return;
        
        // Get input for this tick
        if (inputBuffer.TryGet(tick - 1, out InputCommand cmd))
        {
            // Send to host
            InputCommandMessage message = new InputCommandMessage(cmd);
            NetworkManager.Instance.SendMessageToAll(message, DeliveryMethod.Sequenced);
        }
    }
}
