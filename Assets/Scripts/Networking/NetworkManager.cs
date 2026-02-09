using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using System.Collections;
using System;

/// <summary>
/// Core network manager
/// </summary>
public class NetworkManager : MonoBehaviour, INetEventListener
{
    public static NetworkManager Instance { get; private set; }
    
    [Header("Configuration")]
    [SerializeField] private NetworkConfig config;
    
    [Header("Local Player")]
    [SerializeField] private string localPlayerId;
    [SerializeField] private string localPlayerUsername = "Player";
    
    // Network state
    private NetManager netManager;
    private ConnectionState state = ConnectionState.Disconnected;
    
    // Peers
    public Dictionary<int, PeerInfo> connectedPeers = new Dictionary<int, PeerInfo>();
    private int nextPlayerPosition = 0;
    
    // Host Migration: List of all peers in the session (including self and host)
    private List<PeerConnectionInfo> knownPeers = new List<PeerConnectionInfo>();
    
    public bool isHost = false; // Restored field

    private void BroadcastPeerList()
    {
        if (!isHost) return;
        
        // 1. Collect all connected peers
        List<PeerConnectionInfo> allPeers = new List<PeerConnectionInfo>();
        
        // Add Host (Self) - Clients already know Host IP via connection
        
        // Add Clients
        foreach (var peerInfo in connectedPeers.Values)
        {
            // Clients: Address is accessible directly from NetPeer
            string ip = peerInfo.netPeer.Address.ToString();
            int port = peerInfo.netPeer.Port;
            
            allPeers.Add(new PeerConnectionInfo(peerInfo.peerId, peerInfo.username, ip, port));
        }
        
        // 2. Create and Broadcast Message
        PeerListUpdateMessage msg = new PeerListUpdateMessage(allPeers);
        SendMessageToAll(msg, DeliveryMethod.ReliableOrdered);
        // Debug.Log($"[NetworkManager] Broadcasted PeerListUpdate with {allPeers.Count} peers");
    }
    // Events
    public event Action<PeerInfo> OnPeerJoined;
    public event Action<PeerInfo> OnPeerLeft;
    public event Action<INetworkMessage, NetPeer> OnMessageReceived;
    
    // Properties
    public ConnectionState State => state;
    public bool IsHost => isHost;
    public string LocalPlayerId => localPlayerId;
    public IReadOnlyDictionary<int, PeerInfo> ConnectedPeers => connectedPeers;
    
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // CRITICAL: Allow network processing when window is not focused
        Application.runInBackground = true;
        Debug.Log("[NetworkManager] Set Application.runInBackground = true");
        
        // Generate unique player ID if not set
        if (string.IsNullOrEmpty(localPlayerId))
        {
            localPlayerId = Guid.NewGuid().ToString();
        }
        
