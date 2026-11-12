using Sim.Core;
using Sim.Data;
using Sim.Economy;
using Sim.Map;
using Sim.Numerics;
using Sim.Structures;
using Sim.Tech;

namespace Sim.Production;

/// <summary>
/// The rules of production (design 2.3 and 5.4): which structure trains what, what an order
/// costs, placing and cancelling orders, and the rally point. The orders themselves are state, in
/// the store of <see cref="Producer"/>; this holds none.
/// </summary>
/// <remarks>
/// <para>
/// <b>An order is fixed when it is placed.</b> Placing it pays its price, holds its supply and
/// settles whether it is one unit or a pair. A chassis with the Batch perk is ordered as a pair
/// when the stockpile and the free supply cover two units, and as one unit when they cover only
/// one. No research that finishes later changes an order in a queue. Cancelling gives back what
/// was paid.
/// </para>
/// <para>
/// <b>The build time is set when production of the order begins</b>, from the owner's blueprint
/// as it is then: <see cref="BuildTicksOf"/>. The units that come out are placed with the stats
/// of that moment.
/// </para>
/// <para>
/// <b>Supply.</b> The supply of an order is used from the moment it is placed, so this is one of
/// the users of the <see cref="SupplyAccount"/>. It is counted from the queues of the structures
/// that stand each time it is asked, so the supply of a destroyed structure's orders is free at
/// once.
/// </para>
/// </remarks>
public sealed class MatchProduction : ISupplyUser
{
    private readonly GameData _data;
    private readonly EntityRegistry _entities;
    private readonly ComponentStore<Body> _bodies;
    private readonly ComponentStore<PlacedStructure> _structures;
    private readonly Stockpiles _stockpiles;
    private readonly SupplyAccount _supply;
    private readonly IPlayerTechSource _tech;
    private readonly SimEventList _events;
    private readonly int _playerCount;
    private readonly Dictionary<int, int[]> _trainedAt = [];

    public MatchProduction(
        GameData data,
        EntityRegistry entities,
        ComponentStore<Body> bodies,
        ComponentStore<Producer> producers,
        ComponentStore<PlacedStructure> structures,
        Stockpiles stockpiles,
        SupplyAccount supply,
        IPlayerTechSource tech,
        SimEventList events,
        int playerCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(producers);
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(stockpiles);
        ArgumentNullException.ThrowIfNull(supply);
        ArgumentNullException.ThrowIfNull(tech);
        ArgumentNullException.ThrowIfNull(events);
        _data = data;
        _entities = entities;
        _bodies = bodies;
        Producers = producers;
        _structures = structures;
        _stockpiles = stockpiles;
        _supply = supply;
        _tech = tech;
        _events = events;
        _playerCount = playerCount;

        // Fixed by the data, and made here once so that asking never allocates.
        foreach (var structure in data.Structures)
        {
            _trainedAt.Add(structure.Index, UnitsOf(data, structure));
        }
    }

    /// <summary>The orders, progress and rally point of every finished structure that trains.</summary>
    public ComponentStore<Producer> Producers { get; }

    /// <summary>
    /// What a structure of the roster trains, in the order it is offered: the worker first, as
    /// <see cref="TrainOrder.Worker"/>, then the chassis of each of its lines from the oldest
    /// epoch on, by their indexes. Empty for a structure that trains nothing. Which of the
    /// chassis a player may order now, <see cref="CheckTrain"/> says.
    /// </summary>
    public IReadOnlyList<int> TrainedAt(int structureType) =>
        _trainedAt.TryGetValue(structureType, out var units) ? units : [];

    /// <summary>
    /// Gives a structure that has just been finished its queue, when it is one that trains
    /// (design 2.5: production comes with completion). Called once for a structure, by whatever
    /// finishes it.
    /// </summary>
    public void Open(EntityId structure)
    {
        if (_structures.TryGet(structure, out var placed) && TrainedAt(placed.Type).Count > 0)
        {
            Producers.Add(structure, default);
        }
    }

    /// <summary>
    /// The price of one unit in Materials and Energy and its supply, for a player now: the
    /// worker's from the data, a chassis's from the player's blueprint.
    /// </summary>
    public (int Materials, int Energy, int Supply) PriceOf(int player, int unit)
    {
        if (unit == TrainOrder.Worker)
        {
            var worker = _data.Resources.Worker;
            return (worker.Materials, worker.Energy, worker.Supply);
        }

        var blueprint = Blueprint(player, unit);
        return (blueprint.Materials, blueprint.Energy, blueprint.Supply);
    }

