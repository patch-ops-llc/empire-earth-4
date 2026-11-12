using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Research;

namespace Sim.Structures;

/// <summary>
/// Takes the structures that were destroyed in this tick off the map: their tiles are free again,
/// the regions are numbered again, the units on their way get new routes, and a player whose last
/// finished Capitol is gone loses the research slot it gave (design 1.3). A Capitol that is still
/// building itself gives no slot, so it keeps none open and closes none when it goes. Each
/// structure is reported as a <see cref="SimEventKind.StructureRemoved"/>.
/// </summary>
/// <remarks>
/// It runs right after the deaths, so that everything later in the tick, and every command of the
/// next tick, sees the tiles as the structures left standing make them. A destroyed structure is
/// still in its store until the tick ends, which is how it is found here, exactly once, with
/// nothing remembered from one tick to the next. Whatever else destroys a structure inside a tick
/// does it before this system runs, as the order that cancels a structure under construction does.
/// </remarks>
public sealed class StructureSystem : ISystem
{
    private readonly GameData _data;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly ComponentStore<Foundation> _foundations;
    private readonly StructureTiles _tiles;
    private readonly MatchResearch _research;
    private readonly SimEventList _events;

    public StructureSystem(
        GameData data,
        ComponentStore<Body> bodies,
        ComponentStore<PlacedStructure> structures,
        ComponentStore<Foundation> foundations,
        StructureTiles tiles,
        MatchResearch research,
        SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(foundations);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(events);
        _data = data;
        _bodies = bodies;
        _structures = structures;
        _foundations = foundations;
        _tiles = tiles;
        _research = research;
        _events = events;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var removed = false;
        for (var i = 0; i < _structures.Count; i++)
        {
            var entity = _structures.EntityAt(i);
            if (world.Entities.IsAlive(entity))
            {
                continue;
            }

            removed = true;
            var structure = _structures.ValueAt(i);
            if (!_bodies.TryGet(entity, out var body))
            {
                continue;
            }

            _events.Add(SimEventKind.StructureRemoved, body.Owner, entity, position: body.Position, amount: structure.Type);
            if (GivesPlayerSlot(structure.Type) && !_foundations.Contains(entity) && !HasPlayerSlotGiver(world, body.Owner))
            {
                _research.ClosePlayerSlot(body.Owner);
            }
        }

        if (removed)
        {
            _tiles.Changed(world);
        }
    }

    /// <summary>
    /// True for the structure whose research slot is the player's own (design 1.3): the one every
    /// player starts with, when its file gives a slot.
    /// </summary>
    internal bool GivesPlayerSlot(int type) =>
        _data.StructureOf(type) is { ResearchSlots: > 0 } def && def.Id == DataRules.StartStructure;

    private bool HasPlayerSlotGiver(World world, int player)
    {
        for (var i = 0; i < _structures.Count; i++)
        {
            var entity = _structures.EntityAt(i);
            if (world.Entities.IsAlive(entity)
                && GivesPlayerSlot(_structures.ValueAt(i).Type)
                && !_foundations.Contains(entity)
                && _bodies.TryGet(entity, out var body)
                && body.Owner == player)
            {
                return true;
            }
        }

        return false;
    }
}
