using Sim.Combat;
using Sim.Core;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// Which structures a unit picks by itself (design 2.4, "What may be picked"; design 8.4, row
/// 17): a Tower like a unit, any other structure only on an attack-move and only for want of a
/// unit or a Tower, and a wall never.
/// </summary>
/// <remarks>
/// The Towers of these tests are laid and not finished, so that they do not shoot back and the
/// units stay where the test put them. A unit tells a Tower by its kind, finished or not.
/// </remarks>
public class StructurePickingTests
{
    [Fact]
    public void AnIdleUnitPicksNoStructureButATower()
    {
        var match = Field();
        var capitol = match.PlaceStructure(1, Capitol, 10, 10);
        var barracks = match.PlaceStructure(1, Barracks, 10, 5);
        var wall = match.PlaceStructure(1, Wall, 8, 9);
        var peltast = match.Spawn(0, Peltast, "7.5", "11.5");
        var hoplite = match.Spawn(0, Hoplite, "9.5", "12.5");

        var events = match.RunAndCollect(40);

        Assert.Empty(events);
        Assert.Equal(EntityId.None, match.UnitOf(peltast).Target);
        Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
        Assert.Equal(TestMatches.Point("7.5", "11.5"), match.PositionOf(peltast));
        Assert.Equal(TestMatches.Point("9.5", "12.5"), match.PositionOf(hoplite));

        var tower = match.Lay(1, Tower, 4, 14);
        events = match.RunAndCollect(80);

        Assert.Equal(tower, match.UnitOf(peltast).Target);
        Assert.Equal(tower, match.UnitOf(hoplite).Target);
        Assert.Contains(events, reported => reported.Kind == SimEventKind.MeleeHit && reported.Entity == tower && reported.Other == hoplite);
        Assert.Contains(events, reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == tower);
        Assert.DoesNotContain(events, reported => reported.Kind is SimEventKind.MeleeHit or SimEventKind.ShotHit && reported.Entity != tower);
        Assert.Equal(2_000_000, match.HitPointsOf(capitol));
        Assert.Equal(1_000_000, match.HitPointsOf(barracks));
        Assert.Equal(400_000, match.HitPointsOf(wall));

        // The Peltast had the Tower in range from where it stood. The Hoplite went no further than it had to.
        Assert.Equal(TestMatches.Point("7.5", "11.5"), match.PositionOf(peltast));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
    }

    /// <summary>
    /// A Peltast at (10.5, 10.5), a Tower whose nearest edge is 2.5 tiles off and whose center is
    /// 3.5 off, and a worker at a distance between, nearer, or the same as the edge. The nearest
    /// wins, the Tower measured to its edge, and of two equally near the one made first.
    /// </summary>
    [Theory]
    [InlineData("7.5", true, true)]
    [InlineData("7.5", false, true)]
    [InlineData("8.5", true, false)]
    [InlineData("8.5", false, false)]
    [InlineData("8", true, true)]
    [InlineData("8", false, false)]
    public void AUnitPicksTheNearerOfAUnitAndATowerMeasuringTheTowerToItsEdge(string workerY, bool towerFirst, bool picksTower)
    {
        var match = Field();
        var tower = towerFirst ? match.Lay(1, Tower, 13, 10) : EntityId.None;
        var worker = match.SpawnWorker(1, "10.5", workerY);
        if (!towerFirst)
        {
            tower = match.Lay(1, Tower, 13, 10);
        }

        var peltast = match.Spawn(0, Peltast, "10.5", "10.5");

        match.Run(4);

        Assert.Equal(picksTower ? tower : worker, match.UnitOf(peltast).Target);
    }