    /// <summary>
    /// Whether the player can place an order for this unit at this structure now, and if not, why
    /// not. It changes nothing: it is what <see cref="Train"/> asks first, and what a panel asks to
    /// show which orders can be given.
    /// </summary>
    /// <param name="player">The player who would give the order.</param>
    /// <param name="structure">The structure it would be given to.</param>
    /// <param name="unit">A chassis by its index, or <see cref="TrainOrder.Worker"/>.</param>
    public TrainCheck CheckTrain(int player, EntityId structure, int unit)
    {
        if (!IsProducerOf(player, structure, out var index))
        {
            return TrainCheck.NoProducer;
        }

        if (!Trains(_structures.Get(structure).Type, unit))
        {
            return TrainCheck.NotTrainedHere;
        }

        if (unit != TrainOrder.Worker && !Blueprint(player, unit).Unlocked)
        {
            return TrainCheck.Locked;
        }

        if (Producers.ValueAt(index).Count >= Producer.MaxOrders)
        {
            return TrainCheck.QueueFull;
        }

        var (materials, energy, supply) = PriceOf(player, unit);
        if (!_stockpiles.Covers(player, materials, energy))
        {
            return TrainCheck.TooExpensive;
        }

        return _supply.HasRoomFor(player, supply) ? TrainCheck.Allowed : TrainCheck.NoSupply;
    }

    /// <summary>
    /// The units an order for this unit would make if it were placed now: 2 when the chassis has
    /// the Batch perk and the stockpile and the free supply cover two, otherwise 1. It does not
    /// say whether the order can be placed at all; <see cref="CheckTrain"/> does.
    /// </summary>
    public int CountOf(int player, int unit)
    {
        if (unit == TrainOrder.Worker)
        {
            return 1;
        }

        var batch = Math.Min(Blueprint(player, unit).BatchSize, TrainOrder.MaxCount);
        if (batch <= 1)
        {
            return 1;
        }

        var (materials, energy, supply) = PriceOf(player, unit);
        return _stockpiles.Covers(player, checked(batch * materials), checked(batch * energy))
            && _supply.HasRoomFor(player, checked(batch * supply))
                ? batch
                : 1;
    }

    /// <summary>
    /// Places an order at the end of a structure's queue: pays its price, holds its supply and
    /// settles whether it is one unit or a pair. Call it inside a tick.
    /// </summary>
    /// <returns><see cref="TrainCheck.Allowed"/> when the order was placed; otherwise why it was not, with nothing changed.</returns>
    public TrainCheck Train(int player, EntityId structure, int unit)
    {
        var check = CheckTrain(player, structure, unit);
        if (check != TrainCheck.Allowed)
        {
            return check;
        }

        var count = CountOf(player, unit);
        var (materials, energy, supply) = PriceOf(player, unit);
        var order = new TrainOrder
        {
            Unit = unit,
            Count = count,
            Materials = checked(count * materials),
            Energy = checked(count * energy),
            Supply = checked(count * supply),
        };
        if (!_stockpiles.TryPay(player, order.Materials, order.Energy))
        {
            throw new InvalidOperationException("The stockpile covered the order and could not pay it.");
        }

        Producers.Get(structure).Add(order);
        Report(SimEventKind.TrainOrdered, player, structure, order);
        return TrainCheck.Allowed;
    }

    /// <summary>
    /// Takes the order at a position out of a structure's queue and gives back what was paid for
    /// it (design 2.3). Position 0 is the order in production, whose progress is lost; the next
    /// order begins anew. Call it inside a tick.
    /// </summary>
    /// <returns>False, with nothing changed, when the structure is no producer of the player's or has no order at that position.</returns>
    public bool Cancel(int player, EntityId structure, int position)
    {
        if (!IsProducerOf(player, structure, out var index))
        {
            return false;
        }

        ref var producer = ref Producers.ValueAt(index);
        if (position < 0 || position >= producer.Count)
        {
            return false;
        }

        var order = producer.RemoveAt(position);
        _stockpiles.Add(player, order.Materials, order.Energy);
        Report(SimEventKind.TrainCancelled, player, structure, order);
        return true;
    }

