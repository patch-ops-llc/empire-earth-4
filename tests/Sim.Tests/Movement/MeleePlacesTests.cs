using Sim.Combat;
using Sim.Core;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Movement;

/// <summary>
/// The places round one target (design 2.4: at most 7 Guards in reach of one infantry unit, 10
/// Guards in reach of one Cavalry/Armor unit, 7 Cavalry/Armor units in reach of any one unit): a
/// melee unit that its own side leaves no room finds a free place by its target and goes there,
/// units with no place left stand behind, and a block still fights with its front rank.
/// </summary>
public class MeleePlacesTests
{
    private const string Worker = "worker";

    private static FixedVector2 Point(string x, string y) => TestMatches.Point(x, y);

    /// <summary>
    /// As many units as the design counts round one target come at it from one side, and every
    /// one of them gets a place and fights from it, with its spacing kept from the others.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, Worker, 7, false)]
    [InlineData(Hoplite, Worker, 7, true)]
    [InlineData(Hippeus, Worker, 7, false)]
    [InlineData(Hippeus, Worker, 7, true)]
    [InlineData(Hoplite, Hippeus, 10, false)]
    [InlineData(Hoplite, Hippeus, 10, true)]
    [InlineData(Hippeus, Hippeus, 7, false)]
    [InlineData(Hippeus, Hippeus, 7, true)]
    public void AsManyUnitsAsTheDesignCountsFightOneTargetAtOnce(string chassis, string targetKind, int count, bool attackOrder)
    {
        var (match, units, target) = Siege(chassis, targetKind, count, sides: 1, attackOrder);
        var watch = new EnemyPairs(match, units, [target]);
        var allFought = -1;
        for (var tick = 1; tick <= 400; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            if (allFought < 0 && units.All(unit => IsFighting(match, unit)))
            {
                allFought = tick;
            }
        }

        // Each of them fights from a place in reach of the target, and none is in another's way.
        Assert.InRange(allFought, 1, 300);
        Assert.All(units, unit => Assert.True(IsFighting(match, unit)));
        var reach = match.Combat.ReachSquared(match.Targeting().LineOf(units[0]), match.Targeting().LineOf(target));
        Assert.All(units, unit => Assert.InRange(match.DistanceSquared(unit, target), 0, reach));
        match.AssertKeepTheirSpacing(units);

        // One more than the design counts has no place: the last one stands behind.
        var (crowded, more, _) = Siege(chassis, targetKind, count + 1, sides: 1, attackOrder);
        crowded.Run(400);
        Assert.Equal(count, more.Count(unit => IsFighting(crowded, unit)));
    }

    /// <summary>
    /// Units that find every place taken stop behind the units that have them, out of reach, and
    /// stand still there with their spacing kept: they do not walk in among the others and get
    /// stepped away again.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, false)]
    [InlineData(Hoplite, true)]
    [InlineData(Hippeus, false)]
    public void UnitsWithNoPlaceLeftStandStillBehindTheOthers(string chassis, bool attackOrder)
    {
        var (match, units, target) = Siege(chassis, Worker, 12, sides: 1, attackOrder);
        match.Run(300);

        var fighting = units.Where(unit => IsFighting(match, unit)).ToArray();
        var waiting = units.Except(fighting).ToArray();
        Assert.Equal(7, fighting.Length);
        var before = units.Select(match.PositionOf).ToArray();
        match.Run(200);

        Assert.Equal(before, units.Select(match.PositionOf));
        match.AssertKeepTheirSpacing(units);
        var reach = match.Combat.ReachSquared(match.Targeting().LineOf(units[0]), CombatRules.NoLine);
        Assert.All(waiting, unit =>
        {
            Assert.True(match.DistanceSquared(unit, target) > reach);
            Assert.Equal(default, match.UnitOf(unit).Fought);
            Assert.Equal(target, match.UnitOf(unit).Target);
            Assert.False(match.UnitOf(unit).HasPlace);
        });
    }

