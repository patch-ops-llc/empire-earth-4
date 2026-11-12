using Sim.Research;

namespace Game;

/// <summary>
/// The smoke test's check that the research panel's orders are answered with a sound: two
/// seconds of play in which the local player starts the first research the match allows and
/// cancels it, which gives the whole price back. Each of the two is an order the panel sends,
/// and each is answered.
/// </summary>
/// <remarks>
/// A research costs Energy, and a match starts with enough for one. So this part runs before
/// the smoke test's own research spends it, and not with the other checks of the sound at the
/// end of the script, where a worker at a Generator would need minutes to earn it again.
/// </remarks>
public sealed class ResearchSoundSmoke : PanelSoundSmoke
{
    private int _track = -1;
    private bool _ordered;

    public override string Name => "Sound of the research panel";

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        var research = orders.Match.Research;
        var player = orders.Player;
        switch (tick)
        {
            case 0:
                Start(orders);
                break;

            case TakeBackAt when _ordered:
                if (research.State.SlotResearching(player, _track) < 0)
                {
                    orders.Fail($"the research of track {_track} is not in a slot {TakeBackAt} ticks after the research panel ordered it.");
                    _ordered = false;
                    break;
                }

                _ordered = Panel(orders) is { } panel
                    && Answered(orders, "cancelled a research in the research panel", () => panel.Cancel(_track));
                break;

            case GoneAt when _ordered:
                if (research.State.SlotResearching(player, _track) >= 0)
                {
                    orders.Fail($"the research of track {_track} is still in its slot {GoneAt - TakeBackAt} ticks after the research panel cancelled it.");
                }

                break;
        }
    }

    private void Start(SmokeOrders orders)
    {
        var research = orders.Match.Research;
        if (Panel(orders) is not { } panel)
        {
            return;
        }

        for (var track = 0; track < research.State.TrackCount && _track < 0; track++)
        {
            _track = research.CheckStart(orders.Player, track) == ResearchStartCheck.Allowed ? track : -1;
        }

        if (_track < 0)
        {
            var match = orders.Match;
            var checks = string.Join(", ", Enumerable.Range(0, research.State.TrackCount).Select(track => $"{match.Data.Tracks[track].Name} {research.CheckStart(orders.Player, track)}"));
            orders.Fail(
                $"the local player can start no research now, with {match.Stockpiles.Materials(orders.Player) / 1000} Materials and " +
                $"{match.Stockpiles.Energy(orders.Player) / 1000} Energy. What stands in the way of each track: {checks}. " +
                "If a part before this one now spends what a match starts with, this part must run before it.");
            return;
        }

        var name = orders.Match.Data.Tracks[_track].Name;
        _ordered = Answered(orders, $"started a research of {name} in the research panel", () => panel.Start(_track));
    }

    private static ResearchPart? Panel(SmokeOrders orders)
    {
        if (orders.View.Interface.Parts.OfType<ResearchPart>().FirstOrDefault() is { } panel)
        {
            return panel;
        }

        orders.Fail("the view has no research panel.");
        return null;
    }
}
