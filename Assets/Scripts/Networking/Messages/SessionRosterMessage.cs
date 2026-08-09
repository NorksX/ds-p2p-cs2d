using System.IO;
using System.Collections.Generic;

/// <summary>
/// One participant in the session.
/// </summary>
[System.Serializable]
public class RosterEntry
{
    public string playerId;
    public string username;
    public int spawnSlot;
    public string ipAddress;
    public int listenPort;
    public bool isHost;

    public RosterEntry() { }

    public RosterEntry(string playerId, string username, int spawnSlot, string ipAddress, int listenPort, bool isHost)
    {
        this.playerId = playerId;
        this.username = username;
        this.spawnSlot = spawnSlot;
        this.ipAddress = ipAddress;
        this.listenPort = listenPort;
        this.isHost = isHost;
    }
}

/// <summary>
/// The authoritative session list, owned by the host and rebroadcast on every change.
/// Unlike the old PeerListUpdate it includes the host, which is what lets clients spawn
/// the host, elect a replacement, and dial a mesh without inventing placeholder ids.
/// </summary>
public class SessionRosterMessage : INetworkMessage
{
    public List<RosterEntry> entries = new List<RosterEntry>();

    public SessionRosterMessage() { }

    public SessionRosterMessage(List<RosterEntry> entries)
    {
        this.entries = entries;
    }

    public MessageType GetMessageType() => MessageType.SessionRoster;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(entries.Count);
        foreach (var e in entries)
        {
            writer.Write(e.playerId ?? "");
            writer.Write(e.username ?? "");
            writer.Write(e.spawnSlot);
            writer.Write(e.ipAddress ?? "");
            writer.Write(e.listenPort);
            writer.Write(e.isHost);
        }
    }

    public void Deserialize(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        entries.Clear();

        for (int i = 0; i < count; i++)
        {
            entries.Add(new RosterEntry(
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadBoolean()));
        }
    }
}