    /// <summary>A place that falls free is taken by a unit that was waiting for one.</summary>
    [Fact]
    public void AUnitThatWaitsTakesAPlaceThatFallsFree()
    {
        var (match, units, _) = Siege(Hoplite, Worker, 9, sides: 1, attackOrder: false);
        match.Run(300);
        var fighting = units.Where(unit => IsFighting(match, unit)).ToArray();
        var waiting = units.Except(fighting).ToArray();
        Assert.Equal(2, waiting.Length);

        match.World.Entities.Destroy(fighting[3]);
        match.Run(100);

        var left = units.Where(match.IsAlive).ToArray();
        Assert.Equal(7, left.Count(unit => IsFighting(match, unit)));
        Assert.Single(waiting, unit => IsFighting(match, unit));
        match.AssertKeepTheirSpacing(left);
    }

    /// <summary>
    /// Units that come from two sides each take the place that faces them (design 2.4: a unit
    /// stops as soon as the target is in reach), and nobody who has a place makes way. What is
    /// left between the two groups may be less than one place wide, so one place fewer than the
    /// design counts may be used. The others still stand still.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, false)]
    [InlineData(Hoplite, true)]
    [InlineData(Hippeus, false)]
    [InlineData(Hippeus, true)]
    public void UnitsFromTwoSidesFillAllPlacesButPerhapsOne(string chassis, bool attackOrder)
    {
        var (match, units, _) = Siege(chassis, Worker, 8, sides: 2, attackOrder);
        match.Run(400);

        var fighting = units.Where(unit => IsFighting(match, unit)).ToArray();
        Assert.InRange(fighting.Length, 6, 7);
        var before = units.Select(match.PositionOf).ToArray();
        match.Run(100);
        Assert.Equal(before, units.Select(match.PositionOf));
        match.AssertKeepTheirSpacing(units);
    }

    /// <summary>
    /// Design 2.4, approach: a unit that nobody is in the way of stops as soon as the target is in
    /// reach. Its place is the point that faces it, a hair inside its reach.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, false)]
    [InlineData(Hoplite, true)]
    [InlineData(Hippeus, true)]
    public void AUnitAloneStopsWhereTheTargetComesIntoReachOnItsWay(string chassis, bool attackOrder)
    {
        var match = Create(TestMaps.Open(40, 20));
        var unit = match.Spawn(0, chassis, "8", "6");
        var worker = match.SpawnWorker(1, "20", "10.6");
        match.SetHitPoints(worker, 100_000_000);
        var start = match.PositionOf(unit);
        var at = match.PositionOf(worker);
        if (attackOrder)
        {
            match.OrderAttack(0, worker, unit);
        }
        else
        {
            match.OrderAttackMove(0, at, unit);
        }

        match.Run(200);

        // On the straight line from where it set out, between its reach and 1/64 of a tile less.
        Assert.True(IsFighting(match, unit));
        var reach = match.Combat.Reach(match.Targeting().LineOf(unit), CombatRules.NoLine);
        var stands = match.PositionOf(unit);
        Assert.InRange(
            match.DistanceSquared(unit, worker),
            FixedVector2.SquareRaw(reach - Fixed.FromRaw(Fixed.RawOne / 64)),
            FixedVector2.SquareRaw(reach));
        var off = FixedVector2.CrossRaw(at - start, stands - start) / IntegerMath.Sqrt((ulong)(at - start).LengthSquaredRaw());
        Assert.InRange(off, -8, 8);
        Assert.Equal(stands, match.UnitOf(unit).Place);
        Assert.True(match.UnitOf(unit).HasPlace);
    }

