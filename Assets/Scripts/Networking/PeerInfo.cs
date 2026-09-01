using UnityEngine;

/// <summary>
/// Represents information about a connected peer
/// </summary>
public class PeerInfo
{
    public string peerId;
    public string username;
    public int assignedPlayerPosition;
    public LiteNetLib.NetPeer netPeer;
    public float lastHeartbeatReceiveTime; // Local time when last heartbeat was received

    // Port the peer listens on, for dialling it directly during host migration.
    public int listenPort;

    // Set on the peer we currently treat as host. Replaces matching on username.
    public bool isHost;

    public PeerInfo(string peerId, string username, int playerPosition, LiteNetLib.NetPeer netPeer)
    {
        this.peerId = peerId;
        this.username = username;
        this.assignedPlayerPosition = playerPosition;
        this.netPeer = netPeer;
        this.lastHeartbeatReceiveTime = Time.time;
    }
}
