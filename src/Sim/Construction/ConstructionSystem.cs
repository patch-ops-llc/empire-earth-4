using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Map;
using Sim.Movement;
using Sim.Numerics;

namespace Sim.Construction;

/// <summary>
/// Takes construction one tick further (design 2.5 and 5.4): the structures that are building
/// themselves, and then the build orders, whose workers lay their foundations as they arrive.
/// </summary>
/// <remarks>
/// <para>
/// <b>A structure builds itself.</b> Each tick after the one in which it was laid counts, and in
/// the tick in which the count reaches its build time it is finished: it gives what its file
/// says from then on, and is reported as a <see cref="SimEventKind.StructureFinished"/>. No
/// worker stays with it and none makes it faster.
/// </para>
/// <para>
/// <b>A build order</b>, oldest worker first:
/// </para>
/// <list type="bullet">
/// <item>A worker that died in this tick ends its order.</item>
/// <item>
/// A worker whose center is within <see cref="ConstructionRules.ArrivalReach"/> of the nearest
/// edge of the footprint has arrived. Every rule of placement is judged again with the state as
/// it is now (<see cref="ConstructionRules.CheckArrival"/>), and the foundation is laid in this
/// tick or the order ends.
/// </item>
/// <item>
/// A worker that stands still anywhere else cannot get nearer: the move order took it to the
/// nearest tile it can reach. Its order ends.
/// </item>
/// </list>
/// <para>
/// An order that ends gives its whole price back and is reported to its owner as a
/// <see cref="SimEventKind.BuildOrderEnded"/> that says why. A worker whose order ends on arrival
/// stops and has no work.
/// </para>
/// <para>
/// <b>Laying a foundation.</b> The owner's units that stand on the footprint are first moved to
/// the center of the nearest tile outside it that they can stand on: a tile of the ground they
/// stand in, ties to the lower y and then the lower x. They keep their orders; an idle unit's
/// post moves with it. Then the structure is laid, which blocks its tiles and sends the units on
/// their way round it. Workers of any player whose place at a work site is now under the
/// structure take another. The worker goes back to the work site it was assigned to, if it had
/// one, and stands otherwise.
/// </para>
/// <para>
/// <b>Where it runs.</b> After movement, so that a worker lays its foundation in the tick in
/// which it arrives. After the deaths, so that the order of a worker killed in this tick ends in
/// this tick, while its values are still in the stores. After the structure system, so that a
/// site is judged on the tiles as they are once the structures destroyed in this tick are gone.
/// Before income and research, so that what a structure finished in this tick gives is there for
/// both. A system that destroys entities goes before it.
/// </para>
/// </remarks>
public sealed class ConstructionSystem : ISystem
{
    private readonly ConstructionRules _rules;
    private readonly GameData _data;

    // Scratch for one tick, emptied before each use. Not state.
    private readonly List<EntityId> _due = [];

    public ConstructionSystem(ConstructionRules rules, GameData data)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(data);
        _rules = rules;
        _data = data;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        BuildFoundations(world);

        // The orders that there are now, so that ending one does not disturb the walk.
        var orders = _rules.Orders;
        _due.Clear();
        for (var i = 0; i < orders.Count; i++)
        {
            _due.Add(orders.EntityAt(i));
        }

        var laid = false;
        long reach = FixedVector2.SquareRaw(ConstructionRules.ArrivalReach);
        foreach (var worker in _due)
        {
            if (!world.Entities.IsAlive(worker))
            {
                _rules.EndOrder(worker, BuildProblem.WorkerDied);
                continue;
            }

            var order = orders.Get(worker);
            var body = _rules.Bodies.Get(worker);
            if (order.Tiles.DistanceSquaredRawTo(body.Position) <= reach)
            {
                var structure = _data.StructureOf(order.Type)!;
                var problem = _rules.CheckArrival(body.Owner, structure, order.X, order.Y, worker);
                if (problem == BuildProblem.None)
                {
                    Lay(world, worker, order, structure, body.Owner);
                    laid = true;
                }
                else
                {
                    Fail(worker, problem);
                }
            }
            else if (_rules.Movers.Get(worker).State == MoveState.Idle)
            {
                Fail(worker, BuildProblem.CannotReach);
            }
        }

