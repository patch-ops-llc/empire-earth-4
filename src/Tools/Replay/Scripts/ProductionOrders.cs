using Sim.Core;
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
/// </remarks>
[OrderScript("production")]
internal sealed class ProductionOrders : OrderScript
{
    private const int LongestPause = 60;

    /// <summary>What the seed of these orders differs by from the seed of the orders.</summary>
    private const ulong Generator = 0x4D15_D000_0000_0000;

    private readonly DeterministicRandom _random;
    private readonly List<int> _open = [];
    private int _nextTick;

    public ProductionOrders(ScriptSetup setup)
        : base(setup)
    {
        _random = setup.Generator(Generator);
        _nextTick = 1 + _random.NextInt(10);
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (tick < _nextTick)
        {
            return;
        }

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
