using Sim.Core;
using Sim.Research;
using Sim.Setup;
using Sim.Tech;
using Sim.Tests.Map;
using Sim.Tests.Tech;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>What a finished research changes: the tier, the player's table, the epoch, and what stands on the map.</summary>
public class FinishingResearchTests
{
    [Fact]
    public void AResearchRaisesItsTrackByOneTierAfterExactlyItsTime()
    {
        var match = Create();
        match.Simulation.Run(7);
        var events = match.Events();
        match.OrderResearch(0, Firepower);

        match.Simulation.Run(TicksToTier2 - 1);
        Assert.Equal(1, match.Research.State.Tier(0, Firepower));
        Assert.Equal(1, match.Slot(0).TicksLeft);
        Assert.Single(events);

        match.Simulation.Tick();
        Assert.Equal(7 + TicksToTier2, match.World.Tick);
        Assert.Equal([2, 1, 1, 1, 1, 1], match.Research.State.Tiers(0).ToArray());
        Assert.Equal([1, 1, 1, 1, 1, 1], match.Research.State.Tiers(1).ToArray());
        Assert.True(match.Slot(0).IsFree);
        Assert.Equal(new ResearchEvent(SimEventKind.ResearchFinished, 7 + TicksToTier2 - 1, 0, Firepower, 2, 1), events[^1]);
        Assert.Equal(2, events.Count);

        // Tiers are permanent and the price is spent.
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
        Assert.Equal(3, match.Research.Tech(0).Research[Firepower].Tier);
    }

    [Fact]
    public void ThePlayersTableIsReplacedAndNobodyElsesIs()
    {
        var match = Create();
        var (mine, theirs) = (match.Research.Tech(0), match.Research.Tech(1));
        match.OrderResearch(0, Logistics);

        match.Simulation.Run(TicksToTier2 - 1);
        Assert.Same(mine, match.Research.Tech(0));
        match.Simulation.Tick();

        Assert.NotSame(mine, match.Research.Tech(0));
        Assert.Same(theirs, match.Research.Tech(1));
        TechAssert.Same(TechResolver.Resolve(DesignData.Data, DesignData.Mix(1, 1, 1, 1, 2, 1)), match.Research.Tech(0));
        Assert.Equal((55, 70), (mine.SupplyCap, match.Research.Tech(0).SupplyCap));

        // The old table is not changed: whoever still holds it sees the player as they were.
        Assert.Equal(1, mine.Tiers[Logistics]);
    }

    /// <summary>
    /// The plan's scenario: two players with different mixes get different stats for the same
    /// chassis. One researches quality and the other quantity, and each then has their own
    /// Hoplite (130 hit points, 8 damage, 70 Materials and 10 Energy, 18 seconds at tier 1).
    /// </summary>
    [Fact]
    public void TwoPlayersWithDifferentMixesGetDifferentStatsForTheSameChassis()
    {
        var match = Create();
        match.Research.OpenSlot(0, match.PlaceHolder(0));
        var hoplite = DesignData.Chassis("hoplite").Index;
        var start = match.Research.Tech(0).Blueprints[hoplite];
        Assert.Equal((130_000, 8_000, 70, 10, 360), (start.HitPoints, start.DamagePerSecond, start.Materials, start.Energy, start.BuildTicks));

        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Protection);
        match.OrderResearch(1, Manufacturing);
        match.Simulation.Run(TicksToTier2);

