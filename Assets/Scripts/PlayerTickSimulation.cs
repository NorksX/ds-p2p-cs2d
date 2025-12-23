using UnityEngine;

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
        // Disable tick simulation on remote players (they are updated by network state)
        NetworkedPlayer networkedPlayer = GetComponent<NetworkedPlayer>();
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.Log($"[PlayerTickSimulation] Disabling on REMOTE player {networkedPlayer.playerId}");
            this.enabled = false;
            return;
        }
        
        Debug.Log("[PlayerTickSimulation] Start() - Enabled for LOCAL player");

        if (TickManager.Instance == null)
        {
            Debug.LogError("TickManager.Instance is NULL in PlayerTickSimulation.Start()");
            return;
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

        if (player == null || buffer == null)
            return;

        if (!buffer.TryGet(tick - 1, out InputCommand cmd))
            return;


        player.SimulateMovement(cmd.move);
        player.SimulateLook(cmd.aimDir);
        // Debug.Log($"Sim tick {tick}, move={cmd.move}");

        if (cmd.firePressed)
            player.SimulateShoot(cmd.aimDir);
    }
}
