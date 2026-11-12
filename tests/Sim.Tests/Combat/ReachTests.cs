using Sim.Combat;
using Sim.Movement;
using Sim.Numerics;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>Range and melee reach of design 2.4: measured between centers, compared as squares, the limit itself included.</summary>
public class ReachTests
{
    private static readonly FixedVector2 Center = TestMatches.Point("8.5", "8.5");

    [Theory]
    [InlineData(Hoplite, "guard", "1.25")]
    [InlineData(Hoplite, "line_infantry", "1.25")]
    [InlineData(Hoplite, "artillery", "1.25")]
    [InlineData(Hoplite, null, "1.25")]
    [InlineData(Hoplite, "cavalry_armor", "1.75")]
    [InlineData(Hippeus, "guard", "1.75")]
    [InlineData(Hippeus, "line_infantry", "1.75")]
    [InlineData(Hippeus, "cavalry_armor", "1.75")]
    [InlineData(Hippeus, null, "1.75")]
    public void MeleeReachIsTheSpacingBetweenTheTwoPlusAQuarterTile(string attacker, string? targetLine, string reach)
    {
        var match = Create(TestMaps.Open(17, 17));
        var targeting = match.Targeting();
        var profile = match.Combat.ProfileOf(0, Chassis(attacker));
        var line = targetLine is null ? CombatRules.NoLine : Line(targetLine);
        var distance = Fraction.Parse(reach).ToFixed();

        // A target at exactly the reach can be hit. One distance unit further it cannot.
        Assert.True(targeting.Reaches(profile, Center, new FixedVector2(Center.X + distance, Center.Y), line));
        Assert.True(targeting.Reaches(profile, Center, new FixedVector2(Center.X, Center.Y - distance), line));
        Assert.False(targeting.Reaches(profile, Center, new FixedVector2(Center.X + distance + Fixed.Epsilon, Center.Y), line));
        Assert.False(targeting.Reaches(profile, Center, new FixedVector2(Center.X, Center.Y - distance - Fixed.Epsilon), line));

        // There is no lower limit.
        Assert.True(targeting.Reaches(profile, Center, Center, line));
        Assert.Equal(Squared(reach), match.Combat.ReachSquared(profile.Line, line));
    }

    [Fact]
    public void ReachIsComparedAsSquaresSoADiagonalAtExactlyTheReachIsIn()
    {
        var match = Create(TestMaps.Open(17, 17));
        var targeting = match.Targeting();
        var guard = match.Combat.ProfileOf(0, Chassis(Hoplite));

        // 3/4 across and 1 down is exactly 5/4 away.
        var corner = Center + TestMatches.Point("0.75", "1");
        Assert.True(targeting.Reaches(guard, Center, corner, Line("guard")));
        Assert.False(targeting.Reaches(guard, Center, new FixedVector2(corner.X + Fixed.Epsilon, corner.Y), Line("guard")));
    }

    [Fact]
    public void InASquareGridAGuardReachesTheEnemyFacingItAndNotThatEnemysNeighbor()
    {
        var match = Create(TestMaps.Open(17, 17));
        var targeting = match.Targeting();
        var guard = match.Combat.ProfileOf(0, Chassis(Hoplite));

        Assert.True(targeting.Reaches(guard, Center, Center + TestMatches.Point("1", "0"), Line("guard")));
        Assert.False(targeting.Reaches(guard, Center, Center + TestMatches.Point("1", "1"), Line("guard")));
        Assert.False(targeting.Reaches(guard, Center, Center + TestMatches.Point("2", "0"), Line("guard")));
    }

    [Fact]
    public void NoTrackChangesReach()
    {
        var match = Create(TestMaps.Open(17, 17));
        match.SetTiers(0, 3, 3, 3, 3, 3, 3);
        Assert.Equal(Squared("1.25"), match.Combat.ReachSquared(Line("guard"), Line("guard")));
        Assert.Equal(Squared("1.75"), match.Combat.ReachSquared(Line("guard"), Line("cavalry_armor")));
        Assert.NotNull(match.Blueprint(0, Hoplite).Perks.AttackRateBonus);
    }

    [Theory]
    [InlineData(Peltast, "5")]
    [InlineData(Ballista, "9")]
    public void ATargetAtExactlyTheRangeCanBeHit(string attacker, string range)
    {
        var match = Create(TestMaps.Open(40, 40));
        var targeting = match.Targeting();
        var profile = match.Combat.ProfileOf(0, Chassis(attacker));
        var from = TestMatches.Point("20.5", "20.5");
        var distance = Fraction.Parse(range).ToFixed();

        Assert.True(targeting.Reaches(profile, from, new FixedVector2(from.X + distance, from.Y), CombatRules.NoLine));
        Assert.False(targeting.Reaches(profile, from, new FixedVector2(from.X + distance + Fixed.Epsilon, from.Y), CombatRules.NoLine));
    }