    /// <summary>
    /// A place is by a target that stands still. When the target walks off, the places are gone;
    /// the units follow it and find new ones where it stops.
    /// </summary>
    [Fact]
    public void WhenTheTargetMovesTheUnitsFindNewPlaces()
    {
        var (match, units, target) = Siege(Hoplite, Worker, 6, sides: 1, attackOrder: true);
        match.Run(300);
        Assert.All(units, unit => Assert.True(IsFighting(match, unit)));
        var places = units.Select(unit => match.UnitOf(unit).Place).ToArray();

        // Units are in its way on every side but one. It walks out through the gap.
        match.OrderMove(1, Point("36", "15.5"), target);
        match.Run(20);
        Assert.All(units, unit => Assert.False(match.UnitOf(unit).HasPlace && match.UnitOf(unit).Place == places[Array.IndexOf(units, unit)]));

        var watch = new EnemyPairs(match, units, [target]);
        for (var tick = 0; tick < 500; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
        }

        Assert.Equal(MoveState.Idle, match.Movers.Get(target).State);
        Assert.All(units, unit => Assert.True(IsFighting(match, unit)));
        Assert.All(units, unit => Assert.True(match.UnitOf(unit).HasPlace));
        match.AssertKeepTheirSpacing(units);
    }

    /// <summary>
    /// Design 2.4: in a square grid at 1 tile a Guard reaches the enemy facing it and not that
    /// enemy's neighbor or the second rank, so only the front rank of a melee block fights. A
    /// block three ranks deep fills a pass from side to side and walks up to a block that stands
    /// still: the six in front fight, and the ranks behind find no place and stand still behind
    /// them.
    /// </summary>
    [Fact]
    public void BehindAFrontRankThatFillsAPassNobodyFindsAPlace()
    {
        var match = Create(Pass());
        var guards = Grid(match, x => match.Spawn(0, Hoplite, x.X, x.Y), firstX: 4, ranks: 3);
        var workers = Grid(match, x => match.SpawnWorker(1, x.X, x.Y), firstX: 11, ranks: 3);
        foreach (var worker in workers)
        {
            match.SetHitPoints(worker, 100_000_000);
        }

        match.RefreshDerivedState();
        match.OrderAttackMove(0, Point("13", "4"), guards);
        var front = guards.Where(guard => match.PositionOf(guard).X == Fixed.FromInt(6)).ToArray();
        Assert.Equal(6, front.Length);

        var watch = new EnemyPairs(match, guards, workers);
        for (var tick = 0; tick < 200; tick++)
        {
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            Assert.All(guards.Except(front), guard => Assert.False(IsFighting(match, guard)));
        }

        Assert.All(front, guard => Assert.True(IsFighting(match, guard)));
        var before = guards.Select(match.PositionOf).ToArray();
        match.Run(200);
        Assert.Equal(before, guards.Select(match.PositionOf));
        Assert.All(guards.Except(front), guard => Assert.Equal(default, match.UnitOf(guard).Fought));
        match.AssertKeepTheirSpacing(guards);
    }

