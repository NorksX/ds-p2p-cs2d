using System.IO;
using System.Collections.Generic;

public class StartGameMessage : INetworkMessage
{
    public int tick;
    // We send a list of all players so clients know who to spawn
    public List<PlayerSpawnInfo> players = new List<PlayerSpawnInfo>();
    
    public StartGameMessage() { }
    
    public StartGameMessage(int tick, List<PlayerSpawnInfo> players)
    {
        this.tick = tick;
        this.players = players;
    }
    
    public MessageType GetMessageType() => MessageType.StartGame;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(tick);
        writer.Write(players.Count);
        foreach (var p in players)
        {
            writer.Write(p.playerId);
            writer.Write(p.username);
            writer.Write(p.spawnPositionIndex);
            writer.Write(p.isHost);
        }
    }
    
    public void Deserialize(BinaryReader reader)
    {
        tick = reader.ReadInt32();
        int count = reader.ReadInt32();
        players.Clear();
        for (int i = 0; i < count; i++)
        {
            players.Add(new PlayerSpawnInfo
            {
                playerId = reader.ReadString(),
                username = reader.ReadString(),
                spawnPositionIndex = reader.ReadInt32(),
                isHost = reader.ReadBoolean()
            });
        }
    }
}

public struct PlayerSpawnInfo
{
    public string playerId;
    public string username;
    public int spawnPositionIndex;
    public bool isHost;
}
