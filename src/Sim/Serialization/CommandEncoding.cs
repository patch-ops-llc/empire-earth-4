using Sim.Core;

namespace Sim.Serialization;

/// <summary>
/// The one encoding of a <see cref="Command"/> as bytes. Replay files and the network protocol both
/// store commands this way, so a command recorded from a network match is the same bytes in its
/// replay.
/// </summary>
/// <remarks>
/// <para>
/// Layout, in the byte format of <see cref="ByteWriter"/>:
/// </para>
/// <code>
/// offset  bytes  value
///      0      2  Type, as its number from 0 to 65,535
///      2      4  Player
///      6      4  TargetX, raw Q16.16
///     10      4  TargetY, raw Q16.16
///     14      4  TargetEntity
///     18      4  ContentId
///     22      4  the number of entities, n
///     26    4·n  Entities, in the order the sender listed them
/// </code>
/// <para>
/// Every field is always present, so a command has exactly one encoding and two commands are the
/// same when their bytes are. The encoding keeps every value a command can hold and judges none of
/// them: an unknown type, a player who is not in the match and an entity that does not exist all
/// read back as they were written, and the simulation drops such a command when it runs, on every
/// machine alike. A command does not carry its tick: whatever holds commands (a replay, a network
/// message) says which tick they are for.
/// </para>
/// </remarks>
public static class CommandEncoding
{
    /// <summary>
    /// The version of the layout above. It goes up whenever the layout changes, including when
    /// <see cref="Command"/> gains a field. It is there for replay files to store and for peers to
    /// compare before a match starts.
    /// </summary>
    public const int Version = 1;

    /// <summary>The bytes a command with no entities takes.</summary>
    public const int MinimumSize = 26;

    private const int BytesPerEntity = sizeof(int);

    /// <summary>Up to this many entities are read on the stack. A larger selection takes one more array.</summary>
    private const int EntitiesOnStack = 128;

    /// <summary>The number of bytes <see cref="Write"/> produces for a command.</summary>
    public static int SizeOf(in Command command) => checked(MinimumSize + (command.Entities.Length * BytesPerEntity));

    /// <summary>
    /// Appends a command to the writer. Throws if the type's number is outside 0 to 65,535, which
    /// the rule in <see cref="CommandType"/> rules out.
    /// </summary>
    public static void Write(ByteWriter writer, in Command command)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var type = (int)command.Type;
        if (type is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentException($"Command type number {type} is outside 0 to {ushort.MaxValue}.", nameof(command));
        }

        writer.WriteUInt16((ushort)type);
        writer.WriteInt32(command.Player);
        writer.WriteFixed(command.TargetX);
        writer.WriteFixed(command.TargetY);
        writer.WriteEntityId(command.TargetEntity);
        writer.WriteInt32(command.ContentId);

        var entities = command.Entities;
        writer.WriteCount(entities.Length);
        foreach (var entity in entities)
        {
            writer.WriteEntityId(entity);
        }
    }

    /// <summary>
    /// Reads one command and leaves the reader after it. Throws <see cref="InvalidDataException"/>
    /// if the bytes end early or announce more entities than the bytes that are left can hold.
    /// </summary>
    public static Command Read(ByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var type = (CommandType)reader.ReadUInt16();
        var player = reader.ReadInt32();
        var targetX = reader.ReadFixed();
        var targetY = reader.ReadFixed();
        var targetEntity = reader.ReadEntityId();
        var contentId = reader.ReadInt32();

        var count = reader.ReadCount(BytesPerEntity);
        Span<EntityId> entities = count <= EntitiesOnStack ? stackalloc EntityId[count] : new EntityId[count];
        for (var i = 0; i < count; i++)
        {
            entities[i] = reader.ReadEntityId();
        }

        return new Command(type, player, entities, targetX, targetY, targetEntity, contentId);
    }
}
