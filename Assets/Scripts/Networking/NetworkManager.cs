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

    // Measures RTT to every participant and ranks them. Owns the whole latency side of host
    // election; a plain object rather than a component, so none of this needs scene wiring.
    private HostQualityMonitor quality;
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
    public string CurrentHostId => currentHostId;
    public NetworkConfig Config => config;

    public HostQualityMonitor Quality => quality;

    // Election tunables are read through here, never straight off the asset, so a command line
    // can retune a demo without a rebuild. Overriding the ScriptableObject itself would be
    // worse than useless: every instance on one machine shares that asset, and in the Editor a
    // runtime write to it persists into the project.
    public float SimulatedExtraMs => Overridden("rttExtraMs", config != null ? config.rttSimulatedExtraMs : 0f);
    public float RttProbeInterval => Overridden("rttProbeInterval", config != null ? config.rttProbeInterval : 3000f);
    public int RttMinSamples => (int)Overridden("rttMinSamples", config != null ? config.rttMinSamples : 5);
    public float ProactiveCheckInterval => Overridden("proactiveCheckInterval", config != null ? config.proactiveCheckInterval : 30000f);
    public float ProactiveThresholdFactor => Overridden("proactiveThresholdFactor", config != null ? config.proactiveThresholdFactor : 3f);
    public int ProactiveSustainedChecks => (int)Overridden("proactiveSustainedChecks", config != null ? config.proactiveSustainedChecks : 2);
    public float ProactiveCooldown => Overridden("proactiveCooldown", config != null ? config.proactiveCooldown : 60000f);
    public float ProactiveMinCostGap => Overridden("proactiveMinCostGap", config != null ? config.proactiveMinCostGap : 100f);

    // The port actually bound, which after a migration is the ephemeral one this peer took as
    // a client - not config.gamePort. Discovery advertises this.
    public int LocalPort => netManager != null ? netManager.LocalPort : 0;
    public int MaxPlayers => config != null ? config.maxPlayers : 4;

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

        // Per-player state keyed to someone who left has to go, or a rejoin inherits a stale
        // estimator and is judged on measurements of a previous session.
        quality?.PruneToRoster();
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

        // RTT probes travel as connectionless packets on this same socket, which is what lets
        // clients measure each other without a mesh. LiteNetLib drops them silently otherwise.
        netManager.UnconnectedMessagesEnabled = true;

        ParseCommandLineOverrides();

        quality = new HostQualityMonitor(this);

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

    // -rttExtraMs <ms> on the command line beats the config asset, so four builds launched from
    // one script can each carry a different simulated distance. The Editor has no such argument
    // and falls back to the asset.
    // Numeric flags a build accepts, e.g. -rttExtraMs 400 -proactiveCheckInterval 10000.
    private static readonly string[] OverridableKeys =
    {
        "rttExtraMs", "rttProbeInterval", "rttMinSamples",
        "proactiveCheckInterval", "proactiveThresholdFactor",
        "proactiveSustainedChecks", "proactiveCooldown", "proactiveMinCostGap"
    };

    private readonly Dictionary<string, float> cliOverrides = new Dictionary<string, float>();
    private string autoHostPort;
    private string autoJoinEndpoint;
    private string cliUsername;

    private float Overridden(string key, float fallback)
    {
        return cliOverrides.TryGetValue(key, out float value) ? value : fallback;
    }

    private void ParseCommandLineOverrides()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            string flag = args[i].StartsWith("-") ? args[i].Substring(1) : null;
            if (flag == null) continue;

            if (flag == "autohost") { autoHostPort = args[i + 1]; continue; }
            if (flag == "autojoin") { autoJoinEndpoint = args[i + 1]; continue; }
            if (flag == "username") { cliUsername = args[i + 1]; continue; }

            if (Array.IndexOf(OverridableKeys, flag) < 0) continue;

            // Invariant culture: a machine with a comma decimal separator would otherwise read
            // "3.0" as 30, silently making the threshold ten times stricter.
            if (float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                cliOverrides[flag] = parsed;
            else
                Debug.LogWarning($"[NetworkManager] Could not parse -{flag} '{args[i + 1]}'");
        }

        foreach (var kvp in cliOverrides)
            Debug.Log($"[NetworkManager] Override -{kvp.Key} = {kvp.Value}");

        ApplyUsername();
    }

    // The scene ships one username for everybody, so without this every instance on a machine
    // logs as the same name and no roster, vote or cost table can be read. Identity itself was
    // never affected - playerId is a per-instance GUID - but the logs were unusable.
    private void ApplyUsername()
    {
        if (!string.IsNullOrEmpty(cliUsername))
        {
            localPlayerUsername = cliUsername;
        }
        else
        {
            string suffix = localPlayerId.Length >= 4 ? localPlayerId.Substring(0, 4) : localPlayerId;
            localPlayerUsername = $"{localPlayerUsername}-{suffix}";
        }

        Debug.Log($"[NetworkManager] I am {localPlayerUsername} ({localPlayerId})");
    }

    // Autostart exists purely so a scripted multi-instance demo does not depend on clicking the
    // same buttons in four windows in the right order.
    private IEnumerator Start()
    {
        yield return null; // let PlayerSpawner and LanDiscovery come up

        if (!string.IsNullOrEmpty(autoHostPort) && int.TryParse(autoHostPort, out int port))
        {
            Debug.Log($"[NetworkManager] -autohost {port}");
            HostLobby(port);
            yield break;
        }

        if (string.IsNullOrEmpty(autoJoinEndpoint))
            yield break;

        string[] parts = autoJoinEndpoint.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int joinPort))
        {
            Debug.LogError($"[NetworkManager] -autojoin needs ip:port, got '{autoJoinEndpoint}'");
            yield break;
        }

        // Stagger so several instances do not all dial on the same frame.
        yield return new WaitForSeconds(1.0f + UnityEngine.Random.Range(0f, 0.5f));

        // Retry rather than give up: a scripted demo usually launches the clients before the
        // host finishes coming up, and one failed dial would otherwise need a manual relaunch.
        float deadline = Time.time + AutoJoinRetryWindow;
        int attempt = 0;

        while (Time.time < deadline)
        {
            if (state == ConnectionState.InLobby)
            {
                Debug.Log($"[NetworkManager] -autojoin connected after {attempt} attempt(s)");
                yield break;
            }

            if (state == ConnectionState.Disconnected)
            {
                attempt++;
                Debug.Log($"[NetworkManager] -autojoin attempt {attempt} -> {parts[0]}:{joinPort}");
                JoinLobby(parts[0], joinPort);
            }

            yield return new WaitForSeconds(1f);
        }

        Debug.LogError($"[NetworkManager] -autojoin gave up after {attempt} attempt(s) - is the host running on {parts[0]}:{joinPort}?");
    }

    private const float AutoJoinRetryWindow = 60f;

    private float lastHeartbeatSendTime = 0f;
    private float lastRosterResyncTime = 0f;
    private float connectAttemptDeadline = 0f;
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

        // Give up on a dial that never landed, so the UI returns to the browser instead of
        // sitting in ConnectingToLobby forever.
        if (state == ConnectionState.ConnectingToLobby && Time.time > connectAttemptDeadline)
        {
            Debug.LogError("Connection attempt timed out - wrong address or port, or the host is gone");
            AbortConnectionAttempt();
            return;
        }

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

            // RTT probing, plus the periodic "is the host still the right one" check.
            quality.Tick();

            if (quality.TryConsumeProposal(out string challengerId))
                BeginProactiveElection(challengerId);
            
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

    // Set while an election is replacing a host that is still alive. It changes who counts as
    // a participant: a dead host is gone, a challenged one is still a voter.
    private bool migrationIsProactive;
    private bool proactiveElectionActive;
    private Coroutine proactiveRoutine;

    // The one claimant we agreed may demote our host. A live host stands down for nobody else.
    private string grantedProactiveVoteTo;

    private readonly List<string> electionPool = new List<string>();
    private readonly List<string> electionVoters = new List<string>();

    private const int MaxElectionRounds = 5;

    private void HandleHostFailureDetect(HostFailureDetectMessage message)
    {
        if (state == ConnectionState.HostMigration)
            return;

        Debug.Log($"[HostMigration] Host failure detected by {message.reporterId}, dead host: {currentHostId}");

        // A real failure supersedes a proactive vote in flight. The mesh links already dialled
        // are kept - DialSurvivors would only have to open them again.
        if (proactiveRoutine != null)
        {
            StopCoroutine(proactiveRoutine);
            proactiveRoutine = null;
        }

        proactiveElectionActive = false;

        state = ConnectionState.HostMigration;
        migrationIsProactive = false;
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

            // Skip a dead host. A challenged live one is still a participant and a voter, and
            // we already hold a link to it, so ContainsKey below skips it anyway.
            if (!migrationIsProactive && entry.playerId == currentHostId) continue;

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
            if (!migrationIsProactive && entry.playerId == currentHostId) continue;
            if (peers.ContainsKey(entry.playerId)) count++;
        }

        return count;
    }

    // Everyone we can actually hear from. Serves as both the candidate pool and the electorate,
    // which is what keeps quorum reachable when part of the roster is unreachable.
    private void BuildReachable(List<string> into)
    {
        into.Clear();
        into.Add(localPlayerId);

        foreach (var entry in roster)
        {
            if (entry.playerId == localPlayerId) continue;
            if (!migrationIsProactive && entry.playerId == currentHostId) continue;
            if (!peers.ContainsKey(entry.playerId)) continue;

            into.Add(entry.playerId);
        }
    }

    private static string LowestId(List<string> ids)
    {
        string lowest = null;

        foreach (string id in ids)
        {
            if (lowest == null || string.CompareOrdinal(id, lowest) < 0)
                lowest = id;
        }

        return lowest;
    }

    /// <summary>
    /// Who should stand. The aggregate argmin when RTT data exists, so exactly one peer
    /// campaigns and a majority is actually attainable; lowest ordinal id when it does not,
    /// which is the original behaviour and the reason a fresh session still elects normally.
    /// </summary>
    private string ChooseCandidate()
    {
        BuildReachable(electionVoters);

        electionPool.Clear();
        foreach (string id in electionVoters)
        {
            // A live host being challenged cannot stand to replace itself.
            if (migrationIsProactive && id == currentHostId) continue;
            electionPool.Add(id);
        }

        return quality.PickByAggregateCost(electionPool, electionVoters, 1) ?? LowestId(electionPool);
    }

    private void StartElection(int round)
    {
        string candidate = ChooseCandidate();
        bool amIBestCandidate = candidate == localPlayerId;

        Debug.Log($"[HostMigration] Round {round}: reachable={CountReachableParticipants()}, candidate={candidate}, me={amIBestCandidate}, proactive={migrationIsProactive}");

        if (!amIBestCandidate)
            return;

        isCandidate = true;
        receivedVotes = 1; // a candidate votes for itself

        HostElectionRequest req = new HostElectionRequest(localPlayerId, TickManager.Instance.CurrentTick, round, migrationIsProactive);
        SendMessageToAll(req, DeliveryMethod.ReliableOrdered);

        CheckElectionVictory();
    }

    private void HandleHostElectionRequest(HostElectionRequest request, NetPeer peer)
    {
        bool accepted;

        if (request.proactive)
        {
            // Re-derive the verdict independently rather than trusting the claim. Agreeing here
            // is also the permission a live host needs before it will step down for this peer.
            accepted = quality.ValidateProactive(request.candidateId);

            // Overwrite either way. A stale yes left over from an earlier failed round must not
            // authorise a later claim we have since voted against.
            grantedProactiveVoteTo = accepted ? request.candidateId : null;
        }
        else
        {
            accepted = request.candidateId == PreferredHost(request.round);
        }

        Debug.Log($"[HostMigration] Vote for {request.candidateId}: {(accepted ? "yes" : "no")} (round={request.round}, proactive={request.proactive})");

        HostElectionResponse response = new HostElectionResponse(localPlayerId, request.candidateId, accepted);
        SendMessage(response, peer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Round 1 is the rule as specified: vote for whoever WE have the best ping to. If that
    /// splits - two peers each preferring a different neighbour - round 2 onwards switches to
    /// the shared aggregate, which every peer computes from the same matrix and so resolves
    /// unanimously. With no RTT data at all it degrades to the original lowest-id rule.
    /// </summary>
    private string PreferredHost(int round)
    {
        BuildReachable(electionVoters);

        electionPool.Clear();
        foreach (string id in electionVoters)
        {
            // Never rank ourselves: our cost to ourselves is zero, so including it would make
            // every peer prefer itself and no candidate could ever collect a vote.
            if (id == localPlayerId) continue;
            if (migrationIsProactive && id == currentHostId) continue;
            electionPool.Add(id);
        }

        string pick = round <= 1
            ? quality.PickByLocalCost(electionPool)
            : quality.PickByAggregateCost(electionPool, electionVoters, 1);

        return pick ?? LowestId(electionPool);
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
        bool proactive = migrationIsProactive;

        Debug.Log($"[HostMigration] CLAIMING HOST ROLE (proactive={proactive})...");

        string previousHostId = currentHostId;

        isCandidate = false;
        StopMigrationRoutine();
        StopProactiveRoutine();

        proactiveElectionActive = false;
        migrationIsProactive = false;
        grantedProactiveVoteTo = null;

        isHost = true;
        currentHostId = localPlayerId;
        state = ConnectionState.InLobby;

        // A failed host is gone and must leave the roster. A host that merely stepped down is
        // still connected and becomes an ordinary client, so it stays.
        if (!proactive && !string.IsNullOrEmpty(previousHostId))
            UnregisterPeer(previousHostId);

        foreach (var p in peers.Values)
            p.isHost = false;

        RefreshPeerIdentitiesFromRoster();

        HostClaimMessage claimMsg = new HostClaimMessage(localPlayerId, TickManager.Instance.CurrentTick, proactive);
        SendMessageToAll(claimMsg, DeliveryMethod.ReliableOrdered);

        BroadcastRoster();
        quality.NoteMigration();

        Debug.Log("[HostMigration] Host claim broadcast. I am now the host.");
    }

    // A client records the host as "Host"/slot 0 when it joins, which is harmless until that
    // host steps down and we have to publish a roster describing it.
    private void RefreshPeerIdentitiesFromRoster()
    {
        foreach (var p in peers.Values)
        {
            RosterEntry entry = FindRosterEntry(p.peerId);

            if (entry == null) continue;

            p.username = entry.username;
            p.assignedPlayerPosition = entry.spawnSlot;
        }
    }
    
    private void HandleHostClaim(HostClaimMessage message, NetPeer sender)
    {
        if (message.newHostId == localPlayerId)
            return;

        // Two peers can claim at once. Ordinal comparison gives every peer the same verdict,
        // so the lower id always wins and the session cannot split in two.
        if (isHost)
        {
            // Stepping down while alive is only ever allowed for a claimant we voted for.
            // Without that interlock any peer could demote the host by announcing itself.
            bool grantedByUs = message.proactive && grantedProactiveVoteTo == message.newHostId;

            if (!grantedByUs && string.CompareOrdinal(message.newHostId, localPlayerId) >= 0)
            {
                Debug.Log($"[HostMigration] Ignoring claim from {message.newHostId} - our id wins");
                return;
            }

            Debug.LogWarning($"[HostMigration] Standing down for {message.newHostId} (proactive={message.proactive})");
            isHost = false;
        }

        Debug.Log($"[HostMigration] Received host claim from {message.newHostId}");

        isCandidate = false;
        StopMigrationRoutine();
        StopProactiveRoutine();

        proactiveElectionActive = false;
        migrationIsProactive = false;
        grantedProactiveVoteTo = null;

        // Drop the previous host, then promote the claimant. Keyed by playerId, so this no
        // longer needs the old re-keying dance against connection-local NetPeer ids.
        if (!string.IsNullOrEmpty(currentHostId) && currentHostId != message.newHostId)
        {
            // UnregisterPeer only clears bookkeeping. When the old host is still alive its
            // socket has to be closed explicitly, or the link leaks for the rest of the session.
            if (message.proactive && peers.TryGetValue(currentHostId, out PeerInfo oldHost) && oldHost.netPeer != null)
                oldHost.netPeer.Disconnect();

            UnregisterPeer(currentHostId);
        }

        RosterEntry entry = FindRosterEntry(message.newHostId);
        int slot = entry != null ? entry.spawnSlot : 0;
        string username = entry != null ? entry.username : "NewHost";

        PeerInfo newHostInfo = RegisterPeer(message.newHostId, username, slot, sender);
        newHostInfo.isHost = true;
        newHostInfo.lastHeartbeatReceiveTime = Time.time;

        currentHostId = message.newHostId;
        state = ConnectionState.InLobby;

        DropMeshPeers();
        quality.NoteMigration();

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

    private void StopProactiveRoutine()
    {
        if (proactiveRoutine == null)
            return;

        StopCoroutine(proactiveRoutine);
        proactiveRoutine = null;
    }

//proactive migration - the host is alive, just badly placed

    // Unlike failure migration this must not freeze the simulation and must not touch the
    // roster. If the vote does not carry, nothing whatsoever changed.
    private void BeginProactiveElection(string challengerId)
    {
        if (challengerId != localPlayerId) return;
        if (state != ConnectionState.InLobby) return;
        if (proactiveElectionActive || migrationRoutine != null) return;

        Debug.Log($"[Proactive] Host {currentHostId} is far worse placed than us - standing for election");

        proactiveElectionActive = true;
        migrationIsProactive = true;
        receivedVotes = 0;
        isCandidate = false;

        proactiveRoutine = StartCoroutine(RunProactiveElection());
    }

    private IEnumerator RunProactiveElection()
    {
        // The star is intact, so the other clients have no link to us. Dial them for the vote;
        // AbortProactiveElection tears the mesh back down if it fails.
        DialSurvivors();

        yield return new WaitForSeconds(1.0f);

        if (!proactiveElectionActive)
            yield break;

        StartElection(1);

        yield return new WaitForSeconds(1.5f);

        if (!proactiveElectionActive)
            yield break;

        // A split proactive vote is a safe no-op: no retry and no runoff, because the session
        // already has a working host. The cooldown then keeps a stable disagreement - the
        // classic "my neighbour has better ping to me" split - from re-proposing every check.
        Debug.Log("[Proactive] No majority, keeping the current host");
        AbortProactiveElection();
    }

    private void AbortProactiveElection()
    {
        StopProactiveRoutine();

        proactiveElectionActive = false;
        migrationIsProactive = false;
        isCandidate = false;
        receivedVotes = 0;
        grantedProactiveVoteTo = null;

        // currentHostId never changed, so this drops exactly the links dialled for the vote
        // and leaves the star as it was.
        DropMeshPeers();
        quality.NoteMigration();
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
        return HostLobby(config != null ? config.gamePort : 7777);
    }

    /// <summary>
    /// Host on an explicit port, so several instances can host on one machine - the default
    /// port is already taken by the first of them.
    /// </summary>
    public bool HostLobby(int port)
    {
        if (state != ConnectionState.Disconnected)
        {
            Debug.LogWarning("Cannot host lobby - already connected");
            return false;
        }

        bool success = netManager.Start(port);
        
        if (success)
        {
            isHost = true;
            localSpawnSlot = 0;
            currentHostId = localPlayerId;
            state = ConnectionState.InLobby;
            RebuildRoster();
            ApplyRoster();
            Debug.Log($"Hosting lobby on port {port}");
        }
        else
        {
            Debug.LogError($"Failed to start host on port {port} - already in use?");
        }
        
        return success;
    }
    
    /// <summary>
    /// Join an existing lobby
    /// </summary>
    public bool JoinLobby(string hostAddress, int hostPort)
    {
        // A dial that never completed leaves us in ConnectingToLobby. Abandon it rather than
        // refusing forever - otherwise one failed attempt makes the client unable to retry.
        if (state == ConnectionState.ConnectingToLobby)
        {
            Debug.LogWarning("Abandoning the previous connection attempt");
            AbortConnectionAttempt();
        }

        if (state != ConnectionState.Disconnected)
        {
            Debug.LogWarning($"Cannot join lobby - state is {state}");
            return false;
        }

        netManager.Start();
        NetPeer peer = netManager.Connect(hostAddress, hostPort, "");

        if (peer != null)
        {
            state = ConnectionState.ConnectingToLobby;
            connectAttemptDeadline = Time.time + ConnectTimeoutSeconds;
            Debug.Log($"Connecting to {hostAddress}:{hostPort}");
            return true;
        }

        Debug.LogError($"Failed to start a connection to {hostAddress}:{hostPort}");
        netManager.Stop();
        return false;
    }

    private float ConnectTimeoutSeconds => config != null ? config.connectionTimeout / 1000f : 5f;

    private void AbortConnectionAttempt()
    {
        netManager.Stop();
        quality.ResetAll();
        peers.Clear();
        playerIdByNetPeer.Clear();
        roster.Clear();
        ApplyRoster();
        state = ConnectionState.Disconnected;
        currentHostId = null;
        localSpawnSlot = -1;
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
        StopProactiveRoutine();
        proactiveElectionActive = false;
        migrationIsProactive = false;
        grantedProactiveVoteTo = null;
        isCandidate = false;
        receivedVotes = 0;

        netManager.Stop();
        quality.ResetAll();

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

    /// <summary>
    /// Connectionless send. RTT probing uses it to reach peers we hold no connection to, which
    /// in a star is every client except from the host's point of view.
    /// </summary>
    public void SendUnconnected(NetDataWriter writer, string address, int port)
    {
        netManager?.SendUnconnectedMessage(writer, address, port);
    }

    public void SendUnconnected(NetDataWriter writer, System.Net.IPEndPoint endPoint)
    {
        netManager?.SendUnconnectedMessage(writer, endPoint);
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

        if (state == ConnectionState.HostMigration || proactiveElectionActive)
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
        // RTT probes arrive here rather than through HandleMessage: LiteNetLib routes an
        // unconnected packet before any peer lookup, which is exactly why one code path can
        // reach the connected host and unconnected clients alike.
        quality?.HandleUnconnected(remoteEndPoint, reader);
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        PeerInfo peerInfo = FindPeer(peer);

        if (peerInfo != null)
        {
            peerInfo.latency = latency;
        }
    }
    
    private bool IsKnownRosterEndpoint(System.Net.IPEndPoint endPoint)
    {
        string ip = endPoint.Address.ToString();

        foreach (var e in roster)
        {
            if (e.playerId != localPlayerId && e.ipAddress == ip && e.listenPort == endPoint.Port)
                return true;
        }

        return false;
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        // 1. Host accepts everyone
        if (isHost)
        {
            request.Accept();
        }
        // 2. Host Migration: Accept peers trying to form a mesh
        else if (state == ConnectionState.HostMigration || proactiveElectionActive)
        {
            Debug.Log($"[HostMigration] Accepting P2P connection from {request.RemoteEndPoint}");
            request.Accept();
        }
        // 3. A peer already in the session dialling us directly - a challenger building the
        // mesh it needs to run a proactive vote while the star is still up.
        else if (IsKnownRosterEndpoint(request.RemoteEndPoint))
        {
            Debug.Log($"[NetworkManager] Accepting roster peer {request.RemoteEndPoint}");
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
            if ((state == ConnectionState.HostMigration || proactiveElectionActive) && !peers.ContainsKey(request.playerId))
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
