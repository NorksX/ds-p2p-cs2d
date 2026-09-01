using UnityEngine;
using System.IO;

/// <summary>
/// Message to broadcast shooting events to all clients.
/// Carries no origin: the client replays the shot from the shooter's own transform, which is
/// the position it is already interpolating toward.
/// </summary>
public class ShootEventMessage : INetworkMessage
{
    public string shooterId;
    public Vector2 aimDir;

    public ShootEventMessage() { }

    public ShootEventMessage(string shooterId, Vector2 aimDir)
    {
        this.shooterId = shooterId;
        this.aimDir = aimDir;
    }

    public MessageType GetMessageType() => MessageType.ShootEvent;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(shooterId);
        writer.Write(aimDir.x);
        writer.Write(aimDir.y);
    }

    public void Deserialize(BinaryReader reader)
    {
        shooterId = reader.ReadString();
        aimDir = new Vector2(reader.ReadSingle(), reader.ReadSingle());
    }
}
