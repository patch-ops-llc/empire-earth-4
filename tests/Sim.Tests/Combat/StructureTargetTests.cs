using Sim.Combat;
using Sim.Core;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// Structures as targets (design 2.4 and 2.5; design 8.4, rows 3, 15 and 34): what an attack
/// order may name, where reach and range are measured to, where a shot flies, what a blast
/// reaches, and what Artillery does to a structure.
/// </summary>
/// <remarks>
/// The Capitol of these tests stands on tiles 10 to 13 both ways, so its edges are at 10 and 14
/// and its center at (12, 12). A Hoplite hits for 8 once a second; a Peltast shoots for 6 once a
/// second at range 5; a Ballista fires a shell of 48 every 4 seconds at range 9, not nearer than 3.
/// </remarks>
public class StructureTargetTests
{
    private const long CapitolHitPoints = 2_000_000;

    private static Match WithCapitol(out EntityId capitol)
    {
        var match = Field();
        capitol = match.PlaceStructure(1, Capitol, 10, 10);
        return match;
    }

    [Theory]
    [InlineData(Capitol)]
    [InlineData(Barracks)]
    [InlineData(Generator)]
    [InlineData(Wall)]
    [InlineData(Tower)]
    public void AnAttackOrderMayNameAnyEnemyStructure(string structure)
    {
        var match = Field();
        var target = match.Lay(1, structure, 10, 10);
        var full = match.HitPointsOf(target);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "10.5");
        var peltast = match.Spawn(0, Peltast, "2.5", "11.5");
        match.OrderAttack(0, target, hoplite, peltast);

        match.Run(1);
        Assert.Empty(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal((UnitOrder.Attack, target), (match.UnitOf(hoplite).Order, match.UnitOf(hoplite).Target));
        Assert.Equal((UnitOrder.Attack, target), (match.UnitOf(peltast).Order, match.UnitOf(peltast).Target));

        var events = match.RunAndCollect(100);
        Assert.True(match.HitPointsOf(target) <= full - 8_000 - 6_000);
        Assert.Contains(events, reported => reported.Kind == SimEventKind.MeleeHit && reported.Entity == target && reported.Other == hoplite);
        Assert.Contains(events, reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == target);
    }

    [Fact]
    public void AnOrderToAttackAStructureOfOnesOwnIsRefused()
    {
        var match = Field();
        var own = match.PlaceStructure(0, Barracks, 10, 10);
        var hoplite = match.Spawn(0, Hoplite, "5.5", "10.5");
        match.OrderAttack(0, own, hoplite);

        match.Run(40);

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(1_000_000, match.HitPointsOf(own));
    }

    /// <summary>
    /// Design 8.4, row 3: 3/4 of a tile from the attacker's center to the nearest edge of the
    /// footprint. A unit that is within it hits in the tick of its order, from where it stands.
    /// One that is not walks up, no further than it must: a Hoplite covers 0.13 tiles in a tick.
    /// </summary>
    [Theory]
    [InlineData("9.25", "11.5", true)]
    [InlineData("9.2499", "11.5", false)]
    [InlineData("12", "14.75", true)]
    [InlineData("12", "14.7501", false)]
    [InlineData("14.75", "10", true)]
    [InlineData("9.5", "9.5", true)]
    [InlineData("9.4", "9.4", false)]
    [InlineData("14.5", "14.5", true)]
    [InlineData("14.6", "14.6", false)]
    public void MeleeReachesAStructureFromThreeQuartersOfATileOffItsNearestEdge(string x, string y, bool reaches)
    {
        var match = WithCapitol(out var capitol);
        var hoplite = match.Spawn(0, Hoplite, x, y);
        var stood = match.PositionOf(hoplite);
        match.OrderAttack(0, capitol, hoplite);

        match.Run(1);

        Assert.Equal(reaches ? CapitolHitPoints - 8_000 : CapitolHitPoints, match.HitPointsOf(capitol));
        Assert.Equal(reaches, match.PositionOf(hoplite) == stood);

        match.Run(60);
        var distance = new TileSquare(10, 10, 4).DistanceSquaredRawTo(match.PositionOf(hoplite));

        // It hits every 20 ticks from tick 0, or from tick 1, having stepped into reach in tick 0.
        Assert.Equal(CapitolHitPoints - ((reaches ? 4 : 3) * 8_000), match.HitPointsOf(capitol));
        Assert.True(distance <= Squared("0.75"));
        Assert.True(reaches || distance > Squared("0.6"), "It walked further than it had to.");
    }

