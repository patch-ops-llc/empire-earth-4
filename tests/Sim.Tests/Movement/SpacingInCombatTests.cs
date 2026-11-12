using Sim.Combat;
using Sim.Core;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Movement;

/// <summary>
/// The spacing rules of design 2.4 under combat's orders: units of a chassis get the spacing of
/// their line, enemies that fight stay their spacing apart and still reach each other, a unit
/// that enemies stop fights the one in its way, and a unit that fights is not pushed about by its
/// own side.
/// </summary>
public class SpacingInCombatTests
{
    private static FixedVector2 Point(string x, string y) => TestMatches.Point(x, y);

    /// <summary>Design 2.4: 1 tile for infantry, 1.5 for Cavalry/Armor, and a worker's own value.</summary>
    [Fact]
    public void AUnitIsPlacedWithTheSpacingOfItsLine()
    {
        var match = Create(TestMaps.Open(20, 20));
        foreach (var chassis in CombatMatches.Data.Chassis)
        {
            var unit = match.SpawnChassis(0, chassis.Index, Point("5.5", "5.5"));
            Assert.Equal(SpacingOf(chassis.Index), match.Movers.Get(unit).Spacing);
        }

        Assert.Equal(Fixed.One, match.Movers.Get(match.Spawn(0, Hoplite, "8.5", "8.5")).Spacing);
        Assert.Equal(Fixed.One * 3 / 2, match.Movers.Get(match.Spawn(0, Hippeus, "8.5", "8.5")).Spacing);
        Assert.Equal(
            CombatMatches.Data.Resources.Worker.Spacing.ToFraction().ToFixed(),
            match.Movers.Get(match.SpawnWorker(0, "8.5", "8.5")).Spacing);
    }

