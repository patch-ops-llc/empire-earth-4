using Sim.Combat;
using Sim.Core;

namespace Game;

/// <summary>
/// The attack order on a structure (design 2.4, 2.5): a click with the order button on another
/// player's structure sends the selected units that can attack to destroy it. The structure
/// answers with a line round its tiles in the color of the attack order.
/// </summary>
/// <remarks>
/// <para>
/// An attack order may name a structure the player sees or remembers (design 5.3), and the
/// pointer finds exactly those, where they are drawn. So a click on a remembered structure is
/// an attack order like any other, also when the structure has been destroyed since: the
/// player cannot know, the simulation takes the order all the same, and the units walk to
/// where the structure is remembered. Nothing here asks whether the structure still stands.
/// </para>
/// <para>
/// A worker has no attack. With nobody selected who can attack, this row has no order, and the
/// click goes on to the move order: the selected walk to the point.
/// </para>
/// </remarks>
public static class StructureAttack
{
    /// <summary>The name of the row, as the log has it.</summary>
    public const string Name = "Attack a structure";

    /// <summary>The row for a click on another player's structure.</summary>
    /// <param name="effects">The layer that shows the mark round the structure.</param>
    public static OrderRow Row(StructureEffects effects) => new()
    {
        Name = Name,
        Targets = PointerTargets.EnemyStructure,

        // The mark is the line round the structure, and not a ring on the ground under the pointer.
        Mark = null,
        Give = request => Attack(request, effects),
    };

    /// <summary>
    /// True when at least one of these units can take an attack order on the structure: it is
    /// another player's, the player sees or remembers it, and one of the units is the player's
    /// own and has an attack. Every line can hit a structure.
    /// </summary>
    public static bool CanAttack(MatchView view, ReadOnlySpan<EntityId> units, EntityId structure)
    {
        // What the player knows of, whatever the development switch shows.
        if (!view.Sight.Seen.TryGetStructure(structure, out var known) || known.Owner == view.Player)
        {
            return false;
        }

        var match = view.Match;
        foreach (var entity in units)
        {
            if (match.Combatants.TryGet(entity, out var unit)
                && match.Bodies.TryGet(entity, out var body)
                && body.Owner == view.Player
                && match.Combat.CanHit(match.Combat.LineOf(unit.Chassis), CombatRules.NoLine))
            {
                return true;
            }
        }

        return false;
    }

    // Selected units that cannot take the order are listed all the same: the simulation skips
    // them and they keep the order they had.
    private static bool Attack(OrderRequest request, StructureEffects effects)
    {
        var view = request.View;
        var target = request.Target.Entity;

        // Only with the development switch that lifts the fog from the screen can the player
        // click on a structure they have never seen. The simulation would refuse the order, so
        // the tester is told why the units walk there instead.
        if (view.Sight.FogOff && !view.Sight.Seen.KnowsStructure(target))
        {
            view.Interface.Say("Development: the fog is off on the screen only. You have not seen that structure, so your units walk there instead of attacking it.");
        }

        if (!CanAttack(view, request.Selected, target)
            || !view.Client.Submit(new Command(CommandType.Attack, view.Player, request.Selected, targetEntity: target)))
        {
            return false;
        }

        effects.MarkAttacked(target);
        return true;
    }
}
