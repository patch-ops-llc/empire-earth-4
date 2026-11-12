using System.Globalization;
using Net;
using Sim;
using Sim.Outcome;

namespace Game;

/// <summary>
/// How a match stands and how it ended, in words (design 5.1): the headline of the end screen,
/// the sentence under it, and each player's line. The end screen in a match and the page the
/// menu shows when a match is over both say it from here.
/// </summary>
public static class OutcomeWords
{
    public const string Victory = "Victory";
    public const string Defeat = "Defeat";
    public const string NoWinner = "No winner";

    /// <summary>
    /// The headline for one player: victory when it is the last in the match, defeat once it is
    /// out, and nothing while it is still playing an undecided match.
    /// </summary>
    public static string? Headline(Standings standings, int player)
    {
        if (standings.IsPlaying(player))
        {
            return standings.IsDecided && standings.Winner == player ? Victory : null;
        }

        return standings.IsDecided && standings.Winner == Standings.None ? NoWinner : Defeat;
    }

    /// <summary>
    /// The sentence under the headline: why the player won or lost. Empty while there is no
    /// headline.
    /// </summary>
    /// <param name="standings">The standings of the match.</param>
    /// <param name="player">The player the sentence is for.</param>
    /// <param name="left">Why a seat's player is gone from the session, or null when it is not or this is not known.</param>
    public static string Why(Standings standings, int player, Func<int, SessionEndReason?>? left = null)
    {
        switch (Headline(standings, player))
        {
            case Victory:
                var others = Enumerable.Range(0, standings.PlayerCount).Where(seat => seat != player).ToArray();
                return others.Length == 1 ? WentOut(others[0], standings.Of(others[0]), left?.Invoke(others[0])) : "Every other player is out of the match.";

            case NoWinner:
                return standings.PlayerCount == 1
                    ? OwnWayOut(standings.Of(player))
                    : "The last players lost their last Capitol and Outpost at the same moment.";

            case Defeat:
                var how = OwnWayOut(standings.Of(player));
                if (!standings.IsDecided)
                {
                    return $"{how} The match goes on without you.";
                }

                return $"{how} {MatchPanel.PlayerName(standings.Winner)} has won.";

            default:
                return string.Empty;
        }
    }

    /// <summary>How another player went out of the match, as a sentence.</summary>
    public static string WentOut(int seat, PlayerStanding how, SessionEndReason? left = null)
    {
        var name = MatchPanel.PlayerName(seat);
        return (how, left) switch
        {
            (PlayerStanding.Eliminated, _) => $"{name} has no Capitol and no Outpost left.",
            (PlayerStanding.Conceded, SessionEndReason.Left) => $"{name} left the match, and so conceded it.",
            (PlayerStanding.Conceded, SessionEndReason.ConnectionLost) => $"{name} lost the connection, and so conceded the match.",
            (PlayerStanding.Conceded, not null) => $"{name} was taken out of the match, and so conceded it.",
            (PlayerStanding.Conceded, null) => $"{name} conceded.",
            _ => $"{name} is in the match.",
        };
    }

    /// <summary>A player's line in the list of players: "playing", or how and when it went out.</summary>
    public static string Standing(Standings standings, int seat) => standings.Of(seat) switch
    {
        PlayerStanding.Eliminated => $"eliminated at {Clock(standings.OutTick(seat))}",
        PlayerStanding.Conceded => $"conceded at {Clock(standings.OutTick(seat))}",
        _ => standings.IsDecided ? "the winner" : "playing",
    };

    /// <summary>How long the match has run, or ran until it was decided, as a sentence.</summary>
    public static string Length(Standings standings, int tick) => standings.IsDecided
        ? $"The match lasted {Clock(standings.DecidedTick + 1)}."
        : $"Match time {Clock(tick)}.";

    /// <summary>A number of ticks as minutes and seconds of the match: 3:07.</summary>
    public static string Clock(int ticks)
    {
        var seconds = Math.Max(0, ticks) / SimInfo.TicksPerSecond;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    private static string OwnWayOut(PlayerStanding how) => how == PlayerStanding.Conceded
        ? "You conceded."
        : "You have no Capitol and no Outpost left.";
}
