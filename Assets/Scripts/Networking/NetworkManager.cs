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
    
    // Identified participants, keyed by stable playerId. NetPeer.Id is connection-local and
    // differs on every machine, so it serves only as a lookup index into this.
    private Dictionary<string, PeerInfo> peers = new Dictionary<string, PeerInfo>();
    private Dictionary<int, string> playerIdByNetPeer = new Dictionary<int, string>();

    // Authoritative participant list. Host builds and broadcasts it; clients cache it.
    private List<RosterEntry> roster = new List<RosterEntry>();

    private string currentHostId;
    private int localSpawnSlot = -1;
    private readonly List<string> meshPeersToDrop = new List<string>();

    public bool isHost = false;

    // Events
    public event Action<PeerInfo> OnPeerJoined;
    public event Action<PeerInfo> OnPeerLeft;
    public event Action<INetworkMessage, NetPeer> OnMessageReceived;
    
    // Properties
    public ConnectionState State => state;
    public bool IsHost => isHost;
    public string LocalPlayerId => localPlayerId;
    public string LocalUsername => localPlayerUsername;
    public int LocalSpawnSlot => localSpawnSlot;
    public IReadOnlyDictionary<string, PeerInfo> ConnectedPeers => peers;
    public IReadOnlyList<RosterEntry> Roster => roster;

//peer table

    private PeerInfo RegisterPeer(string playerId, string username, int spawnSlot, NetPeer peer)
    {
        // Drop any stale NetPeer mapping if this player reconnected on a new connection.
        if (peers.TryGetValue(playerId, out PeerInfo existing) && existing.netPeer != null)
            playerIdByNetPeer.Remove(existing.netPeer.Id);

        PeerInfo info = new PeerInfo(playerId, username, spawnSlot, peer);
        peers[playerId] = info;
        playerIdByNetPeer[peer.Id] = playerId;
        return info;
    }

    private void UnregisterPeer(string playerId)
    {
        if (!peers.TryGetValue(playerId, out PeerInfo info))
            return;

        if (info.netPeer != null)
            playerIdByNetPeer.Remove(info.netPeer.Id);

        peers.Remove(playerId);
    }

    private PeerInfo FindPeer(NetPeer peer)
    {
        if (peer == null || !playerIdByNetPeer.TryGetValue(peer.Id, out string playerId))
            return null;

        peers.TryGetValue(playerId, out PeerInfo info);
        return info;
    }

