using UnityEngine;
using LiteNetLib;
using System.Collections.Generic;

/// <summary>
/// Host-authoritative zombie waves.
///
/// The host owns spawning, AI and health; clients only mirror what it broadcasts. State is
/// sent as the full living set every tick and reconciled by id, so late join and host
/// migration need no special handling - a new host simply continues from the wave number it
/// was already receiving.
/// </summary>
public class ZombieSpawner : MonoBehaviour
{
    public static ZombieSpawner Instance { get; private set; }

    [Header("Prefab")]
    [SerializeField] private GameObject zombiePrefab;

    [Header("Waves")]
    [SerializeField] private int zombiesInFirstWave = 4;
    [SerializeField] private int extraZombiesPerWave = 2;
    [SerializeField] private float delayBeforeFirstWave = 5f;
    [SerializeField] private float delayBetweenWaves = 8f;

    [Tooltip("Hard cap on living zombies. Also bounds the state packet: Sequenced delivery " +
             "does not fragment, so an unbounded set would eventually exceed MTU.")]
    [SerializeField] private int maxAliveZombies = 40;

    [Header("Spawn placement")]
    [Tooltip("Never spawn closer than this to any player")]
    [SerializeField] private float minSpawnDistance = 10f;
    [SerializeField] private float maxSpawnDistance = 16f;
    [SerializeField] private int placementAttempts = 30;
    [SerializeField] private float spawnFootprintRadius = 0.4f;

    [Header("Pathfinding")]
    [Tooltip("How often the shared flow field is re-flooded from player positions")]
    [SerializeField] private float flowFieldRefreshInterval = 0.4f;

    private readonly ZombieFlowField flowField = new ZombieFlowField();
    private readonly List<Vector2> flowSources = new List<Vector2>();
    private float nextFlowRebuild;

    private readonly Dictionary<int, NetworkedZombie> zombies = new Dictionary<int, NetworkedZombie>();
    private readonly List<ZombieState> stateBuffer = new List<ZombieState>();
    private readonly List<int> departedZombies = new List<int>();

    private int waveNumber;
    private int nextZombieId = 1;
    private float nextWaveTime;
    private bool waveInProgress;

    public int WaveNumber => waveNumber;
    public int AliveCount => zombies.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick += HandleTick;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnMessageReceived += HandleMessage;

        nextWaveTime = Time.time + delayBeforeFirstWave;
    }

    private void OnDestroy()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick -= HandleTick;

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.OnMessageReceived -= HandleMessage;

        if (Instance == this)
            Instance = null;
    }

    private void HandleTick(int tick)
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsHost)
            return;

        // Nothing to fight over until somebody is actually in the world.
        if (PlayerSpawner.Instance == null || PlayerSpawner.Instance.GetAllPlayers().Count == 0)
            return;

        PruneDeadZombies();
        RebuildFlowField();
        AdvanceWaves();
        BroadcastZombieState();
    }

    // One flood for every zombie, rather than a path each. Host only - clients just mirror.
    private void RebuildFlowField()
    {
        if (Time.time < nextFlowRebuild || WalkableMap.Instance == null)
            return;

        nextFlowRebuild = Time.time + flowFieldRefreshInterval;

        flowSources.Clear();

        foreach (var kvp in PlayerSpawner.Instance.GetAllPlayers())
        {
            if (kvp.Value != null)
                flowSources.Add(kvp.Value.transform.position);
        }

        flowField.Rebuild(WalkableMap.Instance, flowSources);
    }

    public bool TryGetFlowDirection(Vector2 worldPosition, out Vector2 direction)
    {
        direction = Vector2.zero;

        if (WalkableMap.Instance == null)
            return false;

        return flowField.TryGetDirection(WalkableMap.Instance, worldPosition, out direction);
    }

