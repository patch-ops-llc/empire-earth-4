using Sim.Combat;
using Sim.Core;
using Sim.Setup;
using Sim.Tests.Combat;
using Sim.Tests.Construction;
using Sim.Tests.Economy;
using Sim.Tests.Serialization;
using static Sim.Tests.Walls.WalledMatches;

namespace Sim.Tests.Walls;

/// <summary>
/// Wall orders and the wall a unit is breaking are state: they are in the state hash, and a
/// match restored from a snapshot taken at any point carries on as the first match does. What is
/// worked out from the walls, the <see cref="Sim.Walls.WallMap"/>, is rebuilt on a restore.
/// </summary>
public class WallSnapshotTests
{
    private static (Match Match, EntityId First, EntityId Second) Builders()
    {
        var match = ConstructionMatches.WithACapitol();
        var first = match.WorkerAt(0, 2, 15);
        var second = match.WorkerAt(0, 12, 8);
        match.RefreshDerivedState();
        return (match, first, second);
    }

    /// <summary>
    /// Two wall orders, one of them diagonal. The first is cancelled with tiles still to lay;
    /// the worker of the second is sent elsewhere, which ends its order too.
    /// </summary>
    [Fact]
    public void AMatchWithWallOrdersUnderWayRestoresAndContinues()
    {
        var (probe, first, second) = Builders();
        var script = new List<(int Tick, Command Command)>
        {
            (0, probe.BuildWall(0, (4, 14), (11, 14), first)),
            (0, probe.BuildWall(0, (4, 16), (11, 18), second)),
            (70, CombatMatches.MoveCommand(0, Center(20, 20), second)),
            (30, new Command(CommandType.CancelBuild, 0, [first])),
        };

        // What the run holds, so that the snapshots below are of what they are meant to be.
        foreach (var (tick, command) in script)
        {
            probe.Simulation.Commands.Enqueue(tick, command);
        }

        probe.Simulation.Run(20);
        Assert.NotEmpty(probe.PlansOf(first));
        Assert.NotEmpty(probe.PlansOf(second));
        Assert.NotEmpty(probe.WallOf(0));
        probe.Simulation.Run(180);
        Assert.Equal(0, probe.WallPlans.Count);
        Assert.InRange(probe.WallOf(0).Length, 4, 12);
        Assert.Equal(1000 - (20 * probe.WallOf(0).Length), probe.StockOf(0).Materials);

        foreach (var snapshotTick in new[] { 0, 1, 2, 6, 15, 20, 30, 31, 45, 70, 71, 100, 140 })
        {
            SnapshotAssert.RestoresAndContinues(() => Builders().Match, script, snapshotTick, lastTick: 200);
        }
    }

    private static (Match Match, EntityId[] Group, EntityId[] Shooters, EntityId Lone, EntityId Inside) Besiegers()
    {
        var match = SiegeMatches.Field();
        match.Ring(1, (14, 8), (22, 16));
        match.Ring(1, (16, 10), (20, 14));
        match.WeakenWalls(40);
        EntityId[] group =
        [
            match.Spawn(0, SiegeMatches.Hoplite, "5.5", "11.5"),
            match.Spawn(0, SiegeMatches.Hoplite, "5.5", "12.5"),
            match.Spawn(0, SiegeMatches.Hoplite, "5.5", "13.5"),
        ];
        EntityId[] shooters = [match.Spawn(0, SiegeMatches.Peltast, "18.5", "2.5"), match.Spawn(0, SiegeMatches.Peltast, "19.5", "2.5")];
        var lone = match.Spawn(0, SiegeMatches.Hoplite, "29.5", "12.5");
        var inside = match.SpawnWorker(1, "18.5", "12.5");

        // Between the two rings, so that player 0 sees the worker its attack order names.
        match.Watcher(0, "15.5", "9.5");
        match.RefreshDerivedState();
        return (match, group, shooters, lone, inside);
    }

    /// <summary>
    /// A group on a move order, two units that shoot on an attack-move and one unit on an attack
    /// order break into a camp of two rings from three sides.
    /// </summary>
    [Fact]
    public void AMatchWithUnitsBreakingInRestoresAndContinues()
    {
        var (probe, group, shooters, lone, inside) = Besiegers();
        var script = new List<(int Tick, Command Command)>
        {
            (0, CombatMatches.MoveCommand(0, Center(18, 11), group)),
            (2, CombatMatches.AttackMoveCommand(0, Center(18, 12), shooters)),
            (3, CombatMatches.AttackCommand(0, inside, lone)),
        };
        foreach (var (tick, command) in script)
        {
            probe.Simulation.Commands.Enqueue(tick, command);
        }

        var walls = probe.WallHitPoints();
        probe.Simulation.Run(100);
        Assert.All(group.Append(lone), unit => Assert.NotEqual(EntityId.None, probe.UnitOf(unit).WallInWay));
        probe.Simulation.Run(1400);
        Assert.False(probe.IsAlive(inside));
        Assert.InRange(probe.Damaged(walls).Count, 2, 8);
        Assert.All(group.Concat(shooters).Append(lone), unit => Assert.Equal(UnitOrder.Idle, probe.UnitOf(unit).Order));

        foreach (var snapshotTick in new[] { 0, 1, 2, 3, 4, 60, 150, 300, 500, 700, 900, 1200 })
        {
            SnapshotAssert.RestoresAndContinues(() => Besiegers().Match, script, snapshotTick, lastTick: 1500);
        }
    }
}
