using Sim.Combat;
using Sim.Core;
using Sim.Movement;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Setup;
using Sim.Vision;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Vision;

/// <summary>
/// Fog and targets (design 5.3; design 8.4, row 40): units pick only targets their owner sees, an
/// attack order may name only a unit the player sees, a unit whose target is lost from sight walks
/// to where it was last seen and is idle there, a shot in flight still lands, and shooting reveals
/// nothing.
/// </summary>
public class FogAndCombatTests
{
    private const long Sturdy = 1_000_000;

    private static Match Field() => Create(TestMaps.Open(40, 12));

    [Fact]
    public void AnAttackOrderOnAUnitThePlayerDoesNotSeeIsRefusedAndChangesNothing()
    {
        static Match Build()
        {
            var match = Field();
            match.Spawn(0, Hoplite, "2.5", "5.5");
            match.SpawnWorker(1, "20.5", "5.5");
            return match;
        }

        EntityId hoplite = new(1), enemy = new(2);
        var match = Build();
        Assert.False(match.Vision.Sees(0, enemy));
        match.OrderAttack(0, enemy, hoplite);
        match.Run(1);
        var untouched = Build();
        untouched.Run(1);

        // It is refused as an order on a unit that does not exist is, so the refusal tells the player nothing.
        Assert.Equal(
            [new SimEvent(SimEventKind.OrderRefused, 0, 0, hoplite, enemy, default, (long)CommandType.Attack)],
            match.Simulation.Events);
        Assert.Equal(1, match.Simulation.DroppedCommandCount);
        Assert.Equal(untouched.World.ComputeHash(), match.World.ComputeHash());
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(MoveState.Idle, match.Movers.Get(hoplite).State);
    }

    [Fact]
    public void AnAttackOrderMayNameAUnitThatAnyOfThePlayersUnitsSees()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        var enemy = match.SpawnWorker(1, "20.5", "5.5");
        match.SpawnWorker(0, "20.5", "8.5");

        // 18 tiles from the Guard, which sees 8, and 3 from the worker, which sees 6.
        Assert.True(match.Vision.Sees(0, enemy));
        match.OrderAttack(0, enemy, hoplite);
        match.Run(1);

        Assert.Empty(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
        Assert.Equal(enemy, match.UnitOf(hoplite).Target);
        Assert.NotEqual(MoveState.Idle, match.Movers.Get(hoplite).State);
    }

    /// <summary>What one player sees says nothing of what the other does: here one sees the other and is not seen.</summary>
    [Fact]
    public void OfTwoUnitsTheOneThatSeesMayBeOrderedToAttackAndTheOtherMayNot()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.25", "5.5");
        var ballista = match.Spawn(1, Ballista, "10.5", "5.5");
        Assert.True(match.Vision.Sees(1, hoplite));
        Assert.False(match.Vision.Sees(0, ballista));

        match.OrderAttack(0, ballista, hoplite);
        match.OrderAttack(1, hoplite, ballista);
        match.Run(1);

