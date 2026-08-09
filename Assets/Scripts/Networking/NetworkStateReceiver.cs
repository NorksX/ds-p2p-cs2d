using UnityEngine;
using LiteNetLib;

/// <summary>
/// CLIENT ONLY: Recieve state
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
        // CRITICAL: Only clients should receive and apply state updates
        // The host is the authority and generates the state, not receives it
        if (NetworkManager.Instance == null || NetworkManager.Instance.IsHost)
        {
            return;
        }
        
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

            if (networkedPlayer == null)
                continue;

            if (networkedPlayer.isLocalPlayer)
            {
                // Our own position IS corrected now - it used to be skipped entirely, which is
                // why local prediction drifted from the host permanently.
                PlayerTickSimulation simulation = networkedPlayer.GetComponent<PlayerTickSimulation>();

                if (simulation != null)
                    simulation.ApplyAuthoritativeState(playerState.lastProcessedInputTick, playerState.position);

                continue;
            }

            // Remote players are buffered and played back slightly late, rather than snapped.
            RemoteInterpolator interpolator = networkedPlayer.GetComponent<RemoteInterpolator>();

            if (interpolator != null)
            {
                interpolator.Push(playerState.position, playerState.rotation);
            }
            else
            {
                networkedPlayer.transform.position = new Vector3(
                    playerState.position.x,
                    playerState.position.y,
                    networkedPlayer.transform.position.z);

                networkedPlayer.transform.rotation = Quaternion.Euler(0, 0, playerState.rotation);
            }
        }
    }
}