    /// <summary>
    /// Design 2.4: only the front rank of a melee block fights. Two blocks three ranks deep fight
    /// it out in a pass that the files fill from side to side, so that neither has a flank. Until
    /// the first unit is destroyed only the front ranks fight, each unit of them, and no unit
    /// behind finds a place. After that the ranks behind move up as places fall free, units that
    /// are fighting keep their spacing, and the fight comes to an end.
    /// </summary>
    [Theory]
    [InlineData(Hoplite, 6, false)]
    [InlineData(Hoplite, 6, true)]
    [InlineData(Hippeus, 4, false)]
    [InlineData(Hippeus, 4, true)]
    public void InAPassOnlyTheFrontRanksFightUntilAPlaceFallsFree(string chassis, int files, bool rearFirst)
    {
        var match = Create(Pass());
        var spacing = chassis == Hoplite ? Fixed.One : Fixed.FromRaw(Fixed.RawOne * 3 / 2);
        var top = chassis == Hoplite ? Point("0", "1.5").Y : Point("0", "1.75").Y;

        // The units of a side in the order of their ranks, the front rank first. The order they
        // are made in decides which of them acts first in a tick, so both orders are tried.
        EntityId[] Side(int owner, int frontX, int back)
        {
            var units = new EntityId[3 * files];
            for (var made = 0; made < units.Length; made++)
            {
                var i = rearFirst ? units.Length - 1 - made : made;
                units[i] = match.SpawnChassis(owner, Chassis(chassis), new FixedVector2(Fixed.FromInt(frontX) + (spacing * (back * (i / files))), top + (spacing * (i % files))));
            }

            return units;
        }

        var west = Side(0, 9, -1);
        var east = Side(1, 21, 1);
        match.RefreshDerivedState();
        match.OrderAttackMove(0, Point("22", "4"), west);
        match.OrderAttackMove(1, Point("8", "4"), east);

        var watch = new EnemyPairs(match, west, east);
        var most = 0;
        var started = 0;
        var ticks = 0;
        var destroyed = false;
        var frontFought = new HashSet<EntityId>();
        while (west.Any(match.IsAlive) && east.Any(match.IsAlive))
        {
            Assert.InRange(++ticks, 1, 6000);
            var fought = west.Concat(east).Where(match.IsAlive).Where(unit => IsFighting(match, unit)).ToHashSet();
            match.Simulation.Tick();
            watch.AssertNoPairCameTooClose();
            destroyed |= west.Concat(east).Any(unit => !match.IsAlive(unit));
            foreach (var side in new[] { west, east })
            {
                var fighting = side.Where(match.Bodies.Contains).Where(unit => IsFighting(match, unit)).ToArray();
                match.AssertKeepTheirSpacing(fighting);
                most = Math.Max(most, fighting.Length);
                if (!destroyed)
                {
                    Assert.All(fighting, unit => Assert.InRange(Array.IndexOf(side, unit), 0, files - 1));
                    frontFought.UnionWith(fighting);
                }
            }

            started += west.Concat(east).Count(unit => match.Bodies.Contains(unit) && !fought.Contains(unit) && IsFighting(match, unit));
        }

        // Every unit of both front ranks fought before the first was destroyed, and units took a
        // place more often than there are units in the front ranks: those behind moved up.
        Assert.Equal(2 * files, frontFought.Count);
        Assert.InRange(most, files, files + 2);
        Assert.InRange(started, (2 * files) + 1, int.MaxValue);
    }

    /// <summary>
    /// A unit with a place holds its ground from the tick it stands on it, before its first
    /// attack, and a melee unit of its side that stands closer to it than their spacing has no
    /// room: the place is taken.
    /// </summary>
    [Fact]
    public void AUnitOnItsPlaceHoldsItsGroundAndLeavesNoRoomBesideIt()
    {
        var match = Create(TestMaps.Open(20, 20));
        var first = match.Spawn(0, Hoplite, "10", "10");
        var second = match.Spawn(0, Hoplite, "10", "10.5");
        var worker = match.SpawnWorker(1, "11.1", "10");
        match.RefreshDerivedState();
        var fighters = new Fighters(CombatMatches.Data, match.Combat, match.Combatants, match.Bodies, match.Movers, match.SpatialIndex);
        Assert.False(fighters.HoldsGround(first));
        Assert.False(fighters.HasNoRoom(second, match.Bodies.Get(second)));

        ref var unit = ref match.Combatants.Get(first);
        unit.Target = worker;
        unit.PlaceFor = worker;
        unit.Place = match.PositionOf(first);
        Assert.True(fighters.HoldsGround(first));
        Assert.False(fighters.IsFighting(first));
        Assert.True(fighters.HasNoRoom(second, match.Bodies.Get(second)));

        // Not while it is somewhere else, and not once it has another target.
        unit.Place = Point("10", "9");
        Assert.False(fighters.HoldsGround(first));
        Assert.False(fighters.HasNoRoom(second, match.Bodies.Get(second)));
        unit.Place = match.PositionOf(first);
        unit.Target = EntityId.None;
        Assert.False(unit.HasPlace);
        Assert.False(fighters.HoldsGround(first));
    }