//host

    private void AdvanceWaves()
    {
        // Just became host mid-wave: adopt the zombies we were mirroring rather than treating
        // the field as empty and immediately starting the next wave on top of them.
        if (!waveInProgress && zombies.Count > 0)
        {
            waveInProgress = true;
            return;
        }

        if (waveInProgress)
        {
            if (zombies.Count > 0)
                return;

            waveInProgress = false;
            nextWaveTime = Time.time + delayBetweenWaves;
            Debug.Log($"[ZombieSpawner] Wave {waveNumber} cleared");
            return;
        }

        if (Time.time < nextWaveTime)
            return;

        waveNumber++;
        int count = zombiesInFirstWave + (waveNumber - 1) * extraZombiesPerWave;

        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (zombies.Count >= maxAliveZombies)
                break;

            if (SpawnOne())
                spawned++;
        }

        waveInProgress = spawned > 0;

        // If nowhere was valid, do not latch into an empty "wave in progress" forever.
        if (!waveInProgress)
            nextWaveTime = Time.time + delayBetweenWaves;

        Debug.Log($"[ZombieSpawner] Wave {waveNumber} started: {spawned}/{count} spawned");
    }

    private bool SpawnOne()
    {
        if (zombiePrefab == null)
        {
            Debug.LogError("[ZombieSpawner] No zombie prefab assigned");
            return false;
        }

        if (!TryFindSpawnPoint(out Vector2 position))
            return false;

        GameObject obj = Instantiate(zombiePrefab, position, Quaternion.identity);

        NetworkedZombie zombie = obj.GetComponent<NetworkedZombie>();
        if (zombie == null)
            zombie = obj.AddComponent<NetworkedZombie>();

        // A new host inherits zombies it was mirroring, whose ids came from the old host.
        // Without this the counter restarts at 1 and collides with them.
        foreach (int existingId in zombies.Keys)
        {
            if (existingId >= nextZombieId)
                nextZombieId = existingId + 1;
        }

        zombie.zombieId = nextZombieId++;
        zombies[zombie.zombieId] = zombie;

        return true;
    }

    // Far enough from everyone to be fair, on a painted tile so they cannot spawn in the void.
    private bool TryFindSpawnPoint(out Vector2 position)
    {
        position = Vector2.zero;

        var players = PlayerSpawner.Instance.GetAllPlayers();
        if (players.Count == 0)
            return false;

        for (int attempt = 0; attempt < placementAttempts; attempt++)
        {
            Vector2 anchor = RandomPlayerPosition(players);
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float distance = Random.Range(minSpawnDistance, maxSpawnDistance);

            Vector2 candidate = anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

            // Footprint, not just the centre - a centre-only test let them spawn half over
            // the void, or wedged against an edge.
            if (WalkableMap.Instance != null && !WalkableMap.Instance.CanStandAt(candidate, spawnFootprintRadius))
                continue;

            if (IsTooCloseToAnyPlayer(candidate, players))
                continue;

            position = candidate;
            return true;
        }

        return false;
    }

    private Vector2 RandomPlayerPosition(IReadOnlyDictionary<string, NetworkedPlayer> players)
    {
        int index = Random.Range(0, players.Count);
        int i = 0;

        foreach (var kvp in players)
        {
            if (i++ == index && kvp.Value != null)
                return kvp.Value.transform.position;
        }

        return Vector2.zero;
    }

    private bool IsTooCloseToAnyPlayer(Vector2 candidate, IReadOnlyDictionary<string, NetworkedPlayer> players)
    {
        foreach (var kvp in players)
        {
            if (kvp.Value == null)
                continue;

            if (Vector2.Distance(candidate, kvp.Value.transform.position) < minSpawnDistance)
                return true;
        }

        return false;
    }

    private void PruneDeadZombies()
    {
        departedZombies.Clear();

        foreach (var kvp in zombies)
        {
            if (kvp.Value == null)
                departedZombies.Add(kvp.Key);
        }

        foreach (int id in departedZombies)
            zombies.Remove(id);
    }

    private void BroadcastZombieState()
    {
        if (NetworkManager.Instance.ConnectedPeers.Count == 0)
            return;

        stateBuffer.Clear();

        foreach (var kvp in zombies)
        {
            NetworkedZombie zombie = kvp.Value;
            if (zombie == null)
                continue;

            stateBuffer.Add(new ZombieState(
                zombie.zombieId,
                zombie.transform.position,
                zombie.transform.rotation.eulerAngles.z,
                zombie.health != null ? zombie.health.currentHealth : 0));
        }

        ZombieStateMessage message = new ZombieStateMessage(waveNumber, stateBuffer);
        NetworkManager.Instance.SendMessageToAll(message, DeliveryMethod.Sequenced);
    }

//client

    private void HandleMessage(INetworkMessage message, NetPeer peer)
    {
        if (NetworkManager.Instance == null || NetworkManager.Instance.IsHost)
            return;

        if (message.GetMessageType() != MessageType.ZombieState)
            return;

        ApplyZombieState((ZombieStateMessage)message);
    }

    private void ApplyZombieState(ZombieStateMessage message)
    {
        // Cached so a client promoted to host resumes at the right wave instead of restarting.
        waveNumber = message.waveNumber;

        foreach (var state in message.zombies)
        {
            if (!zombies.TryGetValue(state.zombieId, out NetworkedZombie zombie) || zombie == null)
            {
                zombie = SpawnMirror(state);
                if (zombie == null)
                    continue;
            }

            if (zombie.interpolator != null)
                zombie.interpolator.Push(state.position, state.rotation);

            if (zombie.health != null)
                zombie.health.SetHealthFromNetwork(state.health);
        }

        DespawnMissing(message);
    }

    private NetworkedZombie SpawnMirror(ZombieState state)
    {
        if (zombiePrefab == null)
            return null;

        GameObject obj = Instantiate(zombiePrefab, state.position, Quaternion.Euler(0f, 0f, state.rotation));

        NetworkedZombie zombie = obj.GetComponent<NetworkedZombie>();
        if (zombie == null)
            zombie = obj.AddComponent<NetworkedZombie>();

        zombie.zombieId = state.zombieId;

        if (zombie.interpolator == null)
            zombie.interpolator = obj.GetComponent<RemoteInterpolator>();

        if (zombie.interpolator == null)
            zombie.interpolator = obj.AddComponent<RemoteInterpolator>();

        zombies[state.zombieId] = zombie;
        return zombie;
    }

    private void DespawnMissing(ZombieStateMessage message)
    {
        departedZombies.Clear();

        foreach (var kvp in zombies)
        {
            bool stillAlive = false;

            foreach (var state in message.zombies)
            {
                if (state.zombieId == kvp.Key)
                {
                    stillAlive = true;
                    break;
                }
            }

            if (!stillAlive)
                departedZombies.Add(kvp.Key);
        }

        foreach (int id in departedZombies)
        {
            if (zombies.TryGetValue(id, out NetworkedZombie zombie) && zombie != null)
                Destroy(zombie.gameObject);

            zombies.Remove(id);
        }
    }
}
