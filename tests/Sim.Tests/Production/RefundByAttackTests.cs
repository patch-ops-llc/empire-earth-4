using Sim.Core;
using Sim.Production;
using Sim.Tests.Combat;
using Sim.Tests.Map;
using static Sim.Tests.Production.ProductionMatches;

namespace Sim.Tests.Production;

/// <summary>
/// Design 2.5 and design 8.4, row 25, where production meets structures in combat: a structure
/// that falls to an attack gives back every order of its queue, in full, in the tick it falls.
/// </summary>
/// <remarks>
/// <see cref="CancelAndRefundTests"/> has the refund itself, for a structure whose hit points a
/// test sets to zero. Here the last hit points are taken by a unit of another player, so the
/// blow, the fall and the refund must come in that order within one tick: the attacks, then the
/// deaths, then the refund.
/// </remarks>
public class RefundByAttackTests
{
    private static readonly int Hoplite = Chassis("hoplite");
    private static readonly int Peltast = Chassis("peltast");

    /// <summary>
    /// A Barracks of player 0 on tiles 10 to 12 both ways, with a Peltast, a Hoplite and a Peltast
    /// ordered and 100 ticks into the first. A unit of player 1 has stood in reach or in range of
    /// it since the start, is ordered to attack it when it has one hit point left, and destroys
    /// it with its next blow: a Hoplite's from beside it, or a Peltast's shot from 4.5 tiles off.
    /// </summary>
    [Theory]
    [InlineData("hoplite", "9.5", "11.5", SimEventKind.MeleeHit)]
    [InlineData("peltast", "5.5", "11.5", SimEventKind.ShotHit)]
    public void AStructureThatFallsToAnAttackRefundsItsQueueInTheTickItFalls(string chassis, string x, string y, SimEventKind blow)
    {
        var match = Create(TestMaps.Open(24, 24));
        var barracks = match.Place(0, "barracks", 10, 10);
        var attacker = match.Unit(1, chassis, x, y);
        match.Stockpiles.Add(0, 200, 20);
        match.OrderTrain(0, barracks, Peltast);
        match.OrderTrain(0, barracks, Hoplite);
        match.OrderTrain(0, barracks, Peltast);
        match.Simulation.Run(100);
        Assert.Equal((50, 10), match.Stock(0));
        Assert.Equal(6, match.Supply.Used(0));
        Assert.True(match.IsAlive(attacker));
        var center = match.Bodies.Get(barracks).Position;

        match.SetHitPoints(barracks, 1_000);
        match.OrderAttack(1, barracks, attacker);
        var fell = false;
        for (var tick = 0; tick < 60 && !fell; tick++)
        {
            match.Simulation.Tick();
            fell = match.EventsOf(SimEventKind.Death).Length > 0;
            if (!fell)
            {
                // While it stands it keeps its queue and what was paid for it.
                Assert.Empty(match.TrainEvents());
                Assert.Equal((50, 10), match.Stock(0));
                Assert.Equal(6, match.Supply.Used(0));
                Assert.Equal(3, match.OrdersAt(barracks).Length);
            }
        }

        Assert.True(fell, "The Barracks still stands 60 ticks after the order to attack it.");
        var fellIn = match.World.Tick - 1;

        // The blow, the fall and the refund, in that order, in one tick.
        var kinds = match.Simulation.Events
            .Where(reported => reported.Kind == blow || reported.Kind is SimEventKind.Death or SimEventKind.TrainRefunded)
            .Select(reported => (reported.Kind, reported.Entity))
            .ToArray();
        Assert.Equal(
            [(blow, barracks), (SimEventKind.Death, barracks), (SimEventKind.TrainRefunded, barracks), (SimEventKind.TrainRefunded, barracks), (SimEventKind.TrainRefunded, barracks)],
            kinds);
        Assert.Equal(
            [
                new TrainEvent(SimEventKind.TrainRefunded, fellIn, 0, barracks, Peltast, 1, TrainCheck.Allowed, EntityId.None, center),
                new TrainEvent(SimEventKind.TrainRefunded, fellIn, 0, barracks, Hoplite, 1, TrainCheck.Allowed, EntityId.None, center),
                new TrainEvent(SimEventKind.TrainRefunded, fellIn, 0, barracks, Peltast, 1, TrainCheck.Allowed, EntityId.None, center),
            ],
            match.TrainEvents());

        // Everything paid for the three orders is back, with nothing kept for the progress, and their supply is free.
        Assert.False(match.IsAlive(barracks));
        Assert.Equal((200, 20), match.Stock(0));
        Assert.Equal(0, match.Supply.Used(0));
        Assert.Equal((0, 0), match.Stock(1));

        // It is given back once, and nothing comes out of the fallen structure.
        match.Simulation.Tick();
        Assert.Empty(match.TrainEvents());
        Assert.False(match.Production.Producers.Contains(barracks));
        match.Simulation.Run(500);
        Assert.Equal((200, 20), match.Stock(0));
        Assert.Equal(0, match.Supply.Used(0));
        Assert.Equal(attacker, Assert.Single(Enumerable.Range(0, match.Combatants.Count).Select(match.Combatants.EntityAt)));
    }
}
