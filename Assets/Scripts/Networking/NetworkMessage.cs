/// <summary>
/// Types of network messages
/// </summary>
public enum MessageType : byte
{
    // Lobby messages (1-10)
    JoinLobbyRequest = 1,
    JoinLobbyResponse = 2,
    LeaveLobby = 3,
    PlayerDisconnected = 4,  // Broadcast when player disconnects
    
    // Game messages (20-30)
    InputCommand = 20,
    StateUpdate = 21,
    StartGame = 22,
    ShootEvent = 23,  // Broadcast shooting events
    
    // System messages (100+)
    Heartbeat = 100,
    HostFailureDetect = 101, // Broadcast when host timeout detected
    PeerListUpdate = 102,    // Host sends list of all peers to everyone
    HostElectionRequest = 103, // Candidate requests votes
    HostElectionResponse = 104, // Peer votes
    HostClaim = 105
    
    //HostClaim = 25
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
