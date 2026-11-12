using Sim.Core;
using Sim.Data;
using Sim.Map;

namespace Sim.Economy;

/// <summary>Something that uses supply besides workers: the units of an army, or orders in a queue.</summary>
public interface ISupplyUser
{
    /// <summary>The supply this uses for a player now.</summary>
    int SupplyUsedBy(int player);
}

/// <summary>
/// Each player's supply (design 4.1): a cap, not a stockpile. The cap follows the Logistics tier,
/// and what is used is counted from the state each time it is asked, so there is nothing here to
/// save or to refresh.
/// </summary>
/// <remarks>
/// Used supply may exceed the cap, which only blocks new orders (design 1.3): whatever places an
/// order asks <see cref="HasRoomFor"/> first.
/// </remarks>
public sealed class SupplyAccount
{
    private readonly GameData _data;
    private readonly IPlayerTechSource _tech;
    private readonly EntityRegistry _entities;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Worker> _workers;
    private readonly List<ISupplyUser> _users = [];

    public SupplyAccount(GameData data, IPlayerTechSource tech, EntityRegistry entities, ComponentStore<Body> bodies, ComponentStore<Worker> workers)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(tech);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(workers);
        _data = data;
        _tech = tech;
        _entities = entities;
        _bodies = bodies;
        _workers = workers;
    }

    /// <summary>
    /// Adds something whose supply counts as used. Add it when the match is set up, the same on
    /// every machine.
    /// </summary>
    public void AddUser(ISupplyUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _users.Add(user);
    }

    /// <summary>The most supply the player may use: the base plus a fixed amount per Logistics tier.</summary>
    public int Cap(int player) => _tech.Tech(player).SupplyCap;

    /// <summary>The supply the player uses: one worker's supply for each living worker, and what every added user reports.</summary>
    public int Used(int player)
    {
        var workers = 0;
        for (var i = 0; i < _workers.Count; i++)
        {
            var entity = _workers.EntityAt(i);
            if (_entities.IsAlive(entity) && _bodies.TryGet(entity, out var body) && body.Owner == player)
            {
                workers++;
            }
        }

        var used = checked(workers * _data.Resources.Worker.Supply);
        foreach (var user in _users)
        {
            used = checked(used + user.SupplyUsedBy(player));
        }

        return used;
    }

    /// <summary>The supply the player can still use. Zero when the used supply is at or over the cap.</summary>
    public int Free(int player) => Math.Max(0, Cap(player) - Used(player));

    /// <summary>True when the player can use this much more supply without passing the cap.</summary>
    public bool HasRoomFor(int player, int supply)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(supply);
        return supply <= Cap(player) - Used(player);
    }
}
