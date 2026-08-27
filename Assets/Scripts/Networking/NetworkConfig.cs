using UnityEngine;

/// <summary>
/// Configuration for networking settings (tick rate, ports, timeouts, etc.)
/// </summary>
[CreateAssetMenu(fileName = "NetworkConfig", menuName = "Networking/Network Config")]
public class NetworkConfig : ScriptableObject
{
    [Header("Connection Settings")]
    [Tooltip("Port for P2P game connections")]
    public int gamePort = 7777;

    [Header("Timing")]
    [Tooltip("Simulation ticks per second (should match TickManager)")]
    public int tickRate = 30;
    
    [Tooltip("How often to send full state snapshots (milliseconds)")]
    public int fullStateInterval = 500;
    
    [Tooltip("How often to send heartbeats (milliseconds)")]
    public int heartbeatInterval = 2000;
    
    [Header("Timeouts")]
    [Tooltip("Connection timeout in milliseconds")]
    public int connectionTimeout = 5000;
    
    [Tooltip("Host failure detection timeout in milliseconds")]
    public int hostFailureTimeout = 2000;
    
    [Header("Player Settings")]
    [Tooltip("Maximum players per lobby")]
    public int maxPlayers = 4;
    
    [Header("Performance")]
    [Tooltip("Size of input buffer (how many ticks to keep)")]
    public int inputBufferSize = 240;
    
    [Tooltip("Lag compensation window in milliseconds")]
    public int lagCompensationWindow = 200;

    [Header("RTT Estimation (Jacobson/Karels, RFC 6298)")]
    [Tooltip("a in EstimatedRTT = (1-a)*EstimatedRTT + a*SampleRTT")]
    public float rttAlpha = 0.125f;

    [Tooltip("b in DevRTT = (1-b)*DevRTT + b*|SampleRTT - EstimatedRTT|")]
    public float rttBeta = 0.25f;

    [Tooltip("K in TimeoutInterval = EstimatedRTT + K*DevRTT. This is the link cost elections rank on.")]
    public float rttDeviationWeight = 4f;

    [Tooltip("How often each peer pings every other peer (milliseconds)")]
    public int rttProbeInterval = 3000;

    [Tooltip("Estimates with no sample this recent are stale and their peer cannot be elected (milliseconds)")]
    public int rttStatsWindow = 30000;

    [Tooltip("Fresh samples required inside the window before a peer may be elected on RTT grounds")]
    public int rttMinSamples = 5;

    [Tooltip("Debug only: milliseconds added to every measured sample, so latency-based election is demonstrable on one machine where real RTT is ~0")]
    public float rttSimulatedExtraMs = 0f;

    [Header("Proactive Host Migration")]
    [Tooltip("Allow migrating away from a healthy but badly-placed host")]
    public bool proactiveMigrationEnabled = true;

    [Tooltip("How often every peer re-evaluates whether the host is still the right one (milliseconds)")]
    public int proactiveCheckInterval = 30000;

    [Tooltip("Migrate only when the host's aggregate cost exceeds the best challenger's by this factor")]
    public float proactiveThresholdFactor = 3f;

    [Tooltip("Also require this much absolute improvement (ms of link cost). A ratio alone is meaningless at low latency - 20ms vs 70ms is 3.5x and worth nothing.")]
    public float proactiveMinCostGap = 100f;

    [Tooltip("Consecutive checks the condition must hold, so a transient spike cannot move the host")]
    public int proactiveSustainedChecks = 2;

    [Tooltip("Quiet period after any migration before another proactive one may start (milliseconds)")]
    public int proactiveCooldown = 60000;
}