        // Initialize LiteNetLib
        netManager = new NetManager(this);
    }
    
    private void Start()
    {
        // Subscribe to tick events for heartbeat
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick += HandleTick;
        }
    }
    
    private float lastHeartbeatSendTime = 0f;
    private const float HEARTBEAT_INTERVAL = 0.5f;
    private const float HOST_TIMEOUT = 5.0f;
    
    private void Update()
    {
        netManager?.PollEvents();
        
        // Heartbeat Logic
        if (state != ConnectionState.Disconnected)
        {
            // 1. Send Heartbeats
            if (Time.time - lastHeartbeatSendTime > HEARTBEAT_INTERVAL)
            {
                SendHeartbeat();
                lastHeartbeatSendTime = Time.time;
            }
            
            // 2. Check for Host Failure (Clients only)
            if (!isHost)
            {
                CheckHostTimeout();
            }
        }
    }
    
    private void SendHeartbeat()
    {
        int currentTick = TickManager.Instance != null ? TickManager.Instance.CurrentTick : 0;
        Heartbeat msg = new Heartbeat(currentTick, localPlayerId);
        
        // Send to all connected peers (Host -> Clients, Client -> Host)
        // Note: For client, connectedPeers usually contains just the host
        foreach (var peer in netManager.ConnectedPeerList)
        {
            SendMessage(msg, (NetPeer)peer, DeliveryMethod.Unreliable);
        }
    }
    
    private void CheckHostTimeout()
    {
        // Iterate through connected peers to find the host
        foreach (var kvp in connectedPeers)
        {
            PeerInfo p = kvp.Value;
            
            // If we haven't received a heartbeat from the host in allowed time
            if (Time.time - p.lastHeartbeatReceiveTime > HOST_TIMEOUT)
            {
                Debug.LogError($"[NetworkManager] HOST TIMEOUT DETECTED! Last heard: {p.lastHeartbeatReceiveTime}, Now: {Time.time}");
                
                // Trigger Host Failure sequence
                if (state != ConnectionState.HostMigration)
                {
                    HostFailureDetectMessage msg = new HostFailureDetectMessage(localPlayerId, TickManager.Instance.CurrentTick);
                    HandleHostFailureDetect(msg);
                }
            }
        }
    }
    
    private void HandleHostFailureDetect(HostFailureDetectMessage message)
    {
        Debug.Log($"[NetworkManager] Host Failure Detected by {message.reporterId}!");
        
        if (state != ConnectionState.HostMigration)
        {
            state = ConnectionState.HostMigration;
            Debug.Log("[NetworkManager] STATE CHANGED TO: HostMigration");
        }
        
        // Connect to all known peers to form a mesh
        foreach (var peer in knownPeers)
        {
             // Skip self
             if (peer.peerId == localPlayerId) continue;
             
             // Skip dead host (connection failed) and connect to everyone else
             
             // Check if already connected
             bool alreadyConnected = false;
             foreach(var p in connectedPeers.Values) 
             { 
                 if(p.peerId == peer.peerId) alreadyConnected = true; 
             }
             
             if (!alreadyConnected)
             {
                 Debug.Log($"[HostMigration] Connecting to peer {peer.ipAddress}:{peer.port}");
                 netManager.Connect(peer.ipAddress, peer.port, "");
             }
        }
        
        // Broadcast failure to anyone we ARE connected to (to spread the word)
        // SendMessageToAll(message, DeliveryMethod.ReliableOrdered);
        
        // Start Election Timer
        StartCoroutine(ElectionTimer());
    }
    
    private System.Collections.IEnumerator ElectionTimer()
    {
        // Random delay to avoid collision (0-500ms) + connection time
        yield return new WaitForSeconds(UnityEngine.Random.Range(0.5f, 1.0f));
        
        StartElection();
    }
    
    private void StartElection()
    {
        Debug.Log($"[HostMigration] Checking candidacy... My ID: {localPlayerId}");
        
        // Logic: Lowest string ID wins
        bool amIBestCandidate = true;
        
        Debug.Log($"[HostMigration] Known Peers Count: {knownPeers.Count}");
        foreach (var peer in knownPeers)
        {
             // Debug.Log($"[HostMigration] Comparing against: {peer.peerId} (isHost={peer.peerId=="host"})");
             
             // Skip self
             if (peer.peerId == localPlayerId) continue;
             if (peer.peerId == "host") continue; // Old host ID
             
             // Compare IDs
             if (string.Compare(peer.peerId, localPlayerId) < 0)
             {
                 Debug.Log($"[HostMigration] Found better candidate: {peer.peerId} < {localPlayerId}");
                 // Peer ID is smaller than mine -> They should be host
                 amIBestCandidate = false;
                 break;
             }
        }
        
        if (amIBestCandidate)
        {
            Debug.Log("[HostMigration] I AM CANDIDATE! Broadcasting request...");
            
            // Broadcast candidacy
            HostElectionRequest req = new HostElectionRequest(localPlayerId, TickManager.Instance.CurrentTick);
            
            // We need to send this to EVERYONE we managed to connect to
            SendMessageToAll(req, DeliveryMethod.ReliableOrdered);
            
            // Vote for self
            receivedVotes = 1;
            
            // Check if we win immediately (e.g. only candidate, or self > threshold)
            CheckElectionVictory();
        }
    }
    
    // Election State
    private int receivedVotes = 0;
    
    private void HandleHostElectionRequest(HostElectionRequest request, NetPeer peer)
    {
        // Debug.Log($"[HostElection] Received request from {request.candidateId}");
        
        // Simple Logic: If their ID is lower than mine, accept.
        bool isBetter = string.Compare(request.candidateId, localPlayerId) < 0;
        
        if (isBetter)
        {
             // Debug.Log($"[HostElection] Voting YES for {request.candidateId}");
        }
        else
        {
             // Debug.Log($"[HostElection] Voting NO for {request.candidateId} (My ID is lower)");
        }
        
        HostElectionResponse response = new HostElectionResponse(localPlayerId, request.candidateId, isBetter);
        SendMessage(response, peer, DeliveryMethod.ReliableOrdered);
    }
    
    private void HandleHostElectionResponse(HostElectionResponse response)
    {
        if (response.accepted && response.candidateId == localPlayerId)
        {
            receivedVotes++;
            // Debug.Log($"[HostElection] Received VOTE from {response.voterId}! Total: {receivedVotes}");
            CheckElectionVictory();
        }
    }
    
    private void CheckElectionVictory()
    {
         // Victory Condition: > 50% of known peers (excluding dead host)
         int totalVoters = 0;
         foreach(var p in knownPeers)
         {
             if(p.peerId != "host") totalVoters++;
         }
         
         // If knownPeers is empty (standalone), assume totalVoters=1 (self) if not in list
         if (totalVoters == 0 && knownPeers.Count == 0) totalVoters = 1; 
         
         int threshold = totalVoters / 2;
         
         // Debug.Log($"[HostElection] Victory Check: Votes={receivedVotes}, Threshold={threshold}, TotalVoters={totalVoters}");
         
         if (receivedVotes > threshold)
         {
             Debug.Log(">>> ELECTION WON! I AM THE NEW HOST! <<<");
             ClaimHostRole();
         }
    }
    
    private void ClaimHostRole()
    {
        Debug.Log("[HostMigration] CLAIMING HOST ROLE...");
        
        // 1. Become Host
        isHost = true;
        state = ConnectionState.InLobby; // Restore to valid gameplay state
        
        // 2. Identify ourselves as "host" (connectedPeers will now contain only clients)
        
        // 3. Broadcast Claim
        HostClaimMessage claimMsg = new HostClaimMessage(localPlayerId, TickManager.Instance.CurrentTick);
        SendMessageToAll(claimMsg, DeliveryMethod.ReliableOrdered);
        
        // 4. Input handling automatically resumes as we are now Host
        
        Debug.Log("[HostMigration] Host Claim Broadcasted. I am now the Captain.");
    }
    
    private void HandleHostClaim(HostClaimMessage message, NetPeer sender)
    {
        Debug.Log($"[HostMigration] Received Host Claim from {message.newHostId}");
        
        // 1. Acknowledge new host
        // We need to treat the sender as the new "host" peer
        
        // Update connectedPeers to allow input sending
        // Use the actual UUID (message.newHostId) so PlayerSpawner can find it later (Fixes Ghost Player?)
        PeerInfo newHostInfo = new PeerInfo(message.newHostId, "NewHost", 0, sender);
        
        // Remove old host key if exists (it might be "host" or the UUID)
        if (connectedPeers.ContainsKey("host".GetHashCode())) connectedPeers.Remove("host".GetHashCode());
        // Also remove any existing mesh connection to this peer (if we knew them as a client before)
        if (connectedPeers.ContainsKey(sender.Id)) connectedPeers.Remove(sender.Id);
        
        // Add new host using the correct LiteNetLib ID (Fixes Timeout)
        newHostInfo.lastHeartbeatReceiveTime = Time.time; 
        connectedPeers[sender.Id] = newHostInfo; 
        
        // 2. Reset Timeout Tracking (Implicit in new PeerInfo creation)
        
        // 3. State Transition
        state = ConnectionState.InLobby;
        Debug.Log("[HostMigration] Accepted new host. Resuming Game.");
    }

    
    private void OnDestroy()
    {
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick -= HandleTick;
        }
        
        netManager?.Stop();
    }
    
