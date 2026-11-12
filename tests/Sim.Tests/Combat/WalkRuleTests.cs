using Sim.Combat;
using Sim.Core;
using Sim.Movement;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tests.Map;
using Sim.Tests.Serialization;
using Sim.Tests.Setup;
using static Sim.Tests.Combat.CombatMatches;

namespace Sim.Tests.Combat;

/// <summary>
/// A unit attacks only in a tick in which it does not walk (design 2.4). A walk is a step under
/// an order or toward a target. The step that a unit's own side gives it to keep their spacing
/// is no walk: a unit that is stepped aside attacks all the same.
/// </summary>
public class WalkRuleTests
{
    private const int PileSize = 8;

    private static Match Field() => Create(TestMaps.Open(24, 12));

    /// <summary>Eight Peltasts on one point, three tiles from an enemy worker: all in range of it, and far too close to each other.</summary>
    private static Match Pile()
    {
        var match = Field();
        for (var i = 0; i < PileSize; i++)
        {
            match.Spawn(0, Peltast, "5.5", "5.5");
        }

        match.SpawnWorker(1, "8.5", "5.5");
        match.RefreshDerivedState();
        return match;
    }

    private static EntityId[] PileIds => [.. Enumerable.Range(1, PileSize).Select(id => new EntityId(id))];

    private static EntityId PileTarget => new(PileSize + 1);

    private static (int Tick, Command Command)[] PileScript() => [(0, AttackCommand(0, PileTarget, PileIds))];

    /// <summary>
    /// The units of a pile are all stepped apart in the tick of their order, and every one of
    /// them shoots in that tick: none of them has walked.
    /// </summary>
    [Fact]
    public void AUnitThatItsOwnSideStepsAsideAttacksInThatTick()
    {
        var match = Pile();
        match.Simulation.Commands.Enqueue(0, PileScript()[0].Command);
        var before = PileIds.Select(match.PositionOf).ToArray();

        match.Run(1);

        var shots = match.EventsOf(SimEventKind.ShotFired);
        Assert.Equal(PileIds.Order(), shots.Select(shot => shot.Other).Order());
        for (var i = 0; i < PileSize; i++)
        {
            Assert.NotEqual(before[i], match.PositionOf(PileIds[i]));
            Assert.Equal(MoveState.Idle, match.Movers.Get(PileIds[i]).State);
            Assert.True(match.UnitOf(PileIds[i]).Wait > Fraction.Zero);
        }
    }

    /// <summary>
    /// While the pile spreads to its spacing, which takes many ticks, its units keep to their
    /// attack interval: every one of them makes its second attack 20 ticks after its first, in a
    /// tick in which it is still being stepped.
    /// </summary>
    [Fact]
    public void AUnitThatIsSteppedAsideTickAfterTickKeepsItsAttackInterval()
    {
        var match = Pile();
        match.SetHitPoints(PileTarget, 10_000_000);
        match.Simulation.Commands.Enqueue(0, PileScript()[0].Command);

        match.Run(20);
        Assert.Equal(PileSize, match.Shots.Count + (int)((10_000_000 - match.HitPointsOf(PileTarget)) / 6_000));
        var before = PileIds.Select(match.PositionOf).ToArray();

        match.Run(1);

        Assert.Equal(PileIds.Order(), match.EventsOf(SimEventKind.ShotFired).Select(shot => shot.Other).Order());
        Assert.Contains(Enumerable.Range(0, PileSize), i => before[i] != match.PositionOf(PileIds[i]));
    }

