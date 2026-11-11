using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Sim.Core;
using Sim.Serialization;

namespace Net;

/// <summary>
/// The bytes of every <see cref="NetMessage"/>. A message is one byte that says which kind it is,
/// then its values in the simulation's byte format (<see cref="ByteWriter"/>), with commands in
/// the one encoding there is, <see cref="CommandEncoding"/>.
/// </summary>
/// <remarks>
/// <para>Layout, by kind:</para>
/// <code>
/// kind  message         values after the kind byte
///    1  JoinRequest     int32 protocol version, int32 command encoding version
///    2  JoinAccept      int32 player, int32 player count
///    3  ReadyNotice     nothing
///    4  Ping            int32 sequence
///    5  Pong            int32 sequence
///    6  MatchStart      int32 first tick, int32 command delay, int32 hash interval,
///                       int64 tick interval in units of 100 nanoseconds
///    7  CommandRequest  one command
///    8  TickBundle      int32 tick, count n, n commands in the order they run
///    9  HashReport      int32 tick, uint64 hash
///   10  PeerLeft        int32 player, byte reason
///   11  SessionEnd      byte reason, int32 final tick, int32 desync tick
/// </code>
/// <para>
/// A reason is the number of a <see cref="SessionEndReason"/>. Every message has exactly one
/// encoding and nothing may follow its last value. A change to this table raises
/// <see cref="ProtocolInfo.Version"/>, with one exception that must never change: the kind byte of
/// a JoinRequest and the protocol version after it. A host reads only those two from a client of
/// another version, so that it can say what is wrong instead of failing on the rest.
/// </para>
/// <para>
/// Reading trusts nothing: bytes that are not exactly one message in this layout throw
/// <see cref="InvalidDataException"/> and nothing else. Whether a well-formed message makes sense
/// where it arrives is for the session to judge.
/// </para>
/// </remarks>
public static class MessageCodec
{
    /// <summary>The bytes of a <see cref="TickBundle"/> around its commands: the kind, the tick and the count.</summary>
    public const int BundleOverhead = 1 + sizeof(int) + sizeof(int);

    /// <summary>The bytes of a <see cref="CommandRequest"/> around its command: the kind.</summary>
    public const int CommandRequestOverhead = 1;

    private enum Kind : byte
    {
        JoinRequest = 1,
        JoinAccept = 2,
        ReadyNotice = 3,
        Ping = 4,
        Pong = 5,
        MatchStart = 6,
        CommandRequest = 7,
        TickBundle = 8,
        HashReport = 9,
        PeerLeft = 10,
        SessionEnd = 11,
    }

    /// <summary>Appends one message to the writer.</summary>
    public static void Write(ByteWriter writer, NetMessage message)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(message);

