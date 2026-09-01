/// <summary>
/// Types of network messages
/// </summary>
public enum MessageType : byte
{
    // Lobby messages (1-10)
    JoinLobbyRequest = 1,
    JoinLobbyResponse = 2,
    // 3 was LeaveLobby, retired - departure is detected from the transport disconnect.
    PlayerDisconnected = 4,  // Broadcast when player disconnects
    
    // Game messages (20-30)
    InputCommand = 20,
    StateUpdate = 21,
    // 22 was StartGame, retired when spawning became roster-driven.
    ShootEvent = 23,  // Broadcast shooting events
    ZombieState = 24, // Full living-zombie set + wave number, host to clients
    
    // System messages (100+)
    Heartbeat = 100,
    // 101 was HostFailureDetect, retired - each peer detects host loss independently.
    SessionRoster = 102,     // Host sends the authoritative participant list to everyone
    HostElectionRequest = 103, // Candidate requests votes
    HostElectionResponse = 104, // Peer votes
    HostClaim = 105
}

/// <summary>
/// interface for all message types
/// </summary>
public interface INetworkMessage
{
    MessageType GetMessageType();
    void Serialize(System.IO.BinaryWriter writer);
    void Deserialize(System.IO.BinaryReader reader);
}
