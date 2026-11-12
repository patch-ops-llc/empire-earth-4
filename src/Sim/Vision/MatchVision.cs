using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Tech;

namespace Sim.Vision;

/// <summary>
/// What each player of a match sees (design 5.3): it works the fog of war out from where the units
/// stand, and answers whether a player sees a unit. Combat asks it before a unit picks a target
/// or takes an attack order, and <see cref="PlayerView"/> asks it for everything it hands out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who sees.</b> Every unit, from its center: a unit of a chassis as far as its owner's
/// blueprint of the chassis says, which holds the sight accent, and a worker as far as the
/// worker's profile says. Nothing blocks sight. Structures see from 17b on.
/// </para>
/// <para>
/// <b>When.</b> <see cref="VisionSystem"/> works the grid out again in every tick whose number is
/// a multiple of <see cref="RefreshTicks"/>, and the match setup does once, for what the players
/// see at the start. In between, the grid holds what the last time found, however the units have
/// moved since.
/// </para>
/// <para>
/// <b>Who is seen.</b> A player always sees its own. It sees another player's unit while the tile
/// under the unit's center is visible in the grid as it stands. That is asked anew whenever
/// somebody wants to know, so a unit that walks out of what its enemy last saw is lost to it in
/// that tick, and one that walks into it is seen in that tick.
/// </para>
/// </remarks>
public sealed class MatchVision
{
    private readonly EntityRegistry _entities;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<Combatant> _combatants;
    private readonly ComponentStore<Worker> _workers;
    private readonly Func<int, PlayerTech> _techOf;
    private readonly Fixed _workerSight;

    /// <param name="data">The loaded game data.</param>
    /// <param name="grid">The store of the fog of war, which the world holds.</param>
    /// <param name="entities">The entities of the world.</param>
    /// <param name="bodies">Where everything stands.</param>
    /// <param name="combatants">The units of a chassis.</param>
    /// <param name="workers">The workers.</param>
    /// <param name="techOf">Gives a player's tech as it is now: in a match, <see cref="Research.MatchResearch.Tech"/>.</param>
    public MatchVision(
        GameData data,
        VisionGrid grid,
        EntityRegistry entities,
        ComponentStore<Body> bodies,
        ComponentStore<Combatant> combatants,
        ComponentStore<Worker> workers,
        Func<int, PlayerTech> techOf)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(combatants);
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(techOf);
        Grid = grid;
        RefreshTicks = data.Rules.Vision.RefreshTicks;
        _entities = entities;
        _bodies = bodies;
        _combatants = combatants;
        _workers = workers;
        _techOf = techOf;
        _workerSight = Fixed.FromInt(data.Resources.Worker.Sight);
    }

    /// <summary>The fog of war: what each player has explored and what it sees now.</summary>
    public VisionGrid Grid { get; }

    /// <summary>The grid is worked out again in every tick whose number is a multiple of this (design 5.3: 4).</summary>
    public int RefreshTicks { get; }

    /// <summary>
    /// How far an entity sees, in tiles, from its center. False for one that does not see: a
    /// deposit, a shot, or an entity that is gone.
    /// </summary>
    public bool TryGetSight(EntityId entity, out Fixed sight)
    {
        sight = Fixed.Zero;
        if (!_entities.IsAlive(entity) || !_bodies.TryGet(entity, out var body))
        {
            return false;
        }

        if (_combatants.TryGet(entity, out var unit))
        {
            sight = SightOf(body.Owner, unit.Chassis);
            return true;
        }

        if (_workers.Contains(entity))
        {
            sight = _workerSight;
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when a player sees what stands at <paramref name="body"/>: its own always, and another
    /// player's while the tile under its center is visible to the player.
    /// </summary>
    public bool Sees(int player, in Body body) => body.Owner == player || Grid.IsVisible(player, body.Position);

    /// <summary>True when the entity is alive, stands on the map and the player sees it.</summary>
    public bool Sees(int player, EntityId entity) =>
        _entities.IsAlive(entity) && _bodies.TryGet(entity, out var body) && Sees(player, in body);

    /// <summary>
    /// Works out what every player sees from where the units stand now. Everything that was
    /// visible and is not seen any more stays explored.
    /// </summary>
    /// <remarks>
    /// In a match only <see cref="VisionSystem"/> and the match setup call this. A fixture that
    /// places units before the first tick calls it afterwards, if the units are to see before
    /// that tick has run. It is not for <see cref="Setup.Match.RefreshDerivedState"/>: the grid is
    /// state, and a restored snapshot has it as it was.
    /// </remarks>
    public void Refresh()
    {
        Grid.BeginRefresh();

        // What is visible is the union of what the units see, so their order changes nothing.
        for (var i = 0; i < _combatants.Count; i++)
        {
            var entity = _combatants.EntityAt(i);
            if (_entities.IsAlive(entity) && _bodies.TryGet(entity, out var body))
            {
                Grid.Reveal(body.Owner, body.Position, SightOf(body.Owner, _combatants.ValueAt(i).Chassis));
            }
        }

        for (var i = 0; i < _workers.Count; i++)
        {
            var entity = _workers.EntityAt(i);
            if (_entities.IsAlive(entity) && _bodies.TryGet(entity, out var body))
            {
                Grid.Reveal(body.Owner, body.Position, _workerSight);
            }
        }

        Grid.EndRefresh();
    }

    private Fixed SightOf(int player, int chassis) => Fixed.FromInt(_techOf(player).Blueprints[chassis].Sight);
}
