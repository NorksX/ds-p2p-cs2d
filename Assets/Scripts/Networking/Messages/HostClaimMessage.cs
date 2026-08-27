using System.IO;

public class HostClaimMessage : INetworkMessage
{
    public string newHostId;
    public int currentTick;

    // A proactive claim asks a LIVE host to step down. It only does so for a claimant it
    // voted for, which is the interlock stopping anyone from simply announcing themselves.
    public bool proactive;

    public HostClaimMessage() { }

    public HostClaimMessage(string newHostId, int currentTick, bool proactive)
    {
        this.newHostId = newHostId;
        this.currentTick = currentTick;
        this.proactive = proactive;
    }

    public MessageType GetMessageType()
    {
        return MessageType.HostClaim;
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(newHostId);
        writer.Write(currentTick);
        writer.Write(proactive);
    }

    public void Deserialize(BinaryReader reader)
    {
        newHostId = reader.ReadString();
        currentTick = reader.ReadInt32();
        proactive = reader.ReadBoolean();
    }
}
