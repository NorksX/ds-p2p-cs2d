using System.IO;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player state data for network synchronization
/// </summary>
[System.Serializable]
public struct PlayerState
{
    public string playerId;
    public Vector2 position;
    public float rotation; // Z-axis rotation in degrees
    
    public PlayerState(string playerId, Vector2 position, float rotation)
    {
        this.playerId = playerId;
        this.position = position;
        this.rotation = rotation;
    }
}

/// <summary>
/// Network message containing all player states (sent from host to clients)
/// </summary>
public class StateUpdateMessage : INetworkMessage
{
    public int tick;
    public List<PlayerState> playerStates = new List<PlayerState>();
    
    public StateUpdateMessage() { }
    
    public StateUpdateMessage(int tick, List<PlayerState> playerStates)
    {
        this.tick = tick;
        this.playerStates = playerStates;
    }
    
    public MessageType GetMessageType() => MessageType.StateUpdate;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(tick);
        writer.Write(playerStates.Count);
        
        foreach (var state in playerStates)
        {
            writer.Write(state.playerId);
            writer.Write(state.position.x);
            writer.Write(state.position.y);
            writer.Write(state.rotation);
        }
    }
    
    public void Deserialize(BinaryReader reader)
    {
        tick = reader.ReadInt32();
        int count = reader.ReadInt32();
        
        playerStates.Clear();
        
        for (int i = 0; i < count; i++)
        {
            string playerId = reader.ReadString();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float rotation = reader.ReadSingle();
            
            playerStates.Add(new PlayerState(playerId, new Vector2(x, y), rotation));
        }
    }
}