    /// <summary>
    /// A melee hit on a unit needs a clear line between the two centers. One on a structure does
    /// not: the structure's own tiles are in the way of any line to its center, and here blocked
    /// ground closes both sides of the corner the Hoplite hits across.
    /// </summary>
    [Fact]
    public void AMeleeHitOnAStructureNeedsNoClearLine()
    {
        var rows = Enumerable.Range(0, 24).Select(_ => new string('.', 32).ToCharArray()).ToArray();
        rows[9][10] = '#';
        rows[10][9] = '#';
        var match = Create(TestMaps.FromRows([.. rows.Select(row => new string(row))]));
        var capitol = match.PlaceStructure(1, Capitol, 10, 10);
        var hoplite = match.Spawn(0, Hoplite, "9.5", "9.5");
        var worker = match.SpawnWorker(1, "10.5", "8.5");
        match.OrderAttack(0, capitol, hoplite);

        match.Run(1);

        Assert.Equal(CapitolHitPoints - 8_000, match.HitPointsOf(capitol));
        Assert.Equal(TestMatches.Point("9.5", "9.5"), match.PositionOf(hoplite));

        // The worker is as near, round the same kind of corner, and is a unit: no hit without a clear line.
        Assert.False(match.Grid.IsSegmentClear(match.PositionOf(hoplite), match.PositionOf(worker)));
    }

    /// <summary>A Peltast's range of 5 is measured to the nearest edge, not to the center two tiles further on.</summary>
    [Theory]
    [InlineData("5", "11.5", true)]
    [InlineData("4.9999", "11.5", false)]
    [InlineData("12.5", "19", true)]
    [InlineData("12.5", "19.0001", false)]
    [InlineData("6", "7", true)]
    [InlineData("6", "6.9999", false)]
    public void RangeToAStructureIsMeasuredToTheNearestEdgeOfItsFootprint(string x, string y, bool inRange)
    {
        var match = WithCapitol(out var capitol);
        var peltast = match.Spawn(0, Peltast, x, y);
        var stood = match.PositionOf(peltast);
        match.OrderAttack(0, capitol, peltast);

        match.Run(1);
        Assert.Equal(inRange ? 1 : 0, match.EventsOf(SimEventKind.ShotFired).Length);
        match.Run(1);
        Assert.Equal(inRange, match.PositionOf(peltast) == stood);

        // From out of range it walks only as far as it must: it stops as soon as the edge is in range.
        match.Run(80);
        var position = match.PositionOf(peltast);
        var distance = new TileSquare(10, 10, 4).DistanceSquaredRawTo(position);
        Assert.True(distance <= Squared("5"));
        Assert.True(inRange || distance > Squared("4.8"), "It walked further than it had to.");
        Assert.True(match.HitPointsOf(capitol) < CapitolHitPoints);
    }

