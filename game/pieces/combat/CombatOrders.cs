using Godot;
using Sim.Combat;
using Sim.Core;

namespace Game;

/// <summary>
/// The three fighting orders as the player gives them (design 2.4): attack, by a click with the
/// order button on an enemy unit; attack-move, by the same click with a key held; and stop, by
/// a button and a key. Each leaves as one command through the session.
/// </summary>
/// <remarks>
/// The orders are for units, which are what can walk. A selection with no unit in it, as a
/// selected structure, is offered no stop and given no attack-move: the simulation would refuse
/// both, and the player would be told of an order they never meant to give.
/// </remarks>
public static class CombatOrders
{
    /// <summary>Held with the order click for an attack-move. Named in the input map of project.godot.</summary>
    public static readonly StringName AttackMoveAction = "combat_attack_move";

    /// <summary>Gives the stop order. Named in the input map of project.godot.</summary>
    public static readonly StringName StopAction = "combat_stop";

    /// <summary>The color of the mark an attack order leaves.</summary>
    public static readonly Color AttackMark = new(1f, 0.3f, 0.25f);

    /// <summary>The color of the mark an attack-move order leaves.</summary>
    public static readonly Color AttackMoveMark = new(1f, 0.6f, 0.2f);

    /// <summary>The row for a click on an enemy unit: the selected units that can hit it go after it.</summary>
    public static OrderRow AttackRow() => new()
    {
        Name = "Attack",
        Targets = PointerTargets.EnemyUnit,
        Mark = AttackMark,
        Give = Attack,
    };

    /// <summary>The row for a click with the attack-move key held: the selected walk to the point and fight on the way.</summary>
    public static OrderRow AttackMoveRow() => new()
    {
        Name = "Attack-move",
        Targets = PointerTargets.Any,
        Modifier = AttackMoveAction,
        Mark = AttackMoveMark,
        Give = AttackMove,
    };

    /// <summary>The stop command, for any selection: every selected unit stands where it is.</summary>
    public static SelectionCommand StopCommand() => new()
    {
        Label = "Stop",
        Action = StopAction,
        Applies = view => HasUnit(view, view.Selection.Entities),
        Run = Stop,
    };

    /// <summary>True when at least one of these can walk, and so is a unit that takes the orders of this piece.</summary>
    public static bool HasUnit(MatchView view, IEnumerable<EntityId> selected)
    {
        var movers = view.Match.Movers;
        foreach (var entity in selected)
        {
            if (movers.Contains(entity))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when at least one of these units can take an attack order on the target: the
    /// simulation's own test (design 2.4), so that the view sends no order that would be refused.
    /// A worker has no attack, and some lines cannot hit some others.
    /// </summary>
    public static bool CanAttack(MatchView view, ReadOnlySpan<EntityId> units, EntityId target)
    {
        var match = view.Match;
        if (!match.World.Entities.IsAlive(target)
            || !match.Health.Contains(target)
            || !match.Bodies.TryGet(target, out var targetBody)
            || targetBody.Owner == view.Player)
        {
            return false;
        }

        var targetLine = match.Combatants.TryGet(target, out var enemy) ? match.Combat.LineOf(enemy.Chassis) : CombatRules.NoLine;
        foreach (var entity in units)
        {
            if (match.Combatants.TryGet(entity, out var unit)
                && match.Bodies.TryGet(entity, out var body)
                && body.Owner == view.Player
                && match.Combat.CanHit(match.Combat.LineOf(unit.Chassis), targetLine))
            {
                return true;
            }
        }

        return false;
    }

    // Selected units that cannot take the order are listed all the same: the simulation skips
    // them and they keep the order they had. With nobody who can take it this row has no order,
    // and the click falls through to the move order.
    private static bool Attack(OrderRequest request)
    {
        var view = request.View;
        var target = request.Target.Entity;
        return CanAttack(view, request.Selected, target)
            && view.Client.Submit(new Command(CommandType.Attack, view.Player, request.Selected, targetEntity: target));
    }

    // For a unit without an attack the simulation takes an attack-move as a move order, so the
    // row has an order for every selection with a unit in it. With none the row has no order,
    // and the click goes on to the rows after it.
    private static bool AttackMove(OrderRequest request)
    {
        var view = request.View;
        var point = request.Target.Point;
        return HasUnit(view, request.Selected)
            && view.Client.Submit(new Command(CommandType.AttackMove, view.Player, request.Selected, point.X, point.Y));
    }

    private static void Stop(MatchView view)
    {
        if (!HasUnit(view, view.Selection.Entities))
        {
            return;
        }

        EntityId[] selected = [.. view.Selection.Entities];
        if (view.Client.Submit(new Command(CommandType.Stop, view.Player, selected)) && OS.IsStdOutVerbose())
        {
            GD.Print($"Stop order for {selected.Length} at tick {view.Match.World.Tick}.");
        }
    }
}
