using System.IO;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct PeerConnectionInfo
{
    public string peerId;
    public string username;
    public string ipAddress;
    public int port;
    
    public PeerConnectionInfo(string peerId, string username, string ipAddress, int port)
    {
        this.peerId = peerId;
        this.username = username;
        this.ipAddress = ipAddress;
        this.port = port;
    }
}

/// <summary>
/// Sent by Host to all clients to keep them updated on who else is in the lobby/game
/// Necessary for mesh formation during host migration
/// </summary>
public class PeerListUpdateMessage : INetworkMessage
{
    public List<PeerConnectionInfo> peers = new List<PeerConnectionInfo>();
    
    public PeerListUpdateMessage() { }
    
    public PeerListUpdateMessage(List<PeerConnectionInfo> peers)
    {
        this.peers = peers;
    }
    
    public MessageType GetMessageType() => MessageType.PeerListUpdate;
    
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(peers.Count);
        foreach (var peer in peers)
        {
            writer.Write(peer.peerId);
            writer.Write(peer.username);
            writer.Write(peer.ipAddress);
            writer.Write(peer.port);
        }
    }
    
    public void Deserialize(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        peers.Clear();
        
        for (int i = 0; i < count; i++)
        {
            string id = reader.ReadString();
            string name = reader.ReadString();
            string ip = reader.ReadString();
            int port = reader.ReadInt32();
            
            peers.Add(new PeerConnectionInfo(id, name, ip, port));
        }
    }
}
