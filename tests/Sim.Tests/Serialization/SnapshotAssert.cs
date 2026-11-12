using System.Reflection;
using Sim.Core;
using Sim.Numerics;
using Sim.Serialization;
using Sim.Setup;

namespace Sim.Tests.Serialization;

/// <summary>
/// The test every system adds for its own stores: a snapshot taken in the middle of a run, restored
/// and continued, must match the run that was never interrupted. One line per world:
/// <code>SnapshotAssert.RestoresAndContinues(BuildTheMatch, script, snapshotTick: 40, lastTick: 200);</code>
/// </summary>
/// <remarks>
/// <para>
/// It fails when a field is missing from a store's <see cref="IStateStore.Walk"/>, when a walk
/// does not assign back what the walker returns, when a store keeps something from before the
/// restore, and when state lives outside the stores. Choose a run in which the stores under test
/// change, before the snapshot and after it.
/// </para>
/// <para>
/// Two things are compared. The run that continues from the restored world must have the hashes
/// of the uninterrupted run, which catches what the rules read. And every value of every
/// <see cref="ComponentStore{T}"/> of the restored world must equal, field for field, the value in
/// the world the snapshot was taken from, which catches a field no rule reads yet and no hash
/// covers because the walk leaves it out. State kept in a store of another kind is only covered
/// by the first.
/// </para>
/// <para>
/// For a match, what is worked out from the state must also be the same in the restored match as
/// in the one the snapshot was taken from: <see cref="AssertSameDerivedState"/>. A milestone that
/// adds derived data to <see cref="Match.RefreshDerivedState"/> adds its comparison there.
/// </para>
/// </remarks>
internal static class SnapshotAssert
{
    /// <summary>
    /// The check for a match. It restores with <see cref="Snapshot.Restore(Match, ByteReader)"/>,
    /// as everything that restores a match must, and so also checks that the restore refreshes
    /// what the match works out from its state.
    /// </summary>
    /// <param name="build">
    /// Builds the match as it is before its first tick, the same way every time it is called.
    /// It is called four times.
    /// </param>
    /// <param name="script">The commands of the whole run, each with its tick.</param>
    /// <param name="snapshotTick">The number of ticks run before the snapshot is taken. 0 takes it before the first.</param>
    /// <param name="lastTick">The number of ticks in the whole run.</param>
    public static void RestoresAndContinues(
        Func<Match> build,
        IReadOnlyList<(int Tick, Command Command)> script,
        int snapshotTick,
        int lastTick)
    {
        ArgumentNullException.ThrowIfNull(build);
        RestoresAndContinues(
            () =>
            {
                var match = build();
                return new Subject(match.Simulation, match);
            },
            script,
            snapshotTick,
            lastTick);
    }

