using Sim.Core;

namespace Ai;

/// <summary>
/// How long its orders take, and so how often it may think. An order is not carried out in the
/// tick it is given: the session schedules it some ticks ahead, and from a machine that is not
/// the host it has to cross the network first. A round that reads the match before the orders
/// of the round before have been carried out gives them again, and the second of each is refused.
/// </summary>
/// <remarks>
/// Nobody tells it the delay, so it measures: when a round sends a worker to a site, it watches
/// that worker, and the tick in which the worker has the site is the tick the round's orders
/// were carried out. Until then it does not think again. On the host, where an order takes at
/// most a few ticks, that is always before <see cref="AiPlayer.ThinkInterval"/> has passed and
/// nothing changes. It measures only while it is called after every tick, as
/// <see cref="AiPlayer.Think"/> asks: called less often, it cannot tell the tick.
/// </remarks>
internal sealed class AiPace
{
    /// <summary>The ticks it allows on top of the longest time it has measured, for orders that take longer than that one did.</summary>
    public const int Margin = 2;

    /// <summary>
    /// The longest it waits for the worker it watches: twice the longest command delay the
    /// network protocol allows. An order can come to nothing, and it must not wait for ever.
    /// </summary>
    public const int LongestWait = 40;

    private readonly AiSight _sight;
    private EntityId _worker = EntityId.None;
    private EntityId _site = EntityId.None;
    private int _givenTick;
    private int _watchedTick;
    private bool _watchedEveryTick;

    public AiPace(AiSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        _sight = sight;
    }

    /// <summary>The longest it has seen an order take, from the round that gave it to the tick its effect could be read. Zero before the first.</summary>
    public int OrderTicks { get; private set; }

    /// <summary>The ticks between two rounds: <see cref="AiPlayer.ThinkInterval"/>, or longer when its orders take longer than that.</summary>
    public int Interval => Math.Max(AiPlayer.ThinkInterval, OrderTicks + Margin);

    /// <summary>True while orders it has given have not been carried out, as far as it can tell.</summary>
    public bool IsWaiting => _worker != EntityId.None;

    /// <summary>Looks at the worker it watches. Called after every tick.</summary>
    public void Watch()
    {
        if (_worker == EntityId.None)
        {
            return;
        }

        // It can only say in which tick the worker got the site when it has looked after every one.
        _watchedEveryTick &= _sight.Tick <= _watchedTick + 1;
        _watchedTick = _sight.Tick;

        var waited = _sight.Tick - _givenTick;
        if (_sight.HasBeenSentTo(_worker, _site))
        {
            if (_watchedEveryTick)
            {
                OrderTicks = Math.Max(OrderTicks, waited);
            }

            _worker = EntityId.None;
        }
        else if (waited >= LongestWait || !_sight.IsOwnWorker(_worker))
        {
            _worker = EntityId.None;
        }
    }

    /// <summary>Takes the first worker the round sent to a site it is not at as the one to watch.</summary>
    /// <param name="orders">The orders of the round.</param>
    public void Note(List<Command> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if (_worker != EntityId.None)
        {
            return;
        }

        foreach (var order in orders)
        {
            if (order.Type == CommandType.Gather
                && order.Entities.Length > 0
                && !_sight.HasBeenSentTo(order.Entities[0], order.TargetEntity))
            {
                _worker = order.Entities[0];
                _site = order.TargetEntity;
                _givenTick = _sight.Tick;
                _watchedTick = _sight.Tick;
                _watchedEveryTick = true;
                return;
            }
        }
    }
}
