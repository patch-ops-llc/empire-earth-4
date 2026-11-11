using Sim.Core;
using Sim.Serialization;

namespace Sim.Tests.Serialization;

/// <summary>
/// The test every system adds for its own stores: a snapshot taken in the middle of a run, restored
/// and continued, must match the run that was never interrupted. One line per world:
/// <code>SnapshotAssert.RestoresAndContinues(() => BuildTheMatch().Simulation, script, snapshotTick: 40, lastTick: 200);</code>
/// </summary>
/// <remarks>
/// It fails when a field that matters is missing from a store's <see cref="IStateStore.Walk"/>,
/// when a walk does not assign back what the walker returns, when a store keeps something from
/// before the restore, and when state lives outside the stores. Choose a run in which the stores
/// under test change after the snapshot: a field nothing reads afterwards cannot be caught.
/// </remarks>
internal static class SnapshotAssert
{
    /// <param name="build">
    /// Builds the simulation as it is before its first tick, the same way every time it is called.
    /// It is called four times.
    /// </param>
    /// <param name="script">The commands of the whole run, each with its tick.</param>
    /// <param name="snapshotTick">The number of ticks run before the snapshot is taken. 0 takes it before the first.</param>
    /// <param name="lastTick">The number of ticks in the whole run.</param>
    public static void RestoresAndContinues(
        Func<Simulation> build,
        IReadOnlyList<(int Tick, Command Command)> script,
        int snapshotTick,
        int lastTick)
    {
        Assert.InRange(snapshotTick, 0, lastTick);

        // The run that is never interrupted, which everything else is compared with.
        var uninterrupted = build();
        Enqueue(uninterrupted, script, fromTick: 0);
        var hashes = new ulong[lastTick + 1];
        hashes[0] = uninterrupted.World.ComputeHash();
        for (var tick = 1; tick <= lastTick; tick++)
        {
            uninterrupted.Tick();
            hashes[tick] = uninterrupted.World.ComputeHash();
        }

        // The run the snapshot is taken from. Taking it must not disturb the run.
        var source = build();
        Enqueue(source, script, fromTick: 0);
        source.Run(snapshotTick);
        var writer = new ByteWriter();
        Snapshot.Write(source.World, writer);
        var snapshot = writer.ToArray();
        Assert.True(hashes[snapshotTick] == source.World.ComputeHash(), "Taking the snapshot changed the world.");
        Continue(source, "The run the snapshot was taken from");

        // Restored into a simulation that has not run, and into one that has run to the end and
        // so holds other entities and values, none of which may survive the restore.
        RestoreAndContinue(build(), "The run restored into a new simulation");
        var used = build();
        Enqueue(used, script, fromTick: 0);
        used.Run(lastTick);
        RestoreAndContinue(used, "The run restored into a simulation that had run to the end");

        void RestoreAndContinue(Simulation simulation, string what)
        {
            var reader = new ByteReader(snapshot);
            Snapshot.Restore(simulation, reader);
            reader.ExpectEnd();

            Assert.Equal(snapshotTick, simulation.World.Tick);
            Assert.Equal(snapshotTick, simulation.Commands.NextTick);
            Assert.Equal(0, simulation.Commands.Count);
            Assert.True(hashes[snapshotTick] == simulation.World.ComputeHash(), $"{what} does not have the hash of the world the snapshot was taken from.");

            var again = new ByteWriter();
            Snapshot.Write(simulation.World, again);
            Assert.True(snapshot.AsSpan().SequenceEqual(again.WrittenSpan), $"{what} does not write the snapshot it was restored from.");

            Enqueue(simulation, script, fromTick: snapshotTick);
            Continue(simulation, what);
        }

        void Continue(Simulation simulation, string what)
        {
            for (var tick = snapshotTick + 1; tick <= lastTick; tick++)
            {
                simulation.Tick();
                if (simulation.World.ComputeHash() != hashes[tick])
                {
                    Assert.Fail(
                        $"{what} left the uninterrupted run in tick {tick - 1}, {tick - 1 - snapshotTick} ticks after the snapshot. " +
                        "A field is missing from a store's Walk, or state lives outside the stores.");
                }
            }
        }
    }

    /// <summary>The same check for a run without commands.</summary>
    public static void RestoresAndContinues(Func<Simulation> build, int snapshotTick, int lastTick) =>
        RestoresAndContinues(build, [], snapshotTick, lastTick);

    private static void Enqueue(Simulation simulation, IReadOnlyList<(int Tick, Command Command)> script, int fromTick)
    {
        foreach (var (tick, command) in script)
        {
            if (tick >= fromTick)
            {
                simulation.Commands.Enqueue(tick, command);
            }
        }
    }
}
