using Sim.Combat;
using Sim.Core;
using Sim.Movement;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>The four orders of design 2.4: move, attack-move, attack and stop. A new order replaces the old one at once.</summary>
public class OrderTests
{
    private const long WorkerHitPoints = 50_000;

    private static Match Field() => Create(TestMaps.Open(24, 12));

    [Fact]
    public void AMoveOrderNeverFightsEvenWhenAttackedAndEndsIdleAtItsPost()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "6.5");
        var archer = match.Spawn(1, Peltast, "7.5", "9.5");
        var destination = TestMatches.Point("12.5", "5.5");
        match.OrderMove(0, destination, hoplite);

        // 10 tiles at 0.13 a tick: it arrives in tick 76, having walked straight past both enemies.
        match.Run(1);
        Assert.Equal(UnitOrder.Move, match.UnitOf(hoplite).Order);
        match.Run(75);
        Assert.NotEqual(destination, match.PositionOf(hoplite));
        Assert.Equal(default, match.UnitOf(hoplite).Target);
        match.Run(1);
        Assert.Equal(destination, match.PositionOf(hoplite));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(worker));
        Assert.Equal(60_000, match.HitPointsOf(archer));
        Assert.True(match.HitPointsOf(hoplite) < 130_000);

        // On arrival it is idle, and its post is where it stands.
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(destination, match.UnitOf(hoplite).Anchor);
    }

    [Fact]
    public void AnAttackMoveFightsWhatItPicksOnTheWayThenGoesOnAndEndsIdle()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "6.5");
        match.SetHitPoints(worker, 16_000);
        var destination = TestMatches.Point("12.5", "5.5");
        match.OrderAttackMove(0, destination, hoplite);

        match.Run(1);
        Assert.Equal(UnitOrder.AttackMove, match.UnitOf(hoplite).Order);
        Assert.Equal(destination, match.UnitOf(hoplite).Anchor);

        // It turns on the worker, stops in reach, and needs two hits.
        var ticks = 1;
        for (; match.IsAlive(worker); ticks++)
        {
            Assert.InRange(ticks, 1, 100);
            match.Run(1);
        }

        Assert.NotEqual(destination, match.PositionOf(hoplite));

        match.Run(100);
        Assert.Equal(destination, match.PositionOf(hoplite));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(destination, match.UnitOf(hoplite).Anchor);
        Assert.False(match.UnitOf(hoplite).Engaged);
        Assert.Equal(MoveState.Idle, match.Movers.Get(hoplite).State);
    }

    [Fact]
    public void AnAttackMoveIsAMoveOrderForAUnitWithoutAnAttack()
    {
        var match = Field();
        var worker = match.SpawnWorker(0, "2.5", "5.5");
        var enemy = match.SpawnWorker(1, "4.5", "5.5");
        var destination = TestMatches.Point("8.5", "5.5");
        match.OrderAttackMove(0, destination, worker);
        match.Run(60);

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(destination, match.PositionOf(worker));
        Assert.Equal(WorkerHitPoints, match.HitPointsOf(enemy));
    }

    [Fact]
    public void AnAttackOrderGoesAfterItsTargetUntilItIsDestroyedAndNeverSwapsIt()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var named = match.SpawnWorker(1, "10.5", "5.5");
        var decoy = match.SpawnWorker(1, "5.5", "6.5");
        match.SetHitPoints(named, 8_000);
        match.OrderAttack(0, named, hoplite);

        var ticks = 0;
        for (; match.IsAlive(named); ticks++)
        {
            Assert.InRange(ticks, 0, 100);
            match.Run(1);
            Assert.Equal(WorkerHitPoints, match.HitPointsOf(decoy));
        }

        // Its target is gone: it is idle where it stands, from the next tick on.
        var stood = match.PositionOf(hoplite);
        match.Run(1);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(stood, match.UnitOf(hoplite).Anchor);
        Assert.Equal(stood, match.PositionOf(hoplite));
    }

    [Fact]
    public void AnAttackOrderFollowsATargetThatWalksAway()
    {
        var match = Field();
        var hippeus = match.Spawn(0, Hippeus, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "8.5", "5.5");
        match.OrderAttack(0, worker, hippeus);
        match.OrderMove(1, TestMatches.Point("22.5", "9.5"), worker);
        match.Run(200);

        // 15 a hit against 50 hit points: the fourth hit kills.
        Assert.False(match.IsAlive(worker));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hippeus).Order);
    }

    [Fact]
    public void ANewOrderReplacesTheOldOneAtOnce()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);
        Assert.Equal(WorkerHitPoints - 8_000, match.HitPointsOf(worker));

        // Sent away, it leaves in the same tick and does not strike again.
        match.OrderMove(0, TestMatches.Point("5.5", "1.5"), hoplite);
        match.Run(1);
        Assert.NotEqual(TestMatches.Point("5.5", "5.5"), match.PositionOf(hoplite));
        Assert.Equal(UnitOrder.Move, match.UnitOf(hoplite).Order);
        Assert.Equal(default, match.UnitOf(hoplite).Target);
        match.Run(25);
        Assert.Equal(WorkerHitPoints - 8_000, match.HitPointsOf(worker));

        // And an attack order takes it off its walk in the tick it is given.
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
        Assert.Equal(worker, match.UnitOf(hoplite).Target);
        var before = match.DistanceSquared(hoplite, worker);
        match.Run(1);
        Assert.True(match.DistanceSquared(hoplite, worker) < before);
    }

    [Fact]
    public void AStopOrderLeavesTheUnitIdleWhereItStands()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var worker = match.SpawnWorker(0, "2.5", "7.5");
        match.OrderMove(0, TestMatches.Point("20.5", "5.5"), hoplite, worker);
        match.Run(10);

        match.OrderStop(0, hoplite, worker);
        match.Run(1);
        var stood = match.PositionOf(hoplite);
        var workerStood = match.PositionOf(worker);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(stood, match.UnitOf(hoplite).Anchor);
        Assert.Equal(MoveState.Idle, match.Movers.Get(hoplite).State);
        Assert.Equal(MoveState.Idle, match.Movers.Get(worker).State);

        match.Run(20);
        Assert.Equal(stood, match.PositionOf(hoplite));
        Assert.Equal(workerStood, match.PositionOf(worker));
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void AStopOrderEndsAnAttackOrder()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "20.5", "5.5");

        // A worker of the player's own beside the target: it sees the target, so the order can name it.
        match.SpawnWorker(0, "20.5", "7.5");
        match.OrderAttack(0, worker, peltast);
        match.Run(10);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(peltast).Order);
        match.OrderStop(0, peltast);
        match.Run(1);
        var stood = match.PositionOf(peltast);

        // The worker is far outside its acquisition range, so it stays.
        match.Run(40);
        Assert.Equal(stood, match.PositionOf(peltast));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(peltast).Order);
        Assert.Equal(default, match.UnitOf(peltast).Target);
    }

    [Fact]
    public void AnAttackOrderSkipsTheUnitsThatCannotTakeIt()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var ownWorker = match.SpawnWorker(0, "2.5", "7.5");
        var enemyUnit = match.Spawn(1, Hoplite, "20.5", "1.5");
        var target = match.SpawnWorker(1, "9.5", "5.5");
        match.OrderAttack(0, target, ownWorker, enemyUnit, new EntityId(999), hoplite);
        match.Run(1);

        Assert.Equal(0, match.Simulation.DroppedCommandCount);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(enemyUnit).Order);
        Assert.Equal(MoveState.Idle, match.Movers.Get(ownWorker).State);
        Assert.Equal(TestMatches.Point("2.5", "7.5"), match.PositionOf(ownWorker));
    }

    public static TheoryData<string> InvalidOrders =>
        ["friendly target", "no target", "target that does not exist", "player not in the match", "negative player",
            "only a worker", "only another player's unit", "nobody", "stop nobody", "stop another player's unit",
            "attack-move off the map"];

    [Theory]
    [MemberData(nameof(InvalidOrders))]
    public void AnOrderThatNobodyCanTakeIsDroppedAndChangesNothing(string what)
    {
        Match Build()
        {
            var match = Field();
            match.Spawn(0, Hoplite, "2.5", "5.5");
            match.SpawnWorker(0, "2.5", "7.5");

            // In sight of player 0, so that an attack on it is refused for what the case names and not for the fog.
            match.Spawn(1, Hoplite, "9.5", "5.5");
            match.Spawn(0, Peltast, "3.5", "5.5");
            return match;
        }

        EntityId own = new(1), ownWorker = new(2), enemy = new(3), friend = new(4);
        var command = what switch
        {
            "friendly target" => AttackCommand(0, friend, own),
            "no target" => AttackCommand(0, EntityId.None, own),
            "target that does not exist" => AttackCommand(0, new EntityId(999), own),
            "player not in the match" => AttackCommand(2, enemy, own),
            "negative player" => AttackCommand(-1, enemy, own),
            "only a worker" => AttackCommand(0, enemy, ownWorker),
            "only another player's unit" => AttackCommand(0, enemy, enemy),
            "nobody" => AttackCommand(0, enemy),
            "stop nobody" => new Command(CommandType.Stop, 0),
            "stop another player's unit" => new Command(CommandType.Stop, 0, [enemy]),
            "attack-move off the map" => AttackMoveCommand(0, TestMatches.Point("30.5", "5.5"), own),
            _ => throw new ArgumentException(what, nameof(what)),
        };

        var match = Build();
        match.Simulation.Commands.Enqueue(0, command);
        match.Run(1);
        var untouched = Build();
        untouched.Run(1);

        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
    }

    [Fact]
    public void AnAttackOrderOnATargetTheLineCannotHitIsNotTaken()
    {
        var match = Field();
        var air = CombatMatches.Data.Chassis.First(chassis => chassis.Line == Line("air")).Id;
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var peltast = match.Spawn(0, Peltast, "5.5", "6.5");
        var flyer = match.Spawn(1, air, "6.5", "5.5");
        match.SetHitPoints(flyer, 1_000_000);

        // Only Line infantry can hit Air: the Guard is skipped and keeps what it was doing.
        match.OrderAttack(0, flyer, hoplite, peltast);
        match.Run(1);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(peltast).Order);

        // Alone, the Guard cannot take the order at all, and it never picks the flyer itself.
        match.OrderAttack(0, flyer, hoplite);
        match.Run(1);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        match.Run(40);
        Assert.Equal(default, match.UnitOf(hoplite).Target);
        Assert.Equal(TestMatches.Point("5.5", "5.5"), match.PositionOf(hoplite));
        Assert.Equal(0, (1_000_000 - match.HitPointsOf(flyer)) % 6_000);
        Assert.True(match.HitPointsOf(flyer) < 1_000_000);
    }
}
