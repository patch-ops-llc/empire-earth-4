using Sim.Combat;
using Sim.Core;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// What combat puts on the event list (<see cref="Simulation.Events"/>) for the view: a melee hit,
/// a shot fired, a shot that lands, a shot that is lost, a death and an order that is refused.
/// </summary>
public class CombatEventTests
{
    private const long WorkerHitPoints = 50_000;

    private static Match Field() => Create(TestMaps.Open(24, 12));

    private static SimEvent Event(
        SimEventKind kind, int tick, int player, EntityId entity = default, EntityId other = default, string x = "0", string y = "0", long amount = 0) =>
        new(kind, tick, player, entity, other, TestMatches.Point(x, y), amount);

    [Fact]
    public void AMeleeHitNamesTheAttackerTheTargetThePlaceAndTheDamage()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);

        Assert.Equal([Event(SimEventKind.MeleeHit, 0, 0, worker, hoplite, "6.5", "5.5", 8_000)], match.Simulation.Events);

        // The list holds one tick's events: the next tick has no attack and so no event.
        match.Run(1);
        Assert.Empty(match.Simulation.Events);
    }

    [Fact]
    public void AShotIsReportedWhenItIsFiredAndWhenItLands()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");
        match.OrderAttack(0, worker, peltast);
        match.Run(1);

        var shot = match.Shots.EntityAt(0);
        Assert.Equal([Event(SimEventKind.ShotFired, 0, 0, shot, peltast, "2.5", "5.5")], match.Simulation.Events);

        var ticks = 1;
        while (match.Shots.Count > 0)
        {
            Assert.Empty(match.EventsOf(SimEventKind.ShotHit));
            Assert.Equal(WorkerHitPoints, match.HitPointsOf(worker));
            match.Run(1);
            ticks++;
        }

        Assert.Equal([Event(SimEventKind.ShotHit, ticks - 1, 0, worker, shot, "7.5", "5.5", 6_000)], match.Simulation.Events);
        Assert.Equal(WorkerHitPoints - 6_000, match.HitPointsOf(worker));
    }

    [Fact]
    public void AShotHitNamesTheOwnerOfTheUnitThatFiredItEvenWhenThatUnitIsGone()
    {
        var match = Field();
        var peltast = match.Spawn(1, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(0, "7.5", "5.5");
        match.OrderAttack(1, worker, peltast);
        match.Run(1);
        match.SetHitPoints(peltast, 0);
        match.Run(1);
        Assert.Equal([Event(SimEventKind.Death, 1, 1, peltast, x: "2.5", y: "5.5")], match.Simulation.Events);

        while (match.Shots.Count > 0)
        {
            match.Run(1);
        }

        var hit = Assert.Single(match.Simulation.Events);
        Assert.Equal((SimEventKind.ShotHit, 1, worker), (hit.Kind, hit.Player, hit.Entity));
    }

    [Fact]
    public void AShotWhoseTargetIsGoneIsReportedLostWhereItWas()
    {
        var match = Field();
        var near = match.Spawn(0, Peltast, "4.5", "5.5");
        var far = match.Spawn(0, Peltast, "2.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");
        match.SetHitPoints(worker, 6_000);
        match.OrderAttack(0, worker, near, far);

        // Both fire in tick 0. The nearer shot lands in tick 3 and kills; the other has nothing left to hit.
        match.Run(1);
        Assert.Equal(2, match.EventsOf(SimEventKind.ShotFired).Length);
        var (first, second) = (match.Shots.EntityAt(0), match.Shots.EntityAt(1));
        match.Run(3);
        Assert.Equal(
            [
                Event(SimEventKind.ShotHit, 3, 0, worker, first, "7.5", "5.5", 6_000),
                Event(SimEventKind.Death, 3, 1, worker, x: "7.5", y: "5.5"),
            ],
            match.Simulation.Events);

        var whereItWas = match.Shots.Get(second).Position;
        match.Run(1);
        Assert.Equal([new SimEvent(SimEventKind.ShotLost, 4, 0, second, worker, whereItWas, 0)], match.Simulation.Events);
        Assert.Equal(0, match.Shots.Count);
    }

    [Fact]
    public void ADeathNamesWhatDiedItsOwnerAndWhereItStood()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.SetHitPoints(worker, 1);
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);

        // The damage of the hit is reported whole, though the worker had less left.
        Assert.Equal(
            [
                Event(SimEventKind.MeleeHit, 0, 0, worker, hoplite, "6.5", "5.5", 8_000),
                Event(SimEventKind.Death, 0, 1, worker, x: "6.5", y: "5.5"),
            ],
            match.Simulation.Events);
        Assert.False(match.IsAlive(worker));

        match.Run(1);
        Assert.Empty(match.Simulation.Events);
    }

    /// <summary>Design 8.4, row 21, as the view sees it: both hits are reported before either death.</summary>
    [Fact]
    public void TwoUnitsThatKillEachOtherInOneTickAreReportedAsTwoHitsAndThenTwoDeaths()
    {
        var match = Field();
        var one = match.Spawn(0, Hoplite, "5.5", "5.5");
        var other = match.Spawn(1, Hoplite, "6.5", "5.5");
        match.SetHitPoints(one, 1);
        match.SetHitPoints(other, 1);
        match.OrderAttack(0, other, one);
        match.OrderAttack(1, one, other);
        match.Run(1);

        Assert.Equal(
            [SimEventKind.MeleeHit, SimEventKind.MeleeHit, SimEventKind.Death, SimEventKind.Death],
            match.Simulation.Events.Select(simEvent => simEvent.Kind));
        Assert.Equal([other, one, one, other], match.Simulation.Events.Select(simEvent => simEvent.Entity));
    }

    /// <summary>The events of a tick stand in the order of the systems: shots that land, then attacks, then deaths.</summary>
    [Fact]
    public void InOneTickShotsThatLandComeBeforeAttacksAndAttacksBeforeDeaths()
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "4.5", "5.5");
        var hoplite = match.Spawn(0, Hoplite, "6.5", "5.5");
        var worker = match.SpawnWorker(1, "7.5", "5.5");

        // The Peltast fires in tick 0 from three tiles away, so its shot lands in tick 3. The
        // Hoplite is made to wait until that tick for its attack.
        match.Combatants.Get(hoplite).Wait = 4;
        match.OrderAttack(0, worker, peltast, hoplite);
        match.Run(3);
        Assert.Equal(1, match.Shots.Count);
        Assert.Equal(50_000, match.HitPointsOf(worker));
        match.SetHitPoints(worker, 1);
        match.Run(1);

        Assert.Equal(
            [SimEventKind.ShotHit, SimEventKind.MeleeHit, SimEventKind.Death],
            match.Simulation.Events.Select(simEvent => simEvent.Kind));
        Assert.Equal([worker, worker, worker], match.Simulation.Events.Select(simEvent => simEvent.Entity));
        Assert.Equal(hoplite, match.Simulation.Events[1].Other);
    }

    [Fact]
    public void AnOrderThatIsCarriedOutIsNotReported()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "12.5", "5.5");
        match.OrderAttack(0, worker, hoplite);
        match.OrderAttackMove(0, TestMatches.Point("8.5", "5.5"), hoplite);
        match.OrderMove(0, TestMatches.Point("9.5", "5.5"), hoplite);
        match.OrderStop(0, hoplite);
        match.Run(1);

        Assert.Empty(match.Simulation.Events);
        Assert.Equal(0, match.Simulation.DroppedCommandCount);
    }

    [Fact]
    public void EachOfTheFourOrdersIsReportedWhenItIsRefusedAndChangesNothing()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var friend = match.SpawnWorker(0, "6.5", "5.5");
        var enemy = match.Spawn(1, Hoplite, "20.5", "5.5");
        var point = TestMatches.Point("9.5", "6.5");

        // An attack on a unit of one's own, and three orders for a unit of another player.
        match.OrderAttack(0, friend, hoplite);
        match.OrderAttackMove(0, point, enemy);
        match.OrderMove(0, point, enemy);
        match.OrderStop(0, enemy);
        match.Run(1);

        Assert.Equal(
            [
                new SimEvent(SimEventKind.OrderRefused, 0, 0, hoplite, friend, default, (long)CommandType.Attack),
                new SimEvent(SimEventKind.OrderRefused, 0, 0, enemy, EntityId.None, point, (long)CommandType.AttackMove),
                new SimEvent(SimEventKind.OrderRefused, 0, 0, enemy, EntityId.None, point, (long)CommandType.Move),
                new SimEvent(SimEventKind.OrderRefused, 0, 0, enemy, EntityId.None, default, (long)CommandType.Stop),
            ],
            match.Simulation.Events);
        Assert.Equal(4, match.Simulation.DroppedCommandCount);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(enemy).Order);
    }

    [Fact]
    public void ARefusedOrderWithoutUnitsNamesNone()
    {
        var match = Field();
        match.OrderStop(1);
        match.Run(1);

        Assert.Equal([new SimEvent(SimEventKind.OrderRefused, 0, 1, EntityId.None, EntityId.None, default, (long)CommandType.Stop)], match.Simulation.Events);
    }

    /// <summary>
    /// The events are output and not state: restoring a snapshot taken in the tick of a hit gives
    /// a match that goes on to the same hashes, and whose list starts empty.
    /// </summary>
    [Fact]
    public void TheEventsOfATickAreNotInASnapshot()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "5.5", "5.5");
        var worker = match.SpawnWorker(1, "6.5", "5.5");
        match.OrderAttack(0, worker, hoplite);
        match.Run(1);
        Assert.NotEmpty(match.Simulation.Events);

        var writer = new Sim.Serialization.ByteWriter();
        Sim.Serialization.Snapshot.Write(match.World, writer);
        var restored = Field();
        restored.Spawn(0, Hoplite, "5.5", "5.5");
        restored.SpawnWorker(1, "6.5", "5.5");
        Sim.Serialization.Snapshot.Restore(restored.Simulation, new Sim.Serialization.ByteReader(writer.ToArray()));
        restored.RefreshDerivedState();

        Assert.Empty(restored.Simulation.Events);
        Assert.Equal(match.World.ComputeHash(), restored.World.ComputeHash());
        match.Run(40);
        restored.Run(40);
        Assert.Equal(match.World.ComputeHash(), restored.World.ComputeHash());
        Assert.Equal(match.Simulation.Events, restored.Simulation.Events);
    }
}
