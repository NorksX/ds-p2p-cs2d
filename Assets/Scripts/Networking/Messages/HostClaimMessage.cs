using System.IO;

public class HostClaimMessage : INetworkMessage
{
    public string newHostId;

    // A proactive claim asks a LIVE host to step down. It only does so for a claimant it
    // voted for, which is the interlock stopping anyone from simply announcing themselves.
    public bool proactive;

    public HostClaimMessage() { }

    public HostClaimMessage(string newHostId, bool proactive)
    {
        this.newHostId = newHostId;
        this.proactive = proactive;
    }

    public MessageType GetMessageType()
    {
        return MessageType.HostClaim;
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(newHostId);
        writer.Write(proactive);
    }

    public void Deserialize(BinaryReader reader)
    {
        newHostId = reader.ReadString();
        proactive = reader.ReadBoolean();
    }
}
