using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Data;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using Sim.Tests.Tech;
using static Sim.Tests.Combat.SiegeMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// The Tower (design 2.5; design 8.4, rows 16 and 33): a finished Tower picks like an idle unit
/// that cannot move, with its range measured from the nearest edge of its footprint, and attacks
/// for 10 once a second, times 1.25 for each Firepower tier of its owner above the first.
/// </summary>
/// <remarks>
/// The Tower of most of these tests belongs to player 1 and stands on tiles 10 and 11 both ways:
/// its edges are at 10 and 12 and its center at (11, 11). Its shots fly a tile per tick.
/// </remarks>
public class TowerTests
{
    private static readonly FixedVector2 Center = CenterOf(10, 10, 2);

    /// <summary>A Tower with the Peltast's range of 5, in a match where everyone looks in every tick.</summary>
    private static readonly Lazy<GameData> SameRange = new(() => DataAssert.Valid(Files()
        .Edit(TowerPath, "\"range\": 7", "\"range\": 5")
        .Edit(DesignData.RulesPath, "\"look_interval_ticks\": 4", "\"look_interval_ticks\": 1")));

    private static Match WithTower(out EntityId tower, GameData? data = null)
    {
        var match = Field(data);
        tower = match.PlaceStructure(1, Tower, 10, 10);
        return match;
    }

    private static SimEvent[] ShotsOf(IEnumerable<SimEvent> events, EntityId tower) =>
        [.. events.Where(reported => reported.Kind == SimEventKind.ShotFired && reported.Other == tower)];

    [Fact]
    public void AFinishedTowerShootsAnEnemyInRangeOnceASecondForTen()
    {
        var match = WithTower(out var tower);
        var worker = match.SpawnWorker(0, "15.5", "11");

        var events = match.RunAndCollect(100);

        // The Tower has ID 1, so it looks in tick 3. The worker is 4.5 tiles from its center.
        var fired = ShotsOf(events, tower);
        Assert.Equal([3, 23, 43, 63, 83], fired.Select(shot => shot.Tick));
        Assert.All(fired, shot => Assert.Equal((1, Center, 0L), (shot.Player, shot.Position, shot.Amount)));
        var hits = events.Where(reported => reported.Kind == SimEventKind.ShotHit).ToArray();
        Assert.Equal([8, 28, 48, 68, 88], hits.Select(hit => hit.Tick));
        Assert.All(hits, hit => Assert.Equal((1, worker, 10_000L), (hit.Player, hit.Entity, hit.Amount)));
        Assert.Equal(fired.Select(shot => shot.Entity), hits.Select(hit => hit.Other));
        Assert.Equal(worker, Assert.Single(events, reported => reported.Kind == SimEventKind.Death).Entity);
        Assert.Equal(EntityId.None, match.Turrets.Get(tower).Target);
    }

    /// <summary>Design 8.4, row 33: measured from its nearest edge. The edges are at 10 and 12, and the range is 7.</summary>
    [Theory]
    [InlineData("19", "11", true)]
    [InlineData("19.0001", "11", false)]
    [InlineData("3", "10.5", true)]
    [InlineData("2.9999", "10.5", false)]
    [InlineData("11.5", "19", true)]
    [InlineData("11.5", "19.0001", false)]
    [InlineData("16.9", "16.9", true)]
    [InlineData("17", "16.9", false)]
    public void ATowersRangeIsMeasuredFromItsNearestEdge(string x, string y, bool inRange)
    {
        var match = WithTower(out var tower);
        var worker = match.SpawnWorker(0, x, y);

        var events = match.RunAndCollect(8);

        Assert.Equal(inRange ? 1 : 0, ShotsOf(events, tower).Length);
        Assert.Equal(inRange ? worker : EntityId.None, match.Turrets.Get(tower).Target);
    }