    /// <summary>
    /// A unit that walks into reach of its target, under an attack order, on an attack-move or
    /// of its own accord toward a target it has picked, does not attack in the tick in which it
    /// stops there, though it is ready and in reach. It attacks in the next, standing.
    /// </summary>
    [Theory]
    [InlineData("an attack order")]
    [InlineData("an attack-move")]
    [InlineData("a target it picked itself")]
    public void AUnitThatWalksUnderAnOrderOrTowardATargetDoesNotAttackInThatTick(string why)
    {
        var match = Field();
        var peltast = match.Spawn(0, Peltast, "2.5", "5.5");

        // Seven tiles off: in sight, as the target of an attack order must be (design 5.3), and out of reach.
        var worker = match.SpawnWorker(1, "9.5", "5.5");
        match.RefreshDerivedState();
        var order = UnitOrder.Idle;
        switch (why)
        {
            case "an attack order":
                match.OrderAttack(0, worker, peltast);
                order = UnitOrder.Attack;
                break;
            case "an attack-move":
                match.OrderAttackMove(0, TestMatches.Point("20.5", "5.5"), peltast);
                order = UnitOrder.AttackMove;
                break;
        }

        var (stopped, firstShot) = (-1, -1);
        for (var tick = 0; tick < 200 && firstShot < 0; tick++)
        {
            var before = match.PositionOf(peltast);
            match.Run(1);
            var walked = match.PositionOf(peltast) != before;
            var fired = match.EventsOf(SimEventKind.ShotFired).Length > 0;
            var standsInReach = match.Movers.Get(peltast).State == MoveState.Idle
                && match.UnitOf(peltast).Target == worker
                && match.DistanceSquared(peltast, worker) <= Squared("5");
            if (stopped < 0 && standsInReach)
            {
                stopped = tick;
                Assert.True(walked, $"On {why} the unit stood in reach from the start.");
                Assert.False(fired, $"On {why} the unit attacked in tick {tick}, in which it walked.");
                Assert.Equal(Fraction.Zero, match.UnitOf(peltast).Wait);
                Assert.Equal(order, match.UnitOf(peltast).Order);
            }
            else if (fired)
            {
                firstShot = tick;
                Assert.False(walked);
            }
        }

        Assert.True(stopped > 0, $"On {why} the unit never came to stand in reach.");
        Assert.Equal(stopped + 1, firstShot);
    }

    /// <summary>
    /// The two kinds of step in two ticks that follow each other. Two units walk at their target
    /// side by side, closer than their spacing. In the tick in which they stop in reach they
    /// have walked, and neither attacks. In the next they stand, their own side steps them
    /// apart, and both attack.
    /// </summary>
    [Fact]
    public void AWalkStopsAnAttackAndTheStepApartInTheNextTickDoesNot()
    {
        var match = Field();
        var first = match.Spawn(0, Peltast, "2.5", "5.5");
        var second = match.Spawn(0, Peltast, "2.5", "5.75");
        var worker = match.SpawnWorker(1, "9.5", "5.5");
        match.RefreshDerivedState();
        match.OrderAttack(0, worker, first, second);

        var stopped = -1;
        for (var tick = 0; tick < 200 && stopped < 0; tick++)
        {
            var before = (match.PositionOf(first), match.PositionOf(second));
            match.Run(1);
            if (match.Movers.Get(first).State == MoveState.Idle && match.Movers.Get(second).State == MoveState.Idle)
            {
                stopped = tick;
                Assert.NotEqual(before, (match.PositionOf(first), match.PositionOf(second)));
                Assert.Empty(match.EventsOf(SimEventKind.ShotFired));
            }
        }

        Assert.True(stopped > 0);
        Assert.True(match.DistanceSquared(first, second) < Squared("1"), "The two came to stand at their spacing, so nothing steps them apart.");
        var stood = (match.PositionOf(first), match.PositionOf(second));

        match.Run(1);

        Assert.NotEqual(stood.Item1, match.PositionOf(first));
        Assert.NotEqual(stood.Item2, match.PositionOf(second));
        Assert.Equal([first, second], match.EventsOf(SimEventKind.ShotFired).Select(shot => shot.Other).Order());
    }

    /// <summary>
    /// Which units have walked is noted from the state after the walk and is no state itself: a
    /// match saved in the middle of the pile's fight, between two ticks, continues the same way.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(20)]
    [InlineData(21)]
    public void ASnapshotTakenWhileUnitsAreSteppedAsideAndAttackRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(Pile, PileScript(), snapshotTick, lastTick: 80);

    /// <summary>The note itself: a unit has walked when it no longer stands where it stood at its last turn to attack.</summary>
    [Fact]
    public void AUnitHasWalkedWhenItNoLongerStandsWhereItStoodAtItsLastTurnToAttack()
    {
        var match = Field();
        var walker = match.Spawn(0, Peltast, "2.5", "5.5");
        var stander = match.Spawn(0, Peltast, "6.5", "5.5");
        var worker = match.SpawnWorker(1, "9.5", "5.5");
        var marks = new WalkMarks(match.Bodies, match.Combatants);

        marks.Run(match.World);
        Assert.False(marks.Walked(walker));
        Assert.False(marks.Walked(stander));

        match.Bodies.Get(walker).Position = TestMatches.Point("2.75", "5.5");
        marks.Run(match.World);
        Assert.True(marks.Walked(walker));
        Assert.False(marks.Walked(stander));
        Assert.False(marks.Walked(worker));

        // The attack system takes where the unit stands now as where it stood. The note is made anew each time.
        match.Combatants.Get(walker).Stood = match.PositionOf(walker);
        marks.Run(match.World);
        Assert.False(marks.Walked(walker));
    }
}
