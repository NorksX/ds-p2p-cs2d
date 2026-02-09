using System.IO;
using UnityEngine;

/// <summary>
/// Serializes and deserializes messages
/// </summary>
public static class MessageSerializer
{
    /// <summary>
    /// Serialize a message to byte array
    /// </summary>
    public static byte[] Serialize(INetworkMessage message)
    {
        using (MemoryStream memoryStream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(memoryStream))
        {
            writer.Write((byte)message.GetMessageType());
            
            message.Serialize(writer);
            
            return memoryStream.ToArray();
        }
    }
    
    /// <summary>
    /// Deserialize a message from byte array
    /// Returns null if deserialization fails
    /// </summary>
    public static INetworkMessage Deserialize(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            Debug.LogError("Cannot deserialize null or empty data");
            return null;
        }
        
        using (MemoryStream memoryStream = new MemoryStream(data))
        using (BinaryReader reader = new BinaryReader(memoryStream))
        {
            MessageType messageType = (MessageType)reader.ReadByte();
            
            INetworkMessage message = CreateMessage(messageType);
            
            if (message == null)
            {
                Debug.LogError($"Unknown message type: {messageType}");
                return null;
            }
            
            message.Deserialize(reader);
            
            return message;
        }
    }
    
    /// <summary>
    /// Factory for messages
    /// </summary>
    private static INetworkMessage CreateMessage(MessageType type)
    {
        switch (type)
        {
            case MessageType.JoinLobbyRequest:
                return new JoinLobbyRequest();
            case MessageType.JoinLobbyResponse:
                return new JoinLobbyResponse();
            case MessageType.LeaveLobby:
                return new LeaveLobby();
            case MessageType.PlayerDisconnected:
                return new PlayerDisconnectedMessage();
            case MessageType.InputCommand:
                return new InputCommandMessage();
            case MessageType.StateUpdate:
                return new StateUpdateMessage();
            case MessageType.StartGame:
                return new StartGameMessage();
            case MessageType.ShootEvent:
                return new ShootEventMessage();
            case MessageType.Heartbeat:
                return new Heartbeat();
            case MessageType.HostFailureDetect:
                return new HostFailureDetectMessage();
            case MessageType.PeerListUpdate:
                return new PeerListUpdateMessage();
            case MessageType.HostElectionRequest:
                return new HostElectionRequest();
            case MessageType.HostElectionResponse:
                return new HostElectionResponse();
            case MessageType.HostClaim:
                return new HostClaimMessage();
            default:
                return null;
        }
    }
}
