using Sim.Core;

namespace Ai;

/// <summary>
/// How long its orders take, and so how often it may think. An order is not carried out in the
/// tick it is given: the session schedules it some ticks ahead, and from a machine that is not
/// the host it has to cross the network first. A round that reads the match before the orders
/// of the round before have been carried out gives them again, and the second of each is refused.
/// </summary>
/// <remarks>
/// Nobody tells it the delay, so it measures: when a round sends workers to sites, it watches
/// those workers, and the tick in which the last of them has its site is the tick the round's
/// orders were carried out, give or take: the orders of one round cross the network one by one
/// and need not all be scheduled for the same tick. Until <see cref="Margin"/> ticks after it
/// does not think again. On the host, where an order takes at most a few ticks, that is always
/// before <see cref="AiPlayer.ThinkInterval"/> has passed and nothing changes. It measures only
/// while it is called after every tick, as <see cref="AiPlayer.Think"/> asks: called less often,
/// it cannot tell the tick.
/// </remarks>
internal sealed class AiPace
{
    /// <summary>
    /// The ticks it allows on top of the time it has measured, for the orders of a round that
    /// are scheduled later than the one it saw carried out.
    /// </summary>
    public const int Margin = 2;

    /// <summary>
    /// The longest it waits for the workers it watches: twice the longest command delay the
    /// network protocol allows. An order can come to nothing, and it must not wait for ever.
    /// </summary>
    public const int LongestWait = 40;

    private readonly AiSight _sight;
    private readonly List<(EntityId Worker, EntityId Site)> _watched = [];
    private int _givenTick;
    private int _watchedTick;
    private int _readyTick;
    private bool _watchedEveryTick;

    public AiPace(AiSight sight)
    {
        ArgumentNullException.ThrowIfNull(sight);
        _sight = sight;
    }

    /// <summary>
    /// The longest it has seen the orders of a round take, from the round that gave them to the
    /// tick the effect of the last could be read. Zero before the first.
    /// </summary>
    public int OrderTicks { get; private set; }

    /// <summary>The ticks between two rounds: <see cref="AiPlayer.ThinkInterval"/>, or longer when its orders take longer than that.</summary>
    public int Interval => Math.Max(AiPlayer.ThinkInterval, OrderTicks + Margin);

    /// <summary>True while orders it has given have not all been carried out, as far as it can tell.</summary>
    public bool IsWaiting => _watched.Count > 0 || _sight.Tick < _readyTick;

    /// <summary>Looks at the workers it watches. Called after every tick.</summary>
    public void Watch()
    {
        if (_watched.Count == 0)
        {
            return;
        }

        // It can only say in which tick a worker got its site when it has looked after every one.
        _watchedEveryTick &= _sight.Tick <= _watchedTick + 1;
        _watchedTick = _sight.Tick;

        var waited = _sight.Tick - _givenTick;
        if (waited >= LongestWait)
        {
            _watched.Clear();
            return;
        }

        // A worker that has died is not waited for.
        var seen = _watched.RemoveAll(entry => _sight.HasBeenSentTo(entry.Worker, entry.Site));
        _watched.RemoveAll(entry => !_sight.IsOwnWorker(entry.Worker));
        if (_watched.Count == 0 && seen > 0 && _watchedEveryTick)
        {
            OrderTicks = Math.Max(OrderTicks, waited);
            _readyTick = _sight.Tick + Margin;
        }
    }

    /// <summary>Takes the workers the round sent to a site they are not at as the ones to watch.</summary>
    /// <param name="orders">The orders of the round.</param>
    public void Note(List<Command> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        if (_watched.Count > 0)
        {
            return;
        }

        foreach (var order in orders)
        {
            if (order.Type == CommandType.Gather
                && order.Entities.Length > 0
                && !_sight.HasBeenSentTo(order.Entities[0], order.TargetEntity))
            {
                _watched.Add((order.Entities[0], order.TargetEntity));
            }
        }

        _givenTick = _sight.Tick;
        _watchedTick = _sight.Tick;
        _watchedEveryTick = true;
    }
}
