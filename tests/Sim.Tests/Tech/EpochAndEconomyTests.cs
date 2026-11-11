using Sim.Numerics;
using Sim.Tech;
using Sim.Tests.Data;

namespace Sim.Tests.Tech;

/// <summary>The derived epoch (design 1.4), the supply cap (4.1) and the income multiplier (4.2).</summary>
public class EpochAndEconomyTests
{
    /// <summary>Every tier mix the design works an example with, and the epoch it states for it.</summary>
    [Theory]
    [InlineData(5, 5, 3, 3, 2, 2, 3)] // Design 1.4, the definition's own example.
    [InlineData(2, 2, 3, 5, 5, 3, 3)] // Example B, swarm player.
    [InlineData(5, 5, 3, 2, 2, 3, 3)] // Example B, elite player.
    [InlineData(1, 1, 2, 4, 4, 2, 2)] // Example B at first-playable tiers, swarm player.
    [InlineData(4, 4, 2, 1, 1, 2, 2)] // Example B at first-playable tiers, elite player.
    [InlineData(6, 3, 7, 4, 3, 5, 5)] // Example C.
    [InlineData(7, 4, 6, 6, 4, 5, 6)] // Example D, attacker.
    [InlineData(4, 7, 4, 5, 5, 5, 5)] // Example D, defender.
    [InlineData(7, 6, 6, 9, 9, 8, 8)] // Example E, swarm player.
    [InlineData(9, 9, 8, 6, 6, 7, 8)] // Example E, elite player.
    public void TheEpochIsTheHighestTierThatThreeTracksHaveReached(int fp, int pr, int mo, int mf, int lg, int ex, int epoch)
    {
        Assert.Equal(epoch, TechRules.Epoch(DesignData.Data, DesignData.Mix(fp, pr, mo, mf, lg, ex)));
    }

    [Theory]
    [InlineData(1, 1, 1, 1, 1, 1, 1)]
    [InlineData(9, 9, 1, 1, 1, 1, 1)] // Two spikes do not raise the epoch.
    [InlineData(9, 9, 2, 1, 1, 1, 2)] // The third track does.
    [InlineData(1, 1, 1, 4, 4, 4, 4)] // Which tracks lead does not matter.
    [InlineData(9, 9, 9, 9, 9, 9, 9)]
    public void TwoTracksAheadNeverRaiseTheEpoch(int fp, int pr, int mo, int mf, int lg, int ex, int epoch)
    {
        Assert.Equal(epoch, TechRules.Epoch(DesignData.Data, DesignData.Mix(fp, pr, mo, mf, lg, ex)));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 5)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    public void TheNumberOfTracksNeededIsData(int tracksNeeded, int epoch)
    {
        var files = DesignData.Files().Edit(DesignData.RulesPath, "\"tracks_needed\": 3", $"\"tracks_needed\": {tracksNeeded}");
        var data = DataAssert.Valid(files);

        Assert.Equal(epoch, TechRules.Epoch(data, DesignData.Mix(5, 5, 3, 3, 2, 2)));
    }

    /// <summary>Design 2.6 and 3.1: 40 + 15 per Logistics tier, from 55 to 175.</summary>
    [Theory]
    [InlineData(2, 2, 3, 5, 5, 3, 115)] // Example B, swarm player.
    [InlineData(5, 5, 3, 2, 2, 3, 70)] // Example B, elite player.
    [InlineData(1, 1, 2, 4, 4, 2, 100)] // Example B at first-playable tiers, swarm player.
    [InlineData(4, 4, 2, 1, 1, 2, 55)] // Example B at first-playable tiers, elite player.
    [InlineData(7, 6, 6, 9, 9, 8, 175)] // Example E, swarm player.
    [InlineData(9, 9, 8, 6, 6, 7, 130)] // Example E, elite player.
    public void TheSupplyCapFollowsTheLogisticsTier(int fp, int pr, int mo, int mf, int lg, int ex, int supplyCap)
    {
        Assert.Equal(supplyCap, TechRules.SupplyCap(DesignData.Data, DesignData.Mix(fp, pr, mo, mf, lg, ex)));
    }