    [Fact]
    public void TheMinimumRangeItselfCanBeFiredAtAndAnythingCloserCannot()
    {
        var match = Create(TestMaps.Open(40, 40));
        var targeting = match.Targeting();
        var ballista = match.Combat.ProfileOf(0, Chassis(Ballista));
        var from = TestMatches.Point("20.5", "20.5");
        var minimum = Fixed.FromInt(3);

        Assert.True(targeting.Reaches(ballista, from, new FixedVector2(from.X - minimum, from.Y), CombatRules.NoLine));
        Assert.False(targeting.Reaches(ballista, from, new FixedVector2(from.X - minimum + Fixed.Epsilon, from.Y), CombatRules.NoLine));
        Assert.True(Targeting.IsTooClose(ballista, from, new FixedVector2(from.X - minimum + Fixed.Epsilon, from.Y)));
        Assert.False(Targeting.IsTooClose(ballista, from, new FixedVector2(from.X - minimum, from.Y)));
    }

    [Fact]
    public void AMeleeHitDoesNotCrossAWallOneTileThick()
    {
        // Cavalry/Armor standing as close to the wall as a unit can, 3/8 of a tile, on both sides:
        // 3/8 + 1 + 3/8 is exactly its reach of 7/4.
        var walled = Create(TestMaps.FromRows(
            "......",
            "...#..",
            "......"));
        var open = Create(TestMaps.Open(6, 3));
        var from = TestMatches.Point("2.625", "1.5");
        var to = TestMatches.Point("4.375", "1.5");
        var cavalry = walled.Combat.ProfileOf(0, Chassis(Hippeus));

        Assert.True(open.Targeting().Reaches(cavalry, from, to, Line("guard")));
        Assert.False(walled.Targeting().Reaches(cavalry, from, to, Line("guard")));
        Assert.False(walled.Targeting().Reaches(cavalry, to, from, Line("guard")));

        // A shot is not stopped by it.
        var peltast = walled.Combat.ProfileOf(0, Chassis(Peltast));
        Assert.True(walled.Targeting().Reaches(peltast, from, to, Line("guard")));
    }

    [Fact]
    public void AMeleeHitDoesNotSlipThroughTheJointOfTwoWallTilesThatTouchAtACorner()
    {
        var match = Create(TestMaps.FromRows(
            "......",
            "..#...",
            "...#..",
            "......"));
        var guard = match.Combat.ProfileOf(0, Chassis(Hoplite));

        // The line between the two runs exactly through the corner the wall tiles share.
        var from = TestMatches.Point("2.625", "2.375");
        var to = TestMatches.Point("3.375", "1.625");
        Assert.True(FixedVector2.DistanceSquaredRaw(from, to) < Squared("1.25"));
        Assert.False(match.Targeting().Reaches(guard, from, to, Line("guard")));
        Assert.False(match.Targeting().Reaches(guard, to, from, Line("guard")));

        // Round the end of the wall the same distance is a hit.
        Assert.True(match.Targeting().Reaches(guard, TestMatches.Point("1.5", "1.5"), TestMatches.Point("1.5", "2.5"), Line("guard")));
    }

    [Fact]
    public void AMeleeUnitWalksUpToItsTargetAndStopsAsSoonAsItIsInReach()
    {
        var match = Create(TestMaps.Open(24, 12));
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "12.5", "7.5");
        match.SetHitPoints(worker, 1_000_000);
        match.OrderAttack(0, worker, hoplite);
        match.Run(120);

        // In reach, and no closer than one tick's walk inside it.
        var distance = FixedVector2.Distance(match.PositionOf(hoplite), match.PositionOf(worker));
        var reach = Fraction.Parse("1.25").ToFixed();
        var step = match.Blueprint(0, Hoplite).SpeedPerTick;
        Assert.InRange(distance.Raw, (reach - step).Raw, reach.Raw);
        Assert.Equal(MoveState.Idle, match.Movers.Get(hoplite).State);
        Assert.True(match.HitPointsOf(worker) < 1_000_000);

        // It attacks from where it stands.
        var stood = match.PositionOf(hoplite);
        match.Run(60);
        Assert.Equal(stood, match.PositionOf(hoplite));
    }

    [Fact]
    public void ATargetAcrossGroundThatCannotBeCrossedIsTreatedAsAPlaceTheUnitCannotReach()
    {
        var match = Create(TestMaps.FromRows(
            ".....#....",
            ".....#....",
            ".....#....",
            ".....#....",
            ".....#...."));
        var hoplite = match.Spawn(0, Hoplite, "1.5", "2.5");
        var peltast = match.Spawn(0, Peltast, "1.5", "1.5");
        var worker = match.SpawnWorker(1, "7.5", "2.5");
        match.SetHitPoints(worker, 1_000_000);
        match.OrderAttack(0, worker, hoplite, peltast);
        match.Run(120);

        // The melee unit goes to the tile nearest the target that it can reach, and waits there
        // with its order. The ranged unit walks into range and shoots across.
        Assert.Equal(TestMatches.Point("4.5", "2.5"), match.PositionOf(hoplite));
        Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
        Assert.Equal(worker, match.UnitOf(hoplite).Target);
        Assert.True(match.DistanceSquared(peltast, worker) <= Squared("5"));
        Assert.True(match.HitPointsOf(worker) < 1_000_000);
        Assert.Equal(0, (1_000_000 - match.HitPointsOf(worker)) % 6_000);
    }
}
