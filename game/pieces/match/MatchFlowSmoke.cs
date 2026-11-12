using Sim.Outcome;

namespace Game;

/// <summary>
/// The smoke test's match flow: the match is paused, let go on and run at another speed for no
/// time at all, and the part checks that the local player's side of the session and the view
/// follow. The end screen is asked what it says of standings the part makes up, because the
/// smoke test's match must go on to its end with every player in it.
/// </summary>
/// <remarks>
/// It gives no order and changes nothing in the match. That a pause holds the ticks back, and
/// that conceding and conquest take a player out, is tested with the network and the
/// simulation. Here it is the path from the host's controls to what the player sees.
/// </remarks>
public sealed class MatchFlowSmoke : SmokePart
{
    private const int Length = 20;

    public override string Name => "Match flow";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (tick != 0)
        {
            return;
        }

        var view = orders.View;
        var pace = view.Interface.Parts.OfType<PaceControl>().FirstOrDefault();
        var screen = view.Interface.Parts.OfType<EndScreen>().FirstOrDefault();
        if (pace is null || screen is null)
        {
            orders.Fail("the view has no pace control or no end screen.");
            return;
        }

        CheckPace(orders, pace);
        CheckScreen(orders, screen);
        CheckWords(orders);
    }

    // The session is given no time while the pace changes, so that the smoke test's ticks stay
    // what they are: the host tells the client, and the view reads the client.
    private static void CheckPace(SmokeOrders orders, PaceControl pace)
    {
        var run = orders.View.Run;
        var client = orders.View.Client;
        var tick = orders.Match.World.Tick;
        if (!run.CanSetPace)
        {
            orders.Fail("a match alone cannot set its own pace.");
            return;
        }

        pace.TogglePause();
        run.Advance(TimeSpan.Zero);
        if (!client.IsPaused || !pace.ShowsPaused)
        {
            orders.Fail($"the match was paused, and the session says paused={client.IsPaused} and the banner shows={pace.ShowsPaused}.");
        }

        // Half a second passes, and no tick runs in it.
        var ran = run.Advance(TimeSpan.FromSeconds(0.5));
        if (ran != 0 || orders.Match.World.Tick != tick)
        {
            orders.Fail($"{ran} ticks ran while the match was paused.");
        }

        pace.TogglePause();
        run.Advance(TimeSpan.Zero);
        if (client.IsPaused || pace.ShowsPaused)
        {
            orders.Fail("the match was let go on, and it is still shown as paused.");
        }

        pace.SetSpeed(2.0);
        run.Advance(TimeSpan.Zero);
        if (client.TickInterval != MatchSpeed.IntervalOf(2.0) || pace.SpeedText != "double speed")
        {
            orders.Fail($"the match was set to double speed, and a tick takes {client.TickInterval.TotalMilliseconds} ms and the view says '{pace.SpeedText}'.");
        }

        pace.SetSpeed(1.0);
        run.Advance(TimeSpan.Zero);
        if (client.TickInterval != MatchSpeed.Normal || pace.SpeedText != "normal speed")
        {
            orders.Fail($"the match was set back to normal speed, and a tick takes {client.TickInterval.TotalMilliseconds} ms.");
        }

        if (orders.Match.World.Tick != tick)
        {
            orders.Fail("a tick ran while the pace was changed in no time.");
        }
    }

    // Every player of the smoke test's match is in it, so the screen has no result to show, and
    // asked to open it asks whether to leave.
    private static void CheckScreen(SmokeOrders orders, EndScreen screen)
    {
        if (screen.IsOpen)
        {
            orders.Fail("the end screen is up in a match nobody has left.");
        }

        screen.Open();
        var reading = screen.Reading;
        if (!screen.IsOpen || reading.Headline is not null || reading.Players.Count != orders.Match.Players.Count)
        {
            orders.Fail($"the end screen, opened in an undecided match, says '{reading.Headline}' of {reading.Players.Count} players.");
        }

        screen.Close();
        if (screen.IsOpen)
        {
            orders.Fail("the end screen did not close.");
        }
    }

    // What the screen says of every way a match of two ends, for each of its players.
    private static void CheckWords(SmokeOrders orders)
    {
        var conquered = new Standings(2);
        conquered.TakeOut(1, PlayerStanding.Eliminated, 2399);
        Expect(orders, conquered, 0, OutcomeWords.Victory, "Player 2 has no Capitol and no Outpost left.");
        Expect(orders, conquered, 1, OutcomeWords.Defeat, "You have no Capitol and no Outpost left. Player 1 has won.");
        if (EndReading.Of(conquered, 0, 5000).Length != "The match lasted 2:00.")
        {
            orders.Fail($"a match decided in tick 2399 is said to have lasted otherwise: '{EndReading.Of(conquered, 0, 5000).Length}'.");
        }

        var conceded = new Standings(2);
        conceded.TakeOut(0, PlayerStanding.Conceded, 600);
        Expect(orders, conceded, 0, OutcomeWords.Defeat, "You conceded. Player 2 has won.");
        Expect(orders, conceded, 1, OutcomeWords.Victory, "Player 1 conceded.");

        var nobody = new Standings(2);
        nobody.TakeOut(0, PlayerStanding.Eliminated, 900);
        nobody.TakeOut(1, PlayerStanding.Eliminated, 900);
        Expect(orders, nobody, 0, OutcomeWords.NoWinner, "The last players lost their last Capitol and Outpost at the same moment.");

        var three = new Standings(3);
        three.TakeOut(2, PlayerStanding.Conceded, 100);
        Expect(orders, three, 2, OutcomeWords.Defeat, "You conceded. The match goes on without you.");
        Expect(orders, three, 0, null, string.Empty);
    }

    private static void Expect(SmokeOrders orders, Standings standings, int player, string? headline, string why)
    {
        var reading = EndReading.Of(standings, player, standings.IsDecided ? standings.DecidedTick + 1 : 0);
        if (reading.Headline != headline || reading.Why != why)
        {
            orders.Fail($"the end screen says '{reading.Headline}: {reading.Why}' to player {player + 1}, and should say '{headline}: {why}'.");
        }
    }
}
