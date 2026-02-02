using UnityEngine;
using System.IO;

/// <summary>
/// Message to broadcast shooting events to all clients
/// </summary>
public class ShootEventMessage : INetworkMessage
{
    public string shooterId;
    public Vector2 origin;
    public Vector2 aimDir;

    public ShootEventMessage() { }

    public ShootEventMessage(string shooterId, Vector2 origin, Vector2 aimDir)
    {
        this.shooterId = shooterId;
        this.origin = origin;
        this.aimDir = aimDir;
    }

    public MessageType GetMessageType() => MessageType.ShootEvent;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(shooterId);
        writer.Write(origin.x);
        writer.Write(origin.y);
        writer.Write(aimDir.x);
        writer.Write(aimDir.y);
    }

    public void Deserialize(BinaryReader reader)
    {
        shooterId = reader.ReadString();
        origin = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        aimDir = new Vector2(reader.ReadSingle(), reader.ReadSingle());
    }
}
