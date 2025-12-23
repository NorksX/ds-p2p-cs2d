/// <summary>
/// Types of network messages that can be sent/received
/// </summary>
public enum MessageType : byte
{
    // Lobby messages (1-10)
    JoinLobbyRequest = 1,
    JoinLobbyResponse = 2,
    LeaveLobby = 3,
    
    // Game messages (20-30)
    InputCommand = 20,
    StateUpdate = 21,
    StartGame = 22,
    
    // System messages (100+)
    Heartbeat = 100,
    
    HostClaim = 25
}

/// <summary>
/// Base interface for all network messages
/// </summary>
public interface INetworkMessage
{
    MessageType GetMessageType();
    void Serialize(System.IO.BinaryWriter writer);
    void Deserialize(System.IO.BinaryReader reader);
}