    /// <summary>
    /// Sets a structure's rally point (design 5.4): from now on a unit that appears gets a move
    /// order to it, and a worker is assigned to the deposit or to the player's own Generator the
    /// point lies on, when it lies on one. Call it inside a tick.
    /// </summary>
    /// <returns>False, with nothing changed, when the structure is no producer of the player's.</returns>
    public bool SetRallyPoint(int player, EntityId structure, FixedVector2 point)
    {
        if (!IsProducerOf(player, structure, out var index))
        {
            return false;
        }

        ref var producer = ref Producers.ValueAt(index);
        producer.RallySet = true;
        producer.RallyPoint = point;
        _events.Add(SimEventKind.RallyPointSet, player, structure, position: point);
        return true;
    }

    /// <summary>The supply held for the orders in the queues of the player's structures that stand.</summary>
    public int SupplyUsedBy(int player)
    {
        var used = 0;
        for (var i = 0; i < Producers.Count; i++)
        {
            var entity = Producers.EntityAt(i);
            if (_entities.IsAlive(entity) && _bodies.TryGet(entity, out var body) && body.Owner == player)
            {
                used = checked(used + Producers.ValueAt(i).ReservedSupply);
            }
        }

        return used;
    }

    /// <summary>
    /// The build time of an order whose production begins now, under its owner's tech as it is
    /// now: the worker's from the data; one unit's from the blueprint; a pair's the batch time of
    /// the blueprint (design 2.3: 1.5 times the unrounded time of one, rounded once).
    /// </summary>
    /// <remarks>
    /// A pair stays a pair when a research has taken the Batch perk from its chassis since it
    /// was ordered. It then takes the time of two units, one after the other.
    /// </remarks>
    public int BuildTicksOf(int player, in TrainOrder order)
    {
        if (order.IsWorker)
        {
            return Math.Max(1, _data.Resources.Worker.BuildTicks);
        }

        var blueprint = Blueprint(player, order.Unit);
        if (order.Count <= 1)
        {
            return blueprint.BuildTicks;
        }

        return blueprint.BatchSize == order.Count ? blueprint.BatchBuildTicks : checked(order.Count * blueprint.BuildTicks);
    }

    /// <summary>
    /// Gives back every order of a structure that was destroyed, in the order of the queue, and
    /// empties it (design 2.5). The supply they held is free already.
    /// </summary>
    internal void RefundAll(int player, EntityId structure, ref Producer producer)
    {
        while (producer.Count > 0)
        {
            var order = producer.RemoveAt(0);
            _stockpiles.Add(player, order.Materials, order.Energy);
            Report(SimEventKind.TrainRefunded, player, structure, order);
        }
    }

    internal void ReportRefused(in Command command, TrainCheck reason) =>
        _events.Add(
            SimEventKind.TrainRefused,
            command.Player,
            command.Entities.IsEmpty ? EntityId.None : command.Entities[0],
            amount: TrainEvent.Amount(command.ContentId, reason: reason));

    private static int[] UnitsOf(GameData data, StructureDef structure)
    {
        var units = new List<int>();
        if (structure.TrainsWorkers)
        {
            units.Add(TrainOrder.Worker);
        }

        foreach (var line in structure.Trains)
        {
            if (data.LineOf(line) is { } def)
            {
                units.AddRange(def.Chassis.OrderBy(chassis => chassis.Epoch).Select(chassis => chassis.Index));
            }
        }

        return [.. units];
    }

    private bool Trains(int structureType, int unit)
    {
        foreach (var trained in TrainedAt(structureType))
        {
            if (trained == unit)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsProducerOf(int player, EntityId structure, out int index)
    {
        index = Producers.IndexOf(structure);
        return player >= 0
            && player < _playerCount
            && index >= 0
            && _entities.IsAlive(structure)
            && _structures.Contains(structure)
            && _bodies.TryGet(structure, out var body)
            && body.Owner == player;
    }

    private ChassisBlueprint Blueprint(int player, int chassis) => _tech.Tech(player).Blueprints[chassis];

    private void Report(SimEventKind kind, int player, EntityId structure, in TrainOrder order) =>
        _events.Add(
            kind,
            player,
            structure,
            position: _bodies.TryGet(structure, out var body) ? body.Position : default,
            amount: TrainEvent.Amount(order.Unit, order.Count));
}
