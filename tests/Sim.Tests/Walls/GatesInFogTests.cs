using Sim.Combat;
using Sim.Core;
using Sim.Setup;
using Sim.Structures;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.SiegeMatches;
using static Sim.Tests.Walls.WalledMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// Gates under the fog of war (design 2.5 and 5.3): to another player a gate is a wall tile, so
/// it is one the player's units may be sent to break only when the player sees or remembers it,
/// and until then it is ground nobody can reach. To its owner a finished gate is open ground,
/// which needs nobody's sight.
/// </summary>
/// <remarks>
/// The camp is that of <see cref="GateTests"/> and <see cref="WallsInFogTests"/>: a ring of
/// player 1 on the edge of the box from tile (14, 8) to tile (22, 16), with a finished gate in
/// place of the wall tile at (14, 12). Nobody else has seen any of it.
/// </remarks>
public class GatesInFogTests
{
    private const string Gate = "gate";

    private static readonly (int X, int Y) Low = (14, 8);
    private static readonly (int X, int Y) High = (22, 16);
    private static readonly (int X, int Y) GateTile = (14, 12);

    private static Match WithUnseenCamp(int players = 2)
    {
        var match = Create(TestMaps.Open(32, 24), players: players);
        for (var y = Low.Y; y <= High.Y; y++)
        {
            for (var x = Low.X; x <= High.X; x++)
            {
                if ((x == Low.X || x == High.X || y == Low.Y || y == High.Y) && (x, y) != GateTile)
                {
                    match.UnseenWallTile(1, x, y);
                }
            }
        }

        match.PlaceStructure(1, Gate, GateTile.X, GateTile.Y);
        match.WeakenWalls(24);
        return match;
    }

    private static bool Knows(Match match, int player, EntityId wall) => match.Vision.KnowsStructure(player, wall);

    [Theory]
    [InlineData(Hoplite, false)]
    [InlineData(Peltast, true)]
    public void AUnitOrderedBehindAGateItsPlayerHasNeverSeenLearnsOfItOnItsWayAndBreaksIt(string chassis, bool attackMove)
    {
        var match = WithUnseenCamp();
        var unit = match.Spawn(0, chassis, "5.5", "12.5");
        var gate = match.WallOn(14, 12);
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

        // The gate is ground nobody can reach to a player who has never seen it, as a wall
        // tile is: the unit goes for the nearest tile outside, and no search runs.
        match.Run(1);
        var given = match.UnitOf(unit);
        Assert.Equal((attackMove ? UnitOrder.AttackMove : UnitOrder.Move, EntityId.None), (given.Order, given.WallInWay));
        Assert.Equal((Center(13, 12), Center(18, 12)), (given.Goal, given.Ordered));
        Assert.Equal(0, match.Breaching.SearchCount);

        while (match.UnitOf(unit).WallInWay == EntityId.None)
        {
            Assert.True(match.World.Tick < 200, "The unit never found the gate.");
            Assert.False(Knows(match, 0, gate) && match.World.Tick > 2 && match.UnitOf(unit).Goal != Center(18, 12) && match.Stands(unit));
            match.Run(1);
        }

        // Once its player has seen the gate it is the wall tile in the unit's way.
        var learned = match.UnitOf(unit);
        Assert.Equal(gate, learned.WallInWay);
        Assert.True(Knows(match, 0, gate));
        Assert.Equal((given.Order, Center(18, 12)), (learned.Order, learned.Goal));

        match.Run(700);
        Assert.Equal([gate], match.Damaged(walls));
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Equal((UnitOrder.Idle, EntityId.None), (match.UnitOf(unit).Order, match.UnitOf(unit).WallInWay));
    }

