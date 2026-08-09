using System.IO;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct ZombieState
{
    public int zombieId;
    public Vector2 position;
    public float rotation;
    public int health;

    public ZombieState(int zombieId, Vector2 position, float rotation, int health)
    {
        this.zombieId = zombieId;
        this.position = position;
        this.rotation = rotation;
        this.health = health;
    }
}

/// <summary>
/// The full set of living zombies plus the current wave, sent by the host every tick.
///
/// Self-describing on purpose, exactly like SessionRoster: clients spawn ids they do not
/// have and despawn ids that are absent. That makes one message cover spawning, despawning,
/// movement, late join and post-migration recovery, with no separate lifecycle messages to
/// keep consistent.
/// </summary>
public class ZombieStateMessage : INetworkMessage
{
    public int waveNumber;
    public List<ZombieState> zombies = new List<ZombieState>();

    public ZombieStateMessage() { }

    public ZombieStateMessage(int waveNumber, List<ZombieState> zombies)
    {
        this.waveNumber = waveNumber;
        this.zombies = zombies;
    }

    public MessageType GetMessageType() => MessageType.ZombieState;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(waveNumber);
        writer.Write(zombies.Count);

        foreach (var z in zombies)
        {
            writer.Write(z.zombieId);
            writer.Write(z.position.x);
            writer.Write(z.position.y);
            writer.Write(z.rotation);
            writer.Write(z.health);
        }
    }

    public void Deserialize(BinaryReader reader)
    {
        waveNumber = reader.ReadInt32();
        int count = reader.ReadInt32();

        zombies.Clear();

        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float rotation = reader.ReadSingle();
            int health = reader.ReadInt32();

            zombies.Add(new ZombieState(id, new Vector2(x, y), rotation, health));
        }
    }
}
