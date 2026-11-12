using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Structures.StructureMatches;

namespace Sim.Tests.Structures;

/// <summary>Design 5.2: every player starts with a finished Capitol, which the match setup places.</summary>
public class StartingCapitolTests
{
    [Fact]
    public void EveryPlayerStartsWithAFinishedCapitolOnTheirStart()
    {
        var match = WithStarts();
        var capitol = match.Data.StartStructure;
        Assert.Equal((2, 6), (capitol.Footprint, capitol.TerritoryRadius));

        Assert.Equal(2, match.Structures.Count);
        for (var player = 0; player < 2; player++)
        {
            var start = match.Map.Starts[player];
            var entity = Assert.Single(match.StructuresOf(player));

            Assert.Equal(new PlacedStructure { Type = capitol.Index, X = start.X, Y = start.Y, Size = 2 }, match.Structures.Get(entity));
            Assert.Equal(new Body { Owner = player, Position = StructureSetup.CenterOf(new TileSquare(start.X, start.Y, 2)) }, match.Bodies.Get(entity));
            Assert.Equal(Health.Full(2_000_000), match.Health.Get(entity));
            Assert.Equal(Territory.Around(player, start.X, start.Y, 2, 6), match.Territories.Get(entity));
            Assert.False(match.WorkSites.Contains(entity));
            Assert.True(match.Research.State.Slot(player, 0).IsOpen);
            Assert.False(match.Research.State.Slot(player, 1).IsOpen);

            for (var tile = 0; tile < 4; tile++)
            {
                Assert.False(match.Grid.IsPassable(start.X + (tile % 2), start.Y + (tile / 2)));
            }
        }
    }

    /// <summary>
    /// Plan 15b: the twelve starting workers still have numbers 1 to 12. The Capitols come next,
    /// in the place of the territories that stood alone before, so the deposits keep theirs too.
    /// </summary>
    [Fact]
    public void TheWorkersKeepTheirNumbersTheCapitolsComeNextAndTheDepositsLast()
    {
        var match = WithStarts();

        Assert.Equal(Enumerable.Range(1, 12), Enumerable.Range(0, match.Workers.Count).Select(index => match.Workers.EntityAt(index).Value));
        Assert.Equal([13, 14], Enumerable.Range(0, match.Structures.Count).Select(index => match.Structures.EntityAt(index).Value));
        Assert.Equal([13, 14], Enumerable.Range(0, match.Territories.Count).Select(index => match.Territories.EntityAt(index).Value));
        Assert.Equal(Enumerable.Range(15, 6), Enumerable.Range(0, match.WorkSites.Count).Select(index => match.WorkSites.EntityAt(index).Value));
        Assert.Equal((0, 1), (match.Bodies.Get(new EntityId(13)).Owner, match.Bodies.Get(new EntityId(14)).Owner));
    }

    [Fact]
    public void NoWorkerStartsUnderTheCapitolAndEveryWorkerCanWalk()
    {
        var match = WithStarts();

        for (var i = 0; i < match.Workers.Count; i++)
        {
            Assert.NotEqual(0, match.Grid.RegionOf(match.Bodies.Get(match.Workers.EntityAt(i)).Position));
        }
    }

    [Fact]
    public void APlayerWithoutAStartHasNoCapitolAndNoResearchSlot()
    {
        var data = GeneratorData;
        var match = MatchSetup.Create(data, data.Maps[0], 1, [PlayerSetup.WithoutStart, new(1)]);

        Assert.Empty(match.StructuresOf(0));
        Assert.False(match.Research.State.Slot(0, 0).IsOpen);
        var capitol = Assert.Single(match.StructuresOf(1));
        Assert.Equal(34, match.Structures.Get(capitol).X);
        Assert.True(match.Research.State.Slot(1, 0).IsOpen);
        Assert.True(match.Grid.IsPassable(4, 7));
    }

    [Fact]
    public void AStartWithoutRoomForTheCapitolIsRejected()
    {
        var map = TestMaps.FromRows(
            [
                "........",
                ".F......",
                "........",
                "........",
                "........",
            ],
            MapSymmetry.MirrorX,
            [new MapStart(0, 0)]);

        var error = Assert.Throws<ArgumentException>(() => MatchSetup.Create(TestMatches.Data, map, 1, [new(0)]));
        Assert.Contains("has no room for the 2 by 2 tiles of the Capitol of player 0", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Design 4.1: a deposit pays the player whose territory holds it, which is now the Capitol's.</summary>
    [Fact]
    public void TheTerritoryOfTheCapitolIsWhatLetsItsOwnerEarnAtTheMainDeposit()
    {
        var match = WithStarts();
        var main = match.Map.Deposits[0];
        Assert.True(Territory.Holds(match.Territories, match.World.Entities, 0, main.X, main.Y, 2));
        Assert.False(Territory.Holds(match.Territories, match.World.Entities, 1, main.X, main.Y, 2));

        match.Raze(match.StructuresOf(0)[0]);
        match.Simulation.Tick();

        Assert.False(Territory.Holds(match.Territories, match.World.Entities, 0, main.X, main.Y, 2));
    }
}
