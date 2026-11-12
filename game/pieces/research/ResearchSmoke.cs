using Sim.Core;
using Sim.Research;

namespace Game;

/// <summary>
/// The smoke test's research: 50 seconds of play in which the local player starts a research,
/// cancels it, and starts another that runs until its track is a tier higher.
/// </summary>
public sealed class ResearchSmoke : SmokePart
{
    private const int CancelAt = 40;
    private const int StartAgainAt = 80;

    // The second research is a first tier above the start, which takes 900 ticks in the data.
    // The rest is room for the order to arrive.
    private const int Length = 1000;

    private int _first = -1;
    private int _second = -1;
    private int _tierBefore;

    public override string Name => "Research";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        var research = orders.Match.Research;
        var player = orders.Player;
        switch (tick)
        {
            case 0:
                _first = Allowed(research, player, fromTheEnd: false);
                Start(orders, _first);
                break;

            case CancelAt:
                if (research.State.SlotResearching(player, _first) < 0)
                {
                    orders.Fail($"the research of track {_first} that was ordered {CancelAt} ticks ago is not in a slot.");
                    break;
                }

                orders.Submit(new Command(CommandType.CancelResearch, player, contentId: _first));
                break;

            case StartAgainAt:
                if (research.State.SlotResearching(player, _first) >= 0)
                {
                    orders.Fail($"the research of track {_first} is still in its slot {StartAgainAt - CancelAt} ticks after it was cancelled.");
                    break;
                }

                _second = Allowed(research, player, fromTheEnd: true);
                _tierBefore = _second < 0 ? 0 : research.State.Tier(player, _second);
                Start(orders, _second);
                break;

            case Length - 1:
                if (research.State.Tier(player, _second) != _tierBefore + 1)
                {
                    orders.Fail(
                        $"track {_second} is at tier {research.State.Tier(player, _second)} and not at {_tierBefore + 1}, {Length - 1 - StartAgainAt} ticks after its research was ordered. " +
                        "If a research takes longer in the data now, this part needs more ticks.");
                }

                break;
        }
    }

    // The first or the last track whose next tier the player may start now, or -1.
    private static int Allowed(MatchResearch research, int player, bool fromTheEnd)
    {
        var tracks = research.State.TrackCount;
        for (var i = 0; i < tracks; i++)
        {
            var track = fromTheEnd ? tracks - 1 - i : i;
            if (research.CheckStart(player, track) == ResearchStartCheck.Allowed)
            {
                return track;
            }
        }

        return -1;
    }

    private static void Start(SmokeOrders orders, int track)
    {
        if (track < 0)
        {
            orders.Fail("the player can start no research: no track's next tier is allowed and paid for.");
            return;
        }

        orders.Submit(new Command(CommandType.StartResearch, orders.Player, contentId: track));
    }
}
