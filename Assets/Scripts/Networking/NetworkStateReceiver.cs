using UnityEngine;
using LiteNetLib;

/// <summary>
/// CLIENT ONLY: Receives state updates from host and applies to remote players
/// </summary>
public class NetworkStateReceiver : MonoBehaviour
{
    private void Start()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived += HandleMessage;
        }
    }
    
    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived -= HandleMessage;
        }
    }
    
    private void HandleMessage(INetworkMessage message, NetPeer peer)
    {
        if (message.GetMessageType() == MessageType.StateUpdate)
        {
            StateUpdateMessage stateMsg = (StateUpdateMessage)message;
            ApplyStateUpdate(stateMsg);
        }
    }
    
    private void ApplyStateUpdate(StateUpdateMessage stateMsg)
    {
        if (PlayerSpawner.Instance == null)
            return;
        
        foreach (var playerState in stateMsg.playerStates)
        {
            NetworkedPlayer networkedPlayer = PlayerSpawner.Instance.GetPlayer(playerState.playerId);
            
            if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
            {
                // Update remote player position and rotation
                networkedPlayer.transform.position = new Vector3(
                    playerState.position.x,
                    playerState.position.y,
                    networkedPlayer.transform.position.z
                );
                
                networkedPlayer.transform.rotation = Quaternion.Euler(0, 0, playerState.rotation);
            }
        }
    }
}
