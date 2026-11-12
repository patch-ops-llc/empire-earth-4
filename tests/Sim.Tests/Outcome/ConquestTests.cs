using Sim.Core;
using Sim.Movement;
using Sim.Outcome;
using Sim.Structures;
using Sim.Tests.Construction;
using Sim.Tests.Economy;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Structures;
using static Sim.Outcome.PlayerStanding;

namespace Sim.Tests.Outcome;

/// <summary>
/// Design 5.1: "Conquest. A player is eliminated when they have no Capitol and no Outpost. Last
/// player or team standing wins." Structures are taken off the map here by bringing their hit
/// points to zero, which is all a weapon does to them.
/// </summary>
public class ConquestTests
{
    [Fact]
    public void AMatchBeginsUndecidedWithEveryoneInIt()
    {
        var match = OutcomeMatches.WithStarts();
        match.Simulation.Run(40);

        Assert.Equal([Playing, Playing], match.Standings.All());
        Assert.Equal(2, match.Standings.PlayingCount);
        Assert.False(match.Standings.IsDecided);
        Assert.Equal((Standings.None, Standings.None), (match.Standings.Winner, match.Standings.DecidedTick));
        Assert.Equal(Standings.None, match.Standings.OutTick(0));
    }

    [Fact]
    public void APlayerWhoseOnlyCapitolFallsIsEliminatedInThatTickAndTheOtherWins()
    {
        var match = OutcomeMatches.WithStarts();
        match.Simulation.Run(10);

        match.Raze(match.CapitolOf(1));
        match.Simulation.Tick();

        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.True(match.Standings.IsDecided);
        Assert.Equal((0, 10, 10), (match.Standings.Winner, match.Standings.DecidedTick, match.Standings.OutTick(1)));
        Assert.Equal(Standings.None, match.Standings.OutTick(0));
    }

    [Fact]
    public void LosingOtherStructuresAndEveryUnitEliminatesNobody()
    {
        var match = OutcomeMatches.WithStarts();
        var works = match.PlaceStructure(1, "works", 46, 20);
        foreach (var worker in Enumerable.Range(0, match.Workers.Count).Select(match.Workers.EntityAt).ToArray())
        {
            match.Health.Get(worker).HitPoints = 0;
        }

        match.Raze(works);
        match.Simulation.Run(5);

        Assert.Equal(0, match.Workers.Count);
        Assert.Equal([Playing, Playing], match.Standings.All());
    }

    [Fact]
    public void AnOutpostKeepsAPlayerInTheMatchUntilItFallsToo()
    {
        var match = OutcomeMatches.WithStarts();
        var outpost = match.PlaceStructure(1, "outpost", 40, 6);

        match.Raze(match.CapitolOf(1));
        match.Simulation.Run(20);
        Assert.Equal([Playing, Playing], match.Standings.All());

        match.Raze(outpost);
        match.Simulation.Tick();
        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.Equal((0, 20), (match.Standings.Winner, match.Standings.DecidedTick));
    }

    /// <summary>Design 2.5 counts "unfinished Capitols and Outposts" as a player's: one still building itself keeps its owner in.</summary>
    [Theory]
    [InlineData("capitol")]
    [InlineData("outpost")]
    public void AnUnfinishedCapitolOrOutpostCounts(string structure)
    {
        var match = OutcomeMatches.WithStarts();
        var unfinished = match.LayStructure(1, match.Def(structure), 40, 6);
        match.Foundations.Add(unfinished, default);

        match.Raze(match.CapitolOf(1));
        match.Simulation.Run(20);
        Assert.Equal([Playing, Playing], match.Standings.All());
        Assert.True(match.Foundations.Contains(unfinished));

        match.Raze(unfinished);
        match.Simulation.Tick();
        Assert.Equal([Playing, Eliminated], match.Standings.All());
    }

    [Fact]
    public void AnOutpostThatIsOrderedButNotYetLaidDoesNotCount()
    {
        var match = OutcomeMatches.WithStarts();
        match.Stockpiles.Add(1, 1000, 1000);
        var worker = match.WorkerAt(1, 30, 20);
        match.OrderBuild(1, "outpost", 48, 15, worker);
        match.Simulation.Tick();
        Assert.True(match.BuildOrders.Contains(worker));

        match.Raze(match.CapitolOf(1));
        match.Simulation.Tick();

        Assert.Equal([Playing, Eliminated], match.Standings.All());
    }

    [Fact]
    public void CancellingTheLastOutpostWhileItIsBuiltEliminatesItsOwner()
    {
        var match = OutcomeMatches.WithStarts();
        var unfinished = match.LayStructure(1, match.Def("outpost"), 40, 6);
        match.Foundations.Add(unfinished, default);
        match.Raze(match.CapitolOf(1));
        match.Simulation.Run(3);
        Assert.Equal([Playing, Playing], match.Standings.All());

        match.OrderCancelBuild(1, unfinished);
        match.Simulation.Tick();

        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.Equal(0, match.Standings.Winner);
    }

    [Fact]
    public void PlayersWhoLoseTheirLastCapitolInTheSameTickGoOutTogetherAndNobodyWins()
    {
        var match = OutcomeMatches.WithStarts();
        match.Simulation.Run(4);

        match.Raze(match.CapitolOf(0));
        match.Raze(match.CapitolOf(1));
        match.Simulation.Tick();

        Assert.Equal([Eliminated, Eliminated], match.Standings.All());
        Assert.True(match.Standings.IsDecided);
        Assert.Equal((Standings.None, 4), (match.Standings.Winner, match.Standings.DecidedTick));
    }

