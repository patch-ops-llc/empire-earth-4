using Sim.Core;
using Sim.Economy;

namespace Game;

/// <summary>
/// The order that sends the selected workers to work at a Generator of the player's own, where
/// they earn Energy (design 4.1): the order button on the Generator, as on a deposit.
/// </summary>
/// <remarks>
/// The simulation takes the same order for a Generator as for a deposit, and the economy's own
/// row names deposits only, because a Generator is a structure and could not be pointed at
/// before structures were drawn. A Generator that is still building itself is no place of work
/// yet, so this row has no order for it and the click falls to the move order.
/// </remarks>
public static class GeneratorWork
{
    /// <summary>The row of the order table. It marks the Generator in the color of the work order at a deposit.</summary>
    public static OrderRow Row() => new()
    {
        Name = "Work at a Generator",
        Targets = PointerTargets.OwnStructure,
        Mark = GatherOrder.MarkColor,
        Give = Give,
    };

    private static bool Give(OrderRequest request)
    {
        var match = request.View.Match;
        var client = request.View.Client;
        if (!match.WorkSites.TryGet(request.Target.Entity, out var site)
            || site.Kind != WorkSiteKind.Generator
            || site.Owner != client.Player)
        {
            return false;
        }

        var workers = new List<EntityId>();
        foreach (var entity in request.Selected)
        {
            if (match.Workers.Contains(entity))
            {
                workers.Add(entity);
            }
        }

        return workers.Count > 0
            && client.Submit(new Command(CommandType.Gather, client.Player, [.. workers], targetEntity: request.Target.Entity));
    }
}
