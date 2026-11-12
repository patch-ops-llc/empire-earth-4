using Sim.Combat;
using Sim.Core;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using Sim.Vision;
using static Sim.Tests.Combat.SiegeMatches;
using static Sim.Tests.Walls.WalledMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// The wall rule under the fog of war (design 2.5 and 5.3): a unit's orders and its choice of a
/// wall to break use only the walls its player sees or remembers. A wall the player has never
/// seen blocks the unit as the ground does; the unit goes to the nearest tile it can reach, and
/// once its player knows of the wall the unit breaks in as the wall rule says.
/// </summary>
/// <remarks>
/// The camp is that of <see cref="WallRuleTests"/>: a ring of player 1's wall on the edge of the
/// box from tile (14, 8) to tile (22, 16). A Hoplite sees 8 tiles, so one at (5.5, 12.5) sees
/// none of it. The wide camp is the ring from (10, 4) to (26, 20); a worker in its middle sees 6
/// tiles and none of its walls.
/// </remarks>
public class WallsInFogTests
{
    private static readonly (int X, int Y) Low = (14, 8);
    private static readonly (int X, int Y) High = (22, 16);
    private static readonly (int X, int Y) WideLow = (10, 4);
    private static readonly (int X, int Y) WideHigh = (26, 20);

    private static Match WithUnseenCamp(int wallHitPoints = 24, int players = 2)
    {
        var match = Create(TestMaps.Open(32, 24), players: players);
        match.UnseenRing(1, Low, High);
        match.WeakenWalls(wallHitPoints);
        return match;
    }

    private static bool Knows(Match match, int player, EntityId wall) => match.Vision.KnowsStructure(player, wall);

    /// <summary>No unit of the player has a wall in its way that the player does not know of.</summary>
    private static void AssertKnowsItsWalls(Match match, int player, params EntityId[] units)
    {
        foreach (var unit in units)
        {
            var wall = match.UnitOf(unit).WallInWay;
            Assert.True(
                wall == EntityId.None || !match.IsAlive(wall) || Knows(match, player, wall),
                $"Tick {match.World.Tick}: {unit} has the wall {wall} in its way, which player {player} has never seen.");
        }
    }

    [Theory]
    [InlineData(Hoplite, false)]
    [InlineData(Hoplite, true)]
    [InlineData(Peltast, true)]
    public void AUnitOrderedBehindWallsItsPlayerHasNeverSeenLearnsOfThemOnItsWayAndBreaksIn(string chassis, bool attackMove)
    {
        var match = WithUnseenCamp();
        var unit = match.Spawn(0, chassis, "5.5", "12.5");
        var inWay = match.WallOn(14, 12);
        var walls = match.WallHitPoints();
        Assert.All(walls.Keys, wall => Assert.False(Knows(match, 0, wall)));
        if (attackMove)
        {
            match.OrderAttackMove(0, Center(18, 12), unit);
        }
        else
        {
            match.OrderMove(0, Center(18, 12), unit);
        }

        // To its player the camp is ground nobody can reach: the unit goes for the nearest tile
        // outside, as to a place in a wood, and no search runs.
        match.Run(1);
        var given = match.UnitOf(unit);
        Assert.Equal((attackMove ? UnitOrder.AttackMove : UnitOrder.Move, EntityId.None), (given.Order, given.WallInWay));
        Assert.Equal((Center(13, 12), Center(18, 12)), (given.Goal, given.Ordered));
        Assert.Equal(0, match.Breaching.SearchCount);

        // It has no wall in its way until its player has seen one.
        while (match.UnitOf(unit).WallInWay == EntityId.None)
        {
            Assert.True(match.World.Tick < 200, "The unit never found the wall.");
            match.Run(1);
        }

        // Then it finds it on its way, without a new order: the tile it would have been given
        // with the camp in sight, and its goal is the place that was ordered.
        var learned = match.UnitOf(unit);
        Assert.Equal(inWay, learned.WallInWay);
        Assert.True(Knows(match, 0, inWay));
        Assert.Equal((given.Order, Center(18, 12)), (learned.Order, learned.Goal));
        Assert.NotEqual(Center(13, 12), match.PositionOf(unit));
        Assert.Equal(1, match.Breaching.SearchCount);

        match.Run(700);
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
    }

