using Sim.Construction;
using Sim.Core;

namespace Game;

/// <summary>
/// Tells the local player what became of a build order that did not end in a foundation: one
/// the simulation refused when it was given, and one that ended on the way or on arrival, with
/// its price back. An order the player cancelled or replaced with another gets no message.
/// </summary>
/// <remarks>
/// The menu sends no order it knows to be refused, so a refusal is seen when the match changed
/// between the click and the tick the order ran in, as when the price went on a research first.
/// The event of an order that ended names the worker and the site but not the structure, so
/// this part remembers what each worker of the player's was sent to build.
/// </remarks>
public partial class BuildMessages : InterfacePart
{
    private readonly Dictionary<EntityId, int> _sentToBuild = [];

    /// <summary>The number of messages given so far.</summary>
    public int Count { get; private set; }

    protected override void Attach()
    {
    }

    public override void AfterTick()
    {
        var match = View.Match;
        foreach (var happened in match.Simulation.Events)
        {
            if (happened.Player != View.Player)
            {
                continue;
            }

            if (happened.Kind == SimEventKind.BuildRefused)
            {
                Say(BuildWords.Refused((BuildProblem)happened.Amount));
            }
            else if (happened.Kind == SimEventKind.BuildOrderEnded)
            {
                var name = _sentToBuild.TryGetValue(happened.Entity, out var type) && match.Data.StructureOf(type) is { } structure
                    ? StructureWords.NameOf(structure)
                    : "structure";
                if (BuildWords.Ended(name, (BuildProblem)happened.Amount) is { } message)
                {
                    Say(message);
                }
            }
        }

        _sentToBuild.Clear();
        var orders = match.BuildOrders;
        for (var i = 0; i < orders.Count; i++)
        {
            var worker = orders.EntityAt(i);
            if (match.Bodies.TryGet(worker, out var body) && body.Owner == View.Player)
            {
                _sentToBuild[worker] = orders.ValueAt(i).Type;
            }
        }
    }

    private void Say(string message)
    {
        Count++;
        Frame.Say(message);
    }
}
