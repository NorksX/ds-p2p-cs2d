using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Transitions from lobby to gameplay and spawns players
/// </summary>
public class SimpleGameStarter : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject lobbyUI;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject startGameButton;
    
    private void Update()
    {
        if (NetworkManager.Instance != null && startGameButton != null)
        {
            // Only show button if we are the Host AND in the lobby
            bool isHost = NetworkManager.Instance.IsHost;
            bool inLobby = NetworkManager.Instance.State == ConnectionState.InLobby;
            
            if (startGameButton.activeSelf != (isHost && inLobby))
            {
                startGameButton.SetActive(isHost && inLobby);
            }
        }
    }
    
    private void Start()
    {
        Debug.Log("[SimpleGameStarter] Start() called");
        
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnGameStarted += HandleGameStarted;
            Debug.Log("[SimpleGameStarter] Subscribed to OnGameStarted event");
        }
        else
        {
            Debug.LogError("[SimpleGameStarter] NetworkManager.Instance is NULL!");
        }
    }
    
    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnGameStarted -= HandleGameStarted;
            Debug.Log("[SimpleGameStarter] Unsubscribed from OnGameStarted event");
        }
    }
    
    // UI Button calls this
    public void StartGame()
    {
        Debug.Log("[SimpleGameStarter] StartGame() called!");
        
        if (NetworkManager.Instance == null)
        {
            Debug.LogError("[SimpleGameStarter] NetworkManager is NULL!");
            return;
        }
        
        // Only host can start
        if (!NetworkManager.Instance.IsHost)
        {
            Debug.LogWarning("[SimpleGameStarter] Only Host can start the game! IsHost=" + NetworkManager.Instance.IsHost);
            return;
        }
        
        Debug.Log("[SimpleGameStarter] Host is starting game...");
        
        // Prepare player list for the message
        List<PlayerSpawnInfo> allPlayers = new List<PlayerSpawnInfo>();
        
        // 1. Add Host (Local)
        allPlayers.Add(new PlayerSpawnInfo
        {
            playerId = NetworkManager.Instance.LocalPlayerId,
            username = "Host",
            spawnPositionIndex = 0,
            isHost = true
        });
        
        Debug.Log($"[SimpleGameStarter] Added Host to player list. ID={NetworkManager.Instance.LocalPlayerId}");
        
        // 2. Add Clients (Remote)
        foreach (var peer in NetworkManager.Instance.ConnectedPeers.Values)
        {
            allPlayers.Add(new PlayerSpawnInfo
            {
                playerId = peer.peerId,
                username = peer.username,
                spawnPositionIndex = peer.assignedPlayerPosition,
                isHost = false
            });
            
            Debug.Log($"[SimpleGameStarter] Added Client to player list. ID={peer.peerId}, Username={peer.username}, Position={peer.assignedPlayerPosition}");
        }
        
        Debug.Log($"[SimpleGameStarter] Broadcasting StartGame message with {allPlayers.Count} players");
        
        // Broadcast Start Game to everyone (including self via local invoke)
        NetworkManager.Instance.BroadcastGameStart(allPlayers);
    }
    
    private void HandleGameStarted(StartGameMessage message)
    {
        Debug.Log($"[SimpleGameStarter] HandleGameStarted() called! Players in message: {message.players.Count}");
        
        // 1. Update UI
        Debug.Log("[SimpleGameStarter] Hiding lobby UI, showing gameplay UI...");
        
        if (lobbyUI != null)
        {
            lobbyUI.SetActive(false);
            Debug.Log("[SimpleGameStarter] LobbyUI hidden");
        }
        else
        {
            Debug.LogWarning("[SimpleGameStarter] LobbyUI is NULL!");
        }
        
        if (gameplayPanel != null)
        {
            gameplayPanel.SetActive(true);
            Debug.Log("[SimpleGameStarter] GameplayPanel shown");
        }
        else
        {
            Debug.LogWarning("[SimpleGameStarter] GameplayPanel is NULL!");
        }
        
        // 2. Spawn Players
        if (PlayerSpawner.Instance != null)
        {
            Debug.Log($"[SimpleGameStarter] PlayerSpawner found. LocalPlayerId={NetworkManager.Instance.LocalPlayerId}");
            
            foreach (var playerInfo in message.players)
            {
                Debug.Log($"[SimpleGameStarter] Processing player: ID={playerInfo.playerId}, Username={playerInfo.username}, Position={playerInfo.spawnPositionIndex}, IsHost={playerInfo.isHost}");
                
                if (playerInfo.playerId == NetworkManager.Instance.LocalPlayerId)
                {
                    // It's ME! Spawn local player
                    Debug.Log($"[SimpleGameStarter] Spawning LOCAL player at position {playerInfo.spawnPositionIndex}");
                    PlayerSpawner.Instance.SpawnLocalPlayer(playerInfo.spawnPositionIndex);
                }
                else
                {
                    // It's SOMEONE ELSE! Spawn remote player
                    Debug.Log($"[SimpleGameStarter] Spawning REMOTE player '{playerInfo.username}' at position {playerInfo.spawnPositionIndex}");
                    PlayerSpawner.Instance.SpawnRemotePlayerByInfo(playerInfo.playerId, playerInfo.spawnPositionIndex, playerInfo.username);
                }
            }
            
            Debug.Log("[SimpleGameStarter] All players spawned!");
        }
        else
        {
            Debug.LogError("[SimpleGameStarter] PlayerSpawner.Instance is NULL!");
        }
    }
}
