using UnityEngine;
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
    
    private void Start()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnPeerJoined += HandlePeerJoined;
            NetworkManager.Instance.OnPeerLeft += HandlePeerLeft;
        }
    }
    
    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnPeerJoined -= HandlePeerJoined;
            NetworkManager.Instance.OnPeerLeft -= HandlePeerLeft;
        }
    }
    
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
    
    public NetworkedPlayer GetPlayer(string playerId)
    {
        spawnedPlayers.TryGetValue(playerId, out NetworkedPlayer player);
        return player;
    }
    
    public IReadOnlyDictionary<string, NetworkedPlayer> GetAllPlayers()
    {
        return spawnedPlayers;
    }
    
    private Vector3 GetSpawnPosition(int playerPosition)
    {
        if (spawnPoints != null && spawnPoints.Length > 0 && playerPosition < spawnPoints.Length)
        {
            return spawnPoints[playerPosition].position;
        }
        
        // Default spawn positions
        return new Vector3(playerPosition * 2, 0, 0);
    }
    
    private void HandlePeerJoined(PeerInfo peerInfo)
    {
        // DON'T auto-spawn here! Players should only spawn when the game starts.
        // This was causing duplicate spawning: once on lobby join, once on game start.
        
        Debug.Log($"[PlayerSpawner] Peer {peerInfo.username} joined lobby (will spawn when game starts)");
    }
    
    private void HandlePeerLeft(PeerInfo peerInfo)
    {
        DespawnPlayer(peerInfo.peerId);
    }
}
