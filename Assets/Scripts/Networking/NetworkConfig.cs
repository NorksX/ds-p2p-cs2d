using UnityEngine;

/// <summary>
/// Configuration for networking settings (ports, timings, timeouts).
/// Every field here is actually read somewhere - do not add speculative ones.
/// </summary>
[CreateAssetMenu(fileName = "NetworkConfig", menuName = "Networking/Network Config")]
public class NetworkConfig : ScriptableObject
{
    [Header("Connection Settings")]
    [Tooltip("Default port for P2P game connections, overridable per-host in the UI")]
    public int gamePort = 7777;

    [Header("Timing")]
    [Tooltip("How often the host rebroadcasts the roster (milliseconds)")]
    public int fullStateInterval = 500;

    [Tooltip("How often to send heartbeats (milliseconds)")]
    public int heartbeatInterval = 2000;

    [Header("Timeouts")]
    [Tooltip("Connection attempt timeout, and LiteNetLib's own DisconnectTimeout (milliseconds)")]
    public int connectionTimeout = 5000;

    [Header("Player Settings")]
    [Tooltip("Maximum players per lobby")]
    public int maxPlayers = 4;

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
