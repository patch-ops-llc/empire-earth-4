using System.Diagnostics.CodeAnalysis;

namespace Net.Tests;

/// <summary>Lets a test stand in for one side of a connection, speaking in messages.</summary>
internal static class ConnectionExtensions
{
    public static void SendMessage(this IConnection connection, NetMessage message) => connection.Send(MessageCodec.Encode(message));

    public static bool TryReceiveMessage(this IConnection connection, [NotNullWhen(true)] out NetMessage? message)
    {
        if (connection.TryReceive(out var data))
        {
            message = MessageCodec.Read(data);
            return true;
        }

        message = null;
        return false;
    }

    /// <summary>Every message that has arrived, in order.</summary>
    public static List<NetMessage> ReceiveMessages(this IConnection connection)
    {
        var messages = new List<NetMessage>();
        while (connection.TryReceiveMessage(out var message))
        {
            messages.Add(message);
        }

        return messages;
    }
}
