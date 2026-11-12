using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The command that cancels construction (design 2.5): for the selected workers of the player's
/// own that are on their way to build, whose whole price comes back, for those that are laying
/// a run of wall, whose tiles not yet laid are paid back, and for the selected structures of
/// the player's own that are still building themselves, a tile of wall among them, which are
/// taken away for three quarters of their price.
/// </summary>
public static class CancelBuild
{
    /// <summary>Cancels what is selected. Named in the input map of project.godot.</summary>
    public static readonly StringName Action = "build_cancel";

    /// <summary>The command, as a button of the bottom panel that is shown while there is something to cancel.</summary>
    public static SelectionCommand Command() => new()
    {
        Label = "Cancel build",
        Action = Action,
        Applies = Applies,
        Run = view => Give(view),
    };

    /// <summary>True when the selection holds something of the player's own whose construction can be cancelled.</summary>
    public static bool Applies(MatchView view) => Count(view, null) > 0;

    /// <summary>
    /// Gives the order for what of the selection can be cancelled. Returns false when there is
    /// nothing, or the session did not take the order.
    /// </summary>
    public static bool Give(MatchView view)
    {
        var entities = new List<EntityId>();
        if (Count(view, entities) == 0)
        {
            return false;
        }

        // The selection keeps no order of its own, and an order must not depend on one.
        entities.Sort();
        return view.Client.Submit(new Command(CommandType.CancelBuild, view.Player, [.. entities]));
    }

    // The workers with a build order or a wall order and the unfinished structures of the player's own in the selection.
    private static int Count(MatchView view, List<EntityId>? into)
    {
        var match = view.Match;
        var count = 0;
        foreach (var entity in view.Selection.Entities)
        {
            if (match.World.Entities.IsAlive(entity)
                && match.Bodies.TryGet(entity, out var body)
                && body.Owner == view.Player
                && (match.BuildOrders.Contains(entity) || match.Foundations.Contains(entity) || match.Walls.Has(entity)))
            {
                count++;
                into?.Add(entity);
            }
        }

        return count;
    }
}
