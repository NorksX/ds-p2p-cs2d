using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Simulates the local player every tick (prediction) and reconciles against the host.
///
/// The host acks the last input tick it simulated, expressed in THIS client's tick numbering,
/// so reconciliation rewinds to that tick, snaps to the authoritative position, and replays
/// every input made since. When the prediction was right the replay lands where we already
/// were and nothing visibly moves.
/// </summary>
public class PlayerTickSimulation : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private LocalInputBuffer buffer;

    [Header("Reconciliation")]
    [Tooltip("Prediction error below this is accepted as correct, to avoid replaying constantly")]
    [SerializeField] private float positionTolerance = 0.05f;

    private const int MaxHistoryTicks = 240;

    private readonly Dictionary<int, Vector2> predictedPositions = new Dictionary<int, Vector2>();
    private int oldestRecordedTick;
    private int lastSimulatedTick = -1;
    private bool isLocal;

    private PlayerHealth health;

    private void Awake()
    {
        if (player == null)
            player = GetComponent<PlayerController>();

        if (buffer == null)
            buffer = GetComponent<LocalInputBuffer>();

        health = GetComponent<PlayerHealth>();
    }

    // IMPORTANT: subscribe in Start, not OnEnable
    private void Start()
    {
        StartCoroutine(InitializeAfterSpawn());
    }

    private IEnumerator InitializeAfterSpawn()
    {
        // Wait one frame to ensure PlayerSpawner has set isLocalPlayer
        yield return null;

        NetworkedPlayer networkedPlayer = GetComponent<NetworkedPlayer>();
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.Log($"[PlayerTickSimulation] Disabling on REMOTE player {networkedPlayer.playerId}");
            this.enabled = false;
            yield break;
        }

        isLocal = true;
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
        if (player == null || buffer == null)
            return;

        // No authority exists mid-migration, so predicting would only build up divergence
        // that the new host has to undo the moment it takes over.
        if (NetworkManager.Instance != null
            && NetworkManager.Instance.State == ConnectionState.HostMigration)
            return;

        // The host ignores a dead player's input, so predicting movement would just be
        // corrected away every snapshot.
        if (health != null && health.IsDead)
            return;

        if (!buffer.TryGet(tick - 1, out InputCommand cmd))
            return;

        player.SimulateMovement(cmd.move);
        player.SimulateLook(cmd.aimDir);

        Record(cmd.tick, player.Position);
        lastSimulatedTick = cmd.tick;

        if (cmd.firePressed)
        {
            player.SimulateShoot(cmd.aimDir);

            // Host broadcasts its own shots; clients let the host author theirs.
            if (NetworkManager.Instance != null && NetworkManager.Instance.IsHost)
            {
                NetworkedPlayer np = GetComponent<NetworkedPlayer>();
                if (np != null)
                {
                    ShootEventMessage shootMsg = new ShootEventMessage(np.playerId, cmd.aimDir);
                    NetworkManager.Instance.SendMessageToAll(shootMsg, LiteNetLib.DeliveryMethod.ReliableOrdered);
                }
            }
        }
    }

    /// <summary>
    /// Apply the host's authoritative position for this player, replaying any inputs the host
    /// had not yet seen. Called only on clients - the host is already the authority.
    /// </summary>
    public void ApplyAuthoritativeState(int ackTick, Vector2 authoritativePosition)
    {
        if (!isLocal || player == null)
            return;

        // Nothing acked yet, or we have no record that far back: accept authority outright.
        if (ackTick <= 0 || !predictedPositions.TryGetValue(ackTick, out Vector2 predicted))
        {
            player.Teleport(authoritativePosition);
            ForgetThrough(ackTick);
            return;
        }

        if ((predicted - authoritativePosition).sqrMagnitude <= positionTolerance * positionTolerance)
        {
            ForgetThrough(ackTick);
            return;
        }

        // Rewind, then replay everything the host had not processed at that point.
        float error = (predicted - authoritativePosition).magnitude;
        int replayCount = Mathf.Max(0, lastSimulatedTick - ackTick);
        Debug.Log($"[Reconcile] tick {ackTick}: off by {error:F3} (tolerance {positionTolerance}), snapping and replaying {replayCount} input(s)");

        player.Teleport(authoritativePosition);
        Record(ackTick, authoritativePosition);

        for (int tick = ackTick + 1; tick <= lastSimulatedTick; tick++)
        {
            if (!buffer.TryGet(tick, out InputCommand cmd))
                continue;

            // Movement only: shots are host-authoritative and must not fire twice.
            player.SimulateMovement(cmd.move);
            player.SimulateLook(cmd.aimDir);
            Record(tick, player.Position);
        }

        ForgetThrough(ackTick);
    }

    private void Record(int tick, Vector2 position)
    {
        if (predictedPositions.Count == 0)
            oldestRecordedTick = tick;

        predictedPositions[tick] = position;

        // The host never receives acks for itself, so nothing would ever prune this.
        if (tick - oldestRecordedTick > MaxHistoryTicks)
            ForgetThrough(tick - MaxHistoryTicks);
    }

    // Ticks at or below the ack are settled, so their predictions are no longer needed.
    private void ForgetThrough(int tick)
    {
        while (oldestRecordedTick < tick && predictedPositions.Count > 0)
        {
            predictedPositions.Remove(oldestRecordedTick);
            oldestRecordedTick++;
        }
    }
}