        var elite = match.Research.Tech(0).Blueprints[hoplite];
        var mass = match.Research.Tech(1).Blueprints[hoplite];
        Assert.Equal((162_500, 10_000, 93, 13, 360, 1), (elite.HitPoints, elite.DamagePerSecond, elite.Materials, elite.Energy, elite.BuildTicks, elite.BatchSize));
        Assert.Equal((130_000, 8_000, 63, 9, 288, 2), (mass.HitPoints, mass.DamagePerSecond, mass.Materials, mass.Energy, mass.BuildTicks, mass.BatchSize));
        Assert.Equal([1, 1], elite.Accents.Select(accent => accent.Level));
        Assert.Equal([new Accent(Manufacturing, 1)], mass.Accents);
    }

    /// <summary>
    /// Design 1.4: the epoch is the highest tier three tracks have reached, and its change is
    /// announced. Three researches that finish in one tick finish in the order of their slots.
    /// </summary>
    [Fact]
    public void TheEpochRisesWithTheThirdTrackAndIsReportedOnce()
    {
        var match = Create();
        var (first, second) = (match.PlaceHolder(0, 3, 3), match.PlaceHolder(0, 5, 3));
        match.Research.OpenSlot(0, first);
        match.Research.OpenSlot(0, second);
        var events = match.Events();

        match.OrderResearch(0, Mobility);
        match.OrderResearch(0, Extraction);
        match.OrderResearch(0, Firepower);
        match.Simulation.Run(TicksToTier2);

        var last = TicksToTier2 - 1;
        Assert.Equal(
            [
                new ResearchEvent(SimEventKind.ResearchStarted, 0, 0, Mobility, 2, 1),
                new ResearchEvent(SimEventKind.ResearchStarted, 0, 0, Extraction, 2, 1, first),
                new ResearchEvent(SimEventKind.ResearchStarted, 0, 0, Firepower, 2, 1, second),
                new ResearchEvent(SimEventKind.ResearchFinished, last, 0, Mobility, 2, 1),
                new ResearchEvent(SimEventKind.ResearchFinished, last, 0, Extraction, 2, 1, first),
                new ResearchEvent(SimEventKind.ResearchFinished, last, 0, Firepower, 2, 2, second),
                new ResearchEvent(SimEventKind.EpochChanged, last, 0, Firepower, 2, 2),
            ],
            events);
        Assert.Equal((2, 1), (match.Research.Tech(0).Epoch, match.Research.Tech(1).Epoch));

        // A fourth track at the same tier is breadth, not a new epoch.
        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2 / 2);
        Assert.Equal(new ResearchEvent(SimEventKind.ResearchFinished, last + (TicksToTier2 / 2), 0, Protection, 2, 2), events[^1]);
        Assert.Equal(9, events.Count);
    }

    /// <summary>Design 1.3 and 1.4: once the epoch has risen, the tracks left behind cost half and take half as long.</summary>
    [Fact]
    public void AfterTheEpochRisesTheNextResearchIsPricedInItsNewBand()
    {
        var match = Create();
        match.Research.SetTiers(0, DesignData.Mix(2, 2, 1, 1, 1, 1));
        Assert.Equal(ResearchBand.Frontier, match.Research.Tech(0).Research[Logistics].Price!.Value.Band);

        match.OrderResearch(0, Mobility);
        match.Simulation.Run(TicksToTier2);

        Assert.Equal(new ResearchPrice(ResearchBand.CatchUp, 50, 50, 450), match.Research.Tech(0).Research[Logistics].Price);
        Assert.Equal(ResearchStatus.Available, match.Research.Tech(0).Research[Firepower].Status);
    }

    /// <summary>
    /// Research reports on the event list of the simulation, which holds the events of one tick
    /// in the order they happened. Each says whose research it is, the track, the tier, the
    /// player's epoch after it, and the holder of the slot.
    /// </summary>
    [Fact]
    public void WhatHappensToAResearchIsOnTheEventListOfItsTick()
    {
        var match = Create();
        var academy = match.PlaceHolder(1);
        match.Research.OpenSlot(1, academy);
        match.Research.SetTiers(0, DesignData.Mix(2, 2, 1, 1, 1, 1));
        Assert.Empty(match.Simulation.Events);

        match.OrderResearch(0, Mobility);
        match.OrderResearch(1, Firepower);
        match.OrderResearch(1, Protection);
        match.OrderResearch(1, Protection);
        match.Simulation.Tick();

        Assert.Equal(
            [
                new ResearchEvent(SimEventKind.ResearchStarted, 0, 0, Mobility, 2, 1),
                new ResearchEvent(SimEventKind.ResearchStarted, 0, 1, Firepower, 2, 1),
                new ResearchEvent(SimEventKind.ResearchStarted, 0, 1, Protection, 2, 1, academy),
            ],
            match.EventsOnTheList());
        Assert.All(match.Simulation.Events, listed => Assert.Equal((EntityId.None, default), (listed.Other, listed.Position)));

        match.OrderCancel(1, Protection);
        match.Simulation.Tick();
        Assert.Equal([new ResearchEvent(SimEventKind.ResearchCancelled, 1, 1, Protection, 2, 1, academy)], match.EventsOnTheList());

        match.Simulation.Tick();
        Assert.Empty(match.Simulation.Events);

        match.Simulation.Run(TicksToTier2 - 3);
        Assert.Equal(
            [
                new ResearchEvent(SimEventKind.ResearchFinished, TicksToTier2 - 1, 0, Mobility, 2, 2),
                new ResearchEvent(SimEventKind.EpochChanged, TicksToTier2 - 1, 0, Mobility, 2, 2),
                new ResearchEvent(SimEventKind.ResearchFinished, TicksToTier2 - 1, 1, Firepower, 2, 1),
            ],
            match.EventsOnTheList());
    }

    /// <summary>Every kind of event of research is read back as it was added, and no other kind is taken for one.</summary>
    [Fact]
    public void AnEventOfResearchIsReadBackWhole()
    {
        var match = Create();
        match.Research.SetTiers(0, DesignData.Mix(9, 9, 3, 3, 3, 7));
        match.OrderResearch(0, Extraction);
        match.Simulation.Tick();

        var listed = Assert.Single(match.Simulation.Events);
        Assert.Equal((SimEventKind.ResearchStarted, 0, 0, EntityId.None), (listed.Kind, listed.Tick, listed.Player, listed.Entity));
        Assert.True(ResearchEvent.TryRead(listed, out var research));
        Assert.Equal(new ResearchEvent(SimEventKind.ResearchStarted, 0, 0, Extraction, 8, 7), research);

        Assert.False(ResearchEvent.TryRead(listed with { Kind = (SimEventKind)1000 }, out var other));
        Assert.Equal(default, other);
        Assert.False(ResearchEvent.IsResearch(SimEventKind.None));
    }

    /// <summary>
    /// The event list is output. A match in which something reads it after every tick runs to the
    /// same state as one in which nothing does.
    /// </summary>
    [Fact]
    public void WhoReadsTheEventsChangesNothing()
    {
        var unread = MatchSetup.Create(DesignData.Data, TestMaps.Open(24, 24), 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);
        for (var player = 0; player < 2; player++)
        {
            unread.Research.OpenPlayerSlot(player);
            unread.Stockpiles.Add(player, Plenty, Plenty);
        }

        var read = Create();
        var events = read.Events();
        foreach (var match in new[] { unread, read })
        {
            match.OrderResearch(0, Firepower);
            match.OrderResearch(1, Protection);
            match.Simulation.Run(500);
            match.OrderCancel(1, Protection);
            match.Simulation.Run(500);
        }

        Assert.Equal(4, events.Count);
        Assert.Equal(unread.World.ComputeHash(), read.World.ComputeHash());
    }

    [Fact]
    public void HandlersRunInTheOrderTheyWereAddedOnceForEachFinishedResearch()
    {
        var match = Create();
        match.Research.OpenSlot(0, match.PlaceHolder(0));
        var calls = new List<string>();
        match.Research.AddTechChangeHandler(new Recorder("first", calls, match));
        match.Research.AddTechChangeHandler(new Recorder("second", calls, match));

        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Protection);
        match.OrderResearch(1, Mobility);
        match.Simulation.Tick();
        match.OrderCancel(1, Mobility);
        match.Simulation.Run(TicksToTier2 - 1);

        // A cancelled research changes no tech, so nothing is told of it.
        Assert.Equal(
            [
                "first: player 0, 1/1/1/1/1/1 -> 2/1/1/1/1/1 in tick 899",
                "second: player 0, 1/1/1/1/1/1 -> 2/1/1/1/1/1 in tick 899",
                "first: player 0, 2/1/1/1/1/1 -> 2/2/1/1/1/1 in tick 899",
                "second: player 0, 2/1/1/1/1/1 -> 2/2/1/1/1/1 in tick 899",
            ],
            calls);
    }

    [Fact]
    public void HandlersAreAddedBeforeTheFirstTickOnly()
    {
        var match = Create();
        match.Simulation.Tick();

        Assert.Throws<InvalidOperationException>(() => match.Research.AddTechChangeHandler(new Recorder("late", [], match)));
        Assert.Throws<ArgumentNullException>(() => Create().Research.AddTechChangeHandler(null!));
    }

    private sealed class Recorder(string name, List<string> calls, Match match) : ITechChangeHandler
    {
        public void TechChanged(World world, int player, PlayerTech before, PlayerTech after)
        {
            Assert.Same(after, match.Research.Tech(player));
            Assert.Same(match.World, world);
            calls.Add($"{name}: player {player}, {string.Join('/', before.Tiers)} -> {string.Join('/', after.Tiers)} in tick {world.Tick}");
        }
    }
}
