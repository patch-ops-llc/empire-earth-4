using Sim.Core;
using Sim.Numerics;
using Sim.Research;
using Sim.Setup;
using Sim.Tech;
using Sim.Tests.Map;
using Sim.Tests.Tech;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>Design 1.3: starting a research, and the checks that are made once, when it starts.</summary>
public class StartingResearchTests
{
    [Fact]
    public void AResearchIsPaidForWhenItStartsAndTakesTheFirstFreeSlot()
    {
        var match = Create();
        var events = match.Events();

        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();

        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
        Assert.Equal((Plenty, Plenty), match.Stock(1));
        var slot = match.Slot(0);
        Assert.True(slot.IsOpen && slot.IsBusy && !slot.IsFree);
        Assert.Equal((Firepower, 2, 100, 100, TicksToTier2), (slot.Track, slot.Tier, slot.Materials, slot.Energy, slot.Ticks));
        Assert.Equal(TicksToTier2 - 1, slot.TicksLeft);
        Assert.Equal(1, match.Research.State.Tier(0, Firepower));
        Assert.Equal([new ResearchEvent(SimEventKind.ResearchStarted, 0, 0, Firepower, 2, 1)], events);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>
    /// The three price bands of design 1.3, each rounded once, half up: tier 4 two ahead costs 338
    /// Materials, and catch-up halves both the price and the time.
    /// </summary>
    [Theory]
    [InlineData(new[] { 2, 2, 2, 1, 1, 1 }, 3, 2, 50, 50, 450)]
    [InlineData(new[] { 2, 2, 2, 1, 1, 1 }, 0, 3, 150, 200, 1200)]
    [InlineData(new[] { 2, 1, 1, 1, 1, 1 }, 0, 3, 225, 300, 1200)]
    [InlineData(new[] { 3, 3, 2, 1, 1, 1 }, 0, 4, 338, 525, 1500)]
    [InlineData(new[] { 4, 4, 4, 3, 1, 1 }, 3, 4, 113, 175, 750)]
    public void ThePriceFollowsTheBandTheTierIsInWhenTheResearchStarts(int[] tiers, int track, int tier, int materials, int energy, int ticks)
    {
        var match = Create();
        match.Research.SetTiers(0, tiers);

        match.OrderResearch(0, track);
        match.Simulation.Tick();

        var slot = match.Slot(0);
        Assert.Equal((track, tier, materials, energy, ticks), (slot.Track, slot.Tier, slot.Materials, slot.Energy, slot.Ticks));
        Assert.Equal((Plenty - materials, Plenty - energy), match.Stock(0));
    }

    [Fact]
    public void TwoSlotsNeverWorkOnTheSameTrack()
    {
        var match = Create();
        Assert.True(match.Research.OpenSlot(0, match.PlaceHolder(0)));
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();

        Assert.Equal(ResearchStartCheck.InProgress, match.Research.CheckStart(0, Firepower));
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.True(match.Slot(0, 1).IsFree);
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));

