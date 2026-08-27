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

    // Highest input tick already simulated per player, so nothing is applied twice.
    private Dictionary<string, int> lastProcessedTick = new Dictionary<string, int>();
    private readonly List<string> departedPlayers = new List<string>();
    
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
            
            // Every input is one discrete tick of movement, so all of them must be applied in
            // order. Applying only the last one silently drops distance whenever two arrive in
            // the same host tick, and the client - which simulated both - is never corrected.
            inputQueue.Sort((a, b) => a.tick.CompareTo(b.tick));

            lastProcessedTick.TryGetValue(playerId, out int processedThrough);

            PlayerHealth health = networkedPlayer.GetComponent<PlayerHealth>();
            bool isDead = health != null && health.IsDead;

            foreach (InputCommand input in inputQueue)
            {
                // Guards against duplicates and late stragglers replaying old movement.
                if (input.tick <= processedThrough)
                    continue;

                // Dead players still advance the ack, so their client keeps reconciling
                // cleanly instead of piling up unacknowledged input while waiting to respawn.
                if (isDead)
                {
                    processedThrough = input.tick;
                    continue;
                }

                networkedPlayer.playerController.SimulateMovement(input.move);
                networkedPlayer.playerController.SimulateLook(input.aimDir);

                if (input.firePressed)
                {
                    networkedPlayer.playerController.SimulateShoot(input.aimDir);

                    Debug.Log($"[Shoot] host resolved shot from {networkedPlayer.name} at tick {input.tick}");

                    // Origin is read after the step, so the shot leaves from where they were.
                    Vector2 shootOrigin = networkedPlayer.transform.position;
                    ShootEventMessage shootMsg = new ShootEventMessage(playerId, shootOrigin, input.aimDir);
                    NetworkManager.Instance.SendMessageToAll(shootMsg, LiteNetLib.DeliveryMethod.ReliableOrdered);
                }

                processedThrough = input.tick;
                totalInputsProcessed++;
            }

            lastProcessedTick[playerId] = processedThrough;
        }

        PruneDepartedPlayers();
        
        // Debug.Log($"[NetworkStateHost] Processed inputs for {totalInputsProcessed} players this tick");
        
        // Clear all input queues
        receivedInputs.Clear();
    }
    
    // Otherwise a rejoining player, whose tick counter restarts low, would have all of its
    // input rejected as stale by the entry left behind from its previous session.
    private void PruneDepartedPlayers()
    {
        if (lastProcessedTick.Count == 0)
            return;

        departedPlayers.Clear();

        foreach (var kvp in lastProcessedTick)
        {
            if (PlayerSpawner.Instance.GetPlayer(kvp.Key) == null)
                departedPlayers.Add(kvp.Key);
        }

        foreach (string playerId in departedPlayers)
            lastProcessedTick.Remove(playerId);
    }

    private void BroadcastStateUpdate(int tick)
    {
        if (PlayerSpawner.Instance == null)
            return;

        // Nothing to tell an empty lobby - this used to broadcast every tick regardless.
        if (NetworkManager.Instance.ConnectedPeers.Count == 0)
            return;

        List<PlayerState> playerStates = new List<PlayerState>();
        
        // Collect player states
        foreach (var np in PlayerSpawner.Instance.GetAllPlayers())
        {
            NetworkedPlayer networkedPlayer = np.Value;
            
            if (networkedPlayer != null)
            {
                Transform playerTransform = networkedPlayer.transform;
                lastProcessedTick.TryGetValue(networkedPlayer.playerId, out int ackTick);

                PlayerHealth health = networkedPlayer.GetComponent<PlayerHealth>();

                PlayerState state = new PlayerState(
                    networkedPlayer.playerId,
                    playerTransform.position,
                    playerTransform.rotation.eulerAngles.z,
                    ackTick,
                    health != null ? health.currentHealth : 0
                );

                playerStates.Add(state);
            }
        }
        
        // send to clients
        StateUpdateMessage message = new StateUpdateMessage(tick, playerStates);
        NetworkManager.Instance.SendMessageToAll(message, DeliveryMethod.Sequenced);
    }
}
