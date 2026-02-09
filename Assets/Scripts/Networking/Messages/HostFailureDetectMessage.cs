using System.IO;

/// <summary>
/// Broadcasted when a peer detects that the host has failed (timeout)
/// </summary>
public class HostFailureDetectMessage : INetworkMessage
{
    public string reporterId;
    public int lastKnownHostTick;
    
    public HostFailureDetectMessage() { }
    
    public HostFailureDetectMessage(string reporterId, int lastKnownHostTick)
    {
        this.reporterId = reporterId;
        this.lastKnownHostTick = lastKnownHostTick;
    }
    
    public MessageType GetMessageType() => MessageType.HostFailureDetect;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(reporterId);
        writer.Write(lastKnownHostTick);
    }
    
    public void Deserialize(BinaryReader reader)
    {
        reporterId = reader.ReadString();
        lastKnownHostTick = reader.ReadInt32();
    }
}
