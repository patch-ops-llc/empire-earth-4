using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Production;

namespace Tools.Replay.Scripts;

/// <summary>
/// Orders at the structures that train (15d): one order to train, more orders in one tick than a
/// queue holds, cancelling, rally points on deposits and on other ground, and orders the
/// simulation must refuse.
/// </summary>
/// <remarks>
/// It gives its orders at whatever structure of a player has a queue: the Capitol of the start,
/// and every structure that trains once workers have built one. In a match without one it gives
/// no orders. Its workers earn only when another script puts them to work, so a recording that
/// is to train for long names <c>gather</c> beside it.
/// <para>
/// So that a match that began with workers alone comes to train units, it also has a worker
/// build a structure that trains them, a Barracks or a Works, for a player who has none of that
/// kind and can pay for it. These orders come from a generator and a clock of their own, and
/// each is one the simulation accepts when it is given.
/// </para>
/// </remarks>
[OrderScript("production")]
internal sealed class ProductionOrders : OrderScript
{
    private const int LongestPause = 60;

    private const int LongestPauseOfBuilds = 200;

    /// <summary>How many sites the script tries for a structure before it lets the order be.</summary>
    private const int SitesTried = 24;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D15_D000_0000_0000;

    /// <summary>The same for the orders to build a structure that trains.</summary>
    private const ulong GeneratorOfBuilds = 0x4D15_D100_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly DeterministicRandom _builds;
    private readonly List<int> _open = [];
    private readonly List<StructureDef> _wanted = [];
    private int _nextTick;
    private int _nextBuild;