    /// <summary>
    /// The search itself. With nobody in the way the place is the point of the circle that faces
    /// the unit, 1/128 of a tile inside its reach. Beside a unit that will not make way it is the
    /// point of the circle that is their spacing and 1/128 from that unit, on the side nearer to
    /// the seeker, and the mirrored fight gives the mirrored place.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePlaceFacesTheUnitOrLiesBesideTheUnitInItsWay(bool mirrored)
    {
        FixedVector2 At(string x, string y) => mirrored ? new FixedVector2(Fixed.FromInt(40) - Point(x, y).X, Point(x, y).Y) : Point(x, y);
        var match = Create(TestMaps.Open(40, 20));
        var holder = match.SpawnChassis(0, Chassis(Hoplite), At("18.75", "10"));
        var seeker = match.SpawnChassis(0, Chassis(Hoplite), At("16", "10.5"));
        var worker = match.SpawnWorker(1, "20", "10");
        match.RefreshDerivedState();
        var places = PlacesOf(match);
        var reach = match.Combat.Reach(match.Targeting().LineOf(seeker), CombatRules.NoLine);
        var radius = reach - MeleePlaces.Inside;
        var target = match.PositionOf(worker);

        // Nobody holds its ground yet: the place faces the seeker.
        Assert.True(Find(match, places, seeker, worker, direct: false, out var facing, out var crowded));
        Assert.False(crowded);
        Assert.InRange(Distance(facing, target), radius.Raw - 2, radius.Raw);
        Assert.InRange(FixedVector2.CrossRaw(match.PositionOf(seeker) - target, facing - target) / radius.Raw, -2, 2);
        Assert.True(FixedVector2.DistanceSquaredRaw(facing, match.PositionOf(seeker)) < FixedVector2.DistanceSquaredRaw(target, match.PositionOf(seeker)));

        // The other unit has its place where it stands, which is closer to that point than their
        // spacing. The seeker's place is beside it, on the seeker's side of it.
        ref var unit = ref match.Combatants.Get(holder);
        unit.Target = worker;
        unit.PlaceFor = worker;
        unit.Place = match.PositionOf(holder);
        Assert.True(FixedVector2.DistanceSquaredRaw(facing, unit.Place) < FixedVector2.SquareRaw(Fixed.One));
        Assert.True(Find(match, places, seeker, worker, direct: false, out var beside, out crowded));
        Assert.True(crowded);
        Assert.InRange(Distance(beside, target), radius.Raw - 2, radius.Raw + 2);
        Assert.InRange(Distance(beside, unit.Place), (Fixed.One + MeleePlaces.Gap).Raw - 2, (Fixed.One + MeleePlaces.Gap).Raw + 2);
        Assert.True(beside.Y > unit.Place.Y);
        Assert.True(match.Targeting().Reaches(match.Combat.ProfileOf(0, Chassis(Hoplite)), beside, target, CombatRules.NoLine));

        if (mirrored)
        {
            // The same fight the other way round gives the same place the other way round.
            var other = Create(TestMaps.Open(40, 20));
            var otherHolder = other.Spawn(0, Hoplite, "18.75", "10");
            var otherSeeker = other.Spawn(0, Hoplite, "16", "10.5");
            var otherWorker = other.SpawnWorker(1, "20", "10");
            other.RefreshDerivedState();
            ref var otherUnit = ref other.Combatants.Get(otherHolder);
            otherUnit.Target = otherWorker;
            otherUnit.PlaceFor = otherWorker;
            otherUnit.Place = other.PositionOf(otherHolder);
            Assert.True(Find(other, PlacesOf(other), otherSeeker, otherWorker, direct: false, out var plain, out _));
            Assert.Equal(new FixedVector2(Fixed.FromInt(40) - plain.X, plain.Y), beside);
        }
    }