//api

    /// <summary>
    /// Host a new lobby
    /// </summary>
    public bool HostLobby()
    {
        if (state != ConnectionState.Disconnected)
        {
            Debug.LogWarning("Cannot host lobby - already connected");
            return false;
        }
        
        bool success = netManager.Start(config.gamePort);
        
        if (success)
        {
            isHost = true;
            nextPlayerPosition = 1; // Host takes position 0 (other clients 1-3)
            state = ConnectionState.InLobby;
            Debug.Log($"Hosting lobby on port {config.gamePort}");
        }
        else
        {
            Debug.LogError($"Failed to start host on port {config.gamePort}");
        }
        
        return success;
    }
    
    /// <summary>
    /// Join an existing lobby
    /// </summary>
    public bool JoinLobby(string hostAddress, int hostPort)
    {
        if (state != ConnectionState.Disconnected)
        {
            Debug.LogWarning("Cannot join lobby - already connected");
            return false;
        }
        
        netManager.Start();
        NetPeer peer = netManager.Connect(hostAddress, hostPort, "");
        
        if (peer != null)
        {
            state = ConnectionState.ConnectingToLobby;
            Debug.Log($"Connecting to {hostAddress}:{hostPort}");
            return true;
        }
        else
        {
            Debug.LogError($"Failed to connect to {hostAddress}:{hostPort}");
            netManager.Stop();
            return false;
        }
    }
    
    /// <summary>
    /// Leave current lobby
    /// </summary>
    public void LeaveLobby()
    {
        if (isHost)
        {
            // Host disconnects all clients
            netManager.DisconnectAll();
        }
        
        netManager.Stop();
        
        connectedPeers.Clear();
        state = ConnectionState.Disconnected;
        isHost = false;
        nextPlayerPosition = 0;
        
        Debug.Log("Left lobby");
    }
    
    /// <summary>
    /// Send a message to a specific peer
    /// </summary>
    public void SendMessage(INetworkMessage message, NetPeer peer, DeliveryMethod deliveryMethod)
    {
        byte[] data = MessageSerializer.Serialize(message);
        peer.Send(data, deliveryMethod);
    }
    
    /// <summary>
    /// Send a message to all connected peers
    /// </summary>
    public void SendMessageToAll(INetworkMessage message, DeliveryMethod deliveryMethod)
    {
        byte[] data = MessageSerializer.Serialize(message);
        
        foreach (var peerInfo in connectedPeers.Values)
        {
            peerInfo.netPeer.Send(data, deliveryMethod);
        }
    }
    
 //event handlers default
    
    public void OnPeerConnected(NetPeer peer)
    {
        Debug.Log($"Peer connected: {peer.Port}");
        
        if (isHost)
        {
            if (nextPlayerPosition >= config.maxPlayers)
            {
                // Lobby full
                JoinLobbyResponse response = new JoinLobbyResponse(false, -1, "Lobby is full", localPlayerId);
                SendMessage(response, peer, DeliveryMethod.ReliableOrdered);
                peer.Disconnect();
                return;
            }
            

        }

        else if (state == ConnectionState.HostMigration)
        {
             Debug.Log($"[HostMigration] Peer connected: {peer.Address}:{peer.Port}. Adding to connectedPeers for Mesh.");
             
             // Create temporary PeerInfo (Mesh Formation) - real UUID resolved during election
             string tempId = "peer_" + peer.Id;
             PeerInfo tempInfo = new PeerInfo(tempId, "Unknown", 0, peer);
             connectedPeers[peer.Id] = tempInfo;
        }
        else
        {
            JoinLobbyRequest request = new JoinLobbyRequest(localPlayerId, localPlayerUsername, 0);
            SendMessage(request, peer, DeliveryMethod.ReliableOrdered);
        }
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Debug.Log($"Peer disconnected: {peer.Port}, reason: {disconnectInfo.Reason}");
        
        if (connectedPeers.TryGetValue(peer.Id, out PeerInfo peerInfo))
        {
            string disconnectedPlayerId = peerInfo.peerId;
            
            connectedPeers.Remove(peer.Id);
            OnPeerLeft?.Invoke(peerInfo);
            
            // Fix Ghost Player: Destroy the object!
            if (PlayerSpawner.Instance != null)
            {
                PlayerSpawner.Instance.DespawnPlayer(disconnectedPlayerId);
            }
            
            // If we're the host, broadcast disconnection to all remaining clients
            if (isHost)
            {
                PlayerDisconnectedMessage msg = new PlayerDisconnectedMessage(disconnectedPlayerId);
                SendMessageToAll(msg, DeliveryMethod.ReliableOrdered);
                
                // Host Migration: Update everyone's peer list
                BroadcastPeerList();
                
                Debug.Log($"[NetworkManager] Broadcasted disconnect for player {disconnectedPlayerId}");
            }
            
            // If we are a CLIENT and the HOST disconnected (graceful or otherwise)
            // Fix: Check username "Host" or "NewHost" because ID is now UUID
            if (!isHost && (peerInfo.username == "Host" || peerInfo.username == "NewHost"))
            {
                 Debug.LogWarning("[NetworkManager] Host disconnected! Triggering immediate migration.");
                 HostFailureDetectMessage failMsg = new HostFailureDetectMessage(localPlayerId, TickManager.Instance.CurrentTick);
                 HandleHostFailureDetect(failMsg);
            }
            
            // Fix Split Brain: Remove the disconnected peer from 'knownPeers' 
            // This ensures we don't consider them a candidate in the next election
            knownPeers.RemoveAll(p => p.peerId == disconnectedPlayerId);
            Debug.Log($"[NetworkManager] Removed {disconnectedPlayerId} from valid candidates. Valid Candidates Remaining: {knownPeers.Count}");
        }
        //change with host migration
        
        if (!isHost && connectedPeers.Count == 0 && state != ConnectionState.HostMigration)
        {
            state = ConnectionState.Disconnected;
        }
    }
    
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        byte[] data = reader.GetRemainingBytes();
        INetworkMessage message = MessageSerializer.Deserialize(data);
        
        if (message == null)
        {
            Debug.LogError("Failed to deserialize message");
            return;
        }
        
        // Handle message based on type
        HandleMessage(message, peer);
        
        // Invoke event for other systems
        OnMessageReceived?.Invoke(message, peer);
    }
    
    public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
    {
        Debug.LogError($"Network error: {socketError} at {endPoint}");
    }
    
    public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        if (connectedPeers.TryGetValue(peer.Id, out PeerInfo peerInfo))
        {
            peerInfo.latency = latency;
        }
    }
    
    public void OnConnectionRequest(ConnectionRequest request)
    {
        // 1. Host accepts everyone
        if (isHost)
        {
            request.Accept();
        }
        // 2. Host Migration: Accept peers trying to form a mesh
        else if (state == ConnectionState.HostMigration)
        {
            Debug.Log($"[HostMigration] Accepting P2P connection from {request.RemoteEndPoint}");
            request.Accept();
        }
        else
        {
            Debug.Log($"[NetworkManager] Rejecting connection from {request.RemoteEndPoint}. isHost={isHost}, State={state}");
            request.Reject();
        }
    }
    
