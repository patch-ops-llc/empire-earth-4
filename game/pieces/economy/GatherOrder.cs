using Godot;
using Sim.Core;
using Sim.Economy;

namespace Game;

/// <summary>
/// The work order: a click with the order button on a deposit sends the selected workers to work
/// there (design 4.1). Each takes a place beside the deposit and earns Materials while it stands
/// at it.
/// </summary>
/// <remarks>
/// Only workers take the order. A selection without a worker has no work order, and the click is
/// then a move order like any other. A deposit outside the player's territory can be the target:
/// the workers stand there and earn nothing, and the message line says so.
/// </remarks>
public sealed class GatherOrder
{
    /// <summary>The color of the ring that closes round the deposit.</summary>
    public static readonly Color MarkColor = new(1f, 0.82f, 0.3f);

    private const string OutsideTerritory = "That deposit is outside your territory. Workers there earn nothing.";
    private const double MessageSeconds = 6.0;

    private readonly DepositLayer _deposits;
    private ulong _saidUntil;

    public GatherOrder(DepositLayer deposits) => _deposits = deposits;

    /// <summary>The row of the order table. It leaves no mark at the pointer: the deposit itself is marked.</summary>
    public OrderRow Row() => new()
    {
        Name = "Gather",
        Targets = PointerTargets.Deposit,
        Mark = null,
        Give = Give,
    };

    private bool Give(OrderRequest request)
    {
        var match = request.View.Match;
        if (!match.WorkSites.TryGet(request.Target.Entity, out var site))
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

        var client = request.View.Client;
        if (workers.Count == 0
            || !client.Submit(new Command(CommandType.Gather, client.Player, [.. workers], targetEntity: request.Target.Entity)))
        {
            return false;
        }

        _deposits.ShowOrder(site, MarkColor);
        if (!Territory.Holds(match.Territories, match.World.Entities, client.Player, site.X, site.Y, site.Size))
        {
            SayOutsideTerritory(request.View.Interface);
        }

        return true;
    }

    // Said once for as long as it stays in the message line, however often the player clicks.
    private void SayOutsideTerritory(InterfaceFrame frame)
    {
        var now = Time.GetTicksMsec();
        if (now < _saidUntil)
        {
            return;
        }

        _saidUntil = now + (ulong)(MessageSeconds * 1000.0);
        frame.Say(OutsideTerritory, MessageSeconds);
    }
}
