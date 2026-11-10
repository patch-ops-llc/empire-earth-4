using Sim.Tech;
using Sim.Tests.Data;

namespace Sim.Tests.Tech;

/// <summary>Research prices, the reach rule and the epoch cap (design 1.3 and 1.4).</summary>
public class ResearchTests
{
    private const int Firepower = 0;
    private const int Protection = 1;
    private const int Mobility = 2;
    private const int Extraction = 5;

    private const int NoCap = 9;

    /// <summary>Design 1.3: "Tier 4 two ahead costs 338 Materials; as catch-up it costs 113."</summary>
    [Fact]
    public void AMultipliedPriceIsRoundedHalfUpToAWholeResource()
    {
        var ahead = Offer(DesignData.Mix(3, 2, 2, 2, 1, 1), Firepower);
        var catchUp = Offer(DesignData.Mix(3, 4, 4, 4, 1, 1), Firepower);

        Assert.Equal(new ResearchPrice(ResearchBand.Ahead, 338, 525, 1500), ahead.Price);
        Assert.Equal(new ResearchPrice(ResearchBand.CatchUp, 113, 175, 750), catchUp.Price);
    }

    /// <summary>Design 3.1 example D: the tier each player bought last, at the price the table states.</summary>
    [Fact]
    public void ExampleDPaysTheStatedPricesForItsKeyTiers()
    {
        var attacker = Offer(DesignData.Mix(6, 4, 6, 6, 4, 5), Firepower);
        var defender = Offer(DesignData.Mix(4, 6, 4, 5, 5, 5), Protection);

        Assert.Equal(new ResearchPrice(ResearchBand.Frontier, 750, 1300, 2400), attacker.Price);
        Assert.Equal(new ResearchPrice(ResearchBand.Ahead, 1125, 1950, 2400), defender.Price);
    }

    /// <summary>The price table of design 1.3 at frontier price, tier by tier.</summary>
    [Theory]
    [InlineData(2, 100, 100, 45)]
    [InlineData(3, 150, 200, 60)]
    [InlineData(4, 225, 350, 75)]
    [InlineData(5, 350, 550, 90)]
    [InlineData(6, 500, 850, 105)]
    [InlineData(7, 750, 1300, 120)]
    [InlineData(8, 1100, 1900, 135)]
    [InlineData(9, 1600, 2800, 150)]
    public void TheTierOneAboveTheEpochCostsTheTablePrice(int tier, int materials, int energy, int seconds)
    {
        var all = tier - 1;

        var offer = Offer(DesignData.Mix(all, all, all, all, all, all), Mobility);

        Assert.Equal((Mobility, tier, ResearchStatus.Available), (offer.Track, offer.Tier, offer.Status));
        Assert.True(offer.IsAvailable);
        Assert.Equal(new ResearchPrice(ResearchBand.Frontier, materials, energy, seconds * 20), offer.Price);
    }

    /// <summary>Every band for one tier: epoch 5 and above is catch-up, epoch 4 frontier, epoch 3 ahead, epoch 2 out of reach.</summary>
    [Theory]
    [InlineData(6, ResearchBand.CatchUp, 175, 275, 900)]
    [InlineData(5, ResearchBand.CatchUp, 175, 275, 900)]
    [InlineData(4, ResearchBand.Frontier, 350, 550, 1800)]
    [InlineData(3, ResearchBand.Ahead, 525, 825, 1800)]
    public void TheBandDependsOnWhereTheTierStandsAgainstTheEpoch(int others, ResearchBand band, int materials, int energy, int ticks)
    {
        var tiers = DesignData.Mix(4, others, others, others, 1, 1);
        Assert.Equal(others, TechRules.Epoch(DesignData.Data, tiers));

        Assert.Equal(new ResearchPrice(band, materials, energy, ticks), Offer(tiers, Firepower).Price);
    }

    [Fact]
    public void NoTrackMayGoMoreThanTwoTiersAboveTheEpoch()
    {
        var tiers = DesignData.Mix(3, 1, 1, 1, 1, 1);

        var offer = Offer(tiers, Firepower);

        Assert.Equal(new ResearchOffer(Firepower, 4, ResearchStatus.BeyondReach, null), offer);
        Assert.False(offer.IsAvailable);
    }

    /// <summary>Design 1.4, rule 1: "bring a third track up so the epoch rises".</summary>
    [Fact]
    public void RaisingTheEpochMovesTheReachLimit()
    {
        Assert.Equal(ResearchStatus.BeyondReach, Offer(DesignData.Mix(3, 2, 1, 1, 1, 1), Firepower).Status);
        Assert.Equal(ResearchStatus.Available, Offer(DesignData.Mix(3, 2, 2, 1, 1, 1), Firepower).Status);
    }

    [Fact]
    public void ATrackAtTheTopTierHasNothingLeftToResearch()
    {
        var offer = Offer(DesignData.Mix(9, 9, 9, 9, 9, 9), Extraction);

        Assert.Equal(new ResearchOffer(Extraction, 10, ResearchStatus.AtTopTier, null), offer);
    }