    [Fact]
    public void TheSupplyCapNumbersAreData()
    {
        var files = DesignData.Files().Edit(DesignData.RulesPath, "\"base\": 40, \"per_logistics_tier\": 15", "\"base\": 30, \"per_logistics_tier\": 20");
        var data = DataAssert.Valid(files);

        Assert.Equal(30 + (20 * 4), TechRules.SupplyCap(data, DesignData.Mix(1, 1, 1, 1, 4, 1)));
    }

    /// <summary>Design 4.2: worker income times 1.12 per tier of Extraction, kept exact.</summary>
    [Fact]
    public void IncomeIsMultipliedOncePerExtractionTierAboveTheFirst()
    {
        Fraction Multiplier(int extraction) => TechRules.IncomeMultiplier(DesignData.Data, DesignData.Mix(9, 9, 9, 9, 9, extraction));

        Assert.Equal(Fraction.One, Multiplier(1));
        Assert.Equal(new Fraction(28, 25), Multiplier(2));
        Assert.Equal(new Fraction(28 * 28, 25 * 25), Multiplier(3));
        Assert.Equal(new Fraction(377_801_998_336, 152_587_890_625), Multiplier(9));

        // "Tier 9 is x2.5" in the design's table is this value to one decimal.
        Assert.Equal(25, Multiplier(9).RoundHalfUp(10));
        Assert.Equal(2_476, Multiplier(9).RoundHalfUp(1_000));
    }

    [Fact]
    public void OnlyTheExtractionTierChangesIncomeAndOnlyTheLogisticsTierChangesSupply()
    {
        var data = DesignData.Data;
        var low = DesignData.Mix(1, 1, 1, 1, 3, 3);
        var high = DesignData.Mix(9, 9, 9, 9, 3, 3);

        Assert.Equal(TechRules.IncomeMultiplier(data, low), TechRules.IncomeMultiplier(data, high));
        Assert.Equal(TechRules.SupplyCap(data, low), TechRules.SupplyCap(data, high));
    }

    /// <summary>The rules find Logistics and Extraction by ID, not by their place in the track list.</summary>
    [Fact]
    public void TheRulesFollowTheirTracksWhenTheTrackOrderChanges()
    {
        var files = DesignData.Files();
        files.Edit(DesignData.TracksPath, "\"id\": \"logistics\", \"name\": \"Logistics\"", "\"id\": \"extraction\", \"name\": \"Logistics\"");
        files.Edit(DesignData.TracksPath, "\"id\": \"extraction\", \"name\": \"Extraction\"", "\"id\": \"logistics\", \"name\": \"Extraction\"");
        files.Edit(DesignData.AccentsPath, "\"logistics\": {", "\"extraction\": {");
        var data = DataAssert.Valid(files);

        // Extraction is now the fifth track and Logistics the sixth.
        int[] tiers = [1, 1, 1, 1, 2, 4];

        Assert.Equal(40 + (15 * 4), TechRules.SupplyCap(data, tiers));
        Assert.Equal(new Fraction(28, 25), TechRules.IncomeMultiplier(data, tiers));
    }

    [Fact]
    public void TiersMustBeOnePerTrackAndInsideTheTierRange()
    {
        var data = DesignData.Data;

        Assert.Throws<ArgumentException>(() => TechRules.Epoch(data, [1, 1, 1, 1, 1]));
        Assert.Throws<ArgumentException>(() => TechRules.Epoch(data, [1, 1, 1, 1, 1, 1, 1]));
        Assert.Throws<ArgumentException>(() => TechRules.SupplyCap(data, [1, 1, 1, 1, 0, 1]));
        Assert.Throws<ArgumentException>(() => TechRules.IncomeMultiplier(data, [1, 1, 1, 1, 1, 10]));
        Assert.Throws<ArgumentNullException>(() => TechRules.Epoch(data, null!));
    }
}
