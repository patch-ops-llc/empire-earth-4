using Sim.Core;
using Sim.Numerics;
using Sim.Outcome;
using Sim.Serialization;
using Sim.Setup;
using Sim.Tests.Research;
using Sim.Tests.Serialization;
using Sim.Tests.Structures;

namespace Sim.Tests.Outcome;

/// <summary>
/// The standings are state: they are in the state hash, and a match restored from a snapshot
/// knows who is out, since when and why, and still turns that player's orders away.
/// </summary>
public class OutcomeSnapshotTests
{
    private static Match Build()
    {
        var match = OutcomeMatches.WithStarts(players: 3);
        match.RefreshDerivedState();
        return match;
    }

    private static readonly (int Tick, Command Command)[] Script =
    [
        (12, new Command(CommandType.Concede, 2)),
        (20, new Command(CommandType.Move, 2, [new EntityId(9)], targetX: Fixed.FromInt(20), targetY: Fixed.FromInt(20))),
        (40, new Command(CommandType.Concede, 0)),
        (50, new Command(CommandType.Concede, 1)),
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(30)]
    [InlineData(41)]
    public void AMatchRestoresAndContinuesAtAnyPointOfItsEnd(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(Build, Script, snapshotTick, lastTick: 60);

    [Fact]
    public void ARestoredMatchKnowsWhoIsOutSinceWhenAndWhy()
    {
        var match = Build();
        match.Raze(match.CapitolOf(1));
        match.Simulation.Run(3);
        match.OrderConcede(2);
        match.Simulation.Run(2);
        var writer = new ByteWriter();
        Snapshot.Write(match.World, writer);

        var restored = Build();
        Snapshot.Restore(restored, new ByteReader(writer.ToArray()));

        Assert.Equal([PlayerStanding.Playing, PlayerStanding.Eliminated, PlayerStanding.Conceded], restored.Standings.All());
        Assert.Equal((0, 3, 0, 3), (restored.Standings.Winner, restored.Standings.DecidedTick, restored.Standings.OutTick(1), restored.Standings.OutTick(2)));
    }

    [Fact]
    public void TheStandingsAreInTheStateHash()
    {
        var (played, conceded) = (Build(), Build());
        conceded.OrderConcede(2);
        played.Simulation.Tick();
        conceded.Simulation.Tick();

        Assert.NotEqual(played.World.ComputeHash(), conceded.World.ComputeHash());
    }

    /// <summary>Player 0 is in the match and player 1 went out in tick 3.</summary>
    [Theory]
    [InlineData("_standing[player]", 0, 3)]
    [InlineData("_standing[player]", 0, -1)]
    [InlineData("_standing[player]", 0, 1)]
    [InlineData("_outTick[player]", 0, 4)]
    [InlineData("_outTick[player]", 0, -2)]
    [InlineData("_standing[player]", 1, 0)]
    [InlineData("_outTick[player]", 1, -1)]
    public void ARestoreRefusesAStandingNoMatchCanReach(string field, int occurrence, long value)
    {
        var standings = new Standings(2);
        standings.TakeOut(1, PlayerStanding.Eliminated, 3);
        var change = new ChangingWalker(field, occurrence, value);

        Assert.Throws<InvalidDataException>(() => standings.Walk(change));
        Assert.True(change.Changed);
    }
}
