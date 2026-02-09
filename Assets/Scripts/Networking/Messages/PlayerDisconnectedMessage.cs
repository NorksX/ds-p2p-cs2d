using System.IO;

/// <summary>
/// Message sent by host to all clients when a player disconnects
/// </summary>
public class PlayerDisconnectedMessage : INetworkMessage
{
    public string playerId;

    public PlayerDisconnectedMessage() { }

    public PlayerDisconnectedMessage(string playerId)
    {
        this.playerId = playerId;
    }

    public MessageType GetMessageType() => MessageType.PlayerDisconnected;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(playerId);
    }

    public void Deserialize(BinaryReader reader)
    {
        playerId = reader.ReadString();
    }
}
