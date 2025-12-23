using System.IO;

/// <summary>
/// Heartbeat message sent between all peers to detect disconnections
/// </summary>
public class Heartbeat : INetworkMessage
{
    public int tick;
    public string senderId;
    
    public Heartbeat() { }
    
    public Heartbeat(int tick, string senderId)
    {
        this.tick = tick;
        this.senderId = senderId;
    }
    
    public MessageType GetMessageType() => MessageType.Heartbeat;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(tick);
        writer.Write(senderId);
    }
    
    public void Deserialize(BinaryReader reader)
    {
        tick = reader.ReadInt32();
        senderId = reader.ReadString();
    }
}
