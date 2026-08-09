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

    // Last input tick the host simulated for this player, in that player's own tick
    // numbering. This is what a client rewinds to before replaying.
    public int lastProcessedInputTick;

    public PlayerState(string playerId, Vector2 position, float rotation, int lastProcessedInputTick)
    {
        this.playerId = playerId;
        this.position = position;
        this.rotation = rotation;
        this.lastProcessedInputTick = lastProcessedInputTick;
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
            writer.Write(state.lastProcessedInputTick);
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
            int ackTick = reader.ReadInt32();

            playerStates.Add(new PlayerState(playerId, new Vector2(x, y), rotation, ackTick));
        }
    }
}