    [Fact]
    public void TheTileAUnitBreaksIsTheFirstOnItsWayThroughTheWallsItsPlayerKnows()
    {
        var match = WithUnseenCamp();
        var known = match.WallOn(14, 9);
        match.MakeKnown(known);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), unit);

        // With the whole camp known its tile would be (14, 12). Its player knows one tile of the
        // ring, three tiles further north, and that is the unit's way in.
        match.Run(1);
        Assert.Equal((known, Center(18, 12)), (match.UnitOf(unit).WallInWay, match.UnitOf(unit).Goal));

        // It walks up to that tile, which nobody of its side sees, and not to the nearest tile
        // outside the camp.
        Assert.False(match.Vision.Sees(0, known));
        var walksTo = match.Movers.Get(unit).Destination;
        Assert.InRange(walksTo.X.FloorToInt(), 13, 13);
        Assert.InRange(walksTo.Y.FloorToInt(), 8, 10);
        Assert.True(match.UnitOf(unit).Engaged);

        // It keeps that tile when it comes to see the others.
        while (match.IsAlive(known))
        {
            Assert.True(match.World.Tick < 600, "The wall tile still stands.");
            match.Run(1);
            Assert.True(!match.IsAlive(known) || match.UnitOf(unit).WallInWay == known);
        }

        Assert.True(Knows(match, 0, match.WallOn(14, 12)));
        match.Run(300);
        Assert.Equal([known], match.Damaged(walls));
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWallItsPlayerRemembersIsInTheWayWhileNobodySeesIt(bool attackOrder)
    {
        var match = Field();
        match.Ring(1, WideLow, WideHigh);
        var unit = match.Spawn(0, Hoplite, "0.5", "12.5");
        var target = match.SpawnWorker(1, "18.5", "12.5");
        match.Watcher(0, "17.5", "11.5");
        if (attackOrder)
        {
            match.OrderAttack(0, target, unit);
        }
        else
        {
            match.OrderMove(0, Center(18, 11), unit);
        }

        match.Run(1);
        var inWay = match.UnitOf(unit).WallInWay;
        Assert.NotEqual(EntityId.None, inWay);
        Assert.False(match.Vision.Sees(0, inWay));

        // It does not give the tile up and find it again for not seeing it.
        var outOfSight = 0;
        for (var tick = 0; tick < 5 * match.Combat.LookInterval && !match.Vision.Sees(0, inWay); tick++)
        {
            match.Run(1);
            Assert.Equal(inWay, match.UnitOf(unit).WallInWay);
            Assert.Equal(1, match.Breaching.SearchCount);
            outOfSight++;
        }

        Assert.True(outOfSight > 2 * match.Combat.LookInterval, $"The wall was out of sight for {outOfSight} ticks only.");
    }

    /// <summary>
    /// The ground is known as it is, as the routes know it: a wall in the way that falls where
    /// nobody of the unit's side sees it lets the unit go on at once.
    /// </summary>
    [Fact]
    public void AWallInTheWayThatFallsOutOfSightNoLongerHoldsTheUnit()
    {
        var match = Field();
        match.Ring(1, Low, High);
        var unit = match.Spawn(0, Hoplite, "3.5", "12.5");
        var inWay = match.WallOn(14, 12);
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), unit);
        match.Run(2);
        Assert.Equal(inWay, match.UnitOf(unit).WallInWay);

        match.SetHitPoints(inWay, 0);
        match.Run(2);

        Assert.False(match.IsAlive(inWay));
        Assert.True(match.Vision.Remembers(0, inWay, out _));
        Assert.Equal((UnitOrder.Move, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));

        match.Run(300);
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.Equal(1, match.Breaching.SearchCount);
    }

    [Fact]
    public void AWallLaidUnseenAcrossTheWayOfAUnitIsBrokenOnceItsPlayerSeesIt()
    {
        // A cliff from edge to edge with one gap, at (16, 12).
        var rows = Enumerable.Range(0, 24).Select(y => y == 12 ? new string('.', 32) : new string('.', 16) + "#" + new string('.', 15));
        var match = Create(TestMaps.FromRows([.. rows]));
        var unit = match.Spawn(0, Hoplite, "1.5", "12.5");
        match.OrderMove(0, Center(25, 12), unit);
        match.Run(8);

        // Player 1 closes the gap while the unit is on its way, out of its sight.
        var gap = match.UnseenWallTile(1, 16, 12);
        match.WeakenWalls(24);
        var unknown = 0;
        while (!Knows(match, 0, gap))
        {
            Assert.True(match.World.Tick < 200, "Player 0 never saw the wall.");
            match.Run(1);
            if (!Knows(match, 0, gap))
            {
                Assert.Equal((UnitOrder.Move, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
                unknown++;
            }
        }

        // It had its turns to look in that time and found nothing to break.
        Assert.True(unknown > 2 * match.Combat.LookInterval, $"The wall was unknown for {unknown} ticks only.");
        Assert.Equal(0, match.Breaching.SearchCount);

        match.Run(match.Combat.LookInterval);
        Assert.Equal((UnitOrder.Move, gap), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));

        match.Run(600);
        Assert.False(match.IsAlive(gap));
        Assert.Equal(Center(25, 12), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void BehindAWallItsPlayerKnowsAUnitLearnsOfTheNextOneAndBreaksThatToo()
    {
        var match = Field();
        match.Ring(1, Low, High);
        match.UnseenRing(1, (16, 10), (20, 14));
        match.WeakenWalls(24);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var outer = match.WallOn(14, 12);
        var inner = match.WallOn(16, 12);
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), unit);

        // Its player knows the outer ring alone: the order takes the unit to the nearest tile
        // between the two.
        match.Run(1);
        var given = match.UnitOf(unit);
        Assert.Equal((outer, Center(15, 12), Center(18, 12)), (given.WallInWay, given.Goal, given.Ordered));
        Assert.False(Knows(match, 0, inner));

        while (match.IsAlive(outer))
        {
            Assert.True(match.World.Tick < 600, "The outer tile still stands.");
            match.Run(1);
            AssertKnowsItsWalls(match, 0, unit);
        }

        // By then it has seen the inner ring, and its order takes it through that as well.
        match.Run(1 + match.Combat.LookInterval);
        var learned = match.UnitOf(unit);
        Assert.Equal((UnitOrder.Move, inner, Center(18, 12)), (learned.Order, learned.WallInWay, learned.Goal));

        match.Run(600);
        Assert.Equal([outer, inner], match.Damaged(walls));
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void AnOrderOntoABuildingInsideWallsItsPlayerHasNeverSeenLeadsThroughThemOnceTheUnitStandsBeforeThem()
    {
        // The place is on a Barracks that covers tiles 17 to 19 by 11 to 13.
        var match = WithUnseenCamp();
        match.PlaceStructure(1, Barracks, 17, 11);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var inWay = match.WallOn(14, 12);
        match.OrderMove(0, Center(18, 12), unit);

        match.Run(1);
        Assert.Equal((EntityId.None, Center(13, 12), Center(18, 12)), (match.UnitOf(unit).WallInWay, match.UnitOf(unit).Goal, match.UnitOf(unit).Ordered));

        // No unit can stand on the place, so it is not what the walls keep the unit from. The
        // unit walks to the nearest tile it can reach, and there its goal is worked out again.
        while (match.UnitOf(unit).WallInWay == EntityId.None)
        {
            Assert.True(match.World.Tick < 200, "The unit never found the wall.");
            match.Run(1);
        }

        Assert.Equal(Center(13, 12), match.PositionOf(unit));
        Assert.Equal((UnitOrder.Move, inWay, Center(16, 12)), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay, match.UnitOf(unit).Goal));

        match.Run(600);
        Assert.False(match.IsAlive(inWay));
        Assert.Equal(Center(16, 12), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    [Fact]
    public void AWorkerOrderedBehindUnseenWallsGoesToTheNearestTileItCanReach()
    {
        var match = WithUnseenCamp();
        var worker = match.SpawnWorker(0, "5.5", "12.5");
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), worker);

        match.Run(400);

        Assert.Equal(Center(13, 12), match.PositionOf(worker));
        Assert.True(match.Stands(worker));
        Assert.True(Knows(match, 0, match.WallOn(14, 12)));
        Assert.Empty(match.Damaged(walls));
        Assert.Equal(0, match.Breaching.SearchCount);
    }

    [Fact]
    public void AGroupOrderedBehindUnseenWallsBreaksInAndNoUnitGoesForAWallItsPlayerDoesNotKnow()
    {
        var match = WithUnseenCamp(wallHitPoints: 100);
        var units = new List<EntityId>();
        foreach (var y in new[] { "11.5", "12.5", "13.5" })
        {
            units.Add(match.Spawn(0, Hoplite, "4.5", y));
            units.Add(match.Spawn(0, Hoplite, "5.5", y));
        }

        match.OrderMove(0, Center(18, 12), [.. units]);
        match.Run(1);
        Assert.All(units, unit => Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay));
        Assert.Equal(0, match.Breaching.SearchCount);

        for (var tick = 0; tick < 1500; tick++)
        {
            match.Run(1);
            AssertKnowsItsWalls(match, 0, [.. units]);
        }

        Assert.All(units, unit =>
        {
            Assert.True(match.IsInside(unit, Low, High));
            Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        });
    }

    [Fact]
    public void AnAttackOrderLearnsOfTheWallBetweenTheUnitAndItsTarget()
    {
        var match = Field();
        match.UnseenRing(1, WideLow, WideHigh);
        match.WeakenWalls(24);
        var unit = match.Spawn(0, Hoplite, "1.5", "12.5");
        var target = match.SpawnWorker(1, "18.5", "12.5");
        match.Watcher(0, "17.5", "11.5");
        var inWay = match.WallOn(10, 12);
        var walls = match.WallHitPoints();
        Assert.All(walls.Keys, wall => Assert.False(Knows(match, 0, wall)));
        Assert.True(match.Vision.Sees(0, target));
        match.OrderAttack(0, target, unit);

        // Its player sees the target and no wall: the unit walks after it as far as it can.
        match.Run(1);
        Assert.Equal((UnitOrder.Attack, target, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).Target, match.UnitOf(unit).WallInWay));
        while (match.UnitOf(unit).WallInWay == EntityId.None)
        {
            Assert.True(match.World.Tick < 200, "The unit never found the wall.");
            match.Run(1);
            Assert.Equal((UnitOrder.Attack, target), (match.UnitOf(unit).Order, match.UnitOf(unit).Target));
        }

        Assert.Equal(inWay, match.UnitOf(unit).WallInWay);
        Assert.True(Knows(match, 0, inWay));

        match.Run(800);
        Assert.False(match.IsAlive(inWay));
        Assert.False(match.IsAlive(target));
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    /// <summary>
    /// Design 5.3: a unit target lost from sight ends the attack order with a walk to where it
    /// was last seen. That walk is a move order, and walls its player knows of come first.
    /// </summary>
    [Fact]
    public void AUnitWhoseTargetIsLostFromSightBehindWallsBreaksInAndIsIdleWhereItWasLastSeen()
    {
        var match = Field();
        match.Ring(1, WideLow, WideHigh);
        match.WeakenWalls(24);
        var unit = match.Spawn(0, Hoplite, "1.5", "12.5");
        var target = match.SpawnWorker(1, "16.5", "10.5");

        // In a corner of the camp: it sees the target where it stands, and not where it goes.
        match.Watcher(0, "12.5", "6.5");
        var walls = match.WallHitPoints();
        match.OrderAttack(0, target, unit);
        match.OrderMove(1, Center(24, 18), target);

        match.Run(1);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(unit).Order);
        Assert.NotEqual(EntityId.None, match.UnitOf(unit).WallInWay);
        var lastSeen = match.PositionOf(target);
        while (match.UnitOf(unit).Order == UnitOrder.Attack)
        {
            Assert.True(match.World.Tick < 200, "The target was never lost from sight.");
            match.Run(1);
            if (match.Vision.Sees(0, target))
            {
                lastSeen = match.PositionOf(target);
            }
        }

        var lost = match.UnitOf(unit);
        Assert.Equal((UnitOrder.Move, EntityId.None), (lost.Order, lost.Target));
        Assert.Equal((lastSeen, lastSeen), (lost.Ordered, lost.Goal));
        Assert.False(match.IsInside(unit, WideLow, WideHigh));
        Assert.Empty(match.Damaged(walls));

        match.Run(1 + match.Combat.LookInterval);
        Assert.NotEqual(EntityId.None, match.UnitOf(unit).WallInWay);
        while (match.UnitOf(unit).Order != UnitOrder.Idle)
        {
            Assert.True(match.World.Tick < 1200, "The unit never ended its order.");
            Assert.Equal((UnitOrder.Move, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).Target));
            match.Run(1);
        }

        Assert.Equal(lastSeen, match.PositionOf(unit));
        Assert.Single(match.Damaged(walls));
    }

    [Fact]
    public void AnAttackOrderMayNameAWallTileItsPlayerRemembersAndNoneItHasNeverSeen()
    {
        var match = Field();
        var remembered = match.WallTile(1, 20, 12);
        var unseen = match.UnseenWallTile(1, 20, 16);
        match.WeakenWalls(24);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var views = new[] { new PlayerView(match, 0), new PlayerView(match, 1) };

        match.OrderAttack(0, unseen, unit);
        match.Run(1);
        var refused = Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        Assert.True(views[0].Knows(in refused));
        Assert.False(views[1].Knows(in refused));

        match.OrderAttack(0, remembered, unit);
        match.Run(1);
        Assert.Empty(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal((UnitOrder.Attack, remembered), (match.UnitOf(unit).Order, match.UnitOf(unit).Target));
        Assert.False(match.Vision.Sees(0, remembered));

        match.Run(400);
        Assert.False(match.IsAlive(remembered));
        Assert.True(match.IsAlive(unseen));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
    }

    /// <summary>
    /// Design 5.3: an attack order on a remembered structure that is gone is taken, and the unit
    /// goes to look. Walls its player knows of that keep it from there come first.
    /// </summary>
    [Fact]
    public void AnAttackOrderOnARememberedStructureThatIsGoneLeadsThroughTheWalls()
    {
        var match = Field();
        match.Ring(1, Low, High);
        match.WeakenWalls(24);
        var barracks = match.PlaceStructure(1, Barracks, 17, 11);
        match.MakeKnown(barracks);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var inWay = match.WallOn(14, 12);
        var walls = match.WallHitPoints();
        match.World.Entities.Destroy(barracks);
        match.Run(1);
        Assert.False(match.IsAlive(barracks));
        Assert.True(match.Vision.Remembers(0, barracks, out _));

        match.OrderAttack(0, barracks, unit);
        match.Run(1);
        var stood = TestMatches.Point("18.5", "12.5");
        var given = match.UnitOf(unit);
        Assert.Empty(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal((UnitOrder.Move, inWay, stood, stood), (given.Order, given.WallInWay, given.Goal, given.Ordered));

        match.Run(600);
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.Equal(stood, match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        Assert.False(match.Vision.Remembers(0, barracks, out _));
    }

    /// <summary>
    /// The decision of the plain walls holds under the fog: on an attack-move, a target that
    /// walls keep out of reach does not hold the unit.
    /// </summary>
    [Fact]
    public void OnAnAttackMoveATargetBehindWallsTheUnitComesToSeeDoesNotHoldIt()
    {
        var match = Field();
        match.UnseenRing(1, WideLow, WideHigh);
        var unit = match.Spawn(0, Peltast, "1.5", "2.5");
        var inside = match.SpawnWorker(1, "18.5", "9.5");
        var full = match.HitPointsOf(inside);
        var walls = match.WallHitPoints();
        Assert.All(walls.Keys, wall => Assert.False(Knows(match, 0, wall)));
        match.OrderAttackMove(0, Center(29, 2), unit);

        var picked = false;
        for (var tick = 0; tick < 500; tick++)
        {
            match.Run(1);
            picked |= match.UnitOf(unit).Target == inside;
            Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);
        }

        Assert.True(picked);
        Assert.Equal(Center(29, 2), match.PositionOf(unit));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        Assert.Equal(full, match.HitPointsOf(inside));
        Assert.Empty(match.Damaged(walls));
    }

    /// <summary>
    /// Who may know of what breaking in does, as for any attack (design 5.3): a hit on a wall
    /// and its fall are known to whoever sees the tile, and to nobody else.
    /// </summary>
    [Fact]
    public void TheHitsOnAWallAndItsFallAreKnownToThoseWhoSeeTheTile()
    {
        var match = WithUnseenCamp(players: 3);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var inWay = match.WallOn(14, 12);
        var views = Enumerable.Range(0, 3).Select(player => new PlayerView(match, player)).ToArray();
        match.OrderMove(0, Center(18, 12), unit);

        var hits = 0;
        var falls = 0;
        while (match.IsAlive(inWay))
        {
            Assert.True(match.World.Tick < 600, "The wall tile still stands.");
            match.Run(1);
            foreach (var reported in match.Simulation.Events)
            {
                if (reported.Entity != inWay || reported.Kind is not (SimEventKind.MeleeHit or SimEventKind.Death))
                {
                    continue;
                }

                hits += reported.Kind == SimEventKind.MeleeHit ? 1 : 0;
                falls += reported.Kind == SimEventKind.Death ? 1 : 0;
                Assert.True(views[0].Knows(in reported));
                Assert.True(views[1].Knows(in reported));
                Assert.False(views[2].Knows(in reported));
            }
        }

        Assert.True(hits > 0);
        Assert.Equal(1, falls);
    }

    [Fact]
    public void TheWallMapKnowsForEachPlayerTheWallsThePlayerKnows()
    {
        var match = WithUnseenCamp();
        var map = match.WallMap;
        var tile = match.WallOn(14, 12);

        // Nobody has seen the camp: to player 0 it is ground closed off, to its owner too.
        Assert.Equal(tile, map.WallAt(14, 12));
        Assert.Equal(EntityId.None, map.KnownWallAt(0, 14, 12));
        Assert.Equal(EntityId.None, map.KnownWallAt(1, 14, 12));
        Assert.NotEqual(map.PastWallsOf(0, 5, 12), map.PastWallsOf(0, 18, 12));
        Assert.NotEqual(0, map.PastWallsOf(0, 18, 12));
        Assert.False(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));

        // A unit of player 0 comes to see three tiles of it.
        match.Spawn(0, Hoplite, "6.75", "12.5");
        Assert.Equal(tile, map.KnownWallAt(0, 14, 12));
        Assert.NotEqual(EntityId.None, map.KnownWallAt(0, 14, 11));
        Assert.NotEqual(EntityId.None, map.KnownWallAt(0, 14, 13));
        Assert.Equal(EntityId.None, map.KnownWallAt(0, 14, 10));
        Assert.Equal(EntityId.None, map.KnownWallAt(0, 22, 12));
        Assert.Equal(map.PastWallsOf(0, 5, 12), map.PastWallsOf(0, 18, 12));
        Assert.Equal(0, map.PastWallsOf(0, 14, 12));
        Assert.Equal(0, map.PastWallsOf(0, 14, 10));
        Assert.True(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));
        Assert.Equal(tile, match.Breaching.FindWall(0, Center(5, 12), Center(18, 12), out _));

        // What the player knows is looked at again when a memory is written, and when the fog is.
        var east = match.WallOn(22, 12);
        match.MakeKnown(east);
        Assert.Equal(east, map.KnownWallAt(0, 22, 12));
        match.Vision.Grid.Reveal(0, Center(22, 8), Sim.Numerics.Fixed.Zero);
        Assert.Equal(match.WallOn(22, 8), map.KnownWallAt(0, 22, 8));
        Assert.Equal(EntityId.None, map.KnownWallAt(0, 21, 8));

        // The player remembers them when it sees them no longer.
        match.Bodies.Get(match.Combatants.EntityAt(0)).Position = TestMatches.Point("2.5", "12.5");
        match.RefreshDerivedState();
        match.Vision.Refresh();
        Assert.False(match.Vision.Sees(0, tile));
        Assert.Equal(tile, map.KnownWallAt(0, 14, 12));
        Assert.True(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));

        // A tile that is gone is no wall, known or not.
        match.World.Entities.Destroy(tile);
        match.RefreshDerivedState();
        Assert.Equal(EntityId.None, map.KnownWallAt(0, 14, 12));
        Assert.False(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));
    }

    private static (Match Match, EntityId[] Group, EntityId Lone, EntityId Inside) Besiegers()
    {
        var match = Field();
        match.Ring(1, Low, High);
        match.UnseenRing(1, (16, 10), (20, 14));
        match.WeakenWalls(40);
        EntityId[] group =
        [
            match.Spawn(0, Hoplite, "3.5", "11.5"),
            match.Spawn(0, Hoplite, "3.5", "12.5"),
            match.Spawn(0, Peltast, "3.5", "13.5"),
        ];
        var lone = match.Spawn(0, Hoplite, "29.5", "12.5");
        var inside = match.SpawnWorker(1, "18.5", "12.5");
        match.RefreshDerivedState();
        return (match, group, lone, inside);
    }

    /// <summary>
    /// A group breaks into a camp whose outer ring its player remembers and whose inner ring it
    /// has never seen, and a unit goes for a tile of the inner ring from the other side. What
    /// each player knows of the walls is worked out from the state, so a restored match goes on
    /// as the one it was taken from.
    /// </summary>
    [Fact]
    public void AMatchWithUnitsLearningOfWallsRestoresAndContinues()
    {
        var (probe, group, lone, inside) = Besiegers();
        var script = new List<(int Tick, Command Command)>
        {
            (0, CombatMatches.MoveCommand(0, Center(18, 12), group)),
            (2, CombatMatches.AttackMoveCommand(0, Center(18, 11), lone)),
        };
        foreach (var (tick, command) in script)
        {
            probe.Simulation.Commands.Enqueue(tick, command);
        }

        var walls = probe.WallHitPoints();
        var learned = new SortedSet<int>();
        var goals = group.Append(lone).ToDictionary(unit => unit, _ => default(Sim.Numerics.FixedVector2));
        for (var tick = 0; tick < 1500; tick++)
        {
            probe.Simulation.Tick();
            foreach (var unit in goals.Keys)
            {
                var goal = probe.UnitOf(unit).Goal;
                if (tick > 2 && goal != goals[unit])
                {
                    learned.Add(tick);
                }

                goals[unit] = goal;
            }
        }

        Assert.True(learned.Count >= 2, "No unit learned of a wall.");
        Assert.InRange(probe.Damaged(walls).Count, 3, 8);
        Assert.False(probe.IsAlive(inside));
        Assert.All(goals.Keys, unit => Assert.Equal(UnitOrder.Idle, probe.UnitOf(unit).Order));

        // Round each tick in which a unit learned of a wall, and at other times.
        int[] others = [0, 1, 2, 3, 4, 30, 150, 600, 1200];
        foreach (var snapshotTick in learned.SelectMany(tick => new[] { tick - 1, tick, tick + 1 }).Concat(others).Distinct().Order())
        {
            SnapshotAssert.RestoresAndContinues(() => Besiegers().Match, script, snapshotTick, lastTick: 1500);
        }
    }
}
