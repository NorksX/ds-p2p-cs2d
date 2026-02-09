using System.IO;

public class HostClaimMessage : INetworkMessage
{
    public string newHostId;
    public int currentTick;

    public HostClaimMessage() { }

    public HostClaimMessage(string newHostId, int currentTick)
    {
        this.newHostId = newHostId;
        this.currentTick = currentTick;
    }

    public MessageType GetMessageType()
    {
        return MessageType.HostClaim;
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(newHostId);
        writer.Write(currentTick);
    }

    public void Deserialize(BinaryReader reader)
    {
        newHostId = reader.ReadString();
        currentTick = reader.ReadInt32();
    }
}
