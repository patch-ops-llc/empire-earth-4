using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Tests.Vision;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// The accents that change a distance (design 2.3): Overgunned I gives a ranged chassis 15% more
/// range, Overgunned II gives an attack with a blast radius half as much again, and Rapid I
/// gives 2 more sight. Each is in the owner's blueprint; these tests are of what combat and
/// sight then do with it.
/// </summary>
/// <remarks>
/// A chassis of epoch 1 has level I of a track's accent at tier 2 and level II at tier 3. A
/// Peltast has range 5 and sight 8, a Ballista range 9, minimum range 3 and a blast radius of 1.5.
/// </remarks>
public class PerkReachTests
{
    private const long Plenty = 100_000_000;

    private static Match Field(int firepower, int mobility = 1)
    {
        var match = Create(TestMaps.Open(40, 24));
        match.SetTiers(0, firepower, 1, mobility, 1, 1, 1);
        return match;
    }

    [Theory]
    [InlineData(Peltast, 1, "5")]
    [InlineData(Peltast, 2, "5.75")]
    [InlineData(Ballista, 1, "9")]
    [InlineData(Ballista, 2, "10.35")]

    // Level II includes level I, and adds no more range.
    [InlineData(Peltast, 3, "5.75")]
    [InlineData(Ballista, 3, "10.35")]

    // The Guard's chassis of epoch 4 attacks at range 3, and is one tier ahead at Firepower 5.
    [InlineData("grenadier", 4, "3")]
    [InlineData("grenadier", 5, "3.45")]
    public void ARangedUnitFiresFromWhereItStandsAtExactlyItsRangeAndWalksFirstFromBeyond(string chassis, int firepower, string range)
    {
        var distance = Fraction.Parse(range).ToFixed();
        var from = TestMatches.Point("5.5", "5.5");

        // At exactly the range: it fires in the first tick and has not moved.
        var match = Field(firepower);
        Assert.Equal(distance, match.Combat.ProfileOf(0, Chassis(chassis)).Range);
        var unit = match.SpawnChassis(0, Chassis(chassis), from);
        var worker = match.SpawnWorker(1, new FixedVector2(from.X + distance, from.Y));
        CombatMatches.SpawnWorker(match, 0, "12.5", "7.5");
        match.OrderAttack(0, worker, unit);
        match.Run(1);
        var fired = Assert.Single(match.EventsOf(SimEventKind.ShotFired));
        Assert.Equal((unit, from), (fired.Other, fired.Position));

        // The smallest distance further: it walks until it is in range, and fires from there.
        match = Field(firepower);
        unit = match.SpawnChassis(0, Chassis(chassis), from);
        worker = match.SpawnWorker(1, new FixedVector2(from.X + distance + Fixed.Epsilon, from.Y));
        CombatMatches.SpawnWorker(match, 0, "12.5", "7.5");
        match.OrderAttack(0, worker, unit);
        match.Run(1);
        Assert.Empty(match.EventsOf(SimEventKind.ShotFired));
        fired = match.RunTo(SimEventKind.ShotFired, limit: 10);
        Assert.NotEqual(from, fired.Position);
        Assert.True(FixedVector2.DistanceSquaredRaw(fired.Position, match.PositionOf(worker)) <= FixedVector2.SquareRaw(distance));
    }

    [Fact]
    public void TheRangeAccentLeavesTheMinimumRangeAsItIs()
    {
        var match = Field(firepower: 3);
        var profile = match.Combat.ProfileOf(0, Chassis(Ballista));

        Assert.Equal(Squared("3"), profile.MinRangeSquared);
        Assert.Equal(Squared("10.35"), profile.RangeSquared);
    }

    [Theory]

    // Overgunned I gives a shell range and no wider blast.
    [InlineData(1, "1.5")]
    [InlineData(2, "1.5")]
    [InlineData(3, "2.25")]
    public void AShellReachesEveryEnemyWithinTheBlastRadiusOfItsFirersBlueprintAndNoneBeyond(int firepower, string radius)
    {
        var match = Field(firepower);
        var blast = Fraction.Parse(radius).ToFixed();
        Assert.Equal(blast, match.Combat.ProfileOf(0, Chassis(Ballista)).Blast);
        var center = TestMatches.Point("13.5", "8.5");
        var ballista = match.Spawn(0, Ballista, "5.5", "8.5");
        var target = match.SpawnWorker(1, center);
        var atTheEdge = match.SpawnWorker(1, new FixedVector2(center.X, center.Y + blast));
        var pastTheEdge = match.SpawnWorker(1, new FixedVector2(center.X, center.Y - blast - Fixed.Epsilon));
        var nearer = match.SpawnWorker(1, new FixedVector2(center.X + Fixed.FromInt(1), center.Y));
        foreach (var worker in new[] { target, atTheEdge, pastTheEdge, nearer })
        {
            match.SetHitPoints(worker, Plenty);
        }

        match.Vision.Refresh();
        match.OrderAttack(0, target, ballista);

        var reported = match.RunTo(SimEventKind.Blast);

        Assert.Equal((center, blast.Raw), (reported.Position, reported.Amount));
        Assert.Equal(
            new[] { target, atTheEdge, nearer }.OrderBy(entity => entity.Value),
            match.EventsOf(SimEventKind.ShotHit).Select(hit => hit.Entity));
        Assert.Equal(Plenty, match.HitPointsOf(pastTheEdge));
        Assert.True(match.HitPointsOf(atTheEdge) < Plenty);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 10)]

    // Level II includes level I, and adds no more sight.
    [InlineData(3, 10)]
    public void ARapidUnitSeesTwoTilesFurther(int mobility, int sight)
    {
        var match = Field(firepower: 1, mobility);
        var position = TestMatches.Point("20.5", "12.5");
        Assert.Equal(sight, match.Blueprint(0, Peltast).Sight);

        match.SpawnChassis(0, Chassis(Peltast), position);
        match.Vision.Refresh();
        VisionAssert.SeesExactly(match.Vision.Grid, 0, (position, sight));

        // And once the match runs, whenever sight is worked out again.
        match.Run(9);
        VisionAssert.SeesExactly(match.Vision.Grid, 0, (position, sight));
    }

    /// <summary>A unit fights what its owner sees, as far as it sees: the two tiles are two tiles of picking targets too.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ARapidUnitPicksATargetItWouldNotHaveSeenWithoutTheAccent(int mobility, bool picks)
    {
        var match = Field(firepower: 1, mobility);
        var peltast = match.Spawn(0, Peltast, "10.5", "12.5");
        var worker = CombatMatches.SpawnWorker(match, 1, "20.5", "12.5");

        match.Run(8);

        Assert.Equal(picks, match.Vision.Sees(0, worker));
        Assert.Equal(picks ? worker : EntityId.None, match.UnitOf(peltast).Target);
    }

    /// <summary>The accent is in the owner's blueprint as it is now: research widens the sight of units already on the field.</summary>
    [Fact]
    public void AUnitSeesFurtherFromTheNextTimeSightIsWorkedOutAfterTheResearch()
    {
        var match = Field(firepower: 1);
        var position = TestMatches.Point("20.5", "12.5");
        match.SpawnChassis(0, Chassis(Peltast), position);
        match.Vision.Refresh();
        VisionAssert.SeesExactly(match.Vision.Grid, 0, (position, 8));

        match.SetTiers(0, 1, 1, 2, 1, 1, 1);
        match.Run(4);

        VisionAssert.SeesExactly(match.Vision.Grid, 0, (position, 10));
    }
}