    [Fact]
    public void OfARingWhoseGateItsPlayerHasNeverSeenAUnitBreaksAWallTileItKnows()
    {
        // Player 0 remembers every wall tile of the ring and has never seen the gate, which is
        // the tile straight before its unit.
        var match = WithUnseenCamp();
        var gate = match.WallOn(14, 12);
        foreach (var wall in match.WallHitPoints().Keys.Where(wall => wall != gate))
        {
            match.MakeKnown(wall);
        }

        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");
        var walls = match.WallHitPoints();
        match.OrderMove(0, Center(18, 12), unit);

        match.Run(1);
        var inWay = match.UnitOf(unit).WallInWay;
        Assert.NotEqual(EntityId.None, inWay);
        Assert.NotEqual(gate, inWay);
        Assert.Contains(inWay, new[] { match.WallOn(14, 11), match.WallOn(14, 13) });
        Assert.Equal(EntityId.None, match.WallMap.KnownWallAt(0, 14, 12));

        // It keeps that tile when it comes to see the gate beside it.
        while (match.IsAlive(inWay))
        {
            Assert.True(match.World.Tick < 600, "The wall tile still stands.");
            match.Run(1);
            Assert.True(!match.IsAlive(inWay) || match.UnitOf(unit).WallInWay == inWay);
        }

        Assert.True(Knows(match, 0, gate));
        Assert.Equal(gate, match.WallMap.KnownWallAt(0, 14, 12));
        match.Run(300);
        Assert.Equal([inWay], match.Damaged(walls));
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
    }

    [Fact]
    public void TheWallMapKnowsAGateForEachPlayerThatKnowsItAndNeverForItsOwner()
    {
        var match = WithUnseenCamp(players: 3);
        var map = match.WallMap;
        var gate = match.WallOn(14, 12);
        Assert.Equal(gate, map.WallAt(14, 12));

        // To its owner the gate is ground: the camp is one with the field outside, whoever
        // sees what.
        Assert.Equal(EntityId.None, map.KnownWallAt(1, 14, 12));
        Assert.NotEqual(0, map.PastWallsOf(1, 14, 12));
        Assert.Equal(map.PastWallsOf(1, 5, 12), map.PastWallsOf(1, 18, 12));
        Assert.Equal(map.PastWallsOf(1, 5, 12), map.PastWallsOf(1, 14, 12));
        Assert.False(match.Breaching.IsCutOff(1, Center(5, 12), Center(18, 12)));

        // To the others, who have seen nothing of the camp, it is ground closed off.
        foreach (var player in new[] { 0, 2 })
        {
            Assert.Equal(EntityId.None, map.KnownWallAt(player, 14, 12));
            Assert.Equal(0, map.PastWallsOf(player, 14, 12));
            Assert.NotEqual(map.PastWallsOf(player, 5, 12), map.PastWallsOf(player, 18, 12));
            Assert.False(match.Breaching.IsCutOff(player, Center(5, 12), Center(18, 12)));
        }

        // Player 0 comes to see the gate and the tiles beside it: a wall tile in its way, as
        // the gate of an enemy is. Player 2 and the owner are where they were.
        match.Spawn(0, Hoplite, "6.75", "12.5");
        Assert.Equal(gate, map.KnownWallAt(0, 14, 12));
        Assert.Equal(0, map.PastWallsOf(0, 14, 12));
        Assert.Equal(map.PastWallsOf(0, 5, 12), map.PastWallsOf(0, 18, 12));
        Assert.True(match.Breaching.IsCutOff(0, Center(5, 12), Center(18, 12)));
        Assert.Equal(gate, match.Breaching.FindWall(0, Center(5, 12), Center(18, 12), out _));

        Assert.Equal(EntityId.None, map.KnownWallAt(2, 14, 12));
        Assert.NotEqual(map.PastWallsOf(2, 5, 12), map.PastWallsOf(2, 18, 12));
        Assert.Equal(EntityId.None, map.KnownWallAt(1, 14, 12));
        Assert.Equal(map.PastWallsOf(1, 5, 12), map.PastWallsOf(1, 18, 12));
        Assert.False(match.Breaching.IsCutOff(1, Center(5, 12), Center(18, 12)));
    }

