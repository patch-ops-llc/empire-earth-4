using Sim.Core;
using Sim.Economy;
using Sim.Research;
using Sim.Setup;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Research.ResearchMatches;

namespace Sim.Tests.Research;

/// <summary>What the match setup gives each player to research with, and how a fixture sets a mix.</summary>
public class ResearchSetupTests
{
    private static readonly Lazy<Match> SampleMatch = new(() =>
    {
        var data = TestMatches.Data;
        return MatchSetup.Create(data, data.Maps[0], 1, [new(1), PlayerSetup.WithoutStart, new(0)]);
    });

    /// <summary>
    /// Design 5.2: every player starts with 400 Materials and 100 Energy and all tracks at the
    /// start epoch. The slot of their own stands in for the Capitol's until the setup places one.
    /// </summary>
    [Fact]
    public void APlayerWithAStartHasTheStartingStockpileAndTheFirstSlot()
    {
        var match = SampleMatch.Value;

        foreach (var player in new[] { 0, 2 })
        {
            Assert.Equal((400, 100), match.Stock(player));
            Assert.True(match.Slot(player).IsFree);
            Assert.False(match.Slot(player, 1).IsOpen || match.Slot(player, 2).IsOpen);
            Assert.Equal(ResearchStartCheck.Allowed, match.Research.CheckStart(player, 0));
        }
    }

    [Fact]
    public void APlayerWithoutAStartIsGivenNothing()
    {
        var match = SampleMatch.Value;

        Assert.Equal((0, 0), match.Stock(1));
        Assert.All(Enumerable.Range(0, ResearchState.SlotsPerPlayer), slot => Assert.False(match.Slot(1, slot).IsOpen));
        Assert.Equal(ResearchStartCheck.NoFreeSlot, match.Research.CheckStart(1, 0));
    }

    [Fact]
    public void EveryTrackStartsAtTheStartEpochAndTheCapIsTheDatas()
    {
        var match = SampleMatch.Value;
        var data = match.Data;

        Assert.Equal(data.Rules.Match.EpochCap, match.Research.EpochCap);
        for (var player = 0; player < 3; player++)
        {
            Assert.All(match.Research.State.Tiers(player).ToArray(), tier => Assert.Equal(data.Rules.Match.StartEpoch, tier));
            var tech = match.Research.Tech(player);
            Assert.Equal((data.Rules.Match.StartEpoch, data.Rules.Match.EpochCap), (tech.Epoch, tech.EpochCap));
            Assert.Equal(data.Chassis.Count, tech.Blueprints.Count);
        }
    }

    /// <summary>
    /// With what a player starts with and no income, exactly one research can be paid for: tier 2
    /// costs 100 of each and the player has 100 Energy.
    /// </summary>
    [Fact]
    public void TheStartingStockpilePaysForOneResearch()
    {
        var data = TestMatches.Data;
        var match = MatchSetup.Create(data, data.Maps[0], 1, [new(0), new(1)]);

        match.OrderResearch(0, 0);
        match.Simulation.Tick();
        match.OrderResearch(0, 1);
        match.Simulation.Tick();

        Assert.Equal((300, 0), match.Stock(0));
        Assert.Equal(ResearchStartCheck.NoFreeSlot, match.Research.CheckStart(0, 1));
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal((400, 100), match.Stock(1));
    }

    /// <summary>
    /// The stores research needs are in the world, under their names, and are the ones the match
    /// hands out. Which other stores a match has is not this test's business.
    /// </summary>
    [Fact]
    public void TheMatchRegistersTheStoresOfResearch()
    {
        var match = SampleMatch.Value;

        Assert.Same(match.Research.State, Assert.Single(match.World.Stores, store => store.Name == ResearchState.StoreName));
        Assert.Same(match.Stockpiles, Assert.Single(match.World.Stores, store => store.Name == Stockpiles.StoreName));
        Assert.Equal(("research", "stockpiles"), (ResearchState.StoreName, Stockpiles.StoreName));
        Assert.Equal((2, 3), ((int)CommandType.CancelResearch, (int)CommandType.StartResearch));
    }

    /// <summary>A fixture gives a player a mix, as the battle harness will for the armies of the design's examples.</summary>
    [Fact]
    public void AFixtureSetsAPlayersTiersBeforeTheMatchRuns()
    {
        var match = Create();
        var mix = DesignData.Mix(1, 1, 2, 4, 4, 2);

        match.Research.SetTiers(1, mix);

        Assert.Equal(mix, match.Research.State.Tiers(1).ToArray());
        Assert.Equal((2, 100), (match.Research.Tech(1).Epoch, match.Research.Tech(1).SupplyCap));
        Assert.Equal(1, match.Research.Tech(0).Epoch);
        var auxiliary = match.Research.Tech(1).Blueprints[DesignData.Chassis("auxiliary").Index];
        Assert.Equal((28, 0, 2), (auxiliary.Materials, auxiliary.Energy, auxiliary.BatchSize));

        // The table already agrees with the tiers, so refreshing changes nothing.
        var table = match.Research.Tech(1);
        match.RefreshDerivedState();
        Assert.Same(table, match.Research.Tech(1));
    }

    [Fact]
    public void TiersThatCannotBeAreRefused()
    {
        var match = Create();

        Assert.Throws<ArgumentException>(() => match.Research.SetTiers(0, [1, 1, 1, 1, 1]));
        Assert.Throws<ArgumentException>(() => match.Research.SetTiers(0, DesignData.Mix(0, 1, 1, 1, 1, 1)));
        Assert.Throws<ArgumentException>(() => match.Research.SetTiers(0, DesignData.Mix(10, 1, 1, 1, 1, 1)));
        Assert.Throws<ArgumentNullException>(() => match.Research.SetTiers(0, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => match.Research.SetTiers(2, DesignData.Mix(1, 1, 1, 1, 1, 1)));
        Assert.Equal([1, 1, 1, 1, 1, 1], match.Research.State.Tiers(0).ToArray());

        match.OrderResearch(0, Firepower);
        match.Simulation.Tick();
        Assert.Throws<InvalidOperationException>(() => match.Research.SetTiers(0, DesignData.Mix(2, 2, 2, 1, 1, 1)));
    }

    [Fact]
    public void TheStoresOfAMatchMustBelongTogether()
    {
        var match = Create();
        var world = new World(1);

        Assert.Throws<ArgumentException>(() => new MatchResearch(match.Data, world, new ResearchState(2, 5, 9, 1), new Stockpiles(2), 9));
        Assert.Throws<ArgumentException>(() => new MatchResearch(match.Data, world, new ResearchState(2, 6, 9, 1), new Stockpiles(3), 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchResearch(match.Data, world, new ResearchState(2, 6, 9, 1), new Stockpiles(2), 10));
    }
}
