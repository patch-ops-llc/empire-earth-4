using Sim.Core;
using Sim.Data;
using Sim.Serialization;
using Sim.Setup;

namespace Net;

/// <summary>
/// What a peer writes to disk when a match stops because two peers no longer agree: the state it
/// ended in, with what is needed to load it again. Every peer stops at the same tick, so two of
/// these files from one match can be compared field by field, which the <c>desync-diff</c> tool
/// does.
/// </summary>
/// <remarks>
/// <para>
/// Layout, in the byte format of <see cref="ByteWriter"/>:
/// </para>
/// <code>
/// 4 bytes   the letters DSNC
/// 2 bytes   Version
/// 4 bytes   the player who wrote the file
/// 4 bytes   the tick whose state hashes differed, or -1 if the session did not say
/// ...       the match, as MatchDescription writes it
/// ...       the state, as Snapshot writes it, to the end of the data
/// </code>
/// <para>
/// The commands of the match are not in it. They are in the replay file the same peer wrote.
/// </para>
/// </remarks>
public sealed class DesyncSnapshot
{
    /// <summary>The version of the layout above.</summary>
    public const ushort Version = 1;

    private static readonly byte[] Magic = "DSNC"u8.ToArray();

    private DesyncSnapshot(int player, int desyncTick, MatchDescription description, ReadOnlyMemory<byte> state)
    {
        Player = player;
        DesyncTick = desyncTick;
        Description = description;
        State = state;
    }

    /// <summary>The player whose machine wrote the file.</summary>
    public int Player { get; }

    /// <summary>The tick whose state hashes differed, or -1. The state is from some ticks later, where the match stopped.</summary>
    public int DesyncTick { get; }

    public MatchDescription Description { get; }

    /// <summary>The state, as <see cref="Snapshot"/> wrote it.</summary>
    public ReadOnlyMemory<byte> State { get; }

    /// <summary>Appends the file for a match that stopped where the world stands.</summary>
    public static void Write(ByteWriter writer, int player, int desyncTick, MatchDescription description, World world)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(world);

        writer.WriteBytes(Magic);
        writer.WriteUInt16(Version);
        writer.WriteInt32(player);
        writer.WriteInt32(desyncTick);
        description.Write(writer);
        Snapshot.Write(world, writer);
    }

    /// <summary>Reads a file, which must be all that is left of the data. The state is only checked when it is restored.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a desync snapshot this build reads.</exception>
    public static DesyncSnapshot Read(ReadOnlyMemory<byte> data)
    {
        var reader = new ByteReader(data);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("The data does not start with DSNC, so it is not a desync snapshot.");
        }

        var version = reader.ReadUInt16();
        if (version != Version)
        {
            throw new InvalidDataException($"The desync snapshot has layout version {version} and this build reads version {Version}.");
        }

        var player = reader.ReadInt32();
        var desyncTick = reader.ReadInt32();
        var description = MatchDescription.Read(reader);
        return new DesyncSnapshot(player, desyncTick, description, data[reader.Position..]);
    }

    /// <summary>
    /// Builds the match from the data and puts it in the state the file holds. The match is built
    /// with <c>MatchSetup.Create</c>, as the peer that wrote the file built it, and what it works
    /// out from its state is brought in line afterwards.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The match cannot be built from this data, or the state is damaged or does not belong to it.
    /// </exception>
    public Match Restore(GameData data)
    {
        var match = Description.CreateMatch(data);
        var reader = new ByteReader(State);
        Snapshot.Restore(match, reader);
        reader.ExpectEnd();
        return match;
    }
}
