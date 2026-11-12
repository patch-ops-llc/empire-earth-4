using Sim.Core;
using Sim.Numerics;

namespace Sim.Production;

/// <summary>
/// What makes a finished structure one that trains (design 5.4, "Production"): its orders, how
/// far the first of them is, and its rally point. It is on the structure's own entity, from the
/// moment the structure is finished.
/// </summary>
/// <remarks>
/// The orders stand in the order they were placed, the first at position 0, and the structure
/// works on the first alone. The positions from <see cref="Count"/> on are empty and hold nothing,
/// so that two structures with the same orders have the same state.
/// </remarks>
public struct Producer : IStateValue
{
    public const string StoreName = "producers";

    /// <summary>The most orders a structure holds at once (design 5.4).</summary>
    public const int MaxOrders = 5;

    private OrderSlots _orders;

    /// <summary>The number of orders, from 0 to <see cref="MaxOrders"/>.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// The build time of the first order, set in the tick its production begins (design 2.3), or
    /// zero while it has not begun or there is no order.
    /// </summary>
    public int BuildTicks { get; set; }

    /// <summary>The ticks the first order has been in production, from 0 to <see cref="BuildTicks"/>.</summary>
    public int Progress { get; set; }

    /// <summary>
    /// Whether the player has set the rally point. Until then it is the structure's own center,
    /// and new units stay where they appear.
    /// </summary>
    public bool RallySet { get; set; }

    /// <summary>The rally point, once it is set.</summary>
    public FixedVector2 RallyPoint { get; set; }

    /// <summary>The order at a position, from 0 to <see cref="Count"/> - 1.</summary>
    public readonly TrainOrder this[int position]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Count);
            return _orders[position];
        }
    }

    /// <summary>The supply held for every order of the queue together.</summary>
    public readonly int ReservedSupply
    {
        get
        {
            var supply = 0;
            for (var position = 0; position < Count; position++)
            {
                supply = checked(supply + _orders[position].Supply);
            }

            return supply;
        }
    }

    /// <summary>Puts an order at the end. There must be room for it.</summary>
    internal void Add(in TrainOrder order)
    {
        if (Count >= MaxOrders)
        {
            throw new InvalidOperationException($"A structure holds at most {MaxOrders} orders.");
        }

        _orders[Count++] = order;
    }

    /// <summary>
    /// Takes the order at a position out and moves the later ones up. Taking the first ends its
    /// production, so that the next begins anew.
    /// </summary>
    internal TrainOrder RemoveAt(int position)
    {
        var order = this[position];
        for (var next = position + 1; next < Count; next++)
        {
            _orders[next - 1] = _orders[next];
        }

        _orders[--Count] = default;
        if (position == 0)
        {
            BuildTicks = 0;
            Progress = 0;
        }

        return order;
    }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        Count = walker.Field(Count);
        if (Count is < 0 or > MaxOrders)
        {
            throw new InvalidDataException($"A structure has {Count} orders, and holds at most {MaxOrders}.");
        }

        for (var position = 0; position < MaxOrders; position++)
        {
            var order = _orders[position];
            order.Walk(walker);
            _orders[position] = order;
        }

        BuildTicks = walker.Field(BuildTicks);
        Progress = walker.Field(Progress);
        RallySet = walker.Field(RallySet);
        RallyPoint = new FixedVector2(walker.Field(RallyPoint.X), walker.Field(RallyPoint.Y));
    }

    /// <summary>
    /// The five positions, as five fields, so that the queue is part of the component's own value
    /// and two components compare field by field.
    /// </summary>
    private struct OrderSlots
    {
        private TrainOrder _first;
        private TrainOrder _second;
        private TrainOrder _third;
        private TrainOrder _fourth;
        private TrainOrder _fifth;

        public TrainOrder this[int position]
        {
            readonly get => position switch
            {
                0 => _first,
                1 => _second,
                2 => _third,
                3 => _fourth,
                4 => _fifth,
                _ => throw new ArgumentOutOfRangeException(nameof(position)),
            };

            set
            {
                switch (position)
                {
                    case 0:
                        _first = value;
                        break;
                    case 1:
                        _second = value;
                        break;
                    case 2:
                        _third = value;
                        break;
                    case 3:
                        _fourth = value;
                        break;
                    case 4:
                        _fifth = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(position));
                }
            }
        }
    }
}
