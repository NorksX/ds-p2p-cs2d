using UnityEngine;
using LiteNetLib;
using System.Collections.Generic;

/// <summary>
/// HOST ONLY: Collects inputs from all players and broadcasts state updates
/// </summary>
public class NetworkStateHost : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private int stateUpdateIntervalTicks = 1; // Send state every tick
    
    private int ticksSinceLastUpdate = 0;
    private Dictionary<string, InputCommand> receivedInputs = new Dictionary<string, InputCommand>();
    
    private void Start()
    {
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick += HandleTick;
        }
        
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived += HandleMessage;
        }
    }
    
    private void OnDestroy()
    {
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick -= HandleTick;
        }
        
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived -= HandleMessage;
        }
    }
    
    private void HandleMessage(INetworkMessage message, NetPeer peer)
    {
        // Only host processes input commands
        if (!NetworkManager.Instance.IsHost)
            return;
        
        if (message.GetMessageType() == MessageType.InputCommand)
        {
            InputCommandMessage inputMsg = (InputCommandMessage)message;
            
            // Store the input for this player
            receivedInputs[inputMsg.inputCommand.playerId] = inputMsg.inputCommand;
        }
    }
    
    private void HandleTick(int tick)
    {
        // Only host sends state updates
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsHost)
            return;
        
        ticksSinceLastUpdate++;
        
        if (ticksSinceLastUpdate >= stateUpdateIntervalTicks)
        {
            ticksSinceLastUpdate = 0;
            BroadcastStateUpdate(tick);
        }
    }
    
    private void BroadcastStateUpdate(int tick)
    {
        if (PlayerSpawner.Instance == null)
            return;
        
        List<PlayerState> playerStates = new List<PlayerState>();
        
        // Collect all player states
        foreach (var kvp in PlayerSpawner.Instance.GetAllPlayers())
        {
            NetworkedPlayer networkedPlayer = kvp.Value;
            
            if (networkedPlayer != null)
            {
                Transform playerTransform = networkedPlayer.transform;
                
                PlayerState state = new PlayerState(
                    networkedPlayer.playerId,
                    playerTransform.position,
                    playerTransform.rotation.eulerAngles.z
                );
                
                playerStates.Add(state);
            }
        }
        
        // Send to all clients
        StateUpdateMessage message = new StateUpdateMessage(tick, playerStates);
        NetworkManager.Instance.SendMessageToAll(message, DeliveryMethod.Sequenced);
    }
}
