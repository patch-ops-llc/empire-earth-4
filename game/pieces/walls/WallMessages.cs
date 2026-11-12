using Sim.Construction;
using Sim.Core;

namespace Game;

/// <summary>
/// Tells the local player what became of a wall order that did not go as it was drawn: one the
/// simulation refused when it was given, tiles their worker could not lay when it came to them,
/// and an order that ended because its worker died. An order the player cancelled or replaced
/// with another gets no message.
/// </summary>
/// <remarks>
/// <para>
/// The wall tool sends no order it knows to be refused, so a refusal is seen when the match
/// changed between the release and the tick the order ran in. Tiles that are left out are
/// reported one by one by the simulation. Those of one tick with one reason are one message,
/// and each is marked on the ground where it was to stand (<see cref="WallSkipMarks"/>).
/// </para>
/// <para>
/// The simulation reports a refused wall order and a wall order that ended with the events of
/// a build order. This part knows its own: a refusal by <see cref="GivenWallOrders"/>, and an
/// ending by the workers that were laying a wall after the tick before. It tells the build
/// menu's messages so, which then say nothing about them.
/// </para>
/// </remarks>
public partial class WallMessages : InterfacePart
{
    private readonly GivenWallOrders _given;
    private readonly WallSkipMarks? _marks;
    private readonly HashSet<EntityId> _laying = [];
    private readonly Dictionary<BuildProblem, int> _skipped = [];

    /// <summary>Makes the messages.</summary>
    /// <param name="given">The wall orders the player has given, as the wall tool notes them.</param>
    /// <param name="marks">The layer that marks a tile that was left out, or null.</param>
    public WallMessages(GivenWallOrders given, WallSkipMarks? marks = null)
    {
        _given = given;
        _marks = marks;
    }

    /// <summary>The number of messages given so far.</summary>
    public int Count { get; private set; }

    /// <summary>The last message given, or null before the first.</summary>
    public string? Last { get; private set; }

    /// <summary>True when an event is about a wall order of the local player's and not about a build order.</summary>
    public bool IsOfWallOrder(SimEvent happened) => happened.Kind switch
    {
        SimEventKind.BuildRefused => _given.IsRefusalOfOne(in happened),
        SimEventKind.BuildOrderEnded => _laying.Contains(happened.Entity),
        _ => false,
    };

    protected override void Attach()
    {
    }

    public override void AfterTick()
    {
        var match = View.Match;
        _skipped.Clear();
        foreach (var happened in match.Simulation.Events)
        {
            if (happened.Player != View.Player)
            {
                continue;
            }

            var why = (BuildProblem)happened.Amount;
            switch (happened.Kind)
            {
                case SimEventKind.BuildRefused when IsOfWallOrder(happened):
                    Say(WallWords.Refused(match, why));
                    break;

                case SimEventKind.BuildOrderEnded when IsOfWallOrder(happened):
                    if (WallWords.Ended(why) is { } message)
                    {
                        Say(message);
                    }

                    break;

                case SimEventKind.WallTileSkipped:
                    _skipped[why] = _skipped.GetValueOrDefault(why) + 1;
                    _marks?.ShowAt(happened.Position);
                    break;

                default:
                    break;
            }
        }

        foreach (var (why, tiles) in _skipped)
        {
            Say(WallWords.Skipped(tiles, why));
        }

        // Who is laying a wall now, for the events of the next tick: by then the tiles of an
        // order that ended are gone.
        _laying.Clear();
        var plans = match.WallPlans;
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans.ValueAt(i);
            if (plan.Owner == View.Player)
            {
                _laying.Add(plan.Worker);
            }
        }
    }

    private void Say(string message)
    {
        Count++;
        Last = message;
        Frame.Say(message);
    }
}
