using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Data;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// The fortification rule (design 2.5; design 8.4, row 24): a structure takes 50% less damage
/// from an attacker whose effective Firepower tier is below the Protection tier of its owner.
/// </summary>
public class FortificationTests
{
    private const int FirepowerTrack = 0;

    /// <summary>
    /// The scenario of the plan's table: a Capitol at Protection 4 takes half damage from
    /// Trebuchets at Firepower 3. A Trebuchet's shell is 48 times 1.25 twice, 75, and three times
    /// that against a structure: 225, and half of it here. At Protection 3 nothing is taken off.
    /// </summary>
    [Theory]
    [InlineData(4, 3_906_250, 112_500)]
    [InlineData(3, 3_125_000, 225_000)]
    public void ACapitolAtProtectionFourTakesHalfDamageFromTrebuchetsAtFirepowerThree(int protection, long hitPoints, long damage)
    {
        var match = Field();
        match.SetTiers(0, DesignData.Mix(3, 1, 1, 1, 1, 1));
        match.SetTiers(1, DesignData.Mix(1, protection, 1, 1, 1, 1));
        var capitol = match.PlaceStructure(1, Capitol, 10, 10);
        var first = match.Spawn(0, "trebuchet", "4", "11.5");
        var second = match.Spawn(0, "trebuchet", "4", "12.5");
        Assert.Equal(3, match.Blueprint(0, "trebuchet").EffectiveTiers[FirepowerTrack]);
        Assert.Equal(Fraction.Parse("75"), match.Blueprint(0, "trebuchet").DamagePerHit);
        Assert.Equal(hitPoints, match.HitPointsOf(capitol));
        match.OrderAttack(0, capitol, first, second);

        var hits = match.RunAndCollect(40).Where(reported => reported.Kind == SimEventKind.ShotHit).ToArray();

        Assert.Equal(2, hits.Length);
        Assert.All(hits, hit => Assert.Equal((capitol, damage), (hit.Entity, hit.Amount)));
        Assert.Equal(hitPoints - (2 * damage), match.HitPointsOf(capitol));
    }

    /// <summary>
    /// Below, and not at: an attacker at the owner's Protection tier does full damage. What counts
    /// is the attacker's effective tier, which for a Hoplite, a chassis of epoch 1, stops at 3
    /// however high its owner's Firepower is.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 8_000)]
    [InlineData(1, 2, 4_000)]
    [InlineData(1, 9, 4_000)]
    [InlineData(2, 2, 10_000)]
    [InlineData(2, 1, 10_000)]
    [InlineData(2, 3, 5_000)]
    [InlineData(3, 3, 12_500)]
    [InlineData(3, 4, 6_250)]
    [InlineData(6, 3, 12_500)]
    [InlineData(6, 4, 6_250)]
    [InlineData(6, 6, 6_250)]
    public void AStructureTakesHalfFromAnAttackerWhoseEffectiveFirepowerIsBelowItsOwnersProtection(int firepower, int protection, long damage)
    {
        var match = Field();
        match.SetTiers(0, DesignData.Mix(firepower, 1, 1, 1, 1, 1));
        match.SetTiers(1, DesignData.Mix(1, protection, 1, 1, 1, 1));
        var barracks = match.PlaceStructure(1, Barracks, 10, 10);
        var before = match.HitPointsOf(barracks);
        var hoplite = match.Spawn(0, Hoplite, "9.5", "11.5");
        Assert.Equal(Math.Min(firepower, 3), match.Blueprint(0, Hoplite).EffectiveTiers[FirepowerTrack]);
        match.OrderAttack(0, barracks, hoplite);

        match.Run(1);

        Assert.Equal(damage, Assert.Single(match.EventsOf(SimEventKind.MeleeHit)).Amount);
        Assert.Equal(before - damage, match.HitPointsOf(barracks));
    }

    /// <summary>Design 8.4, row 24, and the plan's table: the rule covers a foundation too.</summary>
    [Theory]
    [InlineData(Barracks)]
    [InlineData(Wall)]
    [InlineData(Tower)]
    public void TheRuleCoversAFoundation(string structure)
    {
        var match = Field();
        match.SetTiers(1, DesignData.Mix(1, 2, 1, 1, 1, 1));
        var foundation = match.Lay(1, structure, 10, 10);
        var before = match.HitPointsOf(foundation);
        var hoplite = match.Spawn(0, Hoplite, "9.5", "10.5");
        match.OrderAttack(0, foundation, hoplite);

        match.Run(1);

        Assert.Equal(before - 4_000, match.HitPointsOf(foundation));
    }

