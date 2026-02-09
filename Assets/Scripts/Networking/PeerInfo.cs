using UnityEngine;

/// <summary>
/// Represents information about a connected peer
/// </summary>
public class PeerInfo
{
    public string peerId;
    public string username;
    public int latency;
    public int assignedPlayerPosition;
    public LiteNetLib.NetPeer netPeer;
    public int lastHeartbeatTick;
    public float lastHeartbeatReceiveTime; // Local time when last heartbeat was received
    
    public PeerInfo(string peerId, string username, int playerPosition, LiteNetLib.NetPeer netPeer)
    {
        this.peerId = peerId;
        this.username = username;
        this.assignedPlayerPosition = playerPosition;
        this.netPeer = netPeer;
        this.latency = 0;
        this.lastHeartbeatTick = 0;
        this.lastHeartbeatReceiveTime = Time.time;
    }
}