        var refused = Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(0, refused.Player);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(UnitOrder.Attack, match.UnitOf(ballista).Order);
    }

    /// <summary>
    /// Design 8.4, row 40: a unit target lost from sight: walk to where it was last seen, then idle.
    /// </summary>
    [Fact]
    public void AUnitWhoseTargetIsLostFromSightWalksToWhereItWasLastSeenAndIsIdleThere()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");

        // The player sees the target through a worker beside the way, which sees the tiles of the
        // target's row up to x = 19.
        match.SpawnWorker(0, "14.5", "8.5");
        var enemy = match.SpawnWorker(1, "16.5", "5.5");
        match.SetHitPoints(enemy, Sturdy);
        match.OrderAttack(0, enemy, hoplite);
        match.OrderMove(1, TestMatches.Point("38.5", "5.5"), enemy);

        var lastSeen = match.PositionOf(enemy);
        var lostIn = -1;
        for (var tick = 0; tick < 100 && lostIn < 0; tick++)
        {
            match.Run(1);
            if (match.Vision.Sees(0, enemy))
            {
                lastSeen = match.PositionOf(enemy);
                Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
                Assert.Equal(enemy, match.UnitOf(hoplite).Target);
                Assert.Equal(lastSeen, match.UnitOf(hoplite).Anchor);
            }
            else
            {
                lostIn = tick;
            }
        }

        // The target walks 3.5 tiles to the first tile the worker does not see, at 0.15 a tick: it
        // is there in the 24th tick, and is lost in that tick and not when sight is next worked out.
        Assert.Equal(23, lostIn);
        Assert.Equal(19, lastSeen.X.FloorToInt());
        Assert.Equal(20, match.PositionOf(enemy).X.FloorToInt());

        // In the tick the target is lost, the unit is sent to where it was seen last.
        var unit = match.UnitOf(hoplite);
        Assert.Equal(UnitOrder.Move, unit.Order);
        Assert.Equal(EntityId.None, unit.Target);
        Assert.False(unit.Engaged);
        Assert.Equal(lastSeen, match.Movers.Get(hoplite).Destination);
        Assert.NotEqual(MoveState.Idle, match.Movers.Get(hoplite).State);

        // It walks there, 17 tiles at 0.13 a tick, and neither turns back nor goes on after the target.
        var arrivedIn = -1;
        for (var tick = lostIn + 1; tick < 300 && arrivedIn < 0; tick++)
        {
            match.Run(1);
            if (match.Movers.Get(hoplite).State == MoveState.Idle)
            {
                arrivedIn = tick;
            }
            else
            {
                Assert.Equal(UnitOrder.Move, match.UnitOf(hoplite).Order);
                Assert.Equal(lastSeen, match.Movers.Get(hoplite).Destination);
            }
        }

        Assert.InRange(arrivedIn, 130, 150);
        Assert.Equal(lastSeen, match.PositionOf(hoplite));

        // And it is idle there, with its post where it stands, while the target is long gone.
        match.Run(100);
        unit = match.UnitOf(hoplite);
        Assert.Equal(UnitOrder.Idle, unit.Order);
        Assert.Equal(EntityId.None, unit.Target);
        Assert.Equal(lastSeen, unit.Anchor);
        Assert.Equal(lastSeen, match.PositionOf(hoplite));
        Assert.Equal(TestMatches.Point("38.5", "5.5"), match.PositionOf(enemy));
        Assert.False(match.Vision.Sees(0, enemy));
        Assert.Equal(Sturdy, match.HitPointsOf(enemy));
    }

    /// <summary>
    /// The walk to where the target was last seen is a move order: the unit does not take the
    /// attack up again when the target comes back into sight. Once it is idle it picks for itself.
    /// </summary>
    [Fact]
    public void TheAttackIsNotTakenUpAgainWhenTheTargetComesBackIntoSight()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        match.SpawnWorker(0, "14.5", "8.5");
        var enemy = match.SpawnWorker(1, "16.5", "5.5");
        match.SetHitPoints(enemy, Sturdy);
        match.OrderAttack(0, enemy, hoplite);
        match.OrderMove(1, TestMatches.Point("22.5", "5.5"), enemy);

        while (match.Vision.Sees(0, enemy))
        {
            match.Run(1);
        }

        var lastSeen = match.Movers.Get(hoplite).Destination;
        Assert.Equal(UnitOrder.Move, match.UnitOf(hoplite).Order);

        // The target turns round and stops in sight of the worker again, off the unit's way.
        match.OrderMove(1, TestMatches.Point("17.5", "7.5"), enemy);
        var seenAgainWhileWalking = 0;
        while (match.Movers.Get(hoplite).State != MoveState.Idle)
        {
            match.Run(1);
            if (match.Movers.Get(hoplite).State != MoveState.Idle)
            {
                Assert.Equal(UnitOrder.Move, match.UnitOf(hoplite).Order);
                Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
                Assert.Equal(lastSeen, match.Movers.Get(hoplite).Destination);
                seenAgainWhileWalking += match.Vision.Sees(0, enemy) ? 1 : 0;
            }
        }

        Assert.True(seenAgainWhileWalking > 50, $"The target was in sight for {seenAgainWhileWalking} ticks of the walk.");
        Assert.Equal(lastSeen, match.PositionOf(hoplite));
        Assert.Equal(Sturdy, match.HitPointsOf(enemy));

        // Idle where the target was last seen, it finds the target within its acquisition range and fights it.
        match.Run(80);
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(enemy, match.UnitOf(hoplite).Target);
        Assert.True(match.HitPointsOf(enemy) < Sturdy);
    }

    [Fact]
    public void AnAttackOrderGoesOnWhileAnyOfThePlayersUnitsSeesTheTarget()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");
        match.SpawnWorker(0, "14.5", "8.5");
        match.SpawnWorker(0, "24.5", "8.5");
        match.SpawnWorker(0, "34.5", "8.5");
        var enemy = match.SpawnWorker(1, "16.5", "5.5");
        match.SetHitPoints(enemy, Sturdy);
        match.OrderAttack(0, enemy, hoplite);
        match.OrderMove(1, TestMatches.Point("38.5", "5.5"), enemy);

        // The target passes from the sight of one worker into that of the next, and is seen all the way.
        for (var tick = 0; tick < 400; tick++)
        {
            match.Run(1);
            Assert.True(match.Vision.Sees(0, enemy));
            Assert.Equal(UnitOrder.Attack, match.UnitOf(hoplite).Order);
        }

        Assert.True(match.HitPointsOf(enemy) < Sturdy);
    }

    /// <summary>Design 5.3: units pick only targets their owner sees. A Ballista reaches 9 tiles and sees 8.</summary>
    [Fact]
    public void AUnitDoesNotPickATargetInItsRangeThatItsOwnerDoesNotSee()
    {
        var match = Field();
        var ballista = match.Spawn(1, Ballista, "20.5", "5.5");

        // 8.75 tiles away, on a tile whose center is 9 tiles away.
        var target = match.SpawnWorker(0, "11.75", "5.5");
        Assert.True(match.DistanceSquared(ballista, target) <= Squared("9"));
        Assert.False(match.Vision.Sees(1, target));

        match.Run(100);

        Assert.Equal(EntityId.None, match.UnitOf(ballista).Target);
        Assert.Equal(0, match.Shots.Count);
        Assert.Equal(50_000, match.HitPointsOf(target));
        Assert.Equal(TestMatches.Point("20.5", "5.5"), match.PositionOf(ballista));
    }

    [Fact]
    public void AUnitPicksATargetInItsRangeThatAnotherOfItsOwnersUnitsSees()
    {
        var match = Field();
        var ballista = match.Spawn(1, Ballista, "20.5", "5.5");
        var target = match.SpawnWorker(0, "11.75", "5.5");
        match.SetHitPoints(target, Sturdy);

        // A worker of the Ballista's owner, 4 tiles from the center of the target's tile.
        match.SpawnWorker(1, "11.5", "9.5");
        Assert.True(match.Vision.Sees(1, target));

        match.Run(100);

        Assert.Equal(target, match.UnitOf(ballista).Target);
        Assert.True(match.HitPointsOf(target) < Sturdy);
        Assert.Equal(TestMatches.Point("20.5", "5.5"), match.PositionOf(ballista));
    }

    /// <summary>
    /// A target a unit picked for itself is given up when its owner stops seeing it, and a shot
    /// already in flight still lands (design 5.3).
    /// </summary>
    [Fact]
    public void AShotInFlightStillLandsWhenItsTargetIsLostFromSightAndNoMoreAreFired()
    {
        var match = Field();
        var ballista = match.Spawn(1, Ballista, "20.5", "5.5");
        var target = match.SpawnWorker(0, "11.75", "5.5");
        match.SetHitPoints(target, Sturdy);
        var spotter = match.SpawnWorker(1, "11.5", "9.5");

        var firedIn = -1;
        for (var tick = 0; tick < 20 && firedIn < 0; tick++)
        {
            match.Run(1);
            firedIn = match.EventsOf(SimEventKind.ShotFired).Length > 0 ? tick : -1;
        }

        Assert.InRange(firedIn, 0, 4);

        // The spotter dies in the next tick. Its sight is gone the next time sight is worked out.
        match.SetHitPoints(spotter, 0);
        var lostIn = -1;
        var landedIn = -1;
        var fired = 1;
        for (var tick = firedIn + 1; tick < firedIn + 200; tick++)
        {
            match.Run(1);
            var seen = match.Vision.Sees(1, target);
            if (!seen && lostIn < 0)
            {
                lostIn = tick;
            }

            if (lostIn >= 0)
            {
                // Lost for good: the Ballista has no target and fires no more.
                Assert.False(seen);
                Assert.Equal(EntityId.None, match.UnitOf(ballista).Target);
                Assert.Empty(match.EventsOf(SimEventKind.ShotFired));
            }

            fired += match.EventsOf(SimEventKind.ShotFired).Length;
            Assert.Empty(match.EventsOf(SimEventKind.ShotLost));
            if (match.EventsOf(SimEventKind.ShotHit).Length > 0)
            {
                landedIn = tick;
            }
        }

        // The grid is next worked out in a tick that is a multiple of 4, at most 4 ticks after the death.
        Assert.InRange(lostIn, firedIn + 1, firedIn + 5);
        Assert.Equal(0, lostIn % 4);

        // 8.75 tiles at half a tile a tick: the shot is in the air for 18 ticks, and lands long after the target was lost.
        Assert.Equal(1, fired);
        Assert.Equal(firedIn + 18, landedIn);
        Assert.True(landedIn > lostIn);
        Assert.True(match.HitPointsOf(target) < Sturdy);
        Assert.Equal(0, match.Shots.Count);
    }

    /// <summary>
    /// Design 8.4, row 40: shooting reveals nothing. A unit that fires from unseen ground stays
    /// unseen, and the unit it hits, whose owner does not see it, does nothing.
    /// </summary>
    [Fact]
    public void ShootingRevealsNothing()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.25", "5.5");
        var ballista = match.Spawn(1, Ballista, "10.5", "5.5");
        match.SetHitPoints(hoplite, Sturdy);
        var revision = match.Vision.Grid.Revision;

        var hits = 0;
        for (var tick = 0; tick < 200; tick++)
        {
            match.Run(1);
            hits += match.EventsOf(SimEventKind.ShotHit).Length;

            Assert.False(match.Vision.Sees(0, ballista));
            Assert.Equal(FogState.Unexplored, match.Vision.Grid.StateOf(0, 10, 5));
            Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
            Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
        }

        Assert.InRange(hits, 3, 20);
        Assert.True(match.HitPointsOf(hoplite) < Sturdy);
        Assert.Equal(TestMatches.Point("2.25", "5.5"), match.PositionOf(hoplite));
        Assert.Equal(revision, match.Vision.Grid.Revision);

        // Nor can the player order an attack on what shoots at its unit.
        match.OrderAttack(0, ballista, hoplite);
        match.Run(1);
        Assert.Single(match.EventsOf(SimEventKind.OrderRefused));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
    }

    [Fact]
    public void AUnitOnAnAttackMoveGivesUpATargetLostFromSightAndGoesOnToWhereItsOrderEnds()
    {
        var match = Field();
        var hoplite = match.Spawn(0, Hoplite, "2.5", "5.5");

        // A worker in the unit's sight, 7 tiles ahead of it and 2 to the side, that walks away faster than the unit follows.
        var enemy = match.SpawnWorker(1, "9.5", "7.5");
        match.SetHitPoints(enemy, Sturdy);
        var end = TestMatches.Point("12.5", "5.5");
        match.OrderAttackMove(0, end, hoplite);
        match.Run(8);
        Assert.Equal(enemy, match.UnitOf(hoplite).Target);

        match.OrderMove(1, TestMatches.Point("38.5", "10.5"), enemy);
        var lost = false;
        for (var tick = 0; tick < 400; tick++)
        {
            match.Run(1);
            if (!match.Vision.Sees(0, enemy))
            {
                lost = true;
                Assert.Equal(EntityId.None, match.UnitOf(hoplite).Target);
            }
        }

        Assert.True(lost);
        Assert.Equal(end, match.PositionOf(hoplite));
        Assert.Equal(UnitOrder.Idle, match.UnitOf(hoplite).Order);
        Assert.Equal(Sturdy, match.HitPointsOf(enemy));
    }
}