    /// <summary>
    /// A Peltast on an attack-move past a Barracks 3.5 tiles off. Alone, the Barracks is what it
    /// picks. A worker or a Tower further off, but within the 8 tiles it looks, is picked first.
    /// </summary>
    [Theory]
    [InlineData("nothing")]
    [InlineData("worker")]
    [InlineData(Tower)]
    public void OnAnAttackMoveAUnitPicksAnotherStructureOnlyWhenItCanPickNoUnitAndNoTower(string other)
    {
        var match = Field();
        var barracks = match.PlaceStructure(1, Barracks, 8, 10);
        var expected = other switch
        {
            "worker" => match.SpawnWorker(1, "4.5", "18.5"),
            Tower => match.Lay(1, Tower, 4, 18),
            _ => barracks,
        };
        var peltast = match.Spawn(0, Peltast, "4.5", "11.5");
        match.OrderAttackMove(0, TestMatches.Point("4.5", "12.5"), peltast);

        match.Run(5);

        Assert.Equal(expected, match.UnitOf(peltast).Target);
        Assert.Equal(UnitOrder.AttackMove, match.UnitOf(peltast).Order);

        match.Run(60);
        Assert.Equal(expected == barracks, match.HitPointsOf(barracks) < 1_000_000);
    }

    [Fact]
    public void AUnitOnAMoveOrderOrStandingIdlePicksNoStructureThatDoesNotAttack()
    {
        var match = Field();
        var barracks = match.PlaceStructure(1, Barracks, 8, 10);
        var walker = match.Spawn(0, Peltast, "4.5", "11.5");
        var idle = match.Spawn(0, Peltast, "5.5", "11.5");
        match.OrderMove(0, TestMatches.Point("4.5", "16.5"), walker);

        var events = match.RunAndCollect(80);

        Assert.Empty(events);
        Assert.Equal(1_000_000, match.HitPointsOf(barracks));
        Assert.Equal(TestMatches.Point("4.5", "16.5"), match.PositionOf(walker));
        Assert.Equal(TestMatches.Point("5.5", "11.5"), match.PositionOf(idle));
    }

    /// <summary>
    /// "Only when no unit or Tower can be picked": a unit that has such a structure looks again on
    /// each of its turns to look, in reach of the structure or on its way to it, and goes back to
    /// the structure when the unit it turned on is dead.
    /// </summary>
    [Theory]
    [InlineData(Peltast, "4.5")]
    [InlineData(Hoplite, "1.5")]
    public void AUnitThatHasAStructureTurnsOnAUnitAsSoonAsOneCanBePicked(string chassis, string x)
    {
        var match = Field();
        var barracks = match.PlaceStructure(1, Barracks, 8, 10);
        var unit = match.Spawn(0, chassis, x, "11.5");
        match.OrderAttackMove(0, TestMatches.Point("4.5", "20.5"), unit);
        match.Run(8);
        Assert.Equal(barracks, match.UnitOf(unit).Target);

        var worker = match.SpawnWorker(1, "3.5", "15.5");
        match.Run(4);
        Assert.Equal(worker, match.UnitOf(unit).Target);

        match.RunTo(SimEventKind.Death);
        Assert.False(match.IsAlive(worker));
        var before = match.HitPointsOf(barracks);
        match.Run(5);
        Assert.Equal(barracks, match.UnitOf(unit).Target);
        match.Run(80);
        Assert.True(match.HitPointsOf(barracks) < before);
        Assert.Equal(UnitOrder.AttackMove, match.UnitOf(unit).Order);
    }

    /// <summary>It keeps the structure it has: another structure that comes nearer is no reason to change.</summary>
    [Fact]
    public void AUnitKeepsTheStructureItHasWhenAnotherStructureIsNearer()
    {
        var match = Field();
        var barracks = match.PlaceStructure(1, Barracks, 9, 10);
        var peltast = match.Spawn(0, Peltast, "4.5", "11.5");
        match.OrderAttackMove(0, TestMatches.Point("4.5", "20.5"), peltast);
        match.Run(8);
        Assert.Equal(barracks, match.UnitOf(peltast).Target);

        var nearer = match.PlaceStructure(1, Generator, 6, 11);
        var events = match.RunAndCollect(60);

        Assert.Equal(barracks, match.UnitOf(peltast).Target);
        Assert.Equal(500_000, match.HitPointsOf(nearer));
        var hits = events.Where(reported => reported.Kind == SimEventKind.ShotHit).ToArray();
        Assert.True(hits.Length >= 2);
        Assert.All(hits, hit => Assert.Equal(barracks, hit.Entity));
    }

