using Sim.Core;
using Sim.Tech;

namespace Ai.Tests;

/// <summary>
/// Research by personality (design 5.5, item 2): the highest weight it may start and can pay
/// for, a half-price catch-up tier before any other, and the army never starves it.
/// </summary>
public class AiResearchTests
{
    /// <summary>Firepower is wanted most and Extraction least.</summary>
    private static readonly int[] Falling = [6, 5, 4, 3, 2, 1];

    /// <summary>Extraction is wanted most and Firepower least.</summary>
    private static readonly int[] Rising = [1, 2, 3, 4, 5, 6];

    [Fact]
    public void ItStartsTheResearchWithTheHighestWeight()
    {
        Assert.Equal([0], Started(new AiFixture(), Falling));
        Assert.Equal([5], Started(new AiFixture(), Rising));
        Assert.Equal([AiFixture.Track("mobility")], Started(new AiFixture(), [1, 1, 9, 1, 1, 1]));
    }

    [Fact]
    public void TracksOfOneWeightGoInTheOrderOfTheData()
    {
        Assert.Equal([0], Started(new AiFixture(), [3, 3, 3, 3, 3, 3]));
        Assert.Equal([1], Started(new AiFixture(), [1, 3, 3, 1, 3, 1]));
    }

    [Fact]
    public void ItStartsOneResearchForEverySlotThatIsFree()
    {
        var fixture = new AiFixture();
        Assert.Equal(1, fixture.Sight.FreeResearchSlots);

        fixture.Place("academy");
        Assert.Equal(2, fixture.Sight.FreeResearchSlots);

        Assert.Equal([5, 4], Started(fixture, Rising));
    }

    /// <summary>With three tracks at tier 2 the epoch is 2, and tier 2 of the others is at half price.</summary>
    [Fact]
    public void ACatchUpTierGoesBeforeAHigherWeight()
    {
        var fixture = new AiFixture();
        fixture.Match.Research.SetTiers(AiFixture.Player, [2, 2, 2, 1, 1, 1]);
        var offers = fixture.Sight.Tech.Research;
        Assert.Equal(ResearchBand.Frontier, offers[0].Price!.Value.Band);
        Assert.Equal(ResearchBand.CatchUp, offers[3].Price!.Value.Band);

        Assert.Equal([3], Started(fixture, Falling));
        Assert.Equal([5], Started(fixture, Rising));
    }

    /// <summary>
    /// With two tracks at tier 2 the epoch is still 1: their tier 3 is ahead of it and costs more
    /// than tier 2 of the others. It does not save for it while it can start another.
    /// </summary>
    [Fact]
    public void ItStartsTheBestItCanPayForNow()
    {
        var fixture = new AiFixture();
        fixture.Match.Research.SetTiers(AiFixture.Player, [2, 2, 1, 1, 1, 1]);
        var ahead = fixture.Sight.Tech.Research[0].Price!.Value;
        var frontier = fixture.Sight.Tech.Research[2].Price!.Value;
        Assert.Equal(ResearchBand.Ahead, ahead.Band);
        Assert.True(ahead.Materials > frontier.Materials && ahead.Energy > frontier.Energy);

        Assert.Equal([2], Started(fixture, Falling, frontier.Materials, frontier.Energy));
        Assert.Equal([0], Started(fixture, Falling, ahead.Materials, ahead.Energy));
    }

    [Fact]
    public void WhenItCanPayForNoneItHoldsThePriceOfTheCheapestFromEverythingElse()
    {
        var fixture = new AiFixture();
        var price = fixture.Sight.Tech.Research[0].Price!.Value;
        var research = new AiResearch(fixture.Sight, AiFixture.Personality(weights: Falling));

        // Everything but one Energy of the price.
        var budget = fixture.With(price.Materials + 70, price.Energy - 1);
        research.Think(budget, fixture.Commands);

        Assert.Empty(fixture.Commands);
        Assert.True(budget.Covers(70, 0));
        Assert.False(budget.Covers(71, 0));
        Assert.False(budget.Covers(0, 1));
    }

    [Fact]
    public void WhenEverySlotIsBusyNothingIsHeld()
    {
        var fixture = new AiFixture();
        var research = new AiResearch(fixture.Sight, AiFixture.Personality(weights: Falling));
        research.Think(fixture.Rich(), fixture.Commands);
        fixture.Carry();
        Assert.Equal(0, fixture.Sight.FreeResearchSlots);

        var budget = fixture.With(10, 10);
        research.Think(budget, fixture.Commands);

        Assert.Empty(fixture.Commands);
        Assert.True(budget.Covers(10, 10));
    }

    [Fact]
    public void WhatIsInResearchIsNotStartedAgain()
    {
        var fixture = new AiFixture();
        fixture.Place("academy");
        var research = new AiResearch(fixture.Sight, AiFixture.Personality(weights: Falling));

        research.Think(fixture.With(100, 100), fixture.Commands);
        Assert.Equal(0, Assert.Single(fixture.Commands).ContentId);
        fixture.Carry();
        Assert.True(fixture.Sight.IsResearching(0));

        research.Think(fixture.Rich(), fixture.Commands);

        Assert.Equal(1, Assert.Single(fixture.Commands).ContentId);
        Assert.DoesNotContain(research.Offers, offer => offer.Track == 0);
    }

    [Fact]
    public void ItLacksASlotWhenEverySlotIsBusyAndItCouldPayForTheNextResearch()
    {
        var fixture = new AiFixture();
        var research = new AiResearch(fixture.Sight, AiFixture.Personality(weights: Falling));

        research.Think(fixture.Rich(), fixture.Commands);
        Assert.False(research.LacksASlot);
        fixture.Carry();

        var next = fixture.Sight.Tech.Research[1].Price!.Value;
        research.Think(fixture.With(next.Materials, next.Energy), fixture.Commands);
        Assert.True(research.LacksASlot);

        research.Think(fixture.With(next.Materials - 1, next.Energy), fixture.Commands);
        Assert.False(research.LacksASlot);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public void TheWorkersEarnForOneResearchForEverySlot()
    {
        var fixture = new AiFixture();
        var price = fixture.Sight.Tech.Research[0].Price!.Value;
        var research = new AiResearch(fixture.Sight, AiFixture.Personality(weights: Falling));

        var budget = fixture.With(0, 0);
        research.Think(budget, fixture.Commands);
        Assert.Equal(price.Materials, budget.MissingMaterials);
        Assert.Equal(price.Energy, budget.MissingEnergy);

        fixture.Place("academy");
        budget = fixture.With(0, 0);
        research.Think(budget, fixture.Commands);
        Assert.Equal(2 * price.Materials, budget.MissingMaterials);
        Assert.Equal(2 * price.Energy, budget.MissingEnergy);
    }

    /// <summary>The tracks of the researches one round starts, in the order of its orders.</summary>
    private static int[] Started(AiFixture fixture, int[] weights, int materials = AiFixture.Plenty, int energy = AiFixture.Plenty)
    {
        var research = new AiResearch(fixture.Sight, AiFixture.Personality(weights: weights));
        fixture.Commands.Clear();
        research.Think(fixture.With(materials, energy), fixture.Commands);

        Assert.All(fixture.Commands, command =>
        {
            Assert.Equal(CommandType.StartResearch, command.Type);
            Assert.Equal(AiFixture.Player, command.Player);
        });
        return [.. fixture.Commands.Select(command => command.ContentId)];
    }
}
