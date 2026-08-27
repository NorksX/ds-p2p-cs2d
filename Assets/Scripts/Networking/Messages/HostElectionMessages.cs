using System.IO;

/// <summary>
/// Sent by a candidate to propose themselves as the new host
/// </summary>
public class HostElectionRequest : INetworkMessage
{
    public string candidateId;
    public int candidateTick; // To prove they are up to date

    // Round 1 votes on the voter's own RTT measurements; later rounds fall back to the shared
    // aggregate, which every peer computes identically and so cannot split.
    public int round;

    // Set when the current host is alive and merely badly placed. Voters then judge the
    // proposal against the proactive threshold instead of on ping preference.
    public bool proactive;

    public HostElectionRequest() { }

    public HostElectionRequest(string candidateId, int candidateTick, int round, bool proactive)
    {
        this.candidateId = candidateId;
        this.candidateTick = candidateTick;
        this.round = round;
        this.proactive = proactive;
    }
    
    public MessageType GetMessageType() => MessageType.HostElectionRequest;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(candidateId);
        writer.Write(candidateTick);
        writer.Write(round);
        writer.Write(proactive);
    }
    
    public void Deserialize(BinaryReader reader)
    {
        candidateId = reader.ReadString();
        candidateTick = reader.ReadInt32();
        round = reader.ReadInt32();
        proactive = reader.ReadBoolean();
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