    [Fact]
    public void WithThreePlayersTheMatchGoesOnUntilOneIsLeft()
    {
        var match = OutcomeMatches.WithStarts(players: 3);

        match.Raze(match.CapitolOf(2));
        match.Simulation.Run(6);
        Assert.Equal([Playing, Playing, Eliminated], match.Standings.All());
        Assert.False(match.Standings.IsDecided);
        Assert.Equal((Standings.None, Standings.None, 0), (match.Standings.Winner, match.Standings.DecidedTick, match.Standings.OutTick(2)));

        match.Raze(match.CapitolOf(0));
        match.Simulation.Tick();
        Assert.Equal([Eliminated, Playing, Eliminated], match.Standings.All());
        Assert.Equal((1, 6), (match.Standings.Winner, match.Standings.DecidedTick));
    }

    [Fact]
    public void OnceTheMatchIsDecidedTheWinnerStaysTheWinner()
    {
        var match = OutcomeMatches.WithStarts();
        match.Raze(match.CapitolOf(1));
        match.Simulation.Tick();

        match.Raze(match.CapitolOf(0));
        match.Simulation.Run(5);

        Assert.Equal([Playing, Eliminated], match.Standings.All());
        Assert.Equal((0, 0), (match.Standings.Winner, match.Standings.DecidedTick));
    }

    [Fact]
    public void WhatAnEliminatedPlayerHadStaysOnTheMap()
    {
        var match = OutcomeMatches.WithStarts(players: 3);
        var works = match.PlaceStructure(2, "works", 34, 10);
        var workers = match.Workers.Count;

        match.Raze(match.CapitolOf(2));
        match.Simulation.Run(10);

        Assert.Equal([works], match.StructuresOf(2));
        Assert.Equal(workers, match.Workers.Count);
    }

    [Fact]
    public void AnEliminatedPlayerGivesNoMoreOrders()
    {
        var match = OutcomeMatches.WithStarts(players: 3);
        var worker = Enumerable.Range(0, match.Workers.Count).Select(match.Workers.EntityAt).First(entity => match.Bodies.Get(entity).Owner == 2);
        var other = Enumerable.Range(0, match.Workers.Count).Select(match.Workers.EntityAt).First(entity => match.Bodies.Get(entity).Owner == 1);
        match.Raze(match.CapitolOf(2));
        match.Simulation.Tick();
        var (was, dropped) = (match.PositionOf(worker), match.Simulation.DroppedCommandCount);

        match.OrderMove(2, TestMatches.Point("30", "20"), worker);
        match.OrderMove(1, TestMatches.Point("30", "20"), other);
        match.OrderConcede(2);
        match.Simulation.Run(20);

        Assert.Equal(was, match.PositionOf(worker));
        Assert.NotEqual(TestMatches.Point("30", "20"), was);
        Assert.Equal(dropped + 2, match.Simulation.DroppedCommandCount);
        Assert.Equal(Eliminated, match.Standings.Of(2));
        Assert.NotEqual(MoveState.Idle, match.Movers.Get(other).State);
    }

    /// <summary>The battle harness and most tests give their players no start: such a player has no Capitol to lose.</summary>
    [Fact]
    public void PlayersWithoutAStartAreNeverEliminated()
    {
        var match = TestMatches.Create(TestMaps.Open(24, 24));
        match.Simulation.Run(10);
        Assert.Equal([Playing, Playing], match.Standings.All());

        var capitol = match.PlaceStructure(0, "capitol", 4, 4);
        match.Simulation.Tick();
        match.Raze(capitol);
        match.Simulation.Run(3);

        Assert.Equal([Playing, Playing], match.Standings.All());
        Assert.False(match.Standings.IsDecided);
    }

    [Fact]
    public void AMatchOfOnePlayerEndsWithoutAWinnerWhenThatPlayerIsOut()
    {
        var match = OutcomeMatches.WithStarts(players: 1);
        match.Simulation.Run(3);
        Assert.False(match.Standings.IsDecided);

        match.Raze(match.CapitolOf(0));
        match.Simulation.Tick();

        Assert.Equal([Eliminated], match.Standings.All());
        Assert.True(match.Standings.IsDecided);
        Assert.Equal((Standings.None, 3), (match.Standings.Winner, match.Standings.DecidedTick));
    }

    [Theory]
    [InlineData("capitol", true)]
    [InlineData("outpost", true)]
    [InlineData("works", false)]
    [InlineData("generator", false)]
    [InlineData("wall", false)]
    public void OnlyCapitolsAndOutpostsKeepAPlayerIn(string structure, bool keeps) =>
        Assert.Equal(keeps, ConquestSystem.KeepsInTheMatch(ConstructionMatches.Data.StructureOf(structure)));

    [Fact]
    public void TheStandingsRefuseWhatAMatchCannotReach()
    {
        var standings = new Standings(2);
        Assert.Throws<ArgumentException>(() => standings.TakeOut(0, Playing, 3));
        Assert.Throws<ArgumentException>(() => standings.TakeOut(2, Eliminated, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => standings.TakeOut(0, Eliminated, -1));
        standings.TakeOut(0, Eliminated, 3);
        Assert.Throws<ArgumentException>(() => standings.TakeOut(0, Conceded, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Standings(0));
        Assert.False(standings.IsPlaying(-1));
        Assert.False(standings.IsPlaying(2));
        Assert.True(standings.Admits(null!, new Command(CommandType.Move, 7)));
    }
}
