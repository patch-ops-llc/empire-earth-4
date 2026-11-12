using Sim.Combat;
using Sim.Core;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Setup;

namespace Sim.Production;

/// <summary>
/// Takes the first order of every production structure one tick further, and places its units
/// when its build time is over (design 5.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Time.</b> An order begins production in the first tick in which it is the first of its
/// queue: its build time is set then, from its owner's tech as it is then. That tick counts. So an
/// order placed in an empty queue by a command of tick t, with a build time of n ticks, puts its
/// units on the map in tick t + n - 1, and the order behind it begins in tick t + n.
/// </para>
/// <para>
/// <b>Where.</b> <see cref="AppearTiles"/> says on which tiles the units appear, on the grid as the
/// owner's units walk it: the owner's own finished gate is a tile to appear on (design 2.5), and
/// nobody else's is. A worker is placed by <see cref="EconomySetup.SpawnWorker"/> and a unit of a
/// chassis by <see cref="CombatSpawn.SpawnChassis"/>, so each has its full hit points and the
/// stats of its owner's tech in this tick. The units of a pair are created one after the other,
/// the first on the better tile.
/// </para>
/// <para>
/// <b>Rally point.</b> While none is set, the units stay where they appear. Once one is set, a
/// worker is assigned to the deposit, or to its owner's own Generator, whose tiles the point lies
/// on, and every other unit, and a worker whose point lies on neither or that cannot reach a
/// place there, gets a move order to the point. The units of a pair get one order together.
/// </para>
/// <para>
/// It runs first in the tick, before routes are planned, so that a unit that appears takes its
/// rally order and its first step in the same tick. The structures are walked in the order of
/// their IDs, so every machine creates the same units with the same numbers.
/// </para>
/// </remarks>
public sealed class ProductionSystem : ISystem
{
    private readonly MatchProduction _production;
    private readonly ICommandHandler _move;
    private readonly SimEventList _events;
    private Match? _match;

    // Scratch for one order. Not state.
    private readonly EntityId[] _units = new EntityId[TrainOrder.MaxCount];

    /// <param name="production">The rules of production.</param>
    /// <param name="move">The handler of the move order, which sends a new unit to the rally point as it sends any unit.</param>
    /// <param name="events">The event list.</param>
    public ProductionSystem(MatchProduction production, ICommandHandler move, SimEventList events)
    {
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(events);
        _production = production;
        _move = move;
        _events = events;
    }

    /// <summary>
    /// Names the match the units are placed in. The match setup calls it once, when the match
    /// exists, which is after the systems have been added in their order.
    /// </summary>
    internal void Attach(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (_match is not null)
        {
            throw new InvalidOperationException("The production system already has its match.");
        }

        _match = match;
    }

    public void Run(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var match = _match ?? throw new InvalidOperationException("The production system has no match yet.");

        var producers = _production.Producers;
        for (var i = 0; i < producers.Count; i++)
        {
            var structure = producers.EntityAt(i);
            if (producers.ValueAt(i).Count == 0
                || !world.Entities.IsAlive(structure)
                || !match.Bodies.TryGet(structure, out var body))
            {
                continue;
            }

            ref var producer = ref producers.ValueAt(i);
            var order = producer[0];
            if (producer.BuildTicks == 0)
            {
                producer.BuildTicks = _production.BuildTicksOf(body.Owner, order);
                producer.Progress = 0;
            }

            if (producer.Progress < producer.BuildTicks)
            {
                producer.Progress++;
            }

            if (producer.Progress < producer.BuildTicks)
            {
                continue;
            }

            // An order whose units can stand nowhere waits, finished, until they can.
            var rallySet = producer.RallySet;
            var rallyPoint = producer.RallyPoint;
            if (Place(match, structure, body, order, rallySet, rallyPoint))
            {
                producers.ValueAt(i).RemoveAt(0);
            }
        }
    }

    private bool Place(Match match, EntityId structure, Body body, TrainOrder order, bool rallySet, FixedVector2 rallyPoint)
    {
        Span<(int X, int Y)> tiles = stackalloc (int X, int Y)[order.Count];
        var footprint = match.Structures.Get(structure).Tiles;
        if (!AppearTiles.Find(match.Grid.For(body.Owner), footprint, body.Position, rallySet ? rallyPoint : body.Position, tiles))
        {
            return false;
        }

        var units = _units.AsSpan(0, tiles.Length);
        for (var i = 0; i < tiles.Length; i++)
        {
            var position = MapGrid.TileCenter(tiles[i].X, tiles[i].Y);
            units[i] = order.IsWorker ? match.SpawnWorker(body.Owner, position) : match.SpawnChassis(body.Owner, order.Unit, position);
            _events.Add(SimEventKind.UnitTrained, body.Owner, units[i], structure, position, TrainEvent.Amount(order.Unit, 1));
        }

        if (!rallySet)
        {
            return true;
        }

        if (order.IsWorker && SiteAt(match, body.Owner, rallyPoint) is { } site)
        {
            var walking = 0;
            foreach (var worker in units)
            {
                if (!match.Work.Assign(match.World, worker, site))
                {
                    units[walking++] = worker;
                }
            }

            units = units[..walking];
        }

        if (!units.IsEmpty)
        {
            _move.Execute(match.World, new Command(CommandType.Move, body.Owner, units, rallyPoint.X, rallyPoint.Y));
        }

        return true;
    }

    /// <summary>The deposit, or the player's own Generator, on whose tiles a point lies. Null when it lies on neither.</summary>
    private static EntityId? SiteAt(Match match, int player, FixedVector2 point)
    {
        var x = point.X.FloorToInt();
        var y = point.Y.FloorToInt();
        var sites = match.WorkSites;
        for (var i = 0; i < sites.Count; i++)
        {
            ref var site = ref sites.ValueAt(i);
            if (site.Covers(x, y)
                && (site.Kind == WorkSiteKind.Deposit || site.Owner == player)
                && match.World.Entities.IsAlive(sites.EntityAt(i)))
            {
                return sites.EntityAt(i);
            }
        }

        return null;
    }
}
