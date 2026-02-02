using UnityEngine;

/// <summary>
/// Marks a player as networked and stores their network identity
/// </summary>
public class NetworkedPlayer : MonoBehaviour
{
    [Header("Network Identity")]
    public string playerId;
    public int playerPosition; // 0-3 for 4-player co-op
    public bool isLocalPlayer;
    
    [Header("References")]
    public PlayerController playerController;
    
    private void Awake()
    {
        if (playerController == null)
            playerController = GetComponent<PlayerController>();
    }
}