    /// <summary>
    /// Two blocks on attack-move orders onto each other's ground. At the end of every tick every
    /// pair of enemies is its spacing apart (design 2.4), melee reach being a quarter of a tile
    /// more than that, both sides still get to hit each other, and one side wins.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, Hoplite)]
    [InlineData(Hoplite, Hippeus)]
    [InlineData(Hippeus, Hippeus)]
    [InlineData(Peltast, Hoplite)]
    public void EnemiesThatFightKeepTheirSpacingAndReachEachOther(string westChassis, string eastChassis)
    {
        var match = Create(TestMaps.Open(48, 24));
        var west = Block(match, 0, westChassis, 12, 6);
        var east = Block(match, 1, eastChassis, 30, 6);
        var full = west.Concat(east).ToDictionary(unit => unit, match.HitPointsOf);
        var watch = new EnemyPairs(match, west, east);
        match.OrderAttackMove(0, Point("32", "11"), west);
        match.OrderAttackMove(1, Point("14", "11"), east);

        var hurt = new HashSet<EntityId>();
        var ticks = 0;
        while (west.Any(match.IsAlive) && east.Any(match.IsAlive))
        {
            Assert.InRange(++ticks, 1, 6000);
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            foreach (var one in west.Where(match.Bodies.Contains))
            {
                foreach (var other in east.Where(match.Bodies.Contains))
                {
                    var spacing = SpacingRule.Between(match.Movers.Get(one), match.Movers.Get(other));
                    Assert.True(
                        match.DistanceSquared(one, other) >= FixedVector2.SquareRaw(spacing),
                        $"Tick {match.World.Tick}: enemies {one} and {other} are closer than {spacing}.");
                }
            }

            hurt.UnionWith(full.Keys.Where(unit => match.IsAlive(unit) && match.HitPointsOf(unit) < full[unit]));
        }

        Assert.Contains(hurt, west.Contains);
        Assert.Contains(hurt, east.Contains);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    /// <summary>
    /// Design 2.4: a blocked unit on an attack-move order fights, because the units in its way are
    /// within its acquisition range. Here the nearest enemy is a worker 1.4 tiles ahead, out of
    /// the Guard's reach of 1.25, and two cavalry units to either side hold the Guard 1.5 tiles
    /// off, which is within its reach of 1.75 against them. It fights one of those.
    /// </summary>
    [Fact]
    public void AUnitThatEnemiesStopOnAnAttackMoveFightsOneInItsWay()
    {
        var match = Create(TestMaps.Open(40, 20));
        var guard = match.Spawn(0, Hoplite, "19.9", "10");
        var upper = match.Spawn(1, Hippeus, "20.5", "8.586");
        var lower = match.Spawn(1, Hippeus, "20.5", "11.414");
        var worker = match.SpawnWorker(1, "21.4", "10");
        match.SetHitPoints(guard, 100_000_000);
        var full = match.HitPointsOf(upper);
        var workerFull = match.HitPointsOf(worker);
        var watch = new EnemyPairs(match, [guard], [upper, lower, worker]);
        match.OrderAttackMove(0, Point("30", "10"), guard);

        var waited = false;
        for (var tick = 0; tick < 120; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            waited |= match.Movers.Get(guard).Waiting;
        }

        // It was stopped short of the worker, kept its order, and hit the cavalry instead.
        Assert.True(waited);
        Assert.Equal(UnitOrder.AttackMove, match.UnitOf(guard).Order);
        Assert.True(match.DistanceSquared(guard, worker) > match.Combat.ReachSquared(Line("guard"), CombatRules.NoLine));
        Assert.Equal(workerFull, match.HitPointsOf(worker));
        Assert.Contains(match.UnitOf(guard).Target, new[] { upper, lower });
        Assert.True(match.HitPointsOf(upper) < full || match.HitPointsOf(lower) < full);
    }

    /// <summary>
    /// Units that stop within reach of one target stand closer than their spacing and step apart
    /// (design 2.4), which takes some of them out of reach again. Those walk up again, further
    /// round the target, until each has a place: all of them get to attack a target that stands
    /// still, and afterwards they go on and come to rest with their spacing kept.
    /// </summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void UnitsSteppedOutOfReachByTheirOwnSideWalkUpAgain(int count, bool attackOrder)
    {
        var match = Create(TestMaps.Open(40, 20));
        var guards = Enumerable.Range(0, count)
            .Select(i => match.SpawnChassis(0, Chassis(Hoplite), new FixedVector2(Fixed.FromInt(8), Fixed.FromInt(9 + i))))
            .ToArray();
        var worker = match.SpawnWorker(1, "20", "10.6");
        match.SetHitPoints(worker, 100_000_000);
        if (attackOrder)
        {
            match.OrderAttack(0, worker, guards);
        }
        else
        {
            match.OrderAttackMove(0, Point("30", "10.5"), guards);
        }

        var watch = new EnemyPairs(match, guards, [worker]);
        for (var tick = 0; tick < 400; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
        }

        // Every one of them stands where it attacks from, in reach, and they keep their spacing.
        var reach = match.Combat.ReachSquared(Line("guard"), CombatRules.NoLine);
        foreach (var guard in guards)
        {
            Assert.Equal(match.PositionOf(guard), match.UnitOf(guard).Fought);
            Assert.InRange(match.DistanceSquared(guard, worker), FixedVector2.SquareRaw(Fixed.One), reach);
        }

        match.AssertKeepTheirSpacing(guards);

        // The target gone, they go on with their order, or stay if they had been sent at it.
        match.World.Entities.Destroy(worker);
        for (var ticks = 0; guards.Any(guard => match.UnitOf(guard).Order != UnitOrder.Idle); ticks++)
        {
            Assert.InRange(ticks, 0, 600);
            match.Simulation.Tick();
        }

        match.RunUntilAtRest(600, guards);
        match.AssertKeepTheirSpacing(guards);
        if (!attackOrder)
        {
            Assert.All(guards, guard => Assert.InRange(match.PositionOf(guard).X.Raw, 29 * Fixed.RawOne, 31 * Fixed.RawOne));
        }
    }

    /// <summary>
    /// A unit that fights holds its ground: a unit of its own side that stops on top of it steps
    /// away alone, and the one that fights keeps its place and its rate of attack.
    /// </summary>
    [Fact]
    public void AUnitThatFightsIsNotSteppedAsideByOneThatStopsBesideIt()
    {
        var (alone, _, _) = FightWithANewcomer(newcomer: false);
        var (crowded, fighter, other) = FightWithANewcomer(newcomer: true);

        Assert.Equal(alone.PositionOf(fighter), crowded.PositionOf(fighter));
        Assert.Equal(alone.UnitOf(fighter), crowded.UnitOf(fighter));
        Assert.Equal(MoveState.Idle, crowded.Movers.Get(other).State);
        crowded.AssertKeepTheirSpacing(fighter, other);
    }

    /// <summary>
    /// Design 2.4: only the front rank of a melee block fights. Two blocks three ranks deep meet,
    /// the ranks behind walk into the front rank, as units may among their own side, and the enemy
    /// holds them there. At the end of every tick no two units of one side that are fighting stand
    /// closer than their spacing: a unit with no room in the rank does not attack, and the units
    /// ranks behind move up as places fall free. The fight still comes to an end.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, Hoplite)]
    [InlineData(Hoplite, Hippeus)]
    public void InADenseMeleeOnlyUnitsWithRoomInTheRankFight(string westChassis, string eastChassis)
    {
        var match = Create(TestMaps.Open(60, 30));
        var west = Block(match, 0, westChassis, 16, 8, ranks: 3, files: 8);
        var east = Block(match, 1, eastChassis, 34, 8, ranks: 3, files: 8);
        var watch = new EnemyPairs(match, west, east);
        match.OrderAttackMove(0, Point("35", "12"), west);
        match.OrderAttackMove(1, Point("17", "12"), east);

        var started = 0;
        var most = 0;
        var ticks = 0;
        while (west.Any(match.IsAlive) && east.Any(match.IsAlive))
        {
            Assert.InRange(++ticks, 1, 6000);
            var fought = west.Concat(east).Where(match.IsAlive).Where(unit => IsFighting(match, unit)).ToHashSet();
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            foreach (var side in new[] { west, east })
            {
                var fighting = side.Where(match.Bodies.Contains).Where(unit => IsFighting(match, unit)).ToArray();
                match.AssertKeepTheirSpacing(fighting);
                most = Math.Max(most, fighting.Length);
            }

            started += west.Concat(east).Count(unit => match.Bodies.Contains(unit) && !fought.Contains(unit) && IsFighting(match, unit));
        }

        // A rank of eight had room to fight, and units took a place in it more often than there
        // are units in the first ranks: those behind moved up as places fell free.
        Assert.InRange(most, 6, 12);
        Assert.InRange(started, 17, int.MaxValue);
    }

    private static Fighters FightersOf(Match match) =>
        new(CombatMatches.Data, match.Combat, match.Combatants, match.Bodies, match.Movers, match.SpatialIndex);

    /// <summary>Leaves a unit as an attack made from a place leaves it.</summary>
    private static void Attacked(Match match, EntityId unit, FixedVector2 from)
    {
        ref var combatant = ref match.Combatants.Get(unit);
        combatant.Wait = Fraction.One;
        combatant.Fought = from;
    }

    private static bool IsFighting(Match match, EntityId unit) =>
        match.UnitOf(unit).Wait > Fraction.Zero && match.UnitOf(unit).Fought == match.PositionOf(unit);

    /// <summary>
    /// What counts as fighting: a melee unit, where it last attacked from, while it waits for its
    /// next attack. Not a unit that has walked on, not one whose wait is over, not a unit that
    /// shoots and not a worker.
    /// </summary>
    [Fact]
    public void AMeleeUnitIsFightingWhereItLastAttackedFromUntilItsWaitIsOver()
    {
        var match = Create(TestMaps.Open(20, 20));
        var guard = match.Spawn(0, Hoplite, "5", "5");
        var archer = match.Spawn(0, Peltast, "8", "5");
        var worker = match.SpawnWorker(0, "11", "5");
        var fighters = FightersOf(match);
        Assert.False(fighters.IsFighting(guard));

        Attacked(match, guard, match.PositionOf(guard));
        Attacked(match, archer, match.PositionOf(archer));
        Assert.True(fighters.IsFighting(guard));
        Assert.True(fighters.HoldsGround(guard));
        Assert.False(fighters.IsFighting(archer));
        Assert.False(fighters.HoldsGround(archer));
        Assert.False(fighters.HoldsGround(worker));

        Attacked(match, guard, Point("5", "5.01"));
        Assert.False(fighters.IsFighting(guard));

        Attacked(match, guard, match.PositionOf(guard));
        match.Combatants.Get(guard).Wait = Fraction.Zero;
        Assert.False(fighters.IsFighting(guard));
    }

    /// <summary>
    /// A unit has no room in the rank when it stands closer than their spacing to a melee unit of
    /// its own side that is fighting: the larger spacing of the two, and exactly the spacing is room.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, Hoplite, "0.99", true)]
    [InlineData(Hoplite, Hoplite, "1", false)]
    [InlineData(Hippeus, Hoplite, "1.49", true)]
    [InlineData(Hippeus, Hoplite, "1.5", false)]
    [InlineData(Hoplite, Hippeus, "1.49", true)]
    [InlineData(Hoplite, Peltast, "0.5", false)]
    public void AUnitHasNoRoomInsideTheSpacingOfAMeleeUnitOfItsSideThatFights(string chassis, string besideChassis, string apart, bool noRoom)
    {
        var match = Create(TestMaps.Open(20, 20));
        var beside = match.Spawn(0, besideChassis, "5", "5");
        var unit = match.SpawnChassis(0, Chassis(chassis), Point("5", "5") + new FixedVector2(Fraction.Parse(apart).ToFixed(), Fixed.Zero));
        var enemy = match.SpawnChassis(1, Chassis(besideChassis), match.PositionOf(unit) + new FixedVector2(Fixed.Zero, Fixed.One / 4));
        match.RefreshDerivedState();
        var fighters = FightersOf(match);

        // Nobody fights yet, and an enemy that fights takes no room in this side's rank.
        Assert.False(fighters.HasNoRoom(unit, match.Bodies.Get(unit)));
        Attacked(match, enemy, match.PositionOf(enemy));
        Assert.False(fighters.HasNoRoom(unit, match.Bodies.Get(unit)));

        Attacked(match, beside, match.PositionOf(beside));
        Assert.Equal(noRoom, fighters.HasNoRoom(unit, match.Bodies.Get(unit)));
        Assert.False(fighters.HasNoRoom(beside, match.Bodies.Get(beside)));
    }

    /// <summary>
    /// A unit sent at a target it cannot get to walks to the nearest ground it can reach and
    /// stands there: looking again every few ticks starts no new walk.
    /// </summary>
    [Fact]
    public void AUnitAsNearAsItCanGetToATargetItCannotReachStandsStill()
    {
        var rows = Enumerable.Range(0, 12).Select(_ => new string('.', 10) + "#" + new string('.', 9)).ToArray();
        var match = Create(TestMaps.FromRows(rows));
        var guard = match.Spawn(0, Hoplite, "4.5", "5.5");
        var worker = match.SpawnWorker(1, "13.5", "5.5");
        match.OrderAttack(0, worker, guard);
        match.RunUntilAtRest(300, guard);
        var stood = match.PositionOf(guard);

        for (var tick = 0; tick < 40; tick++)
        {
            match.Simulation.Tick();
            Assert.Equal(MoveState.Idle, match.Movers.Get(guard).State);
            Assert.Equal(stood, match.PositionOf(guard));
        }

        Assert.Equal(UnitOrder.Attack, match.UnitOf(guard).Order);
        Assert.Equal((9, 5), (stood.X.FloorToInt(), stood.Y.FloorToInt()));
    }

    /// <summary>A group on an attack-move order is placed as a move order places it (design 5.3) and ends in its shape.</summary>
    [Fact]
    public void AGroupOnAnAttackMoveOrderKeepsItsShape()
    {
        var match = Create(TestMaps.Open(40, 20));
        var units = Block(match, 0, Hoplite, 4, 4);
        var target = Point("30.25", "9.75");
        var places = match.PlacesAround(target, units);
        match.OrderAttackMove(0, target, units);

        match.RunUntilAtRest(600, units);

        Assert.Equal(places, units.Select(match.PositionOf));
        Assert.All(units, unit => Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order));
    }

