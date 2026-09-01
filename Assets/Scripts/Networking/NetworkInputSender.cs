using UnityEngine;
using LiteNetLib;
using System.Collections;

/// <summary>
/// Sends local player inputs to the host every tick
/// Attach to local player
/// </summary>
public class NetworkInputSender : MonoBehaviour
{
    private NetworkedPlayer networkedPlayer;
    private LocalInputBuffer inputBuffer;

    // Per-second summary rather than per-tick lines: at 30 Hz the raw form is unreadable, and
    // it would not collapse in the console either because the tick number changes every time.
    private float lastReportTime;
    private int sentSinceReport;
    private int movingSinceReport;
    
    // Don't get components in Awake - PlayerSpawner adds them AFTER instantiation
    
    private void Start()
    {
        StartCoroutine(InitializeAfterSpawn());
    }
    
    private IEnumerator InitializeAfterSpawn()
    {
        // Wait one frame to ensure PlayerSpawner has added/configured components
        yield return null;
        
        // NOW get the components (they exist now)
        networkedPlayer = GetComponent<NetworkedPlayer>();
        inputBuffer = GetComponent<LocalInputBuffer>();
        
        Debug.Log($"[NetworkInputSender] Checking player: networkedPlayer={(networkedPlayer != null ? networkedPlayer.playerId : "NULL")}, isLocalPlayer={networkedPlayer?.isLocalPlayer}");
        
        // Disable on remote players
        if (networkedPlayer != null && !networkedPlayer.isLocalPlayer)
        {
            Debug.Log($"[NetworkInputSender] Disabling on REMOTE player {networkedPlayer.playerId}");
            this.enabled = false;
            yield break;
        }
        
        Debug.Log("[NetworkInputSender] Enabled for LOCAL player");
        
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick += HandleTick;
        }
    }
    
    private void OnDestroy()
    {
        if (TickManager.Instance != null)
        {
            TickManager.Instance.OnTick -= HandleTick;
        }
    }
    
    private void HandleTick(int tick)
    {
        // Comprehensive null checks
        if (NetworkManager.Instance == null || networkedPlayer == null || inputBuffer == null)
            return;
            
        // Stop sending while disconnected or migrating - there is no authority to send to.
        if (NetworkManager.Instance.State != ConnectionState.InLobby)
            return;
            
        // Only send inputs if we're a client (not host)
        // Host simulates locally, doesn't need to send to itself
        if (NetworkManager.Instance.IsHost)
            return;
        
        if (!networkedPlayer.isLocalPlayer)
            return;
        
        // Get input for this tick
        if (inputBuffer.TryGet(tick - 1, out InputCommand cmd))
        {
            // Send to host
            InputCommandMessage message = new InputCommandMessage(cmd);
            NetworkManager.Instance.SendMessageToAll(message, DeliveryMethod.Sequenced);

            sentSinceReport++;
            if (cmd.move.sqrMagnitude > 0.0001f) movingSinceReport++;

            if (Time.time - lastReportTime >= 1f)
            {
                Debug.Log($"[Input] sent {sentSinceReport} commands to host in the last second ({movingSinceReport} with movement), through tick {tick - 1}");
                lastReportTime = Time.time;
                sentSinceReport = 0;
                movingSinceReport = 0;
            }
        }
    }
}
