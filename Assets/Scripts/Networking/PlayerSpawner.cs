using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Spawns and manages networked players
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    public static PlayerSpawner Instance { get; private set; }
    
    [Header("Prefab")]
    [SerializeField] private GameObject playerPrefab;
    
    [Header("Spawn Points")]
    [SerializeField] private Transform[] spawnPoints;
    
    // Track spawned players
    private Dictionary<string, NetworkedPlayer> spawnedPlayers = new Dictionary<string, NetworkedPlayer>();
    
    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
    }
    
    // No peer-event subscriptions: SyncToRoster is the only thing that spawns or despawns.

    /// <summary>
    /// Spawn local player
    /// </summary>
    public void SpawnLocalPlayer(int playerPosition)
    {
        if (NetworkManager.Instance == null) return;
        
        string playerId = NetworkManager.Instance.LocalPlayerId;
        
        if (spawnedPlayers.ContainsKey(playerId))
        {
            Debug.LogWarning($"Player {playerId} already spawned");
            return;
        }
        
        Vector3 spawnPos = GetSpawnPosition(playerPosition);
        GameObject playerObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        
        NetworkedPlayer networkedPlayer = playerObj.GetComponent<NetworkedPlayer>();
        if (networkedPlayer == null)
            networkedPlayer = playerObj.AddComponent<NetworkedPlayer>();
        
        // CRITICAL: Set these IMMEDIATELY after instantiation, before Start() runs
        networkedPlayer.playerId = playerId;
        networkedPlayer.playerPosition = playerPosition;
        networkedPlayer.isLocalPlayer = true;  // Must be set before Start()
        
        spawnedPlayers[playerId] = networkedPlayer;
        
        Debug.Log($"Spawned local player at position {playerPosition}");
    }
    
    /// <summary>
    /// Spawn a remote player using raw info (for clients spawning other clients)
    /// </summary>
    public void SpawnRemotePlayerByInfo(string playerId, int playerPosition, string username)
    {
        if (spawnedPlayers.ContainsKey(playerId))
        {
            Debug.LogWarning($"Player {playerId} already spawned");
            return;
        }
        
        Vector3 spawnPos = GetSpawnPosition(playerPosition);
        GameObject playerObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);

        StripInputFrom(playerObj);

        // Remote players are played back from a snapshot buffer instead of being snapped.
        if (playerObj.GetComponent<RemoteInterpolator>() == null)
            playerObj.AddComponent<RemoteInterpolator>();

        NetworkedPlayer networkedPlayer = playerObj.GetComponent<NetworkedPlayer>();
        if (networkedPlayer == null)
            networkedPlayer = playerObj.AddComponent<NetworkedPlayer>();

        networkedPlayer.playerId = playerId;
        networkedPlayer.playerPosition = playerPosition;
        networkedPlayer.isLocalPlayer = false;

        spawnedPlayers[playerId] = networkedPlayer;

        Debug.Log($"Spawned remote player '{username}' at position {playerPosition}");
    }
    
    public void SpawnRemotePlayer(PeerInfo peerInfo)
    {
        SpawnRemotePlayerByInfo(peerInfo.peerId, peerInfo.assignedPlayerPosition, peerInfo.username);
    }

    // PlayerInput pairs the keyboard/mouse in OnEnable and the first instance claims them,
    // so a remote player spawned before the local one would steal its input.
    private void StripInputFrom(GameObject playerObj)
    {
        PlayerInput playerInput = playerObj.GetComponent<PlayerInput>();
        if (playerInput == null)
            return;

        // Disable first: it unpairs synchronously, Destroy only lands at end of frame.
        playerInput.enabled = false;
        Destroy(playerInput);
    }
    
    /// <summary>
    /// Despawn a player when they disconnect
    /// </summary>
    public void DespawnPlayer(string playerId)
    {
        if (spawnedPlayers.TryGetValue(playerId, out NetworkedPlayer player))
        {
            Destroy(player.gameObject);
            spawnedPlayers.Remove(playerId);
            Debug.Log($"Despawned player {playerId}");
        }
    }
    
    /// <summary>
    /// Reconcile spawned players against the roster: spawn what is missing, despawn what is gone.
    /// Idempotent, so it doubles as the late-join and post-migration resync path.
    /// </summary>
    public void SyncToRoster(IReadOnlyList<RosterEntry> roster, string localPlayerId)
    {
        foreach (var entry in roster)
        {
            if (spawnedPlayers.ContainsKey(entry.playerId))
                continue;

            if (entry.playerId == localPlayerId)
                SpawnLocalPlayer(entry.spawnSlot);
            else
                SpawnRemotePlayerByInfo(entry.playerId, entry.spawnSlot, entry.username);
        }

        List<string> departed = null;

        foreach (var kvp in spawnedPlayers)
        {
            bool stillPresent = false;
            foreach (var entry in roster)
            {
                if (entry.playerId == kvp.Key)
                {
                    stillPresent = true;
                    break;
                }
            }

            if (!stillPresent)
                (departed ??= new List<string>()).Add(kvp.Key);
        }

        if (departed == null)
            return;

        foreach (string playerId in departed)
            DespawnPlayer(playerId);
    }

    public NetworkedPlayer GetPlayer(string playerId)
    {
        spawnedPlayers.TryGetValue(playerId, out NetworkedPlayer player);
        return player;
    }
    
    public IReadOnlyDictionary<string, NetworkedPlayer> GetAllPlayers()
    {
        return spawnedPlayers;
    }
    
    public Vector3 GetSpawnPosition(int playerPosition)
    {
        if (spawnPoints != null && spawnPoints.Length > 0 && playerPosition < spawnPoints.Length)
        {
            return spawnPoints[playerPosition].position;
        }
        
        // Default spawn positions
        return new Vector3(playerPosition * 2, 0, 0);
    }
}