//message handling    
    private void HandleMessage(INetworkMessage message, NetPeer peer)
    {
        // CRITICAL: Fire event so other systems (like NetworkStateHost) can handle messages
        OnMessageReceived?.Invoke(message, peer);
        
        // Also handle messages internally
        switch (message.GetMessageType())
        {
            case MessageType.JoinLobbyRequest:
                HandleJoinLobbyRequest((JoinLobbyRequest)message, peer);
                break;
                
            case MessageType.JoinLobbyResponse:
                HandleJoinLobbyResponse((JoinLobbyResponse)message, peer);
                break;
                
            case MessageType.LeaveLobby:
                HandleLeaveLobby((LeaveLobby)message, peer);
                break;
                
            case MessageType.PlayerDisconnected:
                HandlePlayerDisconnected((PlayerDisconnectedMessage)message);
                break;
                
            case MessageType.StartGame:
                HandleStartGameMessage((StartGameMessage)message, peer);
                break;
                
            case MessageType.Heartbeat:
                // Debug.Log($"[HEARTBEAT RECEIVED] from peer={peer.Id}");
                HandleHeartbeat((Heartbeat)message, peer);
                break;
                
            case MessageType.PeerListUpdate:
                HandlePeerListUpdate((PeerListUpdateMessage)message);
                break;
                
            case MessageType.HostElectionRequest:
                HandleHostElectionRequest((HostElectionRequest)message, peer);
                break;
                
            case MessageType.HostElectionResponse:
                HandleHostElectionResponse((HostElectionResponse)message);
                break;
                
            case MessageType.HostClaim:
                HandleHostClaim((HostClaimMessage)message, peer);
                break;
        }
    }
    
    public event Action<StartGameMessage> OnGameStarted;
    
    public void BroadcastGameStart(List<PlayerSpawnInfo> players)
    {
        Debug.Log($"[NetworkManager] BroadcastGameStart called. IsHost={isHost}");
        
        if (!isHost)
        {
            Debug.LogWarning("[NetworkManager] BroadcastGameStart called but we are NOT host!");
            return;
        }
        
        Debug.Log($"[NetworkManager] Creating StartGameMessage with {players.Count} players");
        
        // Host creates start message
        StartGameMessage message = new StartGameMessage(TickManager.Instance.CurrentTick, players);
        
        Debug.Log($"[NetworkManager] Sending StartGameMessage to {connectedPeers.Count} peers");
        SendMessageToAll(message, DeliveryMethod.ReliableOrdered);
        
        // trigger it locally for the host
        Debug.Log("[NetworkManager] Invoking OnGameStarted locally for host");
        OnGameStarted?.Invoke(message);
    }
    
    private void HandleStartGameMessage(StartGameMessage message, NetPeer peer)
    {
        // Clients receive from host
        Debug.Log($"[NetworkManager] HandleStartGameMessage! Players to spawn: {message.players.Count}");
        Debug.Log($"[NetworkManager] OnGameStarted has {(OnGameStarted == null ? 0 : OnGameStarted.GetInvocationList().Length)} subscribers");
        OnGameStarted?.Invoke(message);
        Debug.Log("[NetworkManager] OnGameStarted invoked");
    }
    
    private void HandleJoinLobbyRequest(JoinLobbyRequest request, NetPeer peer)
    {
        if (!isHost) return;
        
        int playerPos = nextPlayerPosition++;
        
        PeerInfo peerInfo = new PeerInfo(request.playerId, request.playerUsername, playerPos, peer);
        connectedPeers[peer.Id] = peerInfo;
        
        // Send Response
        JoinLobbyResponse response = new JoinLobbyResponse(true, playerPos, "", localPlayerId);
        SendMessage(response, peer, DeliveryMethod.ReliableOrdered);
        
        Debug.Log($"Player {request.playerUsername} joined as position {playerPos}");
        
        OnPeerJoined?.Invoke(peerInfo);
        
        // Host Migration: Update everyone's peer list
        BroadcastPeerList();
    }
    
    private void HandleJoinLobbyResponse(JoinLobbyResponse response, NetPeer peer)
    {
        if (isHost) return;
        
        if (response.accepted)
        {
            state = ConnectionState.InLobby;
            
            // CRITICAL FIX: Client needs to track the host peer so SendMessageToAll() works
            // Create a PeerInfo for the host and add to connectedPeers
            // Fix Ghost Player: Use actual Host UUID from response
            string hostId = !string.IsNullOrEmpty(response.hostId) ? response.hostId : "host";
            PeerInfo hostPeerInfo = new PeerInfo(hostId, "Host", 0, peer);
            connectedPeers[peer.Id] = hostPeerInfo;
            
            Debug.Log($"Joined lobby! Assigned position: {response.assignedPlayerPosition}");
            Debug.Log($"[NetworkManager] Client added host to connectedPeers. Count={connectedPeers.Count}");
        }
        else
        {
            Debug.LogWarning($"Join rejected: {response.reason}");
            LeaveLobby();
        }
    }
    
    private void HandleLeaveLobby(LeaveLobby message, NetPeer peer)
    {
        if (connectedPeers.TryGetValue(peer.Id, out PeerInfo peerInfo))
        {
            peer.Disconnect();
        }
    }
    
    private void HandlePlayerDisconnected(PlayerDisconnectedMessage message)
    {
        Debug.Log($"[NetworkManager] Received PlayerDisconnected for player {message.playerId}");
        
        // Remove player from game
        if (PlayerSpawner.Instance != null)
        {
            PlayerSpawner.Instance.DespawnPlayer(message.playerId);
        }
    }
    
    // Host Migration: Update local list of known peers
    private void HandlePeerListUpdate(PeerListUpdateMessage message)
    {
        knownPeers = message.peers;
        Debug.Log($"[NetworkManager] Updated known peers list. Count: {knownPeers.Count}");
    }
    
    private void HandleHeartbeat(Heartbeat heartbeat, NetPeer peer)
    {
        if (connectedPeers.TryGetValue(peer.Id, out PeerInfo peerInfo))
        {
            peerInfo.lastHeartbeatTick = heartbeat.tick;
            peerInfo.lastHeartbeatReceiveTime = Time.time;
        }
    }
    
//tick handling    
    private int ticksSinceLastHeartbeat = 0;
    
    private void HandleTick(int tick)
    {
        int heartbeatIntervalTicks = (config.heartbeatInterval * config.tickRate) / 1000;
        
        ticksSinceLastHeartbeat++;
        
        if (ticksSinceLastHeartbeat >= heartbeatIntervalTicks)
        {
            ticksSinceLastHeartbeat = 0;
            SendHeartbeat(tick);
        }
    }
    
    private void SendHeartbeat(int tick)
    {
        if (state == ConnectionState.Disconnected) return;
        
        Heartbeat heartbeat = new Heartbeat(tick, localPlayerId);
        // Debug.Log($"[HEARTBEAT SENT] tick={tick} from HOST {localPlayerId}");
        SendMessageToAll(heartbeat, DeliveryMethod.Unreliable);
    }
}
