using Sim.Data;
using Sim.Tech;

namespace Sim.Tests.Setup;

/// <summary>
/// The structure files of the real <c>game/data</c> against the two tables of design 2.5, for the
/// eight structures of the first playable.
/// </summary>
public class GameDataStructureTests
{
    private static readonly Lazy<GameData> Loaded = new(() =>
    {
        var result = DataLoader.Load(GameDataOnDisk.Files());
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        return result.Data;
    });

    private static GameData Data => Loaded.Value;

    [Fact]
    public void TheEightStructuresOfTheFirstPlayableHaveAFile()
    {
        Assert.Equal(
            ["academy", "barracks", "capitol", "generator", "outpost", "tower", "wall", "works"],
            Data.Structures.Select(structure => structure.Id).Order(StringComparer.Ordinal));
    }

    /// <summary>The first table of design 2.5: track shown, footprint, price and hit points at tier 1.</summary>
    [Theory]
    [InlineData("capitol", "logistics", 4, 400, 0, 2000)]
    [InlineData("outpost", null, 3, 250, 0, 1200)]
    [InlineData("generator", "extraction", 2, 100, 0, 500)]
    [InlineData("barracks", "manufacturing", 3, 150, 0, 1000)]
    [InlineData("works", "mobility", 3, 200, 50, 1000)]
    [InlineData("academy", null, 2, 200, 100, 800)]
    [InlineData("wall", "protection", 1, 20, 0, 400)]
    [InlineData("tower", "protection", 2, 120, 30, 600)]
    public void AStructureHasTheTrackFootprintPriceAndHitPointsOfTheDesign(
        string id, string? track, int footprint, int materials, int energy, int hitPoints)
    {
        var structure = Structure(id);

        Assert.Equal(track, structure.Track is { } index ? Data.Tracks[index].Id : null);
        Assert.Equal((footprint, materials, energy), (structure.Footprint, structure.Materials, structure.Energy));
        Assert.Equal(DataNumber.FromInteger(hitPoints), structure.HitPoints);

        // "A structure with no tier in its name is available from the start."
        Assert.Equal(1, structure.UnlockTier);
        var blueprint = TechResolver.Resolve(Data, [1, 1, 1, 1, 1, 1]).StructureOf(structure.Index)!;
        Assert.True(blueprint.Unlocked);
        Assert.Equal(hitPoints * 1000L, blueprint.HitPoints);
    }

    /// <summary>The second table of design 2.5: build time and sight.</summary>
    [Theory]
    [InlineData("capitol", 90, 10)]
    [InlineData("outpost", 45, 8)]
    [InlineData("generator", 25, 6)]
    [InlineData("barracks", 35, 6)]
    [InlineData("works", 45, 6)]
    [InlineData("academy", 45, 6)]
    [InlineData("wall", 5, 2)]
    [InlineData("tower", 30, 10)]
    public void AStructureHasTheBuildTimeAndSightOfTheDesign(string id, int buildSeconds, int sight)
    {
        var structure = Structure(id);

        Assert.Equal((buildSeconds * SimInfo.TicksPerSecond, sight), (structure.BuildTicks, structure.Sight));
    }

    /// <summary>The "Function" column of the first table.</summary>
    [Fact]
    public void EveryStructureDoesWhatTheDesignSaysAndNothingElse()
    {
        var capitol = Structure("capitol");
        Assert.True(capitol.TrainsWorkers);
        Assert.Empty(capitol.Trains);
        Assert.Equal((16, 1), (capitol.TerritoryRadius, capitol.ResearchSlots));

        var outpost = Structure("outpost");
        Assert.Equal((12, 0), (outpost.TerritoryRadius, outpost.ResearchSlots));

        Assert.True(Structure("generator").IsEnergyWorkSite);
        Assert.Equal(4, Data.Resources.Energy.WorkersPerGenerator);

        Assert.Equal(["line_infantry", "guard"], Trained("barracks"));
        Assert.Equal(["cavalry_armor", "artillery", "support"], Trained("works"));

        var academy = Structure("academy");
        Assert.Equal((1, 2), (academy.ResearchSlots, academy.MaxPerPlayer));

        // Gate: 40 / 0 and 10 s per tile, with the wall's hit points and sight.
        Assert.Equal(new WallDef(new GateDef(Materials: 40, Energy: 0, BuildTicks: 10 * SimInfo.TicksPerSecond)), Structure("wall").Wall);

        foreach (var structure in Data.Structures)
        {
            Assert.Equal(structure.Id is "capitol" or "outpost", structure.TerritoryRadius is not null);
            Assert.Equal(structure.Id is "capitol" or "academy", structure.ResearchSlots > 0);
            Assert.Equal(structure.Id == "academy", structure.MaxPerPlayer is not null);
            Assert.Equal(structure.Id == "capitol", structure.TrainsWorkers);
            Assert.Equal(structure.Id is "barracks" or "works", structure.Trains.Count > 0);
            Assert.Equal(structure.Id == "generator", structure.IsEnergyWorkSite);
            Assert.Equal(structure.Id == "wall", structure.Wall is not null);
            Assert.Equal(structure.Id == "tower", structure.Attack is not null);
        }
    }

