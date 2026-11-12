using Sim.Combat;
using Sim.Construction;
using Sim.Core;
using Sim.Movement;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;
using static Sim.Tests.Walls.WalledMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// Gates on the map (design 2.5; design 8.4, rows 4 and 17): a gate tile is a wall tile that its
/// owner's units walk through once it is finished. It blocks everyone else, and to them it is a
/// wall tile in everything: in the wall rule, in what a unit picks, and in what a melee hit may
/// cross.
/// </summary>
/// <remarks>
/// The camp of these tests is the one of <see cref="WallRuleTests"/>: a ring of player 1's wall
/// on the edge of the box from tile (14, 8) to tile (22, 16), with a gate in place of the wall
/// tile at (14, 12), which is where a unit coming from the west along y = 12.5 meets the ring.
/// </remarks>
public class GateTests
{
    private const string Gate = "gate";

    private static readonly (int X, int Y) Low = (14, 8);
    private static readonly (int X, int Y) High = (22, 16);
    private static readonly (int X, int Y) GateTile = (14, 12);

    /// <summary>
    /// The camp of player 1 with a finished gate, on the west side unless another tile of the
    /// ring is named. A second tile of the ring may be left open.
    /// </summary>
    private static Match WithCamp((int X, int Y)? gate = null, int wallHitPoints = 24, (int X, int Y)? gap = null)
    {
        var match = Field();
        var (gateX, gateY) = gate ?? GateTile;
        for (var y = Low.Y; y <= High.Y; y++)
        {
            for (var x = Low.X; x <= High.X; x++)
            {
                if ((x == Low.X || x == High.X || y == Low.Y || y == High.Y) && (x, y) != (gateX, gateY) && (x, y) != gap)
                {
                    match.WallTile(1, x, y);
                }
            }
        }

        match.PlaceStructure(1, Gate, gateX, gateY);
        match.WeakenWalls(wallHitPoints);
        return match;
    }

    [Fact]
    public void AFinishedGateIsOpenToItsOwnerAndToNobodyElse()
    {
        var match = WithCamp();
        var gate = match.WallOn(14, 12);

        Assert.Equal(match.Data.Gate!.Index, match.Structures.Get(gate).Type);
        Assert.Equal(match.HitPointsOf(match.WallOn(14, 11)), match.HitPointsOf(gate));
        Assert.True(match.WallMap.IsWall(match.Data.Gate.Index));

        // Nobody may build on it, its owner included: it is under a structure.
        Assert.False(match.Grid.IsPassable(14, 12));
        Assert.False(match.HasRoomFor(match.Data.StructureOf(Wall), 14, 12));

        // Player 0 has no gate and walks the grid everyone shares; player 1 walks through.
        Assert.Same(match.Grid, match.Grid.For(0));
        Assert.True(match.Grid.For(1).IsPassable(14, 12));
        Assert.False(match.Grid.For(1).IsPassable(14, 11));
        Assert.Equal(match.Grid.For(1).RegionOf(5, 12), match.Grid.For(1).RegionOf(18, 12));
        Assert.NotEqual(match.Grid.RegionOf(5, 12), match.Grid.RegionOf(18, 12));
    }

    [Theory]
    [InlineData(Hoplite, false)]
    [InlineData(Peltast, true)]
    public void ItsOwnersUnitsWalkThroughIt(string chassis, bool attackMove)
    {
        var match = WithCamp();
        var unit = match.Spawn(1, chassis, "5.5", "12.5");
        var worker = match.SpawnWorker(1, "5.5", "13.5");
        var walls = match.WallHitPoints();
        if (attackMove)
        {
            match.OrderAttackMove(1, Center(18, 12), unit);
        }
        else
        {
            match.OrderMove(1, Center(18, 12), unit);
        }

        match.OrderMove(1, Center(18, 13), worker);

        var through = new HashSet<EntityId>();
        for (var tick = 0; tick < 400; tick++)
        {
            match.Run(1);
            Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);
            foreach (var walker in new[] { unit, worker })
            {
                if (match.TileOf(walker) == GateTile)
                {
                    through.Add(walker);
                }
            }
        }