    [Fact]
    public void AUnitGoesOnWithItsAttackMoveWhenTheStructureItPickedIsDestroyed()
    {
        var match = Field();
        var generator = match.PlaceStructure(1, Generator, 8, 10);
        match.SetHitPoints(generator, 10_000);
        var peltast = match.Spawn(0, Peltast, "4.5", "11.5");
        var goal = TestMatches.Point("4.5", "20.5");
        match.OrderAttackMove(0, goal, peltast);

        match.RunTo(SimEventKind.StructureRemoved);
        match.Run(100);

        Assert.Equal(goal, match.PositionOf(peltast));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(peltast).Order);
        Assert.Equal(0, match.Structures.Count);
    }

    /// <summary>Walls and gates are never picked: a unit walks past them, and a wall takes damage only on an attack order.</summary>
    [Fact]
    public void AWallIsNeverPicked()
    {
        var match = Field();
        var walls = Enumerable.Range(8, 6).Select(y => match.PlaceStructure(1, Wall, 6, y)).ToArray();
        var peltast = match.Spawn(0, Peltast, "4.5", "8.5");
        var hoplite = match.Spawn(0, Hoplite, "5.5", "8.5");
        var idle = match.Spawn(0, Peltast, "4.5", "10.5");
        var goal = TestMatches.Point("4.5", "16.5");
        match.OrderAttackMove(0, goal, peltast, hoplite);

        var events = match.RunAndCollect(120);

        Assert.Empty(events);
        Assert.All(walls, wall => Assert.Equal(400_000, match.HitPointsOf(wall)));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(peltast).Order);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(EntityId.None, match.UnitOf(idle).Target);

        match.OrderAttack(0, walls[0], idle);
        match.Run(40);
        Assert.True(match.HitPointsOf(walls[0]) < 400_000);
    }

    /// <summary>
    /// "Melee units only on ground they can reach": for a structure, a tile beside its footprint
    /// that the unit can walk to. A unit that shoots needs none.
    /// </summary>
    [Fact]
    public void AMeleeUnitPicksAStructureOnlyWhereItCanWalkUpToIt()
    {
        var rows = Enumerable.Range(0, 20).Select(_ => "......##................").ToArray();
        var match = Create(TestMaps.FromRows(rows));
        var barracks = match.PlaceStructure(1, Barracks, 8, 4);
        var hoplite = match.Spawn(0, Hoplite, "4.5", "5.5");
        var peltast = match.Spawn(0, Peltast, "5.5", "5.5");
        var goal = TestMatches.Point("4.5", "15.5");
        match.OrderAttackMove(0, goal, hoplite);
        match.OrderAttackMove(0, TestMatches.Point("5.5", "6.5"), peltast);

        match.Run(5);
        Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
        Assert.Equal(barracks, match.UnitOf(peltast).Target);

        match.Run(120);
        Assert.Equal(goal, match.PositionOf(hoplite));
        Assert.True(match.HitPointsOf(barracks) < 1_000_000);
        Assert.Equal(0, (1_000_000 - match.HitPointsOf(barracks)) % 6_000);
    }

    /// <summary>
    /// "Nothing inside minimum range", measured to the edge: the Barracks two tiles off is passed
    /// over, though its center is 3.5 off, for the one whose edge is five off.
    /// </summary>
    [Fact]
    public void AStructureNearerThanTheMinimumRangeIsNotPicked()
    {
        var match = Field();
        var close = match.PlaceStructure(1, Barracks, 12, 10);
        var far = match.PlaceStructure(1, Barracks, 2, 10);
        var ballista = match.Spawn(0, Ballista, "10", "11.5");
        match.OrderAttackMove(0, TestMatches.Point("10", "12.5"), ballista);

        match.Run(5);
        Assert.Equal(far, match.UnitOf(ballista).Target);

        match.RunTo(SimEventKind.Blast);
        Assert.Equal(1_000_000 - 144_000, match.HitPointsOf(far));
        Assert.Equal(1_000_000, match.HitPointsOf(close));
    }
}