    /// <summary>
    /// "Range 7, 10 damage per second at tier 1 with one attack per second, scaled by the owner's
    /// Firepower tier; hits Air from Protection 5." Its shots fly at the 20 tiles per second of
    /// design 2.4.
    /// </summary>
    [Fact]
    public void TheTowerAttacksAsTheDesignSays()
    {
        var tower = Structure("tower");
        var attack = tower.Attack!;

        Assert.Equal(DataNumber.FromInteger(10), attack.DamagePerSecond);
        Assert.Equal(
            new AttackDef(AttackKind.Ranged, IntervalTicks: SimInfo.TicksPerSecond, Range: DataNumber.FromInteger(7), ProjectileSpeed: DataNumber.FromInteger(20)),
            attack.Attack);
        Assert.Equal(["air"], attack.CannotHit.Select(line => Data.Rules.Lines[line]));
        Assert.Equal(5, attack.CanHitAllFromProtection);

        // "The Tower's 10 covers everything it can shoot": the furthest target stands its range
        // from a corner, which is half the footprint's diagonal from the center.
        var beyondRange = 2 * (tower.Sight - 7);
        Assert.True(beyondRange > 0 && beyondRange * beyondRange >= 2 * tower.Footprint * tower.Footprint);

        var roles = Data.TrackRoles;
        long[] damagePerSecond = [10_000, 12_500, 15_625, 19_531];
        long[] hitPoints = [600_000, 750_000, 937_500, 1_171_875];
        for (var tier = 1; tier <= 4; tier++)
        {
            var tiers = new[] { 1, 1, 1, 1, 1, 1 };
            tiers[roles.Firepower] = tier;
            tiers[roles.Protection] = tier;
            var blueprint = TechResolver.Resolve(Data, tiers).StructureOf(tower.Index)!;

            Assert.Equal(tier, blueprint.Attack!.EffectiveFirepower);
            Assert.Equal(damagePerSecond[tier - 1], blueprint.Attack.DamagePerSecond);
            Assert.Equal(hitPoints[tier - 1], blueprint.HitPoints);
            Assert.Single(blueprint.Attack.CannotHit);
        }
    }

    /// <summary>"All building hit points scale by 1.25 per Protection tier."</summary>
    [Fact]
    public void EveryStructureHasAQuarterMoreHitPointsForEachProtectionTierOfItsOwner()
    {
        var roles = Data.TrackRoles;
        foreach (var structure in Data.Structures)
        {
            var expected = structure.HitPoints.ToFraction();
            for (var tier = 1; tier <= Data.TierCount; tier++)
            {
                var tiers = new[] { 1, 1, 1, 1, 1, 1 };
                tiers[roles.Protection] = tier;

                Assert.Equal(expected.RoundHalfUp(1000), TechResolver.Resolve(Data, tiers).StructureOf(structure.Index)!.HitPoints);
                expected *= new Sim.Numerics.Fraction(5, 4);
            }
        }
    }

    /// <summary>Design 5.2 and the comment in <c>rules.jsonc</c>: the starting territory is the Capitol's.</summary>
    [Fact]
    public void TheStartingTerritoryOfTheRulesIsTheCapitols()
    {
        var capitol = Structure("capitol");

        Assert.Equal((Data.Rules.Map.StartFootprint, (int?)Data.Rules.Map.StartTerritoryRadius), (capitol.Footprint, capitol.TerritoryRadius));
    }

    [Fact]
    public void EveryLineWithAFileIsTrainedAtTheStructureItNames()
    {
        foreach (var line in Data.Lines)
        {
            var structure = Data.StructureOf(line.BuiltAt);

            Assert.NotNull(structure);
            Assert.Contains(line.Index, structure.Trains);
        }
    }

    private static StructureDef Structure(string id) => Data.Structures.Single(structure => structure.Id == id);

    private static IEnumerable<string> Trained(string id) => Structure(id).Trains.Select(line => Data.Rules.Lines[line]);
}