    /// <summary>
    /// A Ballista three tiles from the edge may fire, though it would be five from the center; one
    /// nearer to the edge than three may not, though it is further than three from the center, and
    /// it stands where it is.
    /// </summary>
    [Theory]
    [InlineData("7", "11.5", true)]
    [InlineData("7.0001", "11.5", false)]
    [InlineData("8.5", "11.5", false)]
    public void MinimumRangeToAStructureIsMeasuredToItsNearestEdge(string x, string y, bool fires)
    {
        var match = WithCapitol(out var capitol);
        var ballista = match.Spawn(0, Ballista, x, y);
        var stood = match.PositionOf(ballista);
        match.OrderAttack(0, capitol, ballista);

        match.Run(120);

        Assert.Equal(stood, match.PositionOf(ballista));
        Assert.Equal(fires, match.HitPointsOf(capitol) < CapitolHitPoints);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(ballista).Order);
    }

    [Fact]
    public void AShotAtAStructureFliesToItsCenterAndHitsThere()
    {
        var match = WithCapitol(out var capitol);
        var center = CenterOf(10, 10, 4);
        var peltast = match.Spawn(0, Peltast, "5.5", "12");
        match.OrderAttack(0, capitol, peltast);

        match.Run(1);
        var fired = Assert.Single(match.EventsOf(SimEventKind.ShotFired));
        var shot = fired.Entity;
        Assert.Equal(new SimEvent(SimEventKind.ShotFired, 0, 0, shot, peltast, match.PositionOf(peltast), 0), fired);
        Assert.Equal(capitol, match.Shots.Get(shot).Target);
        Assert.Equal(center, match.Shots.Get(shot).Aim);

        // 6.5 tiles to the center at a tile per tick: it is in the air for 7 ticks, though the
        // edge it was fired at is only 4.5 tiles off.
        match.Run(6);
        Assert.Equal(CapitolHitPoints, match.HitPointsOf(capitol));
        Assert.Equal(TestMatches.Point("11.5", "12"), match.Shots.Get(shot).Position);
        match.Run(1);

        Assert.Equal(new SimEvent(SimEventKind.ShotHit, 7, 0, capitol, shot, center, 6_000), Assert.Single(match.EventsOf(SimEventKind.ShotHit)));
        Assert.Equal(CapitolHitPoints - 6_000, match.HitPointsOf(capitol));
    }

    /// <summary>
    /// A melee hit on a structure is reported where it lands: at the point of the footprint that is
    /// nearest to the attacker.
    /// </summary>
    [Theory]
    [InlineData("9.5", "11.25", "10", "11.25")]
    [InlineData("9.5", "9.5", "10", "10")]
    [InlineData("13", "14.5", "13", "14")]
    public void AMeleeHitOnAStructureIsReportedAtTheNearestPointOfItsFootprint(string x, string y, string hitX, string hitY)
    {
        var match = WithCapitol(out var capitol);
        var hoplite = match.Spawn(0, Hoplite, x, y);
        match.OrderAttack(0, capitol, hoplite);

        match.Run(1);

        Assert.Equal(
            new SimEvent(SimEventKind.MeleeHit, 0, 0, capitol, hoplite, TestMatches.Point(hitX, hitY), 8_000),
            Assert.Single(match.EventsOf(SimEventKind.MeleeHit)));
    }

    /// <summary>
    /// Design 8.4, row 34: a structure counts by its center. The shell lands on a worker at
    /// (12.5, 6), with a radius of 1.5.
    /// </summary>
    [Fact]
    public void AStructureIsInsideABlastWhenItsCenterIs()
    {
        var match = Field();
        var ballista = match.Spawn(0, Ballista, "4.5", "6");
        var target = match.SpawnWorker(1, "12.5", "6");

        // Center (11, 6): 1.5 from the landing point, on the edge of the blast and so inside it.
        var inside = match.PlaceStructure(1, Generator, 10, 5);

        // Center (13.5, 7.5), 1.8 away, though its corner at (13, 7) is only 1.12 away.
        var cornerInside = match.PlaceStructure(1, Wall, 13, 7);

        // Center (12.5, 4.5): a wall is reached like any other structure.
        var wall = match.PlaceStructure(1, Wall, 12, 4);

        // Center (14.5, 5.5), 2.06 away, with its edge at 13 only half a tile from the landing point.
        var edgeInside = match.PlaceStructure(1, Barracks, 13, 4);
        var own = match.PlaceStructure(0, Wall, 12, 7);
        match.OrderAttack(0, target, ballista);

        var blast = match.RunTo(SimEventKind.Blast);

        Assert.Equal(TestMatches.Point("12.5", "6"), blast.Position);
        Assert.Equal(500_000 - 144_000, match.HitPointsOf(inside));
        Assert.Equal(400_000 - 144_000, match.HitPointsOf(wall));
        Assert.Equal(400_000, match.HitPointsOf(cornerInside));
        Assert.Equal(1_000_000, match.HitPointsOf(edgeInside));
        Assert.Equal(400_000, match.HitPointsOf(own));
        Assert.Equal(50_000 - 48_000, match.HitPointsOf(target));
        Assert.Equal(
            [(target, 48_000L), (inside, 144_000L), (wall, 144_000L)],
            match.EventsOf(SimEventKind.ShotHit).Select(hit => (hit.Entity, hit.Amount)));
    }

    /// <summary>
    /// The scenario of the plan's table: one shell deals three times its damage to a structure and
    /// half to a Knight beside it (design 2.4: Artillery against structures 3, against Cavalry 0.5).
    /// </summary>
    [Fact]
    public void OneShellDealsThreeTimesItsDamageToAStructureAndHalfToAKnightBesideIt()
    {
        // A wall of the map from top to bottom keeps the Knight from riding at the Ballista.
        var rows = Enumerable.Range(0, 12).Select(_ => "....#...................").ToArray();
        var match = Create(TestMaps.FromRows(rows));
        var ballista = match.Spawn(0, Ballista, "2.5", "6");
        var generator = match.PlaceStructure(1, Generator, 9, 5);
        var knight = match.Spawn(1, Knight, "11.25", "6");
        var worker = match.SpawnWorker(1, "10", "7.25");
        var knightBefore = match.HitPointsOf(knight);
        match.OrderAttack(0, generator, ballista);

        var blast = match.RunTo(SimEventKind.Blast);

        Assert.Equal(CenterOf(9, 5, 2), blast.Position);
        Assert.Equal(generator, blast.Other);
        Assert.Equal(500_000 - (3 * 48_000), match.HitPointsOf(generator));
        Assert.Equal(knightBefore - (48_000 / 2), match.HitPointsOf(knight));
        Assert.Equal(50_000 - 48_000, match.HitPointsOf(worker));
        Assert.Equal(TestMatches.Point("11.25", "6"), match.PositionOf(knight));
    }

    [Fact]
    public void ADestroyedStructureEndsTheOrdersAgainstItAndFreesItsTiles()
    {
        var match = Field();
        var wall = match.PlaceStructure(1, Wall, 10, 10);
        match.SetHitPoints(wall, 20_000);
        var hoplite = match.Spawn(0, Hoplite, "9.5", "10.5");
        var peltast = match.Spawn(0, Peltast, "6.5", "10.5");
        match.OrderAttack(0, wall, hoplite, peltast);
        Assert.False(match.Grid.IsPassable(10, 10));

        var death = match.RunTo(SimEventKind.Death);

        Assert.Equal((1, wall, TestMatches.Point("10.5", "10.5")), (death.Player, death.Entity, death.Position));
        var removed = Assert.Single(match.EventsOf(SimEventKind.StructureRemoved));
        Assert.Equal((1, wall, StructureIndex(Wall)), (removed.Player, removed.Entity, (int)removed.Amount));
        Assert.True(match.Grid.IsPassable(10, 10));
        Assert.False(match.IsAlive(wall));

        match.Run(2);
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(hoplite).Order, match.UnitOf(hoplite).Target));
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(peltast).Order, match.UnitOf(peltast).Target));
        Assert.Equal(0, match.Structures.Count);
    }

    /// <summary>
    /// A unit sent at a structure goes to the tile beside the footprint that is nearest to itself,
    /// and from the middle of any of those tiles, the corners too, the footprint is in melee reach.
    /// </summary>
    [Theory]
    [InlineData("3.5", "11.5", 9, 11)]
    [InlineData("3.5", "3.5", 9, 9)]
    [InlineData("12.5", "20.5", 12, 14)]
    [InlineData("20.5", "13.5", 14, 13)]
    public void AUnitSentAtAStructureWalksToTheNearestTileBesideIt(string x, string y, int tileX, int tileY)
    {
        var match = WithCapitol(out var capitol);
        var hoplite = match.Spawn(0, Hoplite, x, y);

        // The order may name the Capitol because its player sees it: a worker stands near it, out of the Hoplite's way.
        match.SpawnWorker(0, "16.5", "16.5");
        match.OrderAttack(0, capitol, hoplite);

        match.Run(1);
        Assert.Equal(MapGrid.TileCenter(tileX, tileY), match.Movers.Get(hoplite).Destination);

        match.Run(200);
        Assert.True(match.HitPointsOf(capitol) < CapitolHitPoints);
        Assert.True(new TileSquare(10, 10, 4).DistanceSquaredRawTo(match.PositionOf(hoplite)) <= Squared("0.75"));
    }

    /// <summary>
    /// A structure on ground a melee unit cannot walk up to: the unit goes as near as it can get,
    /// as to any place it cannot reach, and a unit that shoots fires across.
    /// </summary>
    [Fact]
    public void AStructureAcrossGroundThatCannotBeCrossedIsShotAtAndNotHit()
    {
        var rows = Enumerable.Range(0, 12).Select(_ => "......##................").ToArray();
        var match = Create(TestMaps.FromRows(rows));
        var barracks = match.PlaceStructure(1, Barracks, 9, 4);
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var peltast = match.Spawn(0, Peltast, "2.5", "6.5");
        match.OrderAttack(0, barracks, hoplite, peltast);

        match.Run(200);

        Assert.Empty(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.True(match.PositionOf(hoplite).X < Fixed.FromInt(6));
        Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
        Assert.True(match.PositionOf(peltast).X < Fixed.FromInt(6));
        Assert.True(match.HitPointsOf(barracks) < 1_000_000);
        Assert.Equal(0, (1_000_000 - match.HitPointsOf(barracks)) % 6_000);
    }
}