    private static (Match Match, EntityId Fighter, EntityId Other) FightWithANewcomer(bool newcomer)
    {
        var match = Create(TestMaps.Open(40, 20));
        var fighter = match.Spawn(0, Hoplite, "10", "10");
        var other = match.Spawn(0, Hoplite, "4", "10");
        var worker = match.SpawnWorker(1, "11.1", "10");
        match.SetHitPoints(worker, 100_000_000);
        match.OrderAttack(0, worker, fighter);
        match.Run(30);
        Assert.Equal(match.PositionOf(fighter), match.UnitOf(fighter).Fought);

        if (newcomer)
        {
            match.OrderMove(0, match.PositionOf(fighter), other);
        }

        var stood = match.PositionOf(fighter);
        for (var tick = 0; tick < 300; tick++)
        {
            match.Simulation.Tick();
            Assert.Equal(stood, match.PositionOf(fighter));
        }

        return (match, fighter, other);
    }

    private static Fixed SpacingOf(int chassis) => CombatMatches.Data.Lines[CombatMatches.Data.Chassis[chassis].Line].Base.Spacing.ToFraction().ToFixed();

    /// <summary>A block of a chassis at the spacing of its line, from the tile (x, y): ranks along x, files along y.</summary>
    private static EntityId[] Block(Match match, int player, string chassis, int x, int y, int ranks = 2, int files = 3)
    {
        var index = Chassis(chassis);
        var spacing = SpacingOf(index);
        return [.. Enumerable.Range(0, ranks * files).Select(i => match.SpawnChassis(
            player,
            index,
            new FixedVector2(Fixed.FromInt(x) + (spacing * (i % ranks)), Fixed.FromInt(y) + (spacing * (i / ranks)))))];
    }
}