    /// <summary>
    /// What a place may not be: closer than their spacing to a unit of its own side that fights
    /// or has a place, to the place a unit of its side is walking to, or to another enemy; on
    /// ground a unit cannot stand on; and for a unit that enemies have stopped, where it cannot
    /// walk straight to. With every place taken there is none.
    /// </summary>
    [Fact]
    public void APlaceIsFreeOfEveryUnitThatWillNotMakeWay()
    {
        var match = Create(TestMaps.Open(40, 20));
        var seeker = match.Spawn(0, Hoplite, "16", "10");
        var worker = match.SpawnWorker(1, "20", "10");
        var fighter = match.Spawn(0, Hoplite, "18.8", "10.3");
        var walker = match.Spawn(0, Hoplite, "14", "4");
        var enemy = match.Spawn(1, Hoplite, "19.6", "8.6");
        match.RefreshDerivedState();
        var places = PlacesOf(match);
        var target = match.PositionOf(worker);

        ref var fighting = ref match.Combatants.Get(fighter);
        fighting.Wait = Fraction.One;
        fighting.Fought = match.PositionOf(fighter);
        ref var walking = ref match.Combatants.Get(walker);
        walking.Target = worker;
        walking.PlaceFor = worker;
        walking.Place = Point("18.9", "9.3");

        Assert.True(Find(match, places, seeker, worker, direct: false, out var place, out var crowded));
        Assert.True(crowded);
        Assert.True(FixedVector2.DistanceSquaredRaw(place, match.PositionOf(fighter)) >= FixedVector2.SquareRaw(Fixed.One));
        Assert.True(FixedVector2.DistanceSquaredRaw(place, walking.Place) >= FixedVector2.SquareRaw(Fixed.One));
        Assert.True(FixedVector2.DistanceSquaredRaw(place, match.PositionOf(enemy)) >= FixedVector2.SquareRaw(Fixed.One));
        Assert.True(match.Targeting().Reaches(match.Combat.ProfileOf(0, Chassis(Hoplite)), place, target, CombatRules.NoLine));

        // The nearest free place is on the far side of the fighter, past the target's hard
        // spacing as the unit walks: a unit that enemies have stopped does not take it.
        var blocked = !SpacingRule.AllowsStep(match.PositionOf(seeker), place, target, SpacingRule.Between(match.Movers.Get(seeker), match.Movers.Get(worker)));
        Assert.Equal(!blocked, Find(match, places, seeker, worker, direct: true, out var direct, out _) && direct == place);

        // A unit of the side that only stands there, with no place and not fighting, makes way.
        var bystander = match.Spawn(0, Hoplite, place.X.ToString(), place.Y.ToString());
        match.RefreshDerivedState();
        Assert.True(Find(match, places, seeker, worker, direct: false, out var again, out _));
        Assert.Equal(place, again);
        match.World.Entities.Destroy(bystander);
    }

    /// <summary>Seven places round an infantry target and no eighth, whoever asks and from wherever.</summary>
    [Fact]
    public void WhenEveryPlaceIsTakenThereIsNone()
    {
        var (match, units, target) = Siege(Hoplite, Worker, 7, sides: 1, attackOrder: true);
        match.Run(300);
        Assert.All(units, unit => Assert.True(match.UnitOf(unit).HasPlace));
        var places = PlacesOf(match);
        foreach (var (x, y) in new[] { ("20", "15.6"), ("28", "15.6"), ("24", "11"), ("24", "20"), ("21", "13"), ("27", "18") })
        {
            var late = match.Spawn(0, Hoplite, x, y);
            match.RefreshDerivedState();
            Assert.False(Find(match, places, late, target, direct: false, out _, out var crowded));
            Assert.True(crowded);
            match.World.Entities.Destroy(late);
        }
    }

