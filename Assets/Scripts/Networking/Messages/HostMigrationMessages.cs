using System.IO;

/// <summary>
/// Keepalive sent to every connected peer. Deliberately payload-free: the receiver identifies
/// the sender from the NetPeer it arrived on, and the only thing recorded is the arrival time,
/// which is what host-failure detection watches.
/// </summary>
public class Heartbeat : INetworkMessage
{
    public MessageType GetMessageType() => MessageType.Heartbeat;

    public void Serialize(BinaryWriter writer) { }

    public void Deserialize(BinaryReader reader) { }
}