    /// <summary>
    /// The scenario of the plan's table: a unit and a Tower come into each other's range at the
    /// same distance. The Tower of this test has the Peltast's range of 5, and everyone looks in
    /// every tick: from where the Peltast stands, either both fire in the first tick or neither does.
    /// </summary>
    [Theory]
    [InlineData("5", "11", true)]
    [InlineData("4.9999", "11", false)]
    [InlineData("17", "10.25", true)]
    [InlineData("17.0001", "10.25", false)]
    [InlineData("11", "17", true)]
    [InlineData("11", "17.0001", false)]
    [InlineData("6", "7", true)]
    [InlineData("6", "6.9999", false)]
    [InlineData("15", "16", true)]
    [InlineData("15.0001", "16", false)]
    public void AUnitAndATowerComeIntoEachOthersRangeAtTheSameDistance(string x, string y, bool inRange)
    {
        var match = WithTower(out var tower, SameRange.Value);
        var peltast = match.Unit(0, Peltast, x, y);

        match.Run(1);

        Assert.Equal(inRange ? 1 : 0, match.AttacksOf(peltast).Length);
        Assert.Equal(inRange ? 1 : 0, match.AttacksOf(tower).Length);
        Assert.Equal(tower, match.UnitOf(peltast).Target);
        Assert.Equal(inRange ? peltast : EntityId.None, match.Turrets.Get(tower).Target);
    }

    /// <summary>Design 8.4, row 24: a foundation blocks its tiles and has its hit points; everything else comes with completion.</summary>
    [Fact]
    public void ATowerDoesNotAttackUntilItIsFinished()
    {
        var match = Field();
        var tower = match.Lay(1, Tower, 10, 10);
        match.SpawnWorker(0, "14.5", "11");

        Assert.Empty(match.RunAndCollect(40));
        Assert.Equal(0, match.Turrets.Count);

        match.FinishStructure(tower);
        var events = match.RunAndCollect(8);

        Assert.Single(ShotsOf(events, tower));
        Assert.Equal(1, match.Turrets.Count);
    }

    /// <summary>"Scales by 1.25 per Firepower tier": the owner's tier, with no window, rounded once on the hit.</summary>
    [Theory]
    [InlineData(1, 10_000)]
    [InlineData(2, 12_500)]
    [InlineData(3, 15_625)]
    [InlineData(5, 24_414)]
    public void ATowersDamageFollowsItsOwnersFirepowerTier(int firepower, long damage)
    {
        var match = Field();
        match.SetTiers(1, DesignData.Mix(firepower, 1, 1, 1, 1, 1));
        match.PlaceStructure(1, Tower, 10, 10);
        var worker = match.SpawnWorker(0, "14.5", "11");

        var hit = match.RunTo(SimEventKind.ShotHit);

        Assert.Equal((worker, damage), (hit.Entity, hit.Amount));
        Assert.Equal(50_000 - damage, match.HitPointsOf(worker));
    }

    /// <summary>A Tower is of no line: no counter makes its hit larger or smaller.</summary>
    [Theory]
    [InlineData(Hoplite)]
    [InlineData(Knight)]
    [InlineData(Ballista)]
    [InlineData(Peltast)]
    public void ATowerHasNoCounterMultipliers(string chassis)
    {
        var match = WithTower(out _);
        var unit = match.Spawn(0, chassis, "15.5", "11");

        var hit = match.RunAndCollect(40).First(reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == unit);

        Assert.Equal(10_000, hit.Amount);
    }

    /// <summary>"Cannot target Air until Protection tier 5." The Biplane is nearer than the worker.</summary>
    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void ATowerCannotHitAirUntilItsOwnersProtectionIsFive(int protection, bool hitsAir)
    {
        var match = Field();
        match.SetTiers(1, DesignData.Mix(1, protection, 1, 1, 1, 1));
        var tower = match.PlaceStructure(1, Tower, 10, 10);
        var biplane = match.Spawn(0, "biplane", "14.5", "11");
        var worker = match.SpawnWorker(0, "17.5", "11");

        match.Run(8);

        Assert.Equal(hitsAir ? biplane : worker, match.Turrets.Get(tower).Target);
        Assert.Equal(hitsAir, match.Research.Tech(1).StructureOf(StructureIndex(Tower))!.Attack!.CannotHit.Count == 0);
    }

