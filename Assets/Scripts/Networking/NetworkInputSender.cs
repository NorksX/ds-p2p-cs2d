using UnityEngine;
using LiteNetLib;
using System.Collections;

/// <summary>
/// Sends local player inputs to the host every tick
/// Attach to local player
/// </summary>
public class NetworkInputSender : MonoBehaviour
{
    private NetworkedPlayer networkedPlayer;
    private LocalInputBuffer inputBuffer;
    
    // Don't get components in Awake - PlayerSpawner adds them AFTER instantiation
    
    private void Start()
    {
        StartCoroutine(InitializeAfterSpawn());
    }
    
    private IEnumerator InitializeAfterSpawn()
    {
        // Wait one frame to ensure PlayerSpawner has added/configured components
        yield return null;
        
        // NOW get the components (they exist now)
        networkedPlayer = GetComponent<NetworkedPlayer>();
        inputBuffer = GetComponent<LocalInputBuffer>();
        
        Debug.Log($"[NetworkInputSender] Checking player: networkedPlayer={(networkedPlayer != null ? networkedPlayer.playerId : "NULL")}, isLocalPlayer={networkedPlayer?.isLocalPlayer}");
        
        // Disable on remote players
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.Log($"[NetworkInputSender] Disabling on REMOTE player {networkedPlayer.playerId}");
            this.enabled = false;
            yield break;
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
            
        // Stop sending if not connected or migrating
        // Note: Currently the game runs in 'InLobby' state, 'Connected' is unused.
        if (NetworkManager.Instance.State != ConnectionState.Connected && NetworkManager.Instance.State != ConnectionState.InLobby)
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
            
            // Debug.Log($"[NetworkInputSender] Sent input to host, tick={tick}, move={cmd.move}");
        }
    }
}