        // The other player's research of the same track is their own.
        Assert.Equal(ResearchStartCheck.Allowed, match.Research.CheckStart(1, Firepower));
    }

    [Fact]
    public void EachResearchTakesTheLowestFreeSlotAndAPlayerWithNoneFreeMustWait()
    {
        var match = Create();
        match.Research.OpenSlot(0, match.PlaceHolder(0, 3, 3));
        match.Research.OpenSlot(0, match.PlaceHolder(0, 5, 3));

        match.OrderResearch(0, Mobility);
        match.OrderResearch(0, Firepower);
        match.OrderResearch(0, Extraction);
        match.OrderResearch(0, Logistics);
        match.Simulation.Tick();

        Assert.Equal([Mobility, Firepower, Extraction], Enumerable.Range(0, 3).Select(slot => match.Slot(0, slot).Track));
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(ResearchStartCheck.NoFreeSlot, match.Research.CheckStart(0, Logistics));
        Assert.Equal((Plenty - 300, Plenty - 300), match.Stock(0));

        // A slot that was cancelled is the lowest free one again.
        match.OrderCancel(0, Firepower);
        match.OrderResearch(0, Logistics);
        match.Simulation.Tick();
        Assert.Equal(Logistics, match.Slot(0, 1).Track);
    }

    /// <summary>Design 1.3: "A slot holds one research at a time; there is no queue of researches."</summary>
    [Fact]
    public void AnOrderGivenWhileNoSlotIsFreeIsDroppedAndNotKeptForLater()
    {
        var match = Create();
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();

        match.OrderResearch(0, Protection);
        match.Simulation.Run(TicksToTier2 + 50);

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal([2, 1, 1, 1, 1, 1], match.Research.State.Tiers(0).ToArray());
        Assert.True(match.Slot(0).IsFree);
        Assert.Equal((Plenty - 100, Plenty - 100), match.Stock(0));
    }

    [Fact]
    public void APlayerWithoutASlotCannotResearch()
    {
        var match = Create();
        match.Research.ClosePlayerSlot(1);

        Assert.Equal(ResearchStartCheck.NoFreeSlot, match.Research.CheckStart(1, Firepower));
        match.OrderResearch(1, Firepower);
        match.Simulation.Tick();

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal((Plenty, Plenty), match.Stock(1));
    }

    /// <summary>Both resources are needed in full. A research that cannot be paid for takes nothing.</summary>
    [Theory]
    [InlineData(99, 100)]
    [InlineData(100, 99)]
    [InlineData(0, 0)]
    public void AResearchThePlayerCannotPayForDoesNotStart(int materials, int energy)
    {
        var match = MatchSetup.Create(DesignData.Data, TestMaps.Open(8, 8), 1, [PlayerSetup.WithoutStart]);
        match.Research.OpenPlayerSlot(0);
        match.Stockpiles.Add(0, materials, energy);

        Assert.Equal(ResearchStartCheck.TooExpensive, match.Research.CheckStart(0, Firepower));
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.True(match.Slot(0).IsFree);
        Assert.Equal((materials, energy), match.Stock(0));

        match.Stockpiles.Add(0, 100 - materials, 100 - energy);
        Assert.Equal(ResearchStartCheck.Allowed, match.Research.CheckStart(0, Firepower));
    }

    /// <summary>Design 1.4, rule 1: no track may be more than two tiers above the epoch.</summary>
    [Fact]
    public void ATierBeyondReachCannotBeResearchedUntilTheEpochRises()
    {
        var match = Create();
        match.Research.SetTiers(0, DesignData.Mix(3, 1, 1, 1, 1, 1));

        Assert.Equal(ResearchStartCheck.BeyondReach, match.Research.CheckStart(0, Firepower));
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal((Plenty, Plenty), match.Stock(0));

        match.Research.SetTiers(0, DesignData.Mix(3, 2, 2, 1, 1, 1));
        Assert.Equal(ResearchStartCheck.Allowed, match.Research.CheckStart(0, Firepower));
    }

    [Fact]
    public void NoTrackPassesTheEpochCapOfTheMatch()
    {
        var match = Create(data: CappedData);
        match.Research.SetTiers(0, DesignData.Mix(4, 4, 4, 1, 1, 1));

        Assert.Equal(4, match.Research.EpochCap);
        Assert.Equal(ResearchStartCheck.AboveEpochCap, match.Research.CheckStart(0, Firepower));
        Assert.Equal(ResearchStartCheck.Allowed, match.Research.CheckStart(0, Manufacturing));
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.True(match.Slot(0).IsFree);

        // The same tiers with the full game's cap may go on.
        var uncapped = Create();
        uncapped.Research.SetTiers(0, DesignData.Mix(4, 4, 4, 1, 1, 1));
        Assert.Equal(ResearchStartCheck.Allowed, uncapped.Research.CheckStart(0, Firepower));
    }

    [Fact]
    public void ATrackAtItsLastTierHasNothingToResearch()
    {
        var match = Create();
        match.Research.SetTiers(0, DesignData.Mix(9, 9, 9, 1, 1, 1));

        Assert.Equal(ResearchStartCheck.AtTopTier, match.Research.CheckStart(0, Firepower));
        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
    }

    [Theory]
    [InlineData(-1, 0, ResearchStartCheck.UnknownPlayer)]
    [InlineData(2, 0, ResearchStartCheck.UnknownPlayer)]
    [InlineData(0, -1, ResearchStartCheck.UnknownTrack)]
    [InlineData(0, 6, ResearchStartCheck.UnknownTrack)]
    [InlineData(int.MaxValue, int.MinValue, ResearchStartCheck.UnknownPlayer)]
    public void ACommandThatNamesNoPlayerOrNoTrackIsDropped(int player, int track, ResearchStartCheck check)
    {
        var match = Create();
        var before = match.World.ComputeHash();

        Assert.Equal(check, match.Research.CheckStart(player, track));
        match.OrderResearch(player, track);
        match.OrderCancel(player, track);
        match.Simulation.Tick();

        Assert.Equal(2, match.Simulation.DroppedCommandCount);
        var untouched = Create();
        untouched.Simulation.Tick();
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
        Assert.NotEqual(before, match.World.ComputeHash());
    }

    /// <summary>A handler reads the player and the track and nothing else, so what else a command carries does not matter.</summary>
    [Fact]
    public void WhatElseACommandCarriesIsNotRead()
    {
        var match = Create();
        var unit = match.PlaceHolder(1);

        match.Simulation.Commands.Enqueue(0, new Command(CommandType.StartResearch, 0, [unit, new EntityId(99)], Fixed.MaxValue, Fixed.MinValue, unit, Protection));
        match.Simulation.Tick();

        Assert.Equal(Protection, match.Slot(0).Track);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>What the grid shows as the price is what the research then costs.</summary>
    [Fact]
    public void TheOfferOfThePlayersTableIsThePriceTheResearchIsStartedAt()
    {
        var match = Create();
        match.Research.SetTiers(1, DesignData.Mix(3, 3, 2, 1, 1, 1));
        var offer = match.Research.Tech(1).Research[Firepower];

        match.OrderResearch(1, Firepower);
        match.Simulation.Tick();

        var slot = match.Slot(1);
        Assert.Equal(new ResearchPrice(ResearchBand.Ahead, 338, 525, 1500), offer.Price);
        Assert.Equal((offer.Tier, offer.Price!.Value.Materials, offer.Price.Value.Energy, offer.Price.Value.Ticks), (slot.Tier, slot.Materials, slot.Energy, slot.Ticks));
    }
}
