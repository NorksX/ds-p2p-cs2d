using UnityEngine;
using System.Collections;

public class PlayerTickSimulation : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private LocalInputBuffer buffer;

    private void Awake()
    {
        if (player == null)
            player = GetComponent<PlayerController>();

        if (buffer == null)
            buffer = GetComponent<LocalInputBuffer>();
    }

    // IMPORTANT: subscribe in Start, not OnEnable
    private void Start()
    {
        StartCoroutine(InitializeAfterSpawn());
    }
    
    private System.Collections.IEnumerator InitializeAfterSpawn()
    {
        // Wait one frame to ensure PlayerSpawner has set isLocalPlayer
        yield return null;
        
        // Disable tick simulation on remote players (they are updated by network state)
        NetworkedPlayer networkedPlayer = GetComponent<NetworkedPlayer>();
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.Log($"[PlayerTickSimulation] Disabling on REMOTE player {networkedPlayer.playerId}");
            this.enabled = false;
            yield break;
        }
        
        Debug.Log("[PlayerTickSimulation] Start() - Enabled for LOCAL player");

        if (TickManager.Instance == null)
        {
            Debug.LogError("TickManager.Instance is NULL in PlayerTickSimulation.Start()");
            yield break;
        }

        TickManager.Instance.OnTick += HandleTick;
    }

    private void OnDestroy()
    {
        if (TickManager.Instance != null)
            TickManager.Instance.OnTick -= HandleTick;
    }

    private void HandleTick(int tick)
    {
        // CRITICAL: Double-check we're not running on a remote player
        // This handles race conditions where the component might not be disabled yet
        NetworkedPlayer networkedPlayer = GetComponent<NetworkedPlayer>();
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.LogWarning($"[PlayerTickSimulation] HandleTick called on REMOTE player {networkedPlayer.playerId}! This should not happen!");
            return;
        }

        if (player == null || buffer == null)
            return;

        if (!buffer.TryGet(tick - 1, out InputCommand cmd))
            return;


        player.SimulateMovement(cmd.move);
        player.SimulateLook(cmd.aimDir);
        // Debug.Log($"Sim tick {tick}, move={cmd.move}");

        if (cmd.firePressed)
        {
            player.SimulateShoot(cmd.aimDir);
            
            // If we're the host, broadcast shoot event to all clients
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsHost)
            {
                NetworkedPlayer np = GetComponent<NetworkedPlayer>();
                if (np != null)
                {
                    Vector2 shootOrigin = transform.position;
                    ShootEventMessage shootMsg = new ShootEventMessage(np.playerId, shootOrigin, cmd.aimDir);
                    NetworkManager.Instance.SendMessageToAll(shootMsg, LiteNetLib.DeliveryMethod.ReliableOrdered);
                    Debug.Log($"[PlayerTickSimulation] Host broadcasted own shoot event");
                }
            }
        }
    }
}