        Assert.Equal([unit, worker], through.Order());
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal(Center(18, 13), match.PositionOf(worker));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);
        Assert.Empty(match.Damaged(walls));
        Assert.Equal(0, match.Breaching.SearchCount);
    }

    [Theory]
    [InlineData(Hoplite, SimEventKind.MeleeHit)]
    [InlineData(Peltast, SimEventKind.ShotHit)]
    public void AnEnemyGateIsTheWallTileInAUnitsWayWhereItIsTheFirstOnTheWay(string chassis, SimEventKind hit)
    {
        var match = WithCamp();
        var unit = match.Spawn(0, chassis, "5.5", "12.5");
        var gate = match.WallOn(14, 12);
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), unit);

        match.Run(1);
        Assert.Equal((UnitOrder.Move, gate), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));

        var hits = new List<SimEvent>();
        while (match.IsAlive(gate))
        {
            Assert.True(match.World.Tick < 600, "The gate still stands.");
            match.Run(1);
            hits.AddRange(match.EventsOf(hit));
            Assert.False(match.IsInside(unit, Low, High));
        }

        Assert.NotEmpty(hits);
        Assert.All(hits, reported => Assert.Equal(gate, reported.Entity));
        Assert.Equal([gate], match.Damaged(walls));

        // The tile is free for everyone once the gate is gone, and its owner has no grid of its own any more.
        Assert.True(match.Grid.IsPassable(14, 12));
        Assert.Same(match.Grid, match.Grid.For(1));
        match.Run(200);
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
    }

    [Fact]
    public void AGateCostsTheSecondSearchWhatAWallTileCosts()
    {
        // The gate is on the far side of the camp. A unit from the west breaks in at the wall
        // tile in its way and does not walk round to the gate.
        var match = WithCamp(gate: (22, 12));
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        match.OrderMove(0, Center(18, 12), unit);

        match.Run(1);

        Assert.Equal(match.WallOn(14, 12), match.UnitOf(unit).WallInWay);
        Assert.NotEqual(match.WallOn(22, 12), match.UnitOf(unit).WallInWay);
    }

    [Fact]
    public void AGateIsNeverPickedAndIsAttackedOnAnAttackOrder()
    {
        var match = Field();
        var gate = match.PlaceStructure(1, Gate, 10, 12);
        var passing = match.Spawn(0, Hoplite, "5.5", "12.5");
        var ordered = match.Spawn(0, Hoplite, "5.5", "10.5");
        var full = match.HitPointsOf(gate);
        match.OrderAttackMove(0, Center(9, 12), passing);

        match.Run(200);

        // The attack-move ends beside the gate, and the unit stands idle there.
        Assert.Equal(Center(9, 12), match.PositionOf(passing));
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(passing).Order, match.UnitOf(passing).Target));
        Assert.Equal(full, match.HitPointsOf(gate));

        match.OrderAttack(0, gate, ordered);
        match.Run(200);
        Assert.InRange(match.HitPointsOf(gate), 1, full - 1);
        Assert.Equal(gate, match.UnitOf(ordered).Target);
    }

    [Fact]
    public void AGateThatIsStillBuildingItselfIsAWallTileToItsOwnerToo()
    {
        var match = WithCamp();
        match.Health.Get(match.WallOn(14, 12)).HitPoints = 0;
        match.Run(1);
        var built = match.Data.Gate!.BuildTicks - 3;
        var gate = match.StructureFactory.Lay(1, match.Data.Gate, 14, 12, new Foundation { Built = built, Materials = 40 });
        var unit = match.Spawn(1, Hoplite, "5.5", "12.5");
        match.RefreshDerivedState();
        Assert.Same(match.Grid, match.Grid.For(1));
        Assert.False(match.Grid.For(1).IsPassable(14, 12));
        Assert.Equal(gate, match.WallMap.WallAt(14, 12));

        // An order to the ground inside is an order to the nearest tile its unit can reach, as
        // for ground closed off by its owner's walls, and stays one when the gate opens.
        match.OrderMove(1, Center(18, 12), unit);
        match.Run(2);
        Assert.False(match.Grid.For(1).IsPassable(14, 12));
        Assert.Equal(Center(13, 12), match.Movers.Get(unit).Destination);
        Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);

        match.Run(1);
        Assert.Single(match.EventsOf(SimEventKind.StructureFinished));
        Assert.True(match.Grid.For(1).IsPassable(14, 12));
        Assert.False(match.Grid.IsPassable(14, 12));
        match.Run(200);
        Assert.Equal(Center(13, 12), match.PositionOf(unit));

        // The next order leads through.
        match.OrderMove(1, Center(18, 12), unit);
        match.Run(200);
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
    }

    [Fact]
    public void AGateThatOpensGivesItsOwnersUnitsOnTheirWayAShorterWayAndLeavesTheOthersAlone()
    {
        // A wall of player 1 down the column x = 14 from the top of the map to y = 18, with a
        // gate that is three ticks from finished at (14, 12). The way round is by the south.
        var match = Field();
        for (var y = 0; y <= 18; y++)
        {
            if (y != 12)
            {
                match.WallTile(1, 14, y);
            }
        }

        var gate = match.StructureFactory.Lay(1, match.Data.Gate!, 14, 12, new Foundation { Built = match.Data.Gate!.BuildTicks - 3 });
        var own = match.SpawnWorker(1, "5.5", "12.5");
        var other = match.SpawnWorker(0, "5.5", "10.5");
        match.RefreshDerivedState();
        match.OrderMove(1, Center(20, 12), own);
        match.OrderMove(0, Center(20, 10), other);

        match.Run(2);
        var before = (Own: match.Movers.Get(own), Other: match.Movers.Get(other));
        Assert.Equal((MoveState.Following, MoveState.Following), (before.Own.State, before.Other.State));
        var searches = match.Routes.SearchCount;

        match.Run(1);

        // The gate is finished in this tick. Its owner's unit is sent again, and its way is now
        // a straight line through the gate; the other unit keeps the walk it has.
        Assert.True(match.Construction.IsFinished(gate));
        Assert.Equal((MoveState.Moving, Center(20, 12)), (match.Movers.Get(own).State, match.Movers.Get(own).Destination));
        Assert.Equal(MoveState.Following, match.Movers.Get(other).State);
        Assert.Equal((before.Other.RouteStart, before.Other.RouteGoal), (match.Movers.Get(other).RouteStart, match.Movers.Get(other).RouteGoal));
        Assert.Equal(before.Other.Destination, match.Movers.Get(other).Destination);

        var through = false;
        var round = false;
        for (var tick = 0; tick < 500; tick++)
        {
            match.Run(1);
            through |= match.TileOf(own) == GateTile;
            Assert.NotEqual(GateTile, match.TileOf(other));
            round |= match.TileOf(other).Y > 18;
            Assert.False(match.TileOf(own).Y > 14, "The owner's unit walks round.");
        }

        Assert.True(through);
        Assert.True(round);
        Assert.Equal(Center(20, 12), match.PositionOf(own));
        Assert.Equal(Center(20, 10), match.PositionOf(other));

        Assert.True(match.Routes.SearchCount >= searches);
    }

    [Fact]
    public void AUnitInItsOwnGateIsHitFromTheTileBesideItAndNotPickedByAMeleeUnit()
    {
        var match = WithCamp();
        var inGate = match.Spawn(1, Hoplite, "14.5", "12.5");
        var idle = match.Spawn(0, Hoplite, "13.5", "12.5");
        var across = match.Spawn(0, Hoplite, "13.5", "11.5");
        match.RefreshDerivedState();
        var targeting = match.Targeting();
        var enemy = match.Combat.ProfileOf(0, CombatMatches.Chassis(Hoplite));
        var owner = match.Combat.ProfileOf(1, CombatMatches.Chassis(Hoplite));
        var line = CombatMatches.Line("line_infantry");

        // Design 8.4, row 4: the tiles the two stand in are not counted, so the two reach each
        // other across the edge of the gate. A line through the corner of the gate tile needs the
        // wall tile beside it, which no hit crosses.
        Assert.True(targeting.Reaches(enemy, Center(13, 12), Center(14, 12), line));
        Assert.True(targeting.Reaches(owner, Center(14, 12), Center(13, 12), line));
        Assert.False(targeting.Reaches(enemy, Center(13, 11), Center(14, 12), line));
        Assert.False(targeting.Reaches(owner, Center(14, 12), Center(13, 11), line));

        // Row 17: a melee unit picks only on ground it can reach, and the gate is not such
        // ground. The unit in the gate picks the enemy before it.
        match.Run(8);
        Assert.Equal(idle, match.UnitOf(inGate).Target);
        Assert.Equal(EntityId.None, match.UnitOf(across).Target);
        Assert.NotEqual(inGate, match.UnitOf(across).Target);
        Assert.Equal(match.HitPointsOf(across), match.Health.Get(across).Maximum);
    }

    [Fact]
    public void AnAttackOrderOnAUnitInItsGateIsCarriedOutFromTheTileBesideIt()
    {
        var match = WithCamp(wallHitPoints: 400);
        var inGate = match.SpawnWorker(1, "14.5", "12.5");
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        match.RefreshDerivedState();
        var walls = match.WallHitPoints();
        match.OrderAttack(0, inGate, unit);

        match.Run(1);
        Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);
        match.Run(400);

        Assert.False(match.IsAlive(inGate));
        Assert.Equal((13, 12), match.TileOf(unit));
        Assert.Empty(match.Damaged(walls));
    }

    [Fact]
    public void AUnitMayBeMadeOnItsOwnersGateAndNotOnAnEnemys()
    {
        var match = WithCamp();
        match.OrderSpawn(1, Hoplite, "14.5", "12.5");
        match.OrderSpawn(0, Hoplite, "14.5", "12.5");

        match.Run(1);

        Assert.Equal(1, match.Movers.Count);
        Assert.Equal(1, match.Bodies.Get(match.Movers.EntityAt(0)).Owner);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Throws<ArgumentException>(() => match.Spawn(0, Hoplite, "14.5", "12.5"));
    }

    [Fact]
    public void ADebugOrderPlacesAFinishedGate()
    {
        var match = Field();
        match.Simulation.Commands.Enqueue(0, PlaceCommand(1, match.Data.Gate!.Index, Center(10, 12)));

        match.Run(1);

        var gate = match.WallOn(10, 12);
        Assert.Equal(match.Data.Gate.Index, match.Structures.Get(gate).Type);
        Assert.Equal(match.Data.Gate.Index, match.EventsOf(SimEventKind.StructurePlaced).Single().Amount);
        Assert.True(match.Grid.For(1).IsPassable(10, 12));
        Assert.False(match.Grid.For(0).IsPassable(10, 12));
    }

    private static (Match Match, EntityId[] Own, EntityId[] Others) Camp()
    {
        var match = WithCamp(wallHitPoints: 60, gap: (18, 16));
        match.StructureFactory.Lay(1, match.Data.Gate!, 18, 16, new Foundation { Built = 20, Materials = 40 });
        EntityId[] own =
        [
            match.Spawn(1, Hoplite, "5.5", "11.5"),
            match.Spawn(1, Peltast, "5.5", "13.5"),
            match.SpawnWorker(1, "18.5", "20.5"),
        ];
        EntityId[] others =
        [
            match.Spawn(0, Hoplite, "4.5", "11.5"),
            match.Spawn(0, Hoplite, "4.5", "12.5"),
            match.Spawn(0, Peltast, "18.5", "21.5"),
        ];
        match.RefreshDerivedState();
        return (match, own, others);
    }

    /// <summary>
    /// A camp with a finished gate and one that is finished on the way: its owner's units walk
    /// in through the first, and one of them out again through the second, while three enemies
    /// break in.
    /// </summary>
    [Fact]
    public void AMatchWithGatesRestoresAndContinues()
    {
        var (probe, own, others) = Camp();
        var script = new List<(int Tick, Command Command)>
        {
            (0, CombatMatches.MoveCommand(1, Center(18, 12), own[0], own[1])),
            (1, CombatMatches.MoveCommand(1, Center(18, 13), own[2])),
            (5, CombatMatches.MoveCommand(0, Center(17, 11), others[0], others[1])),
            (40, CombatMatches.AttackMoveCommand(0, Center(18, 10), others[2])),
            (260, CombatMatches.MoveCommand(1, Center(18, 20), own[0])),
        };
        foreach (var (tick, command) in script)
        {
            probe.Simulation.Commands.Enqueue(tick, command);
        }

        // What the run holds, so that the snapshots below are of what they are meant to be.
        probe.Simulation.Run(100);
        Assert.NotSame(probe.Grid, probe.Grid.For(1));
        Assert.All(others.Take(2), unit => Assert.NotEqual(EntityId.None, probe.UnitOf(unit).WallInWay));
        Assert.False(probe.Grid.For(1).IsPassable(18, 16));
        probe.Simulation.Run(100);
        Assert.True(probe.Grid.For(1).IsPassable(18, 16));
        probe.Simulation.Run(500);
        Assert.All(others, unit => Assert.True(probe.IsInside(unit, Low, High)));

        foreach (var snapshotTick in new[] { 0, 1, 2, 6, 41, 100, 179, 180, 181, 261, 300, 500 })
        {
            SnapshotAssert.RestoresAndContinues(() => Camp().Match, script, snapshotTick, lastTick: 700);
        }
    }
}
