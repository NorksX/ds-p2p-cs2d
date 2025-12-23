using System.IO;

/// <summary>
/// Request to join a lobby
/// </summary>
public class JoinLobbyRequest : INetworkMessage
{
    public string playerId;
    public string playerUsername;
    public int latency;
    
    public JoinLobbyRequest() { }
    
    public JoinLobbyRequest(string playerId, string playerUsername, int latency)
    {
        this.playerId = playerId;
        this.playerUsername = playerUsername;
        this.latency = latency;
    }
    
    public MessageType GetMessageType() => MessageType.JoinLobbyRequest;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(playerId);
        writer.Write(playerUsername);
        writer.Write(latency);
    }
    
    public void Deserialize(BinaryReader reader)
    {
        playerId = reader.ReadString();
        playerUsername = reader.ReadString();
        latency = reader.ReadInt32();
    }
}

/// <summary>
/// Response to join lobby request
/// </summary>
public class JoinLobbyResponse : INetworkMessage
{
    public bool accepted;
    public int assignedPlayerPosition; // 0-3 for 4-player co-op
    public string reason; // If rejected, why?
    
    public JoinLobbyResponse() { }
    
    public JoinLobbyResponse(bool accepted, int assignedPlayerPosition, string reason = "")
    {
        this.accepted = accepted;
        this.assignedPlayerPosition = assignedPlayerPosition;
        this.reason = reason;
    }
    
    public MessageType GetMessageType() => MessageType.JoinLobbyResponse;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(accepted);
        writer.Write(assignedPlayerPosition);
        writer.Write(reason ?? "");
    }
    
    public void Deserialize(BinaryReader reader)
    {
        accepted = reader.ReadBoolean();
        assignedPlayerPosition = reader.ReadInt32();
        reason = reader.ReadString();
    }
}

/// <summary>
/// Player wants to leave the lobby
/// </summary>
public class LeaveLobby : INetworkMessage
{
    public string playerId;
    
    public LeaveLobby() { }
    
    public LeaveLobby(string playerId)
    {
        this.playerId = playerId;
    }
    
    public MessageType GetMessageType() => MessageType.LeaveLobby;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(playerId);
    }
    
    public void Deserialize(BinaryReader reader)
    {
        playerId = reader.ReadString();
    }
}