    /// <summary>
    /// Nobody takes a place on ground it cannot stand on, and an idle unit takes only a place it
    /// can walk straight to that is no further from its post than its acquisition range.
    /// </summary>
    [Fact]
    public void AnIdleUnitTakesOnlyAPlaceNearItsPostAndNobodyAPlaceOnBlockedGround()
    {
        // A cliff west of the target: the point that faces a unit in the west is on it.
        var rows = Enumerable.Repeat(new string('.', 30), 20).ToArray();
        rows[10] = new string('.', 18) + "#" + new string('.', 11);
        var match = Create(TestMaps.FromRows(rows));
        var seeker = match.Spawn(0, Hoplite, "14", "10.5");
        var worker = match.SpawnWorker(1, "20", "10.5");
        match.RefreshDerivedState();
        var places = PlacesOf(match);
        var target = match.PositionOf(worker);

        // The search looks at that point and beside units in the way, and there are none: it
        // finds no place, and the unit walks up as it did before there were places.
        Assert.False(Find(match, places, seeker, worker, direct: false, out _, out var crowded));
        Assert.False(crowded);
        match.OrderAttack(0, worker, seeker);
        match.SetHitPoints(worker, 100_000_000);
        match.Run(200);
        Assert.True(IsFighting(match, seeker));

        var open = Create(TestMaps.Open(30, 20));
        var openSeeker = open.Spawn(0, Hoplite, "14", "10.5");
        var openWorker = open.SpawnWorker(1, "20", "10.5");
        open.RefreshDerivedState();
        Assert.True(Find(open, PlacesOf(open), openSeeker, openWorker, direct: false, out var plain, out _));
        Assert.NotEqual(match.Grid.RegionOf(match.PositionOf(seeker)), match.Grid.RegionOf(plain));

        // Idle at its post, it finds the same place when the post is near enough and none when
        // the place is further from the post than it acquires.
        var body = open.Bodies.Get(openSeeker);
        var attack = open.Combat.ProfileOf(0, Chassis(Hoplite));
        Assert.True(PlacesOf(open).TryFind(openSeeker, body, attack, UnitOrder.Idle, plain, openWorker, target, direct: false, out var near, out _));
        Assert.Equal(plain, near);
        Assert.False(PlacesOf(open).TryFind(openSeeker, body, attack, UnitOrder.Idle, Point("2", "10.5"), openWorker, target, direct: false, out _, out _));
    }

    /// <summary>
    /// A structure has no circle round it, and a unit that cannot walk goes nowhere: the search
    /// finds nothing for either, and they fight as they did before there were places.
    /// </summary>
    [Fact]
    public void ThereAreNoPlacesByATargetThatIsNotAUnit()
    {
        var match = Create(TestMaps.Open(40, 20));
        var seeker = match.Spawn(0, Hoplite, "16", "10");
        var worker = match.SpawnWorker(1, "20", "10");
        match.RefreshDerivedState();
        var places = PlacesOf(match);
        Assert.True(Find(match, places, seeker, worker, direct: false, out _, out _));

        match.Movers.Remove(worker);
        Assert.False(Find(match, places, seeker, worker, direct: false, out _, out var crowded));
        Assert.False(crowded);
        Assert.False(places.IsFreeWhereItStands(seeker, match.Bodies.Get(seeker), match.Combat.ProfileOf(0, Chassis(Hoplite)), worker, match.PositionOf(worker)));
    }

    /// <summary>
    /// The states this piece adds are in the snapshot: a unit on its way to its place, a unit on
    /// its place, and a unit that waits for one.
    /// </summary>
    [Theory]
    [InlineData(150)]
    [InlineData(160)]
    [InlineData(200)]
    public void ASnapshotTakenWhileUnitsTakeTheirPlacesRestoresAndContinues(int snapshotTick)
    {
        (int Tick, Command Command)[] script =
        [
            (1, AttackMoveCommand(0, Point("24", "15.5"), [.. Enumerable.Range(1, 9).Select(id => new EntityId(id))])),
            (260, MoveCommand(1, Point("30", "15.5"), new EntityId(10))),
        ];
        SnapshotAssert.RestoresAndContinues(() => Siege(Hoplite, Worker, 9, sides: 1, attackOrder: null).Match, script, snapshotTick, 400);

        var match = Siege(Hoplite, Worker, 9, sides: 1, attackOrder: null).Match;
        foreach (var (tick, command) in script)
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Run(snapshotTick);
        var units = Enumerable.Range(0, match.Combatants.Count).Select(i => (Unit: match.Combatants.ValueAt(i), At: match.PositionOf(match.Combatants.EntityAt(i)))).ToArray();
        if (snapshotTick < 200)
        {
            Assert.Contains(units, unit => unit.Unit.HasPlace && unit.At != unit.Unit.Place);
        }

        Assert.Contains(units, unit => unit.Unit.HasPlace && unit.At == unit.Unit.Place);
        if (snapshotTick >= 200)
        {
            Assert.Contains(units, unit => !unit.Unit.HasPlace && unit.Unit.Target != EntityId.None);
        }
    }

