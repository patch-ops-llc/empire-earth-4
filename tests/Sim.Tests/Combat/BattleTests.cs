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
/// A whole battle of mixed armies round a wall, with every kind of order: that a snapshot taken
/// anywhere in it continues the same way, and the rules that must hold in every tick of it.
/// </summary>
/// <remarks>
/// No state hash is pinned here. The fight that every system must bring to the same end is the
/// golden replay <c>ridge-fight.replay</c>.
/// </remarks>
public class BattleTests
{
    private const int LastTick = 900;

    /// <summary>Ten units a side, mirrored left to right, with a wall between them that has a gap at each end.</summary>
    private static Match Build()
    {
        var rows = new string[24];
        for (var y = 0; y < rows.Length; y++)
        {
            var row = new string('.', 40).ToCharArray();
            if (y is >= 5 and <= 18)
            {
                row[19] = '#';
                row[20] = '#';
            }

            rows[y] = new string(row);
        }

        var match = Create(TestMaps.FromRows(rows));
        foreach (var (player, x, back) in new[] { (0, 12, -1), (1, 27, 1) })
        {
            for (var i = 0; i < 4; i++)
            {
                match.Spawn(player, Hoplite, $"{x}.5", $"{9 + (2 * i)}.5");
            }

            for (var i = 0; i < 3; i++)
            {
                match.Spawn(player, Peltast, $"{x + (2 * back)}.5", $"{10 + (2 * i)}.5");
            }

            match.Spawn(player, Hippeus, $"{x}.5", "3.5");
            match.Spawn(player, Hippeus, $"{x}.5", "20.5");
            match.Spawn(player, Ballista, $"{x + (4 * back)}.5", "12.5");
            match.SpawnWorker(player, $"{x + (6 * back)}.5", "12.5");
        }

        match.RefreshDerivedState();
        return match;
    }

    private static (int Tick, Command Command)[] Script()
    {
        static EntityId[] Ids(int first, int count) => [.. Enumerable.Range(first, count).Select(id => new EntityId(id))];

        // Player 0 has entities 1 to 11 and player 1 has 12 to 22, in the order Build places them:
        // four Guards, three Line infantry, two Cavalry/Armor, one Artillery, one worker.
        return
        [
            (1, AttackMoveCommand(0, TestMatches.Point("30.5", "12.5"), Ids(1, 11))),
            (2, AttackMoveCommand(1, TestMatches.Point("9.5", "12.5"), Ids(12, 9))),
            (3, AttackCommand(1, new EntityId(11), new EntityId(21))),
            (40, AttackCommand(0, new EntityId(21), new EntityId(8), new EntityId(9))),
            (90, new Command(CommandType.Stop, 1, Ids(16, 3))),
            (120, MoveCommand(0, TestMatches.Point("14.5", "2.5"), Ids(5, 3))),
            (200, AttackMoveCommand(0, TestMatches.Point("30.5", "12.5"), Ids(1, 11))),
            (260, AttackCommand(1, new EntityId(1), Ids(12, 11))),
            (400, AttackMoveCommand(1, TestMatches.Point("9.5", "12.5"), Ids(12, 11))),
        ];
    }

    private static Match Run(int ticks)
    {
        var match = Build();
        foreach (var (tick, command) in Script())
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        match.Run(ticks);
        return match;
    }

