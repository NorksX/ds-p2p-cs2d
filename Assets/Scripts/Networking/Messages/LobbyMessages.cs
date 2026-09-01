using System.IO;

/// <summary>
/// Request to join a lobby
/// </summary>
public class JoinLobbyRequest : INetworkMessage
{
    public string playerId;
    public string playerUsername;

    // Port this peer listens on, so the roster can carry a dialable endpoint for migration.
    public int listenPort;

    public JoinLobbyRequest() { }

    public JoinLobbyRequest(string playerId, string playerUsername, int listenPort)
    {
        this.playerId = playerId;
        this.playerUsername = playerUsername;
        this.listenPort = listenPort;
    }

    public MessageType GetMessageType() => MessageType.JoinLobbyRequest;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(playerId);
        writer.Write(playerUsername);
        writer.Write(listenPort);
    }

    public void Deserialize(BinaryReader reader)
    {
        playerId = reader.ReadString();
        playerUsername = reader.ReadString();
        listenPort = reader.ReadInt32();
    }
}

/// <summary>
/// Response to join lobby request
/// </summary>
public class JoinLobbyResponse : INetworkMessage
{
    public bool accepted;
    public int assignedPlayerPosition; // 0-3
    public string reason;
    public string hostId;
    
    public JoinLobbyResponse() { }
    
    public JoinLobbyResponse(bool accepted, int assignedPlayerPosition, string reason = "", string hostId = "")
    {
        this.accepted = accepted;
        this.assignedPlayerPosition = assignedPlayerPosition;
        this.reason = reason;
        this.hostId = hostId;
    }
    
    public MessageType GetMessageType() => MessageType.JoinLobbyResponse;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(accepted);
        writer.Write(assignedPlayerPosition);
        writer.Write(reason ?? "");
        writer.Write(hostId ?? "");
    }
    
    public void Deserialize(BinaryReader reader)
    {
        accepted = reader.ReadBoolean();
        assignedPlayerPosition = reader.ReadInt32();
        reason = reader.ReadString();
        hostId = reader.ReadString();
    }
}