        switch (message)
        {
            case JoinRequest join:
                writer.WriteByte((byte)Kind.JoinRequest);
                writer.WriteInt32(join.ProtocolVersion);
                writer.WriteInt32(join.CommandEncodingVersion);
                break;

            case JoinAccept accept:
                writer.WriteByte((byte)Kind.JoinAccept);
                writer.WriteInt32(accept.Player);
                writer.WriteInt32(accept.PlayerCount);
                break;

            case ReadyNotice:
                writer.WriteByte((byte)Kind.ReadyNotice);
                break;

            case Ping ping:
                writer.WriteByte((byte)Kind.Ping);
                writer.WriteInt32(ping.Sequence);
                break;

            case Pong pong:
                writer.WriteByte((byte)Kind.Pong);
                writer.WriteInt32(pong.Sequence);
                break;

            case MatchStart start:
                writer.WriteByte((byte)Kind.MatchStart);
                writer.WriteInt32(start.FirstTick);
                writer.WriteInt32(start.CommandDelay);
                writer.WriteInt32(start.HashInterval);
                writer.WriteInt64(start.TickInterval.Ticks);
                break;

            case CommandRequest request:
                writer.WriteByte((byte)Kind.CommandRequest);
                CommandEncoding.Write(writer, request.Command);
                break;

            case TickBundle bundle:
                writer.WriteByte((byte)Kind.TickBundle);
                writer.WriteInt32(bundle.Tick);
                writer.WriteCount(bundle.Commands.Length);
                foreach (var command in bundle.Commands)
                {
                    CommandEncoding.Write(writer, command);
                }

                break;

            case HashReport report:
                writer.WriteByte((byte)Kind.HashReport);
                writer.WriteInt32(report.Tick);
                writer.WriteUInt64(report.Hash);
                break;

            case PeerLeft left:
                writer.WriteByte((byte)Kind.PeerLeft);
                writer.WriteInt32(left.Player);
                writer.WriteByte(ReasonByte(left.Reason));
                break;

            case SessionEnd end:
                writer.WriteByte((byte)Kind.SessionEnd);
                writer.WriteByte(ReasonByte(end.Reason));
                writer.WriteInt32(end.FinalTick);
                writer.WriteInt32(end.DesyncTick);
                break;

            default:
                throw new ArgumentException($"{message.GetType().Name} is not a message of the protocol.", nameof(message));
        }
    }

    /// <summary>The bytes of one message, in an array of its own.</summary>
    public static byte[] Encode(NetMessage message)
    {
        var writer = new ByteWriter(64);
        Write(writer, message);
        return writer.ToArray();
    }

    /// <summary>
    /// Reads the one message the bytes hold. Throws <see cref="InvalidDataException"/> if they are
    /// anything else.
    /// </summary>
    public static NetMessage Read(ReadOnlyMemory<byte> data)
    {
        if (data.Length > ProtocolInfo.MaxMessageSize)
        {
            throw new InvalidDataException($"A message of {data.Length} bytes is longer than the {ProtocolInfo.MaxMessageSize} the protocol allows.");
        }

        var reader = new ByteReader(data);
        var kind = (Kind)reader.ReadByte();
        NetMessage message;
        switch (kind)
        {
            case Kind.JoinRequest:
                var protocolVersion = reader.ReadInt32();
                if (protocolVersion != ProtocolInfo.Version)
                {
                    // Another version may lay out the rest differently. The version is all that matters.
                    return new JoinRequest(protocolVersion, 0);
                }

                message = new JoinRequest(protocolVersion, reader.ReadInt32());
                break;

            case Kind.JoinAccept:
                message = new JoinAccept(reader.ReadInt32(), reader.ReadInt32());
                break;

            case Kind.ReadyNotice:
                message = new ReadyNotice();
                break;

            case Kind.Ping:
                message = new Ping(reader.ReadInt32());
                break;

            case Kind.Pong:
                message = new Pong(reader.ReadInt32());
                break;

            case Kind.MatchStart:
                message = new MatchStart(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), TimeSpan.FromTicks(reader.ReadInt64()));
                break;

            case Kind.CommandRequest:
                if (reader.Remaining > ProtocolInfo.MaxCommandSize)
                {
                    throw new InvalidDataException($"A command of {reader.Remaining} bytes is longer than the {ProtocolInfo.MaxCommandSize} the protocol allows.");
                }

                message = new CommandRequest(CommandEncoding.Read(reader));
                break;

            case Kind.TickBundle:
                var tick = reader.ReadInt32();
                var count = reader.ReadCount(CommandEncoding.MinimumSize);
                var commands = ImmutableArray.CreateBuilder<Command>(count);
                for (var i = 0; i < count; i++)
                {
                    commands.Add(CommandEncoding.Read(reader));
                }

                message = new TickBundle(tick, commands.MoveToImmutable());
                break;

            case Kind.HashReport:
                message = new HashReport(reader.ReadInt32(), reader.ReadUInt64());
                break;

            case Kind.PeerLeft:
                message = new PeerLeft(reader.ReadInt32(), ReadReason(reader));
                break;

            case Kind.SessionEnd:
                message = new SessionEnd(ReadReason(reader), reader.ReadInt32(), reader.ReadInt32());
                break;

            default:
                throw new InvalidDataException($"A message starts with {(byte)kind}, which is not a kind of message.");
        }

        reader.ExpectEnd();
        return message;
    }

    /// <summary>Reads the one message the bytes hold, or returns false if they are anything else.</summary>
    public static bool TryRead(ReadOnlyMemory<byte> data, [NotNullWhen(true)] out NetMessage? message)
    {
        try
        {
            message = Read(data);
            return true;
        }
        catch (InvalidDataException)
        {
            message = null;
            return false;
        }
    }

    private static byte ReasonByte(SessionEndReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentException($"{(int)reason} is not a reason the protocol knows.", nameof(reason));
        }

        return (byte)reason;
    }

    private static SessionEndReason ReadReason(ByteReader reader)
    {
        var reason = (SessionEndReason)reader.ReadByte();
        if (!Enum.IsDefined(reason))
        {
            throw new InvalidDataException($"A reason is {(int)reason}, which the protocol does not know.");
        }

        return reason;
    }
}
