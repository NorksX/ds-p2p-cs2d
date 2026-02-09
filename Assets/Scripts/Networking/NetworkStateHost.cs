using UnityEngine;
using LiteNetLib;
using System.Collections.Generic;

/// <summary>
/// HOST ONLY: Collect input and broadcast
/// </summary>
public class NetworkStateHost : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private int stateUpdateIntervalTicks = 1; // Interval for state update
    
    private int ticksSinceLastUpdate = 0;
    // Queue-based input processing: store ALL inputs that arrive between ticks
    private Dictionary<string, List<InputCommand>> receivedInputs = new Dictionary<string, List<InputCommand>>();
    
    private void Start()
    {
        Debug.Log("[NetworkStateHost] Start() called");
        
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick += HandleTick;
            Debug.Log("[NetworkStateHost] Subscribed to OnTick");
        }
        
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnMessageReceived += HandleMessage;
            Debug.Log("[NetworkStateHost] Subscribed to OnMessageReceived");
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
        // Debug.Log($"[NetworkStateHost] HandleMessage called! MessageType={message.GetMessageType()}, IsHost={NetworkManager.Instance?.IsHost}");
        
        // Only host processes input commands
        if (!NetworkManager.Instance.IsHost)
        {
            // Debug.Log("[NetworkStateHost] Not host, ignoring message");
            return;
        }
        
        if (message.GetMessageType() == MessageType.InputCommand)
        {
            InputCommandMessage inputMsg = (InputCommandMessage)message;
            string playerId = inputMsg.inputCommand.playerId;
            
            // Debug.Log($"[NetworkStateHost] Received InputCommand from {playerId}, move={inputMsg.inputCommand.move}, aimDir={inputMsg.inputCommand.aimDir}");
            
            // Queue-based processing: Add input to the list for this player
            if (!receivedInputs.ContainsKey(playerId))
            {
                receivedInputs[playerId] = new List<InputCommand>();
            }
            
            receivedInputs[playerId].Add(inputMsg.inputCommand);
            // Debug.Log($"[NetworkStateHost] Queued input! Player {playerId} now has {receivedInputs[playerId].Count} inputs pending");
        }
    }
    
    private void HandleTick(int tick)
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsHost)
            return;
        
        // Process client inputs FIRST
        ProcessClientInputs(tick);
        
        // Then broadcast state
        ticksSinceLastUpdate++;
        
        if (ticksSinceLastUpdate >= stateUpdateIntervalTicks)
        {
            ticksSinceLastUpdate = 0;
            BroadcastStateUpdate(tick);
        }
    }
    
    private void ProcessClientInputs(int tick)
    {
        if (PlayerSpawner.Instance == null)
        {
            Debug.LogWarning("[NetworkStateHost] ProcessClientInputs - PlayerSpawner.Instance is NULL!");
            return;
        }
        
        int totalInputsProcessed = 0;
        
        // Process ALL queued inputs for each player
        foreach (var kvp in receivedInputs)
        {
            string playerId = kvp.Key;
            List<InputCommand> inputQueue = kvp.Value;
            
            // Debug.Log($"[NetworkStateHost] Processing {inputQueue.Count} queued inputs for playerId={playerId}");
            
            NetworkedPlayer networkedPlayer = PlayerSpawner.Instance.GetPlayer(playerId);
            
            if (networkedPlayer == null)
            {
                Debug.LogWarning($"[NetworkStateHost] GetPlayer({playerId}) returned NULL!");
                continue;
            }
            
            if (networkedPlayer.playerController == null)
            {
                Debug.LogWarning($"[NetworkStateHost] Player {playerId} has NULL playerController!");
                continue;
            }
            
            // CRITICAL INSIGHT: SimulateMovement() sets velocity INSTANTLY
            // Processing all inputs in one frame means only the LAST one has effect
            // Solution: Use LAST movement/aim, but process ALL fire inputs
            
            if (inputQueue.Count > 0)
            {
                // Get the most recent input for movement and aim
                InputCommand lastInput = inputQueue[inputQueue.Count - 1];
                
                // Apply movement and look from LAST input
                networkedPlayer.playerController.SimulateMovement(lastInput.move);
                networkedPlayer.playerController.SimulateLook(lastInput.aimDir);
                
                // Debug.Log($"[NetworkStateHost] Applied LAST input from {playerId}, move={lastInput.move}, aimDir={lastInput.aimDir}");
                
                // Process ALL fire inputs (each shot matters!)
                foreach (InputCommand input in inputQueue)
                {
                    if (input.firePressed)
                    {
                        // Execute shooting locally on host
                        networkedPlayer.playerController.SimulateShoot(input.aimDir);
                        
                        // Broadcast shoot event to ALL clients
                        Vector2 shootOrigin = networkedPlayer.transform.position;
                        ShootEventMessage shootMsg = new ShootEventMessage(playerId, shootOrigin, input.aimDir);
                        NetworkManager.Instance.SendMessageToAll(shootMsg, LiteNetLib.DeliveryMethod.ReliableOrdered);
                        
                        // Debug.Log($"[NetworkStateHost] Broadcast shoot event from {playerId}");
                    }
                }
                
                totalInputsProcessed++;
            }
        }
        
        // Debug.Log($"[NetworkStateHost] Processed inputs for {totalInputsProcessed} players this tick");
        
        // Clear all input queues
        receivedInputs.Clear();
    }
    
    private void BroadcastStateUpdate(int tick)
    {
        if (PlayerSpawner.Instance == null)
            return;
        
        List<PlayerState> playerStates = new List<PlayerState>();
        
        // Collect player states
        foreach (var np in PlayerSpawner.Instance.GetAllPlayers())
        {
            NetworkedPlayer networkedPlayer = np.Value;
            
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
        
        // send to clients
        StateUpdateMessage message = new StateUpdateMessage(tick, playerStates);
        NetworkManager.Instance.SendMessageToAll(message, DeliveryMethod.Sequenced);
    }
}