    [Fact]
    public void TheBattleHasEveryStateTheSnapshotTestsAreTakenIn()
    {
        var early = Run(30);
        Assert.Contains(Enumerable.Range(0, early.Combatants.Count), i => early.Combatants.ValueAt(i).Order == UnitOrder.AttackMove);
        Assert.Contains(Enumerable.Range(0, early.Combatants.Count), i => early.Combatants.ValueAt(i).Order == UnitOrder.Attack);
        Assert.Contains(Enumerable.Range(0, early.Movers.Count), i => early.Movers.ValueAt(i).State == MoveState.Following);

        var middle = Run(131);
        Assert.NotEqual(0, middle.Shots.Count);
        Assert.Contains(Enumerable.Range(0, middle.Combatants.Count), i => middle.Combatants.ValueAt(i).Wait > Fraction.Zero);
        Assert.Contains(Enumerable.Range(0, middle.Combatants.Count), i => middle.Combatants.ValueAt(i).Engaged);
        Assert.Contains(Enumerable.Range(0, middle.Combatants.Count), i => middle.Combatants.ValueAt(i).Order == UnitOrder.Move);
        Assert.Contains(Enumerable.Range(0, middle.Health.Count), i => middle.Health.ValueAt(i).HitPoints < middle.Health.ValueAt(i).Maximum);

        var late = Run(LastTick);
        Assert.InRange(late.Combatants.Count, 1, 15);
        Assert.Contains(Enumerable.Range(0, late.Combatants.Count), i => late.Combatants.ValueAt(i).Order == UnitOrder.Idle);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(30)]
    [InlineData(91)]
    [InlineData(131)]
    [InlineData(263)]
    [InlineData(420)]
    [InlineData(700)]
    public void ASnapshotTakenInTheMiddleOfTheBattleRestoresAndContinues(int snapshotTick) =>
        SnapshotAssert.RestoresAndContinues(Build, Script(), snapshotTick, LastTick);

    /// <summary>
    /// A unit attacks only in a tick in which it does not walk (design 2.4). From outside a tick,
    /// a unit that attacked has not walked when it stands where it stood, or when it stood still
    /// before the tick and was given no order in it: then only its own side can have stepped it
    /// aside, which is no walk. The battle has attacks of both kinds.
    /// </summary>
    [Fact]
    public void InEveryTickOfTheBattleTheRulesOfAnAttackHold()
    {
        var match = Build();
        var script = Script();
        foreach (var (tick, command) in script)
        {
            match.Simulation.Commands.Enqueue(tick, command);
        }

        var attacks = 0;
        var attacksWhileStepped = 0;
        var before = new Dictionary<EntityId, (FixedVector2 Position, Fraction Wait, bool Stood)>();
        var ordered = new HashSet<EntityId>();
        for (var tick = 0; tick < LastTick; tick++)
        {
            before.Clear();
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                var unit = match.Combatants.ValueAt(i);
                before[entity] = (match.PositionOf(entity), unit.Wait, match.Movers.Get(entity).State == MoveState.Idle);
            }

            ordered.Clear();
            foreach (var (commandTick, command) in script)
            {
                if (commandTick == tick)
                {
                    ordered.UnionWith(command.Entities.ToArray());
                }
            }

            match.Run(1);
            for (var i = 0; i < match.Combatants.Count; i++)
            {
                var entity = match.Combatants.EntityAt(i);
                var unit = match.Combatants.ValueAt(i);
                var (position, wait, stood) = before[entity];

                // The wait only goes up when the unit attacks.
                if (unit.Wait > wait)
                {
                    attacks++;

                    // A unit attacks only in a tick in which it does not walk, and never on a move order.
                    var stepped = match.PositionOf(entity) != position;
                    attacksWhileStepped += stepped ? 1 : 0;
                    Assert.True(
                        !stepped || (stood && !ordered.Contains(entity)),
                        $"{entity} attacked in tick {tick}, in which it walked from {position} to {match.PositionOf(entity)}.");
                    Assert.Equal(MoveState.Idle, match.Movers.Get(entity).State);
                    Assert.NotEqual(UnitOrder.Move, unit.Order);
                    Assert.True(wait <= 1, $"{entity} attacked in tick {tick} with {wait} ticks still to wait.");
                }

                // A unit on a move order has no target.
                if (unit.Order == UnitOrder.Move)
                {
                    Assert.Equal(EntityId.None, unit.Target);
                }

                Assert.True(match.HitPointsOf(entity) > 0, $"{entity} is still there with no hit points after tick {tick}.");
                Assert.InRange(match.HitPointsOf(entity), 1, match.Health.Get(entity).Maximum);
            }
        }

        Assert.InRange(attacks, 50, 5_000);
        Assert.InRange(attacksWhileStepped, 1, attacks - 1);
    }
}