    /// <summary>
    /// Three times for Artillery and then half: a shell of 48 takes 72 off a Generator at
    /// Protection 2, and the full 48 off the worker beside it, who is no structure.
    /// </summary>
    [Fact]
    public void ABlastIsHalvedForTheStructuresItReachesAndNotForTheUnits()
    {
        var match = Field();
        match.SetTiers(1, DesignData.Mix(1, 2, 1, 1, 1, 1));
        var generator = match.PlaceStructure(1, Generator, 10, 10);
        var before = match.HitPointsOf(generator);
        var worker = match.SpawnWorker(1, "12.25", "11");
        var workerBefore = match.HitPointsOf(worker);
        var ballista = match.Spawn(0, Ballista, "4", "11");
        match.OrderAttack(0, generator, ballista);

        match.RunTo(SimEventKind.Blast);

        Assert.Equal(before - 72_000, match.HitPointsOf(generator));
        Assert.Equal(workerBefore - 48_000, match.HitPointsOf(worker));
    }

    /// <summary>
    /// The attacker's effective Firepower is fixed when it fires, and the owner's Protection is
    /// read when the shot lands (design 2.4, "how a hit is computed", and 2.5). A Peltast's shot
    /// of 6 is in the air for seven ticks, and the tiers change in the fourth.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 1, 2, 3_000)]
    [InlineData(1, 2, 1, 1, 6_000)]
    [InlineData(1, 2, 2, 2, 3_000)]
    [InlineData(2, 2, 1, 2, 7_500)]
    public void ProtectionIsReadWhenAShotLandsAndFirepowerWhenItIsFired(
        int firepowerFired, int protectionFired, int firepowerLanded, int protectionLanded, long damage)
    {
        var match = Field();
        match.SetTiers(0, DesignData.Mix(firepowerFired, 1, 1, 1, 1, 1));
        match.SetTiers(1, DesignData.Mix(1, protectionFired, 1, 1, 1, 1));
        var capitol = match.PlaceStructure(1, Capitol, 10, 10);
        var peltast = match.Spawn(0, Peltast, "5.5", "12");
        match.OrderAttack(0, capitol, peltast);
        match.Run(4);
        Assert.Equal(1, match.Shots.Count);

        match.SetTiers(0, DesignData.Mix(firepowerLanded, 1, 1, 1, 1, 1));
        match.SetTiers(1, DesignData.Mix(1, protectionLanded, 1, 1, 1, 1));
        var hit = match.RunTo(SimEventKind.ShotHit);

        Assert.Equal((7, capitol, damage), (hit.Tick, hit.Entity, hit.Amount));
    }

    /// <summary>
    /// "A Tower's effective Firepower is its owner's Firepower tier": the tier that also scales
    /// its damage of 10. Player 0's Tower shoots player 1's.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 10_000)]
    [InlineData(1, 2, 5_000)]
    [InlineData(2, 2, 12_500)]
    [InlineData(2, 3, 6_250)]
    [InlineData(4, 3, 19_531)]
    [InlineData(4, 5, 9_766)]
    public void ATowersEffectiveFirepowerIsItsOwnersTier(int firepower, int protection, long damage)
    {
        var match = Field();
        match.SetTiers(0, DesignData.Mix(firepower, 1, 1, 1, 1, 1));
        match.SetTiers(1, DesignData.Mix(1, protection, 1, 1, 1, 1));
        var shooter = match.PlaceStructure(0, Tower, 4, 10);
        var target = match.Lay(1, Tower, 10, 10);
        var before = match.HitPointsOf(target);

        var hit = match.RunTo(SimEventKind.ShotHit);

        Assert.Equal((target, damage), (hit.Entity, hit.Amount));
        Assert.Equal(before - damage, match.HitPointsOf(target));
        Assert.True(match.IsAlive(shooter));
    }

    /// <summary>The 50% is a value of the rule set, like the look interval and the two reaches.</summary>
    [Fact]
    public void TheMultiplierAndTheReachAreReadFromTheRules()
    {
        var data = DataAssert.Valid(Files()
            .Edit(DesignData.RulesPath, "\"fortification\": \"0.5\"", "\"fortification\": \"0.25\"")
            .Edit(DesignData.RulesPath, "\"structure_reach\": \"0.75\"", "\"structure_reach\": \"1.5\""));
        var match = Field(data);
        match.Research.SetTiers(1, DesignData.Mix(1, 2, 1, 1, 1, 1));
        var barracks = match.PlaceStructure(1, Barracks, 10, 10);
        var before = match.HitPointsOf(barracks);
        var hoplite = match.Unit(0, Hoplite, "8.5", "11.5");
        match.OrderAttack(0, barracks, hoplite);

        match.Run(1);

        // A Hoplite a tile and a half off the edge reaches it, and takes a quarter of its 8 off it.
        Assert.Equal(before - 2_000, match.HitPointsOf(barracks));
        Assert.Equal(Fraction.Parse("0.25"), match.Combat.Fortification);
        Assert.Equal(Fraction.Parse("1.5").ToFixed(), match.Combat.StructureReach);

        var design = Field().Combat;
        Assert.Equal(
            (Fraction.Parse("0.5"), Fraction.Parse("0.75").ToFixed(), Fraction.Parse("0.25"), 4),
            (design.Fortification, design.StructureReach, design.UnitReachMargin, design.LookInterval));
    }
}