    /// <summary>
    /// The nearest enemy unit or Tower, the lowest ID among equally near ones. Never another
    /// structure, and nothing of its owner's.
    /// </summary>
    [Fact]
    public void ATowerPicksTheNearestEnemyUnitOrTowerAndNothingElse()
    {
        var match = WithTower(out var tower);
        var capitol = match.PlaceStructure(0, Capitol, 13, 9);
        var wall = match.PlaceStructure(0, Wall, 9, 9);
        var own = match.SpawnWorker(1, "12.5", "11");
        var far = match.SpawnWorker(0, "11", "17");
        var madeFirst = match.SpawnWorker(0, "11", "15");
        var madeSecond = match.SpawnWorker(0, "7", "11");
        match.SetHitPoints(madeFirst, 10_000);
        match.SetHitPoints(madeSecond, 10_000);

        // Two workers three tiles off its edges, on two sides of it: the one made first.
        match.Run(4);
        Assert.Equal(madeFirst, match.Turrets.Get(tower).Target);

        match.RunTo(SimEventKind.Death);
        match.Run(4);
        Assert.Equal(madeSecond, match.Turrets.Get(tower).Target);

        match.RunTo(SimEventKind.Death);
        match.Run(4);
        Assert.Equal(far, match.Turrets.Get(tower).Target);

        match.RunTo(SimEventKind.Death);
        var events = match.RunAndCollect(60);
        Assert.Empty(events);
        Assert.Equal(EntityId.None, match.Turrets.Get(tower).Target);
        Assert.Equal(2_000_000, match.HitPointsOf(capitol));
        Assert.Equal(400_000, match.HitPointsOf(wall));
        Assert.Equal(50_000, match.HitPointsOf(own));
    }

    /// <summary>Between two Towers the distance is the gap between the two footprints.</summary>
    [Theory]
    [InlineData(1, 10, true)]
    [InlineData(0, 10, false)]
    [InlineData(10, 19, true)]
    [InlineData(10, 20, false)]
    [InlineData(4, 4, true)]
    [InlineData(3, 3, false)]
    public void TowersFightEachOtherAcrossTheGapBetweenTheirFootprints(int x, int y, bool inRange)
    {
        var match = WithTower(out var tower);
        var other = match.PlaceStructure(0, Tower, x, y);

        var events = match.RunAndCollect(60);

        Assert.Equal(inRange ? 3 : 0, ShotsOf(events, tower).Length);
        Assert.Equal(inRange ? 3 : 0, ShotsOf(events, other).Length);
        Assert.Equal(inRange ? other : EntityId.None, match.Turrets.Get(tower).Target);
        Assert.Equal(inRange ? tower : EntityId.None, match.Turrets.Get(other).Target);
        Assert.Equal(inRange, match.HitPointsOf(tower) < 600_000);
        Assert.Equal(match.HitPointsOf(tower), match.HitPointsOf(other));
        Assert.Equal(0, (600_000 - match.HitPointsOf(tower)) % 10_000);
    }

    /// <summary>
    /// Like a unit: it keeps the target it has while that is in range, however near another
    /// comes, and picks again in the tick its wait is over and its target has left its range.
    /// </summary>
    [Fact]
    public void ATowerKeepsItsTargetWhileItIsInRangeAndPicksAgainOnceItHasLeft()
    {
        // The first stands 6.5 tiles from the Tower's edge and 7.5 from its center: in range by the edge alone.
        var match = WithTower(out var tower);
        var first = match.SpawnWorker(0, "18.5", "11");
        match.Run(4);
        Assert.Equal(first, match.Turrets.Get(tower).Target);

        var nearer = match.SpawnWorker(0, "13.5", "11");
        match.Run(30);
        Assert.Equal(first, match.Turrets.Get(tower).Target);

        match.OrderMove(0, TestMatches.Point("24.5", "11"), first);
        var events = match.RunAndCollect(60);

        Assert.Equal(nearer, match.Turrets.Get(tower).Target);

        // It never missed a beat: a shot every 20 ticks, the first at the one, the later at the other.
        var fired = ShotsOf(events, tower);
        Assert.Equal([43, 63, 83], fired.Select(shot => shot.Tick));
        Assert.Contains(events, reported => reported.Kind == SimEventKind.ShotHit && reported.Entity == nearer);
        Assert.True(match.HitPointsOf(nearer) < 50_000);
        Assert.True(match.PositionOf(first).X > Fixed.FromInt(19));
    }