    public ProductionOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
        _builds = setup.Generator(GeneratorOfBuilds);
        _nextBuild = 1 + _builds.NextInt(10);
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick >= _nextBuild)
        {
            _nextBuild = tick + 1 + _builds.NextInt(LongestPauseOfBuilds);
            AddBuildFor(_builds.NextInt(Match.Players.Count), commands);
        }

        if (tick >= _nextTick)
        {
            AddOrdersAtAStructure(tick, commands);
        }
    }

    private void AddOrdersAtAStructure(int tick, List<Command> commands)
    {
        _nextTick = tick + 1 + _random.NextInt(LongestPause);
        var player = _random.NextInt(Match.Players.Count);
        var kind = _random.NextInt(100);
        CollectProducersOf(player);
        if (Own.Count == 0)
        {
            return;
        }

        var structure = Own[_random.NextInt(Own.Count)];
        var producer = Match.Production.Producers.Get(structure);
        var offered = Match.Production.TrainedAt(Match.Structures.Get(structure).Type);
        if (kind < 40)
        {
            commands.Add(Train(player, structure, UnitToOrder(player, structure, offered)));
        }
        else if (kind < 50)
        {
            // One more than the queue has room for: the last is refused, or an earlier one when the stockpile runs out.
            var unit = UnitToOrder(player, structure, offered);
            for (var order = producer.Count; order <= Producer.MaxOrders; order++)
            {
                commands.Add(Train(player, structure, unit));
            }
        }
        else if (kind < 65)
        {
            // Any order of the queue, the one in production too, and now and then a position with no order.
            var position = _random.NextInt(producer.Count + 1);
            commands.Add(new Command(CommandType.CancelTrain, player, [structure], contentId: position));
        }
        else if (kind < 77 && Match.WorkSites.Count > 0)
        {
            // On a deposit, or on a Generator: a worker that appears is put to work there.
            var site = Match.WorkSites.ValueAt(_random.NextInt(Match.WorkSites.Count));
            var point = new FixedVector2(
                Fixed.FromRaw((site.X * Fixed.RawOne) + _random.NextInt(site.Size * Fixed.RawOne)),
                Fixed.FromRaw((site.Y * Fixed.RawOne) + _random.NextInt(site.Size * Fixed.RawOne)));
            commands.Add(Rally(player, Own, point));
        }
        else if (kind < 87)
        {
            // Anywhere on the map, on open or on blocked ground, for one structure or for all the player has.
            Picked.Clear();
            Picked.Add(structure);
            commands.Add(Rally(player, _random.NextInt(2) == 0 ? Picked : Own, AnyPoint(_random)));
        }
        else if (kind < 90)
        {
            // Structures of every player, a unit and an entity that never existed. Only the player's own get the point.
            Picked.Clear();
            for (var i = 0; i < Match.Production.Producers.Count; i++)
            {
                Picked.Add(Match.Production.Producers.EntityAt(i));
            }

            Picked.Add(Match.Bodies.EntityAt(_random.NextInt(Match.Bodies.Count)));
            Picked.Add(new EntityId(1_000_000 + _random.NextInt(1000)));
            commands.Add(Rally(player, Picked, AnyPoint(_random)));
        }
        else if (kind < 92)
        {
            // A point off the map.
            var beyond = Fixed.FromInt(Match.Grid.Width + _random.NextInt(5));
            commands.Add(Rally(player, Own, new FixedVector2(beyond, Fixed.FromInt(_random.NextInt(Match.Grid.Height)))));
        }
        else if (kind < 95)
        {
            // What this structure does not train: a unit no structure of its kind offers.
            commands.Add(Train(player, structure, NotOfferedBy(offered)));
        }
        else if (kind < 98)
        {
            // At a structure of another player, at a unit, or at an entity that never existed.
            Picked.Clear();
            var producers = Match.Production.Producers;
            for (var i = 0; i < producers.Count; i++)
            {
                if (!Own.Contains(producers.EntityAt(i)))
                {
                    Picked.Add(producers.EntityAt(i));
                }
            }

            Picked.Add(Match.Movers.Count > 0 ? Match.Movers.EntityAt(_random.NextInt(Match.Movers.Count)) : EntityId.None);
            Picked.Add(new EntityId(1_000_000 + _random.NextInt(1000)));
            commands.Add(Train(player, Picked[_random.NextInt(Picked.Count)], offered[0]));
        }
        else
        {
            commands.Add(Train(Match.Players.Count + _random.NextInt(5), structure, offered[0]));
        }
    }

    /// <summary>
    /// Has a worker of the player build a structure that trains units of a chassis, when the
    /// player has none of that kind, standing or under way, and can pay for it now. The site is
    /// one the order accepts, inside one of the player's territories.
    /// </summary>
    private void AddBuildFor(int player, List<Command> commands)
    {
        _wanted.Clear();
        foreach (var structure in Match.Data.Structures)
        {
            if (TrainsAChassis(structure)
                && Match.Construction.CountOf(player, structure.Index) == 0
                && Match.Construction.CheckStructure(player, structure.Index) == BuildProblem.None)
            {
                _wanted.Add(structure);
            }
        }

        CollectWorkersOf(player);
        if (_wanted.Count == 0 || Own.Count == 0 || !TryFindTerritory(player, out var territory))
        {
            return;
        }

        var def = _wanted[_builds.NextInt(_wanted.Count)];
        var worker = Own[_builds.NextInt(Own.Count)];
        var reach = (2 * territory.Radius) + 1;
        for (var i = 0; i < SitesTried; i++)
        {
            var x = (territory.CenterX2 / 2) - territory.Radius + _builds.NextInt(reach);
            var y = (territory.CenterY2 / 2) - territory.Radius + _builds.NextInt(reach);
            if (Match.Construction.CheckSite(player, def, x, y) == BuildProblem.None)
            {
                var point = MapGrid.TileCenter(x, y);
                commands.Add(new Command(CommandType.Build, player, [worker], point.X, point.Y, contentId: def.Index));
                return;
            }
        }
    }

    private bool TrainsAChassis(StructureDef structure)
    {
        foreach (var unit in Match.Production.TrainedAt(structure.Index))
        {
            if (unit != TrainOrder.Worker)
            {
                return true;
            }
        }

        return false;
    }

    private void CollectWorkersOf(int player)
    {
        Own.Clear();
        var workers = Match.Workers;
        for (var i = 0; i < workers.Count; i++)
        {
            var entity = workers.EntityAt(i);
            if (Match.World.Entities.IsAlive(entity) && Match.Bodies.Get(entity).Owner == player)
            {
                Own.Add(entity);
            }
        }
    }

    /// <summary>The first living territory of the player, in the order of the entities.</summary>
    private bool TryFindTerritory(int player, out Territory territory)
    {
        var territories = Match.Territories;
        for (var i = 0; i < territories.Count; i++)
        {
            if (territories.ValueAt(i).Owner == player && Match.World.Entities.IsAlive(territories.EntityAt(i)))
            {
                territory = territories.ValueAt(i);
                return true;
            }
        }

        territory = default;
        return false;
    }

    private static Command Train(int player, EntityId structure, int unit) =>
        new(CommandType.Train, player, [structure], contentId: unit);

    private static Command Rally(int player, List<EntityId> structures, FixedVector2 point) =>
        new(CommandType.SetRallyPoint, player, [.. structures], point.X, point.Y);

    /// <summary>Puts the living structures of a player that have a queue into <see cref="OrderScript.Own"/>, in the order of their numbers.</summary>
    private void CollectProducersOf(int player)
    {
        Own.Clear();
        var producers = Match.Production.Producers;
        for (var i = 0; i < producers.Count; i++)
        {
            var entity = producers.EntityAt(i);
            if (Match.World.Entities.IsAlive(entity) && Match.Bodies.Get(entity).Owner == player)
            {
                Own.Add(entity);
            }
        }
    }

    /// <summary>
    /// One of the units the structure offers that the player's research has opened, each with an
    /// even chance; one time in eight, any unit it offers, which may be one that is still locked.
    /// </summary>
    private int UnitToOrder(int player, EntityId structure, IReadOnlyList<int> offered)
    {
        _open.Clear();
        foreach (var unit in offered)
        {
            if (Match.Production.CheckTrain(player, structure, unit) != TrainCheck.Locked)
            {
                _open.Add(unit);
            }
        }

        return _open.Count == 0 || _random.NextInt(8) == 0
            ? offered[_random.NextInt(offered.Count)]
            : _open[_random.NextInt(_open.Count)];
    }

    /// <summary>A worker for a structure that trains chassis, and a chassis for one that trains workers.</summary>
    private int NotOfferedBy(IReadOnlyList<int> offered)
    {
        var chassis = _random.NextInt(Math.Max(1, Match.Data.Chassis.Count));
        return offered.Contains(TrainOrder.Worker) && !offered.Contains(chassis) ? chassis : TrainOrder.Worker;
    }
}
