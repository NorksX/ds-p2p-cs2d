using System.IO;
using UnityEngine;

/// <summary>
/// Serializes and deserializes network messages to/from byte arrays
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
            // Write message type first
            writer.Write((byte)message.GetMessageType());
            
            // Let the message serialize itself
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
            // Read message type
            MessageType messageType = (MessageType)reader.ReadByte();
            
            // Create appropriate message instance
            INetworkMessage message = CreateMessage(messageType);
            
            if (message == null)
            {
                Debug.LogError($"Unknown message type: {messageType}");
                return null;
            }
            
            // Let the message deserialize itself
            message.Deserialize(reader);
            
            return message;
        }
    }
    
    /// <summary>
    /// Factory method to create message instances based on type
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
            case MessageType.InputCommand:
                return new InputCommandMessage();
            case MessageType.StateUpdate:
                return new StateUpdateMessage();
            case MessageType.StartGame:
                return new StartGameMessage();
            case MessageType.Heartbeat:
                return new Heartbeat();
            // Add more cases as we create more message types
            default:
                return null;
        }
    }
}
