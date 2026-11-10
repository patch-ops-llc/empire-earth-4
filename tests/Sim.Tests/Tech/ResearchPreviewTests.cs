using Sim.Tech;

namespace Sim.Tests.Tech;

/// <summary>
/// What the research grid shows before the player pays: the chassis a research unlocks and every
/// accent it adds, removes or changes (design 2.3, "Accents can be displaced").
/// </summary>
public class ResearchPreviewTests
{
    private const int Firepower = 0;
    private const int Protection = 1;
    private const int Mobility = 2;
    private const int Manufacturing = 3;
    private const int Extraction = 5;

    /// <summary>
    /// Design 3.1, example A: "Player 2 takes Manufacturing 2: Auxiliaries unlock, and existing
    /// Peltasts gain Standardized I."
    /// </summary>
    [Fact]
    public void ExampleATheFirstResearchInManufacturing()
    {
        var preview = TechResolver.Preview(DesignData.Data, DesignData.Mix(1, 1, 1, 1, 1, 1), Manufacturing)!;

        Assert.Equal(new ResearchOffer(Manufacturing, 2, ResearchStatus.Available, new ResearchPrice(ResearchBand.Frontier, 100, 100, 900)), preview.Offer);
        Assert.Equal(["auxiliary"], Ids(preview.UnlockedChassis));
        Assert.Equal(
            ["ballista: Standardized 0 -> 1", "hippeus: Standardized 0 -> 1", "hoplite: Standardized 0 -> 1", "peltast: Standardized 0 -> 1"],
            Describe(preview));
        Assert.Equal([1, 1, 1, 1, 1, 1], preview.Before.Tiers);
        Assert.Equal([1, 1, 1, 2, 1, 1], preview.After.Tiers);
        Assert.Equal((1, 1), (preview.Before.Epoch, preview.After.Epoch));
    }

    /// <summary>Design 3.1, example A: "Player 1 takes Protection 2 and can build Legionaries."</summary>
    [Fact]
    public void ExampleATheFirstResearchInProtection()
    {
        var preview = TechResolver.Preview(DesignData.Data, DesignData.Mix(1, 1, 1, 1, 1, 1), Protection)!;

        Assert.Equal(["legionary"], Ids(preview.UnlockedChassis));
        Assert.Equal(
            ["ballista: Hardened 0 -> 1", "hippeus: Hardened 0 -> 1", "hoplite: Hardened 0 -> 1", "peltast: Hardened 0 -> 1"],
            Describe(preview));
    }

    /// <summary>
    /// A third track rising above a chassis that already carries two accents takes a slot from one
    /// of them. The preview names the accent that goes as well as the one that comes.
    /// </summary>
    [Fact]
    public void AResearchThatDisplacesAnAccentNamesBoth()
    {
        // Hoplites carry Overgunned I and Hardened I, with Mobility also one ahead and out of slots.
        var preview = TechResolver.Preview(DesignData.Data, DesignData.Mix(2, 2, 2, 1, 1, 1), Mobility)!;

        var hoplite = DesignData.Chassis("hoplite").Index;
        Assert.Equal([new Accent(Firepower, 1), new Accent(Protection, 1)], preview.Before.Blueprints[hoplite].Accents);
        Assert.Equal([new Accent(Mobility, 2), new Accent(Firepower, 1)], preview.After.Blueprints[hoplite].Accents);
        Assert.Contains(new AccentChange(hoplite, Protection, 1, 0), preview.AccentChanges);
        Assert.Contains(new AccentChange(hoplite, Mobility, 0, 2), preview.AccentChanges);
        Assert.Equal(
            [
                "ballista: Hardened 1 -> 0", "ballista: Rapid 0 -> 2",
                "eques: Rapid 0 -> 1",
                "hippeus: Hardened 1 -> 0", "hippeus: Rapid 0 -> 2",
                "hoplite: Hardened 1 -> 0", "hoplite: Rapid 0 -> 2",
                "legionary: Rapid 0 -> 1",
                "onager: Rapid 0 -> 1",
                "peltast: Hardened 1 -> 0", "peltast: Rapid 0 -> 2",
            ],
            Describe(preview));
        Assert.Equal(["knight"], Ids(preview.UnlockedChassis));
    }