    /// <summary>Design 5.2: the epoch cap is a match setting, and no track passes it.</summary>
    [Fact]
    public void NoTrackPassesTheEpochCapOfTheMatch()
    {
        var tiers = DesignData.Mix(4, 4, 4, 3, 3, 3);

        Assert.Equal(new ResearchOffer(Firepower, 5, ResearchStatus.AboveEpochCap, null), TechRules.Research(DesignData.Data, tiers, Firepower, epochCap: 4));
        Assert.Equal(ResearchBand.CatchUp, TechRules.Research(DesignData.Data, tiers, Extraction, epochCap: 4).Price!.Value.Band);
        Assert.Equal(ResearchBand.Frontier, TechRules.Research(DesignData.Data, tiers, Firepower, epochCap: 5).Price!.Value.Band);
    }

    [Fact]
    public void TheCapIsNamedBeforeTheReachWhenBothStandInTheWay()
    {
        var tiers = DesignData.Mix(4, 2, 2, 2, 2, 2);

        Assert.Equal(ResearchStatus.AboveEpochCap, TechRules.Research(DesignData.Data, tiers, Firepower, epochCap: 4).Status);
        Assert.Equal(ResearchStatus.BeyondReach, TechRules.Research(DesignData.Data, tiers, Firepower, epochCap: 5).Status);
    }

    [Fact]
    public void TheReachIsData()
    {
        var files = DesignData.Files().Edit(DesignData.RulesPath, "\"reach\": 2", "\"reach\": 3");
        var data = DataAssert.Valid(files);

        var offer = TechRules.Research(data, DesignData.Mix(3, 1, 1, 1, 1, 1), Firepower, NoCap);

        // Three above the epoch is priced as ahead: the price has three bands however far the reach goes.
        Assert.Equal(new ResearchPrice(ResearchBand.Ahead, 338, 525, 1500), offer.Price);
        Assert.Equal(ResearchStatus.BeyondReach, TechRules.Research(data, DesignData.Mix(4, 1, 1, 1, 1, 1), Firepower, NoCap).Status);
    }

    [Fact]
    public void ThePriceMultipliersAreData()
    {
        var files = DesignData.Files().Edit(DesignData.RulesPath, "\"catch_up\": { \"cost\": \"0.5\", \"time\": \"0.5\" }", "\"catch_up\": { \"cost\": \"0.33\", \"time\": \"0.25\" }");
        var data = DataAssert.Valid(files);

        var offer = TechRules.Research(data, DesignData.Mix(1, 2, 2, 2, 1, 1), Firepower, NoCap);

        Assert.Equal(new ResearchPrice(ResearchBand.CatchUp, 33, 33, 225), offer.Price);
    }

    [Fact]
    public void AResearchTimeIsRoundedHalfUpToATickAndIsNeverNothing()
    {
        var files = DesignData.Files();
        files.Edit(DesignData.ResearchPath, "\"seconds\": 45", "\"seconds\": \"0.05\"");
        files.Edit(DesignData.ResearchPath, "\"seconds\": 60", "\"seconds\": \"0.25\"");
        files.Edit(DesignData.RulesPath, "\"catch_up\": { \"cost\": \"0.5\", \"time\": \"0.5\" }", "\"catch_up\": { \"cost\": \"0.5\", \"time\": \"0.3\" }");
        var data = DataAssert.Valid(files);

        // 1 tick x 0.3 rounds to no ticks, and 5 ticks x 0.3 is 1.5 ticks.
        var lessThanHalfATick = TechRules.Research(data, DesignData.Mix(1, 2, 2, 2, 1, 1), Firepower, NoCap);
        var oneAndAHalfTicks = TechRules.Research(data, DesignData.Mix(2, 3, 3, 3, 1, 1), Firepower, NoCap);

        Assert.Equal(1, lessThanHalfATick.Price!.Value.Ticks);
        Assert.Equal(2, oneAndAHalfTicks.Price!.Value.Ticks);
    }

    /// <summary>Design 1.5: every track to tier 9 at table price is 4,775 Materials, 8,050 Energy and 780 seconds.</summary>
    [Fact]
    public void OneTrackToTheTopAtFrontierPriceCostsTheTotalTheDesignStates()
    {
        var (materials, energy, ticks) = (0, 0, 0);
        for (var tier = 1; tier < 9; tier++)
        {
            var price = Offer(DesignData.Mix(tier, tier, tier, tier, tier, tier), Firepower).Price!.Value;
            (materials, energy, ticks) = (materials + price.Materials, energy + price.Energy, ticks + price.Ticks);
        }

        Assert.Equal((4_775, 8_050, 780 * 20), (materials, energy, ticks));
    }

    [Fact]
    public void TheTrackAndTheCapMustExist()
    {
        var data = DesignData.Data;
        var tiers = DesignData.Mix(1, 1, 1, 1, 1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => TechRules.Research(data, tiers, -1, NoCap));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechRules.Research(data, tiers, 6, NoCap));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechRules.Research(data, tiers, Firepower, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechRules.Research(data, tiers, Firepower, 10));
        Assert.Throws<ArgumentException>(() => TechRules.Research(data, [1, 1, 1], Firepower, NoCap));
    }

    private static ResearchOffer Offer(int[] tiers, int track) => TechRules.Research(DesignData.Data, tiers, track, NoCap);
}
