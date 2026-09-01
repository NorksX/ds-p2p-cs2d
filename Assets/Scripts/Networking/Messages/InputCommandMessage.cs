using System.IO;
using UnityEngine;

/// <summary>
/// Network message for input commands (sent from client to host every tick)
/// </summary>
public class InputCommandMessage : INetworkMessage
{
    public InputCommand inputCommand;
    
    public InputCommandMessage() { }
    
    public InputCommandMessage(InputCommand inputCommand)
    {
        this.inputCommand = inputCommand;
    }
    
    public MessageType GetMessageType() => MessageType.InputCommand;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(inputCommand.tick);
        writer.Write(inputCommand.playerId ?? "");
        writer.Write(inputCommand.move.x);
        writer.Write(inputCommand.move.y);
        writer.Write(inputCommand.aimDir.x);
        writer.Write(inputCommand.aimDir.y);
        writer.Write(inputCommand.firePressed);
    }
    
    public void Deserialize(BinaryReader reader)
    {
        int tick = reader.ReadInt32();
        string playerId = reader.ReadString();
        float moveX = reader.ReadSingle();
        float moveY = reader.ReadSingle();
        float aimX = reader.ReadSingle();
        float aimY = reader.ReadSingle();
        bool firePressed = reader.ReadBoolean();
        
        inputCommand = new InputCommand(
            tick,
            new Vector2(moveX, moveY),
            new Vector2(aimX, aimY),
            firePressed,
            playerId
        );
    }
}
