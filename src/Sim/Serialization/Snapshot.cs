using Sim.Core;
using Sim.Setup;

namespace Sim.Serialization;

/// <summary>
/// The whole state of a match as bytes, and back. A snapshot holds exactly what
/// <see cref="World.ComputeHash"/> covers, because both are the same walk over the world
/// (<see cref="World.Walk"/>): tick, random generator, entities and every store.
/// </summary>
/// <remarks>
/// <para>
/// Layout, in the byte format of <see cref="ByteWriter"/>:
/// </para>
/// <code>
/// 4 bytes   the letters SNAP
/// 2 bytes   Version
/// a count   the number of stores, then the name of each as text, in the order they are walked
/// ...       every field the walk visits, in order
/// 8 bytes   the state hash of the world the snapshot was taken from
/// </code>
/// <para>
/// A snapshot is not a match by itself. Systems, command handlers and data are not state: to
/// restore, build the same match setup again and read the snapshot into it. The commands waiting in
/// the <see cref="CommandQueue"/> are not state either, and are not in the snapshot. Whatever needs
/// them writes them beside it with <see cref="CommandQueue.CopyPendingTo"/> and
/// <see cref="CommandEncoding"/>.
/// </para>
/// <para>
/// Take a snapshot between ticks, never from inside one.
/// </para>
/// </remarks>
public static class Snapshot
{
    /// <summary>The version of the layout above. It does not cover what each store walks: the store names and the hash at the end guard that.</summary>
    public const ushort Version = 1;

    private const uint Magic = 0x50414E53; // "SNAP" as the bytes 53 4E 41 50

    /// <summary>Appends a snapshot of the world to the writer. The world is not changed.</summary>
    public static void Write(World world, ByteWriter writer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteUInt32(Magic);
        writer.WriteUInt16(Version);

        var stores = world.Stores;
        writer.WriteCount(stores.Count);
        foreach (var store in stores)
        {
            writer.WriteString(store.Name);
        }

        world.Walk(new StateWriter(writer));
        writer.WriteUInt64(world.ComputeHash());
    }

    /// <summary>
    /// Replaces the state of a match with the snapshot the reader is at, as the other overload
    /// does for its simulation, and then brings what the match works out from its state in line
    /// with it (<see cref="Match.RefreshDerivedState"/>). Everything that restores a match goes
    /// through here.
    /// </summary>
    /// <exception cref="InvalidDataException">As for the other overload. The match must then not be used.</exception>
    public static void Restore(Match match, ByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(match);
        Restore(match.Simulation, reader);
        match.RefreshDerivedState();
    }

    /// <summary>
    /// Replaces the state of the simulation's world with the snapshot the reader is at, and leaves
    /// the reader after it. The simulation must have been set up as the one the snapshot came from
    /// was: the same stores, systems and handlers. Commands waiting in its queue are discarded, and
    /// the next tick to run is the snapshot's; queue again what should run from there. The event
    /// list is emptied: its events were of a tick that the restored state did not come from. For a
    /// <see cref="Match"/>, call the overload that takes one.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The bytes are not a snapshot, are damaged, or belong to a world with different stores; or the
    /// restored state does not hash to the value the snapshot recorded, which means a store's walk
    /// does not give back what it was given. The world is then left half replaced and must not be
    /// used: build a new one.
    /// </exception>
    public static void Restore(Simulation simulation, ByteReader reader)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException("The data is not a snapshot: it does not start with SNAP.");
        }

        var version = reader.ReadUInt16();
        if (version != Version)
        {
            throw new InvalidDataException($"The snapshot has layout version {version} and this build reads version {Version}.");
        }

        var world = simulation.World;
        var stores = world.Stores;
        var storeCount = reader.ReadCount(minimumBytesPerItem: sizeof(int));
        if (storeCount != stores.Count)
        {
            throw new InvalidDataException($"The snapshot has {storeCount} stores and this world has {stores.Count}.");
        }

        foreach (var store in stores)
        {
            var name = reader.ReadString();
            if (!string.Equals(name, store.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The snapshot has a store named '{name}' where this world has '{store.Name}'.");
            }
        }

        world.Walk(new StateReader(reader));

        var recorded = reader.ReadUInt64();
        var restored = world.ComputeHash();
        if (restored != recorded)
        {
            throw new InvalidDataException(
                $"The restored state hashes to 0x{restored:X16} and the snapshot recorded 0x{recorded:X16}. " +
                "Either the snapshot is damaged or a store's Walk does not assign back what the walker returns.");
        }

        simulation.Commands.Restart(world.Tick);
        simulation.Events.Clear();
    }
}