    /// <summary>
    /// Units in a column 16 tiles west of one target that stands still and cannot be destroyed, or
    /// half of them west and half east, with one order for all of them, or none.
    /// </summary>
    private static (Match Match, EntityId[] Units, EntityId Target) Siege(string chassis, string targetKind, int count, int sides, bool? attackOrder)
    {
        var match = Create(TestMaps.Open(48, 30));
        var units = new EntityId[count];
        for (var i = 0; i < count; i++)
        {
            var west = sides == 1 || i % 2 == 0;
            units[i] = match.SpawnChassis(0, Chassis(chassis), new FixedVector2(Fixed.FromInt(west ? 8 : 40), Fixed.FromInt(4 + (2 * (i / sides)))));
            match.SetHitPoints(units[i], 2_000_000_000);
        }

        EntityId target;
        if (targetKind == Worker)
        {
            target = match.SpawnWorker(1, "24", "15.6");
        }
        else
        {
            // A unit of a chassis fights back and would walk up to an attacker. This one cannot walk.
            target = match.Spawn(1, targetKind, "24", "15.6");
            match.Movers.Get(target).Speed = Fixed.Zero;
        }

        match.SetHitPoints(target, 2_000_000_000);
        match.RefreshDerivedState();
        if (attackOrder == true)
        {
            match.OrderAttack(0, target, units);
        }
        else if (attackOrder == false)
        {
            match.OrderAttackMove(0, Point("24", "15.5"), units);
        }

        return (match, units, target);
    }

    private static MeleePlaces PlacesOf(Match match)
    {
        var fighters = new Fighters(CombatMatches.Data, match.Combat, match.Combatants, match.Bodies, match.Movers, match.SpatialIndex);
        return new MeleePlaces(match.Grid, match.Bodies, match.Movers, match.Combatants, match.SpatialIndex, match.Targeting(), fighters);
    }

    private static bool Find(Match match, MeleePlaces places, EntityId seeker, EntityId target, bool direct, out FixedVector2 place, out bool crowded) =>
        places.TryFind(
            seeker,
            match.Bodies.Get(seeker),
            match.Combat.ProfileOf(0, Chassis(Hoplite)),
            UnitOrder.Attack,
            default,
            target,
            match.PositionOf(target),
            direct,
            out place,
            out crowded);

    private static long Distance(FixedVector2 a, FixedVector2 b) => (long)IntegerMath.Sqrt((ulong)FixedVector2.DistanceSquaredRaw(a, b));

    /// <summary>A pass six tiles wide between cliffs: six units at 1 tile fill it from side to side.</summary>
    private static Sim.Data.MapDef Pass() =>
        TestMaps.FromRows([new string('#', 30), .. Enumerable.Repeat(new string('.', 30), 6), new string('#', 30)]);

    /// <summary>Six files across the pass, rank behind rank toward higher x, at 1 tile.</summary>
    private static EntityId[] Grid(Match match, Func<(string X, string Y), EntityId> spawn, int firstX, int ranks)
    {
        _ = match;
        return [.. Enumerable.Range(0, ranks * 6).Select(i =>
            spawn(((firstX + (i / 6)).ToString(System.Globalization.CultureInfo.InvariantCulture), $"{1 + (i % 6)}.5")))];
    }

    private static bool IsFighting(Match match, EntityId unit) =>
        match.UnitOf(unit).Wait > Fraction.Zero && match.UnitOf(unit).Fought == match.PositionOf(unit);
}
