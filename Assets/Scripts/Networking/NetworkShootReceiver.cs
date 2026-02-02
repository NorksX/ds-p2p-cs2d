using UnityEngine;
using LiteNetLib;

/// <summary>
/// CLIENT ONLY: Receives shoot events from host and visualizes them
/// </summary>
public class NetworkShootReceiver : MonoBehaviour
{
    private void Start()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived += HandleMessage;
            Debug.Log("[NetworkShootReceiver] Subscribed to OnMessageReceived");
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
        // Log EVERY message to see if we're receiving anything
        Debug.Log($"[NetworkShootReceiver] HandleMessage called! Type={message.GetMessageType()}");
        
        if (message.GetMessageType() == MessageType.ShootEvent)
        {
            ShootEventMessage shootMsg = (ShootEventMessage)message;
            
            Debug.Log($"[NetworkShootReceiver] Received shoot event from {shootMsg.shooterId}, origin={shootMsg.origin}, aimDir={shootMsg.aimDir}");
            
            // Find the player who shot
            if (PlayerSpawner.Instance != null)
            {
                NetworkedPlayer shooter = PlayerSpawner.Instance.GetPlayer(shootMsg.shooterId);
                
                if (shooter != null && shooter.playerController != null)
                {
                    // Visualize the shoot on the client
                    shooter.playerController.SimulateShoot(shootMsg.aimDir);
                    Debug.Log($"[NetworkShootReceiver] Visualized shoot from {shootMsg.shooterId}");
                }
                else
                {
                    Debug.LogWarning($"[NetworkShootReceiver] Could not find player {shootMsg.shooterId} or playerController is null");
                }
            }
            else
            {
                Debug.LogWarning("[NetworkShootReceiver] PlayerSpawner.Instance is NULL!");
            }
        }
    }
}