    [Fact]
    public void ATowerWhoseTargetHasLeftItsRangePicksAgainOnlyWhenItsWaitIsOver()
    {
        var match = WithTower(out var tower);
        var first = match.SpawnWorker(0, "17.5", "11");
        var standing = match.SpawnWorker(0, "11", "18.5");
        match.Run(4);
        Assert.Equal(first, match.Turrets.Get(tower).Target);

        // The target walks out of range while the Tower waits for its next shot. Another enemy
        // has been in range all along, further off than the target was.
        match.OrderMove(0, TestMatches.Point("24.5", "11"), first);
        while (match.PositionOf(first).X <= Fixed.FromInt(19))
        {
            match.Run(1);
        }

        Assert.InRange(match.Turrets.Get(tower).Wait, 2, 19);
        Assert.Equal(first, match.Turrets.Get(tower).Target);

        // A third comes nearer than that one before the wait is over. The Tower picks when it can
        // fire again, and then it picks the nearest.
        var late = match.SpawnWorker(0, "11", "15.5");
        var events = match.RunAndCollect(match.Turrets.Get(tower).Wait);

        Assert.Equal(late, match.Turrets.Get(tower).Target);
        var shot = Assert.Single(ShotsOf(events, tower));
        Assert.Equal(late, match.Shots.Get(shot.Entity).Target);
        match.Run(40);
        Assert.Equal(50_000, match.HitPointsOf(standing));
        Assert.True(match.HitPointsOf(late) < 50_000);
    }

    /// <summary>Design 2.4: every four ticks, on the ticks where the tick number plus the ID is a multiple of four.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ATowerWithoutATargetLooksEveryFourTicksOnTheTicksItsIdGives(int madeBefore)
    {
        var match = Field();
        for (var i = 0; i < madeBefore; i++)
        {
            match.SpawnWorker(1, "2.5", "2.5");
        }

        var tower = match.PlaceStructure(1, Tower, 10, 10);
        match.SpawnWorker(0, "14.5", "11");

        var events = match.RunAndCollect(8);

        var expected = (4 - (tower.Value % 4)) % 4;
        Assert.Equal(expected, Assert.Single(ShotsOf(events, tower)).Tick);
    }

    /// <summary>
    /// A Tower's attack is one of the attacks of the tick: brought to zero in a tick in which it
    /// is due, it still fires, and its shot flies on without it.
    /// </summary>
    [Fact]
    public void ATowerBroughtToZeroStillAttacksInThatTick()
    {
        var match = WithTower(out var tower);
        match.SpawnWorker(1, "2.5", "2.5");
        match.SpawnWorker(1, "2.5", "3.5");
        match.SpawnWorker(1, "2.5", "4.5");
        var hoplite = match.Spawn(0, Hoplite, "9.5", "11");
        Assert.Equal(tower.Value % 4, hoplite.Value % 4);
        match.SetHitPoints(tower, 8_000);
        var full = match.HitPointsOf(hoplite);

        var events = match.RunAndCollect(4);

        Assert.Equal(
            [SimEventKind.MeleeHit, SimEventKind.ShotFired, SimEventKind.Death, SimEventKind.StructureRemoved],
            events.Select(reported => reported.Kind));
        Assert.All(events, reported => Assert.Equal(3, reported.Tick));
        Assert.Equal(tower, events[1].Other);
        Assert.Equal(tower, events[2].Entity);
        Assert.Equal(0, match.Turrets.Count);
        Assert.Equal(0, match.Structures.Count);

        var hit = match.RunTo(SimEventKind.ShotHit);
        Assert.Equal((hoplite, 10_000L), (hit.Entity, hit.Amount));
        Assert.Equal(full - 10_000, match.HitPointsOf(hoplite));
    }

    /// <summary>
    /// A match with Towers is saved and restored like any other: before a Tower has looked, with
    /// its shots in the air, between two of its attacks, and after one is destroyed.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(17)]
    [InlineData(64)]
    [InlineData(140)]
    public void AMatchWithTowersRestoresAndContinuesFromASnapshot(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(
            () =>
            {
                var match = Field();
                match.PlaceStructure(1, Tower, 10, 10);
                match.PlaceStructure(0, Tower, 4, 10);
                match.Lay(1, Tower, 10, 4);
                match.Spawn(0, Peltast, "4.5", "13.5");
                match.Spawn(0, Hoplite, "5.5", "13.5");
                match.Spawn(1, Ballista, "16.5", "10.5");
                match.SetHitPoints(new EntityId(2), 60_000);
                match.RefreshDerivedState();
                return match;
            },
            [
                (4, CombatMatches.AttackCommand(0, new EntityId(1), new EntityId(4), new EntityId(5))),
                (30, CombatMatches.AttackMoveCommand(1, TestMatches.Point("6.5", "14.5"), new EntityId(6))),
                (60, PlaceCommand(1, StructureIndex(Tower), MapGrid.TileCenter(14, 14))),
            ],
            snapshotTick,
            lastTick: 200);
}