    [Fact]
    public void ItsOwnersUnitsWalkThroughAGateThatNobodySees()
    {
        // The gate sees 2 tiles and the unit 8, so the unit sets out with the gate in the sight
        // of the gate alone; what its owner's units walk is the owner's grid, not what is seen.
        var match = WithUnseenCamp();
        var unit = match.Spawn(1, Hoplite, "2.5", "12.5");
        var walls = match.WallHitPoints();
        match.OrderMove(1, Center(18, 12), unit);

        var through = false;
        for (var tick = 0; tick < 500; tick++)
        {
            match.Run(1);
            Assert.Equal(EntityId.None, match.UnitOf(unit).WallInWay);
            through |= match.TileOf(unit) == GateTile;
        }

        Assert.True(through);
        Assert.Equal(Center(18, 12), match.PositionOf(unit));
        Assert.Empty(match.Damaged(walls));
        Assert.Equal(0, match.Breaching.SearchCount);
    }

    [Fact]
    public void AnAttackOrderMayNameAGateItsPlayerRemembersAndNoneItHasNeverSeen()
    {
        var match = Field();
        var remembered = match.GateTile(1, 20, 12);
        var unseen = match.PlaceStructure(1, Gate, 20, 16);
        match.WeakenWalls(24);
        var unit = match.Spawn(0, Hoplite, "5.5", "12.5");

        match.OrderAttack(0, unseen, unit);
        match.Run(1);
        Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(unit).Order);

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

    private static (Match Match, EntityId Own, EntityId[] Others) Besieged()
    {
        var match = WithUnseenCamp();
        var own = match.Spawn(1, Hoplite, "18.5", "12.5");
        EntityId[] others = [match.Spawn(0, Hoplite, "5.5", "12.5"), match.Spawn(0, Peltast, "4.5", "12.5")];
        match.RefreshDerivedState();
        return (match, own, others);
    }

    /// <summary>
    /// Two units of player 0 ordered into a camp they have never seen learn of its gate and
    /// break it, while a unit of its owner walks out through it and back.
    /// </summary>
    [Fact]
    public void AMatchWithUnitsLearningOfAGateRestoresAndContinues()
    {
        var (probe, own, others) = Besieged();
        var script = new List<(int Tick, Command Command)>
        {
            (0, CombatMatches.MoveCommand(0, Center(18, 12), others)),
            (1, CombatMatches.MoveCommand(1, Center(12, 9), own)),
            (90, CombatMatches.MoveCommand(1, Center(18, 10), own)),
        };
        foreach (var (tick, command) in script)
        {
            probe.Simulation.Commands.Enqueue(tick, command);
        }

        var gate = probe.WallOn(14, 12);
        var walls = probe.WallHitPoints();
        var learned = new SortedSet<int>();
        var before = others.ToDictionary(unit => unit, _ => EntityId.None);
        var through = false;
        for (var tick = 0; tick < 900; tick++)
        {
            probe.Simulation.Tick();
            through |= probe.IsAlive(own) && probe.TileOf(own) == GateTile;
            foreach (var unit in others)
            {
                var inWay = probe.IsAlive(unit) ? probe.UnitOf(unit).WallInWay : EntityId.None;
                if (inWay == gate && before[unit] != gate)
                {
                    learned.Add(tick);
                }

                before[unit] = inWay;
            }
        }

        Assert.True(through, "The owner's unit never stood in its gate.");
        Assert.NotEmpty(learned);
        Assert.Contains(gate, probe.Damaged(walls));

        int[] at = [0, 1, 2, 30, 91, 400, 800];
        foreach (var snapshotTick in learned.SelectMany(tick => new[] { tick - 1, tick, tick + 1 }).Concat(at).Distinct().Order())
        {
            SnapshotAssert.RestoresAndContinues(() => Besieged().Match, script, snapshotTick, lastTick: 900);
        }
    }
}
