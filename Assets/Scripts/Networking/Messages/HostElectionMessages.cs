using System.IO;

/// <summary>
/// Sent by a candidate to propose themselves as the new host
/// </summary>
public class HostElectionRequest : INetworkMessage
{
    public string candidateId;
    public int candidateTick; // To prove they are up to date
    
    public HostElectionRequest() { }
    
    public HostElectionRequest(string candidateId, int candidateTick)
    {
        this.candidateId = candidateId;
        this.candidateTick = candidateTick;
    }
    
    public MessageType GetMessageType() => MessageType.HostElectionRequest;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(candidateId);
        writer.Write(candidateTick);
    }
    
    public void Deserialize(BinaryReader reader)
    {
        candidateId = reader.ReadString();
        candidateTick = reader.ReadInt32();
    }
}

/// <summary>
/// Response to an election request (Vote)
/// </summary>
public class HostElectionResponse : INetworkMessage
{
    public string voterId;
    public string candidateId; // Who we are voting for
    public bool accepted;
    
    public HostElectionResponse() { }
    
    public HostElectionResponse(string voterId, string candidateId, bool accepted)
    {
        this.voterId = voterId;
        this.candidateId = candidateId;
        this.accepted = accepted;
    }
    
    public MessageType GetMessageType() => MessageType.HostElectionResponse;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(voterId);
        writer.Write(candidateId);
        writer.Write(accepted);
    }
    
    public void Deserialize(BinaryReader reader)
    {
        voterId = reader.ReadString();
        candidateId = reader.ReadString();
        accepted = reader.ReadBoolean();
    }
}