    [Fact]
    public void AResearchThatRaisesALevelIsAChangeOfThatAccent()
    {
        var preview = TechResolver.Preview(DesignData.Data, DesignData.Mix(2, 1, 1, 1, 1, 1), Firepower)!;

        Assert.Equal(
            ["ballista: Overgunned 1 -> 2", "hippeus: Overgunned 1 -> 2", "hoplite: Overgunned 1 -> 2", "onager: Overgunned 0 -> 1", "peltast: Overgunned 1 -> 2"],
            Describe(preview));
        Assert.Empty(preview.UnlockedChassis);
    }

    /// <summary>The window: a track already two ahead of a chassis changes nothing on it by rising further.</summary>
    [Fact]
    public void AResearchChangesOnlyTheChassisThatCanStillUseTheTier()
    {
        var preview = TechResolver.Preview(DesignData.Data, DesignData.Mix(3, 2, 2, 1, 1, 1), Firepower)!;

        // The epoch 1 chassis already have Overgunned II from Firepower 3; the epoch 2 chassis gain it.
        Assert.Equal(["eques: Overgunned 1 -> 2", "legionary: Overgunned 1 -> 2", "onager: Overgunned 1 -> 2"], Describe(preview));
        Assert.Empty(preview.UnlockedChassis);
        Assert.Equal(ResearchStatus.Available, preview.Offer.Status);
    }

    [Fact]
    public void ExtractionChangesNoUnit()
    {
        var preview = TechResolver.Preview(DesignData.Data, DesignData.Mix(2, 2, 2, 2, 2, 1), Extraction)!;

        Assert.Empty(preview.AccentChanges);
        Assert.Empty(preview.UnlockedChassis);
        Assert.True(preview.After.IncomeMultiplier > preview.Before.IncomeMultiplier);
    }

    /// <summary>The third track to reach a tier raises the epoch, which the preview shows through its two sides.</summary>
    [Fact]
    public void TheResearchThatRaisesTheEpochShowsItAndTheNewPrices()
    {
        var preview = TechResolver.Preview(DesignData.Data, DesignData.Mix(2, 2, 1, 1, 1, 1), Mobility)!;

        Assert.Equal((1, 2), (preview.Before.Epoch, preview.After.Epoch));
        Assert.Equal(ResearchBand.Frontier, preview.Before.Research[Manufacturing].Price!.Value.Band);
        Assert.Equal(ResearchBand.CatchUp, preview.After.Research[Manufacturing].Price!.Value.Band);
    }

    [Fact]
    public void ATierThatMayNotBeResearchedYetIsStillPreviewed()
    {
        var preview = TechResolver.Preview(DesignData.Data, DesignData.Mix(3, 1, 1, 1, 1, 1), Firepower)!;

        Assert.Equal(new ResearchOffer(Firepower, 4, ResearchStatus.BeyondReach, null), preview.Offer);
        Assert.Equal(4, preview.After.Tiers[Firepower]);
    }

    [Fact]
    public void ATrackAtItsLastTierHasNoPreview()
    {
        Assert.Null(TechResolver.Preview(DesignData.Data, DesignData.Mix(9, 9, 9, 9, 9, 9), Firepower));
    }

    [Fact]
    public void ThePreviewUsesTheEpochCapOfTheMatch()
    {
        var tiers = DesignData.Mix(4, 4, 4, 1, 1, 1);

        var capped = TechResolver.Preview(DesignData.Data, tiers, Firepower, epochCap: 4)!;
        var open = TechResolver.Preview(DesignData.Data, tiers, Firepower)!;

        Assert.Equal(ResearchStatus.AboveEpochCap, capped.Offer.Status);
        Assert.Equal((4, 4), (capped.Before.EpochCap, capped.After.EpochCap));
        Assert.Equal(ResearchStatus.Available, open.Offer.Status);
    }

    [Fact]
    public void TheTrackMustExist()
    {
        var tiers = DesignData.Mix(1, 1, 1, 1, 1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => TechResolver.Preview(DesignData.Data, tiers, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechResolver.Preview(DesignData.Data, tiers, 6));
        Assert.Throws<ArgumentException>(() => TechResolver.Preview(DesignData.Data, [1, 1], 0));
    }

    private static IEnumerable<string> Ids(IReadOnlyList<int> chassis) => chassis.Select(index => DesignData.Data.Chassis[index].Id);

    private static IEnumerable<string> Describe(ResearchPreview preview) => preview.AccentChanges.Select(change =>
        $"{DesignData.Data.Chassis[change.Chassis].Id}: {DesignData.Data.AccentOf(change.Track)!.Name} {change.LevelBefore} -> {change.LevelAfter}");
}