//roster

    // Lowest free slot, so a departing player's slot is reused instead of leaking.
    private int AllocateSpawnSlot()
    {
        for (int slot = 0; slot < config.maxPlayers; slot++)
        {
            if (slot == localSpawnSlot)
                continue;

            bool taken = false;
            foreach (var p in peers.Values)
            {
                if (p.assignedPlayerPosition == slot)
                {
                    taken = true;
                    break;
                }
            }

            if (!taken)
                return slot;
        }

        return -1;
    }

    private void RebuildRoster()
    {
        roster.Clear();

        // Host cannot know which of its addresses clients reached it on, so it leaves
        // ipAddress empty and each client fills in the address it already connected to.
        roster.Add(new RosterEntry(localPlayerId, localPlayerUsername, localSpawnSlot, "", netManager.LocalPort, true));

        foreach (var p in peers.Values)
        {
            roster.Add(new RosterEntry(
                p.peerId,
                p.username,
                p.assignedPlayerPosition,
                p.netPeer.Address.ToString(),
                p.listenPort > 0 ? p.listenPort : p.netPeer.Port,
                false));
        }
    }

    private void BroadcastRoster()
    {
        if (!isHost) return;

        RebuildRoster();
        SendMessageToAll(new SessionRosterMessage(roster), DeliveryMethod.ReliableOrdered);
        ApplyRoster();

        // Resend is periodic, so only log when the composition actually changed.
        string signature = RosterSignature();
        if (signature != lastLoggedRosterSignature)
        {
            lastLoggedRosterSignature = signature;
            Debug.Log($"[NetworkManager] Roster now {roster.Count} participants: {signature}");
        }

        lastRosterResyncTime = Time.time;
    }

    private string RosterSignature()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        foreach (var e in roster)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(e.username).Append("#").Append(e.spawnSlot);
            if (e.isHost) sb.Append("(host)");
        }

        return sb.ToString();
    }

    // The roster is the single source of truth for who exists, so spawning follows it
    // directly. That is what makes joining mid-game work without a separate code path.
    private void ApplyRoster()
    {
        if (PlayerSpawner.Instance != null)
            PlayerSpawner.Instance.SyncToRoster(roster, localPlayerId);
    }

    private RosterEntry FindRosterEntry(string playerId)
    {
        foreach (var e in roster)
        {
            if (e.playerId == playerId)
                return e;
        }
        return null;
    }

    private RosterEntry FindRosterEntryByEndpoint(NetPeer peer)
    {
        string ip = peer.Address.ToString();
        foreach (var e in roster)
        {
            if (e.ipAddress == ip && (e.listenPort == peer.Port))
                return e;
        }
        return null;
    }
    
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

        if (config != null)
        {
            // Drives LiteNetLib's own peer drop; otherwise transport defaults apply.
            netManager.DisconnectTimeout = config.connectionTimeout;
        }
        else
        {
            Debug.LogError("[NetworkManager] No NetworkConfig assigned - using transport defaults");
        }
    }

    private float lastHeartbeatSendTime = 0f;
    private float lastRosterResyncTime = 0f;
    private string lastLoggedRosterSignature = "";
    private const float HOST_TIMEOUT = 5.0f;

    private float HeartbeatInterval => config != null ? config.heartbeatInterval / 1000f : 0.5f;
    private float RosterResyncInterval => config != null ? config.fullStateInterval / 1000f : 0.5f;

    private void Update()
    {
        // Recompiling during play mode wipes non-serialized fields like this one while Update
        // keeps running, so never assume it survived.
        if (netManager == null)
            return;

        netManager.PollEvents();

        // Heartbeat Logic
        if (state != ConnectionState.Disconnected)
        {
            // Only heartbeat sender - a second tick-driven one used to run in parallel.
            if (Time.time - lastHeartbeatSendTime > HeartbeatInterval)
            {
                SendHeartbeat();
                lastHeartbeatSendTime = Time.time;
            }

            // Periodic roster resync, so a peer that somehow diverged heals itself.
            if (isHost && peers.Count > 0 && Time.time - lastRosterResyncTime > RosterResyncInterval)
                BroadcastRoster();
            
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
        
        // Transport-level list, so unidentified mesh connections are covered too.
        foreach (var peer in netManager.ConnectedPeerList)
        {
            SendMessage(msg, (NetPeer)peer, DeliveryMethod.Unreliable);
        }
    }
    
    // Only the host's silence means anything - a quiet mesh peer used to trigger a
    // false host failure and a spurious election.
    private void CheckHostTimeout()
    {
        if (state == ConnectionState.HostMigration) return;
        if (string.IsNullOrEmpty(currentHostId)) return;

        if (!peers.TryGetValue(currentHostId, out PeerInfo host)) return;

        if (Time.time - host.lastHeartbeatReceiveTime > HOST_TIMEOUT)
        {
            Debug.LogError($"[NetworkManager] HOST TIMEOUT: last heard {Time.time - host.lastHeartbeatReceiveTime:F1}s ago");

            HostFailureDetectMessage msg = new HostFailureDetectMessage(localPlayerId, TickManager.Instance.CurrentTick);
            HandleHostFailureDetect(msg);
        }
    }
    
    // Election state
    private int receivedVotes = 0;
    private bool isCandidate = false;
    private Coroutine migrationRoutine;

    private const int MaxElectionRounds = 5;

    private void HandleHostFailureDetect(HostFailureDetectMessage message)
    {
        if (state == ConnectionState.HostMigration)
            return;

        Debug.Log($"[HostMigration] Host failure detected by {message.reporterId}, dead host: {currentHostId}");

        state = ConnectionState.HostMigration;
        receivedVotes = 0;
        isCandidate = false;

        if (migrationRoutine != null)
            StopCoroutine(migrationRoutine);

        migrationRoutine = StartCoroutine(RunMigration());
    }

    // Retries, because a single round can produce no host at all: the best candidate may be
    // unreachable, or votes may not arrive. Without this the session hangs in HostMigration.
    private System.Collections.IEnumerator RunMigration()
    {
        for (int round = 1; round <= MaxElectionRounds; round++)
        {
            DialSurvivors();

            // Random so candidates do not all broadcast on the same frame.
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.5f, 1.0f));

            if (state != ConnectionState.HostMigration)
                yield break;

            StartElection(round);

            yield return new WaitForSeconds(1.5f);

            if (state != ConnectionState.HostMigration)
                yield break;

            Debug.LogWarning($"[HostMigration] Round {round} produced no host, retrying");
            receivedVotes = 0;
            isCandidate = false;
        }

        Debug.LogError("[HostMigration] Election failed - no host could be established");
        LeaveLobby();
    }

    private void DialSurvivors()
    {
        foreach (var entry in roster)
        {
            if (entry.playerId == localPlayerId) continue;
            if (entry.playerId == currentHostId) continue;
            if (peers.ContainsKey(entry.playerId)) continue;

            Debug.Log($"[HostMigration] Connecting to peer {entry.ipAddress}:{entry.listenPort}");
            netManager.Connect(entry.ipAddress, entry.listenPort, "");
        }
    }

    // Candidacy and quorum are judged over peers we actually reached. A roster entry we never
    // connected to cannot host us, and counting it would put a majority permanently out of
    // reach - which is how the old version could deadlock.
    private int CountReachableParticipants()
    {
        int count = 1; // ourselves

        foreach (var entry in roster)
        {
            if (entry.playerId == localPlayerId) continue;
            if (entry.playerId == currentHostId) continue;
            if (peers.ContainsKey(entry.playerId)) count++;
        }

        return count;
    }

    private void StartElection(int round)
    {
        bool amIBestCandidate = true;

        foreach (var entry in roster)
        {
            if (entry.playerId == localPlayerId) continue;
            if (entry.playerId == currentHostId) continue;
            if (!peers.ContainsKey(entry.playerId)) continue;

            if (string.CompareOrdinal(entry.playerId, localPlayerId) < 0)
            {
                amIBestCandidate = false;
                break;
            }
        }

        Debug.Log($"[HostMigration] Round {round}: reachable={CountReachableParticipants()}, candidate={amIBestCandidate}");

        if (!amIBestCandidate)
            return;

        isCandidate = true;
        receivedVotes = 1; // vote for ourselves

        HostElectionRequest req = new HostElectionRequest(localPlayerId, TickManager.Instance.CurrentTick);
        SendMessageToAll(req, DeliveryMethod.ReliableOrdered);

        CheckElectionVictory();
    }

    private void HandleHostElectionRequest(HostElectionRequest request, NetPeer peer)
    {
        // Ordinal, not culture-sensitive: every peer must reach the same verdict.
        bool isBetter = string.CompareOrdinal(request.candidateId, localPlayerId) < 0;

        HostElectionResponse response = new HostElectionResponse(localPlayerId, request.candidateId, isBetter);
        SendMessage(response, peer, DeliveryMethod.ReliableOrdered);
    }

    private void HandleHostElectionResponse(HostElectionResponse response)
    {
        // Ignore votes when not campaigning, otherwise stale replies leak into a later round.
        if (!isCandidate || !response.accepted || response.candidateId != localPlayerId)
            return;

        receivedVotes++;
        CheckElectionVictory();
    }

    private void CheckElectionVictory()
    {
        int totalVoters = CountReachableParticipants();
        int threshold = totalVoters / 2;

        if (receivedVotes > threshold)
        {
            Debug.Log($"[HostMigration] Election won with {receivedVotes}/{totalVoters} votes");
            ClaimHostRole();
        }
    }
    
    private void ClaimHostRole()
    {
        Debug.Log("[HostMigration] CLAIMING HOST ROLE...");

        string deadHostId = currentHostId;

        isCandidate = false;
        StopMigrationRoutine();

        isHost = true;
        currentHostId = localPlayerId;
        state = ConnectionState.InLobby;

        // Drop the dead host and clear stale host flags before taking ownership of the roster.
        if (!string.IsNullOrEmpty(deadHostId))
            UnregisterPeer(deadHostId);

        foreach (var p in peers.Values)
            p.isHost = false;

        HostClaimMessage claimMsg = new HostClaimMessage(localPlayerId, TickManager.Instance.CurrentTick);
        SendMessageToAll(claimMsg, DeliveryMethod.ReliableOrdered);

        BroadcastRoster();

        Debug.Log("[HostMigration] Host claim broadcast. I am now the host.");
    }
    
    private void HandleHostClaim(HostClaimMessage message, NetPeer sender)
    {
        if (message.newHostId == localPlayerId)
            return;

        // Two peers can claim at once. Ordinal comparison gives every peer the same verdict,
        // so the lower id always wins and the session cannot split in two.
        if (isHost)
        {
            if (string.CompareOrdinal(message.newHostId, localPlayerId) >= 0)
            {
                Debug.Log($"[HostMigration] Ignoring claim from {message.newHostId} - our id wins");
                return;
            }

            Debug.LogWarning($"[HostMigration] Standing down for {message.newHostId}");
            isHost = false;
        }

        Debug.Log($"[HostMigration] Received host claim from {message.newHostId}");

        isCandidate = false;
        StopMigrationRoutine();

        // Drop the dead host, then promote the claimant. Keyed by playerId, so this no
        // longer needs the old re-keying dance against connection-local NetPeer ids.
        if (!string.IsNullOrEmpty(currentHostId) && currentHostId != message.newHostId)
            UnregisterPeer(currentHostId);

        RosterEntry entry = FindRosterEntry(message.newHostId);
        int slot = entry != null ? entry.spawnSlot : 0;
        string username = entry != null ? entry.username : "NewHost";

        PeerInfo newHostInfo = RegisterPeer(message.newHostId, username, slot, sender);
        newHostInfo.isHost = true;
        newHostInfo.lastHeartbeatReceiveTime = Time.time;

        currentHostId = message.newHostId;
        state = ConnectionState.InLobby;

        DropMeshPeers();

        Debug.Log($"[HostMigration] Accepted {message.newHostId} as host. Resuming.");
    }

    // Migration dials everyone into a mesh; once a host exists we go back to a star, so a
    // later migration starts from a clean slate instead of a half-connected graph.
    private void DropMeshPeers()
    {
        meshPeersToDrop.Clear();

        foreach (var kvp in peers)
        {
            if (kvp.Key != currentHostId)
                meshPeersToDrop.Add(kvp.Key);
        }

        foreach (string playerId in meshPeersToDrop)
        {
            if (peers.TryGetValue(playerId, out PeerInfo info) && info.netPeer != null)
                info.netPeer.Disconnect();

            UnregisterPeer(playerId);
        }
    }

    private void StopMigrationRoutine()
    {
        if (migrationRoutine == null)
            return;

        StopCoroutine(migrationRoutine);
        migrationRoutine = null;
    }

    
    private void OnDestroy()
    {
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
            localSpawnSlot = 0;
            currentHostId = localPlayerId;
            state = ConnectionState.InLobby;
            RebuildRoster();
            ApplyRoster();
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
        
        StopMigrationRoutine();
        isCandidate = false;
        receivedVotes = 0;

        netManager.Stop();

        peers.Clear();
        playerIdByNetPeer.Clear();
        roster.Clear();
        ApplyRoster(); // empty roster despawns everyone, including our own player
        state = ConnectionState.Disconnected;
        isHost = false;
        currentHostId = null;
        localSpawnSlot = -1;

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

        foreach (var peerInfo in peers.Values)
        {
            peerInfo.netPeer.Send(data, deliveryMethod);
        }
    }
    
 //event handlers default
    
    public void OnPeerConnected(NetPeer peer)
    {
        Debug.Log($"Peer connected: {peer.Address}:{peer.Port}");

        if (isHost)
        {
            // Identity and capacity are settled in HandleJoinLobbyRequest, which is the
            // first point at which we know who this connection actually belongs to.
            return;
        }

        if (state == ConnectionState.HostMigration)
        {
            // Resolve the real playerId from the roster endpoint. The old code invented
            // "peer_<id>" placeholders here, which never matched a spawned player and so
            // left ghosts behind that DespawnPlayer could not find.
            RosterEntry entry = FindRosterEntryByEndpoint(peer);

            if (entry != null)
            {
                RegisterPeer(entry.playerId, entry.username, entry.spawnSlot, peer);
                Debug.Log($"[HostMigration] Mesh peer resolved to {entry.playerId} ({entry.username})");
            }

            // Identify ourselves regardless of whether the endpoint matched. Inferring identity
            // from an address is fragile, and a mesh peer that never registers gets dialled
            // again every retry round - which is the reconnect spam.
            JoinLobbyRequest identity = new JoinLobbyRequest(localPlayerId, localPlayerUsername, 0, netManager.LocalPort);
            SendMessage(identity, peer, DeliveryMethod.ReliableOrdered);
            return;
        }

        JoinLobbyRequest request = new JoinLobbyRequest(localPlayerId, localPlayerUsername, 0, netManager.LocalPort);
        SendMessage(request, peer, DeliveryMethod.ReliableOrdered);
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Debug.Log($"Peer disconnected: {peer.Address}:{peer.Port}, reason: {disconnectInfo.Reason}");

        PeerInfo peerInfo = FindPeer(peer);

        if (peerInfo != null)
        {
            string disconnectedPlayerId = peerInfo.peerId;
            bool wasHost = peerInfo.isHost;

            UnregisterPeer(disconnectedPlayerId);
            OnPeerLeft?.Invoke(peerInfo);

            if (isHost)
            {
                // The host owns membership, so a lost connection really is a departure.
                roster.RemoveAll(e => e.playerId == disconnectedPlayerId);
                ApplyRoster();

                PlayerDisconnectedMessage msg = new PlayerDisconnectedMessage(disconnectedPlayerId);
                SendMessageToAll(msg, DeliveryMethod.ReliableOrdered);

                // Rebroadcast so everyone sees the freed spawn slot.
                BroadcastRoster();
                Debug.Log($"[NetworkManager] Broadcasted disconnect for player {disconnectedPlayerId}");
            }
            else if (wasHost)
            {
                Debug.LogWarning("[NetworkManager] Host disconnected! Triggering immediate migration.");

                roster.RemoveAll(e => e.playerId == disconnectedPlayerId);
                ApplyRoster();

                HostFailureDetectMessage failMsg = new HostFailureDetectMessage(localPlayerId, TickManager.Instance.CurrentTick);
                HandleHostFailureDetect(failMsg);
            }
            else
            {
                // A mesh link closing is not a departure - it is exactly what DropMeshPeers
                // does after a migration. Only the host decides who is still in the session,
                // so leave the roster alone and wait to be told.
                Debug.Log($"[NetworkManager] Peer link closed: {disconnectedPlayerId} (membership unchanged)");
            }
        }

        // Lost the host outright and not migrating - drop back to an empty lobby.
        if (!isHost && peers.Count == 0 && state != ConnectionState.HostMigration)
        {
            state = ConnectionState.Disconnected;
            roster.Clear();
            ApplyRoster();
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
        
        // Only place OnMessageReceived is raised - firing it twice double-processed everything.
        HandleMessage(message, peer);
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
        PeerInfo peerInfo = FindPeer(peer);

        if (peerInfo != null)
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
        // Do not raise OnMessageReceived here - OnNetworkReceive does it after this returns.
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
                
            case MessageType.Heartbeat:
                // Debug.Log($"[HEARTBEAT RECEIVED] from peer={peer.Id}");
                HandleHeartbeat((Heartbeat)message, peer);
                break;
                
            case MessageType.SessionRoster:
                HandleSessionRoster((SessionRosterMessage)message, peer);
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

            // Gameplay messages are intentionally not handled here - NetworkStateHost,
            // NetworkStateReceiver and NetworkShootReceiver take them off OnMessageReceived.
            case MessageType.InputCommand:
            case MessageType.StateUpdate:
            case MessageType.ShootEvent:
            case MessageType.ZombieState:
                break;

            default:
                Debug.LogWarning($"[NetworkManager] No handler for {message.GetMessageType()}");
                break;
        }
    }
    
    private void HandleJoinLobbyRequest(JoinLobbyRequest request, NetPeer peer)
    {
        if (!isHost)
        {
            // During migration this doubles as mesh identification, so a peer counts as
            // reachable for the election and is not dialled again next round.
            if (state == ConnectionState.HostMigration && !peers.ContainsKey(request.playerId))
            {
                RosterEntry known = FindRosterEntry(request.playerId);

                PeerInfo meshPeer = RegisterPeer(
                    request.playerId,
                    request.playerUsername,
                    known != null ? known.spawnSlot : 0,
                    peer);

                meshPeer.listenPort = request.listenPort;
                Debug.Log($"[HostMigration] Mesh peer identified: {request.playerId}");
            }

            return;
        }

        int slot = AllocateSpawnSlot();

        if (slot < 0)
        {
            JoinLobbyResponse full = new JoinLobbyResponse(false, -1, "Lobby is full", localPlayerId);
            SendMessage(full, peer, DeliveryMethod.ReliableOrdered);
            peer.Disconnect();
            Debug.Log($"Rejected {request.playerUsername}: no free spawn slot");
            return;
        }

        PeerInfo peerInfo = RegisterPeer(request.playerId, request.playerUsername, slot, peer);
        peerInfo.listenPort = request.listenPort;

        JoinLobbyResponse response = new JoinLobbyResponse(true, slot, "", localPlayerId);
        SendMessage(response, peer, DeliveryMethod.ReliableOrdered);

        Debug.Log($"Player {request.playerUsername} joined as slot {slot}");

        OnPeerJoined?.Invoke(peerInfo);
        BroadcastRoster();
    }
    
    private void HandleJoinLobbyResponse(JoinLobbyResponse response, NetPeer peer)
    {
        if (isHost) return;
        
        if (response.accepted)
        {
            state = ConnectionState.InLobby;
            localSpawnSlot = response.assignedPlayerPosition;

            // Track the host as a peer so SendMessageToAll reaches it.
            currentHostId = !string.IsNullOrEmpty(response.hostId) ? response.hostId : "host";
            PeerInfo hostPeerInfo = RegisterPeer(currentHostId, "Host", 0, peer);
            hostPeerInfo.isHost = true;

            Debug.Log($"Joined lobby! Assigned slot: {localSpawnSlot}, host={currentHostId}");
        }
        else
        {
            Debug.LogWarning($"Join rejected: {response.reason}");
            LeaveLobby();
        }
    }
    
    private void HandleLeaveLobby(LeaveLobby message, NetPeer peer)
    {
        if (FindPeer(peer) != null)
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
    
    private void HandleSessionRoster(SessionRosterMessage message, NetPeer sender)
    {
        if (isHost) return;

        roster = message.entries;

        // The host leaves its own address blank, so patch in the one we reached it on.
        foreach (var e in roster)
        {
            if (e.isHost)
            {
                if (string.IsNullOrEmpty(e.ipAddress))
                    e.ipAddress = sender.Address.ToString();

                currentHostId = e.playerId;
            }

            if (e.playerId == localPlayerId)
                localSpawnSlot = e.spawnSlot;
        }

        ApplyRoster();
        Debug.Log($"[NetworkManager] Roster updated: {roster.Count} participants, host={currentHostId}");
    }

    private void HandleHeartbeat(Heartbeat heartbeat, NetPeer peer)
    {
        PeerInfo peerInfo = FindPeer(peer);

        if (peerInfo != null)
        {
            peerInfo.lastHeartbeatTick = heartbeat.tick;
            peerInfo.lastHeartbeatReceiveTime = Time.time;
        }
    }
}
