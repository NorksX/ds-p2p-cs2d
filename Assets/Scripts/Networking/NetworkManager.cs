using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
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
    private bool isHost = false;
    
    // Peers
    private Dictionary<int, PeerInfo> connectedPeers = new Dictionary<int, PeerInfo>();
    private int nextPlayerPosition = 0;
    
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
    
    private void Update()
    {
        netManager?.PollEvents();
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
                JoinLobbyResponse response = new JoinLobbyResponse(false, -1, "Lobby is full");
                SendMessage(response, peer, DeliveryMethod.ReliableOrdered);
                peer.Disconnect();
                return;
            }
            

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
            connectedPeers.Remove(peer.Id);
            OnPeerLeft?.Invoke(peerInfo);
        }
        //change with host migratie
        
        if (!isHost && connectedPeers.Count == 0)
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
        if (isHost)
        {
            //accept all
            request.Accept();
        }
        else
        {
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
                
            case MessageType.StartGame:
                HandleStartGameMessage((StartGameMessage)message, peer);
                break;
                
            case MessageType.Heartbeat:
                // Debug.Log($"[HEARTBEAT RECEIVED] from peer={peer.Id}");
                HandleHeartbeat((Heartbeat)message, peer);
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
        
        JoinLobbyResponse response = new JoinLobbyResponse(true, playerPos);
        SendMessage(response, peer, DeliveryMethod.ReliableOrdered);
        
        Debug.Log($"Player {request.playerUsername} joined as position {playerPos}");
        
        OnPeerJoined?.Invoke(peerInfo);
    }
    
    private void HandleJoinLobbyResponse(JoinLobbyResponse response, NetPeer peer)
    {
        if (isHost) return;
        
        if (response.accepted)
        {
            state = ConnectionState.InLobby;
            
            // CRITICAL FIX: Client needs to track the host peer so SendMessageToAll() works
            // Create a PeerInfo for the host and add to connectedPeers
            PeerInfo hostPeerInfo = new PeerInfo("host", "Host", 0, peer);
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
    
    private void HandleHeartbeat(Heartbeat heartbeat, NetPeer peer)
    {
        if (connectedPeers.TryGetValue(peer.Id, out PeerInfo peerInfo))
        {
            peerInfo.lastHeartbeatTick = heartbeat.tick;
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