    /// <summary>The check for a simulation that is not a match, such as a test's own small world.</summary>
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
        ArgumentNullException.ThrowIfNull(build);
        RestoresAndContinues(() => new Subject(build(), null), script, snapshotTick, lastTick);
    }

    /// <summary>The same check for a run without commands.</summary>
    public static void RestoresAndContinues(Func<Simulation> build, int snapshotTick, int lastTick) =>
        RestoresAndContinues(build, [], snapshotTick, lastTick);

    private static void RestoresAndContinues(
        Func<Subject> build,
        IReadOnlyList<(int Tick, Command Command)> script,
        int snapshotTick,
        int lastTick)
    {
        Assert.InRange(snapshotTick, 0, lastTick);

        // The run that is never interrupted, which everything else is compared with.
        var uninterrupted = build().Simulation;
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
        Enqueue(source.Simulation, script, fromTick: 0);
        source.Simulation.Run(snapshotTick);
        var writer = new ByteWriter();
        Snapshot.Write(source.Simulation.World, writer);
        var snapshot = writer.ToArray();
        Assert.True(hashes[snapshotTick] == source.Simulation.World.ComputeHash(), "Taking the snapshot changed the world.");

        // Restored into a simulation that has not run, and into one that has run to the end and
        // so holds other entities and values, none of which may survive the restore. Both are
        // compared with the source while it still stands at the snapshot.
        RestoreAndContinue(build(), "The run restored into a new simulation");
        var used = build();
        Enqueue(used.Simulation, script, fromTick: 0);
        used.Simulation.Run(lastTick);
        RestoreAndContinue(used, "The run restored into a simulation that had run to the end");

        Continue(source.Simulation, "The run the snapshot was taken from");

        void RestoreAndContinue(Subject subject, string what)
        {
            var simulation = subject.Simulation;
            var reader = new ByteReader(snapshot);
            if (subject.Match is { } match)
            {
                Snapshot.Restore(match, reader);
            }
            else
            {
                Snapshot.Restore(simulation, reader);
            }

            reader.ExpectEnd();

            Assert.Equal(snapshotTick, simulation.World.Tick);
            Assert.Equal(snapshotTick, simulation.Commands.NextTick);
            Assert.Equal(0, simulation.Commands.Count);
            Assert.True(hashes[snapshotTick] == simulation.World.ComputeHash(), $"{what} does not have the hash of the world the snapshot was taken from.");
            AssertSameStoreValues(source.Simulation.World, simulation.World, what);
            if (subject.Match is { } restored)
            {
                AssertSameDerivedState(source.Match!, restored, what);
            }

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

    /// <summary>
    /// What a match works out from its state must be the same in both matches: every tile must
    /// be passable or not in both and have the same region, the spatial index must list the same
    /// entities for every square of two tiles on the map, and every player must have the table
    /// of blueprints of their tiers.
    /// </summary>
    public static void AssertSameDerivedState(Match expected, Match actual, string what)
    {
        Assert.Equal(expected.Grid.RegionCount, actual.Grid.RegionCount);
        for (var y = 0; y < expected.Grid.Height; y++)
        {
            for (var x = 0; x < expected.Grid.Width; x++)
            {
                if (expected.Grid.IsPassable(x, y) != actual.Grid.IsPassable(x, y)
                    || expected.Grid.RegionOf(x, y) != actual.Grid.RegionOf(x, y))
                {
                    Assert.Fail(
                        $"{what} has tile ({x}, {y}) {(actual.Grid.IsPassable(x, y) ? "passable" : "blocked")} in region {actual.Grid.RegionOf(x, y)}, " +
                        $"and the match the snapshot was taken from has it {(expected.Grid.IsPassable(x, y) ? "passable" : "blocked")} in region {expected.Grid.RegionOf(x, y)}.");
                }
            }
        }

        for (var player = 0; player < expected.Players.Count; player++)
        {
            Research.TechAssert.Same(expected.Research.Tech(player), actual.Research.Tech(player));
        }

        Assert.Equal(expected.SpatialIndex.Count, actual.SpatialIndex.Count);
        const int Side = 2;
        var expectedFound = new List<EntityId>();
        var actualFound = new List<EntityId>();
        for (var y = 0; y < expected.Grid.Height; y += Side)
        {
            for (var x = 0; x < expected.Grid.Width; x += Side)
            {
                var min = new FixedVector2(Fixed.FromInt(x), Fixed.FromInt(y));
                var max = new FixedVector2(Fixed.FromInt(x + Side), Fixed.FromInt(y + Side));
                expected.SpatialIndex.QueryBox(min, max, expectedFound);
                actual.SpatialIndex.QueryBox(min, max, actualFound);
                if (!expectedFound.SequenceEqual(actualFound))
                {
                    Assert.Fail(
                        $"{what}: the spatial index lists [{string.Join(", ", actualFound)}] in the {Side} tiles from ({x}, {y}) and the match the snapshot " +
                        $"was taken from lists [{string.Join(", ", expectedFound)}]. Restoring a match must refresh what it works out from its state.");
                }
            }
        }
    }

    /// <summary>Every component store of both worlds must hold the same entities with equal values.</summary>
    private static void AssertSameStoreValues(World expected, World actual, string what)
    {
        Assert.Equal(expected.Stores.Count, actual.Stores.Count);
        for (var i = 0; i < expected.Stores.Count; i++)
        {
            var type = expected.Stores[i].GetType();
            Assert.Equal(type, actual.Stores[i].GetType());
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ComponentStore<>))
            {
                typeof(SnapshotAssert)
                    .GetMethod(nameof(AssertSameValues), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(type.GetGenericArguments())
                    .Invoke(null, BindingFlags.DoNotWrapExceptions, null, [expected.Stores[i], actual.Stores[i], what], null);
            }
        }
    }

    private static void AssertSameValues<T>(ComponentStore<T> expected, ComponentStore<T> actual, string what)
        where T : struct, IStateValue
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected.EntityAt(i), actual.EntityAt(i));
            if (!expected.ValueAt(i).Equals(actual.ValueAt(i)))
            {
                Assert.Fail(
                    $"{what}: the {typeof(T).Name} of {expected.EntityAt(i)} in store '{expected.Name}' is not the one in the world the snapshot was taken from. " +
                    $"A field is missing from {typeof(T).Name}.Walk, or the walk does not assign it back.");
            }
        }
    }

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

    /// <summary>A simulation under test, and the match it belongs to when it belongs to one.</summary>
    private readonly record struct Subject(Simulation Simulation, Match? Match);
}
