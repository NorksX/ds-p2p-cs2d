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
    
    [Tooltip("Address of centralized lobby server")]
    public string centralServerAddress = "127.0.0.1";
    
    [Tooltip("Port for centralized lobby server")]
    public int centralServerPort = 8888;
    
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
}