        if (laid)
        {
            // Units were moved and structures added after the index of this tick was made.
            _rules.SpatialIndex.Rebuild();
        }
    }

    private void BuildFoundations(World world)
    {
        var foundations = _rules.Foundations;
        _due.Clear();
        for (var i = 0; i < foundations.Count; i++)
        {
            var entity = foundations.EntityAt(i);
            if (!world.Entities.IsAlive(entity))
            {
                continue;
            }

            ref var foundation = ref foundations.ValueAt(i);
            foundation.Built++;
            if (foundation.Built >= _data.StructureOf(_rules.Structures.Get(entity).Type)!.BuildTicks)
            {
                _due.Add(entity);
            }
        }

        foreach (var entity in _due)
        {
            foundations.Remove(entity);
            _rules.Factory.Finish(entity);
            _rules.Events.Add(
                SimEventKind.StructureFinished,
                _rules.Bodies.Get(entity).Owner,
                entity,
                position: _rules.Bodies.Get(entity).Position,
                amount: _rules.Structures.Get(entity).Type);
        }
    }

    private void Fail(EntityId worker, BuildProblem why)
    {
        _rules.EndOrder(worker, why);
        _rules.Movers.Get(worker).Stop();
        _rules.Work.Release(worker);
    }

    private void Lay(World world, EntityId worker, BuildOrder order, StructureDef structure, int owner)
    {
        var site = order.Tiles;
        MoveOwnUnitsOff(world, site, owner);

        var entity = _rules.Factory.Lay(owner, structure, order.X, order.Y);
        _rules.Foundations.Add(entity, new Foundation { Materials = order.Materials, Energy = order.Energy });
        _rules.Orders.Remove(worker);

        _rules.Work.LeaveTiles(world, site);
        _rules.Movers.Get(worker).Stop();
        _rules.Work.Return(world, worker);
    }

    /// <summary>Moves the owner's ground units that stand on the site off it, before its tiles are blocked.</summary>
    private void MoveOwnUnitsOff(World world, TileSquare site, int owner)
    {
        var movers = _rules.Movers;
        for (var i = 0; i < movers.Count; i++)
        {
            var entity = movers.EntityAt(i);
            var bodyIndex = _rules.Bodies.IndexOf(entity);
            if (bodyIndex < 0 || !_rules.IsGroundUnit(entity) || !world.Entities.IsAlive(entity))
            {
                continue;
            }

            ref var body = ref _rules.Bodies.ValueAt(bodyIndex);
            if (body.Owner != owner || !site.Covers(body.Position) || !TryFindTileOutside(site, body.Position, out var x, out var y))
            {
                continue;
            }

            body.Position = MapGrid.TileCenter(x, y);
            var combatant = _rules.Combatants.IndexOf(entity);
            if (combatant >= 0 && _rules.Combatants.ValueAt(combatant).Order == UnitOrder.Idle)
            {
                _rules.Combatants.ValueAt(combatant).Anchor = body.Position;
            }
        }
    }

    /// <summary>
    /// Finds the tile outside the site whose center is nearest a point on it: a tile of the
    /// ground the point is in, or, when that ground has none outside the site, any tile a unit can
    /// stand on. Ties go to the lower y and then the lower x.
    /// </summary>
    private bool TryFindTileOutside(TileSquare site, FixedVector2 point, out int tileX, out int tileY)
    {
        var region = _rules.Grid.RegionOf(point);
        return (region != 0 && TryFindTileOutside(site, point, region, out tileX, out tileY))
            || TryFindTileOutside(site, point, 0, out tileX, out tileY);
    }

    private bool TryFindTileOutside(TileSquare site, FixedVector2 point, int region, out int tileX, out int tileY)
    {
        var grid = _rules.Grid;
        tileX = -1;
        tileY = -1;
        var best = long.MaxValue;
        var rings = Math.Max(grid.Width, grid.Height);
        for (var ring = 1; ring <= rings; ring++)
        {
            // The center of a tile in this ring is at least ring - 1/2 tiles from every point of the site.
            long sure = ((2L * ring) - 1) * (Fixed.RawOne / 2);
            if (best < sure * sure)
            {
                break;
            }

            var low = -ring;
            var high = site.Size - 1 + ring;
            for (var dy = low; dy <= high; dy++)
            {
                var onEdgeRow = dy == low || dy == high;
                for (var dx = low; dx <= high; dx += onEdgeRow ? 1 : high - low)
                {
                    var x = site.X + dx;
                    var y = site.Y + dy;
                    if (!grid.IsPassable(x, y) || (region != 0 && grid.RegionOf(x, y) != region))
                    {
                        continue;
                    }

                    var distance = FixedVector2.DistanceSquaredRaw(MapGrid.TileCenter(x, y), point);
                    if (distance < best || (distance == best && (y < tileY || (y == tileY && x < tileX))))
                    {
                        (best, tileX, tileY) = (distance, x, y);
                    }
                }
            }
        }

        return tileX >= 0;
    }
}
