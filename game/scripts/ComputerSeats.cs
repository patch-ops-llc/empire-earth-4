using Ai;
using Net;
using Sim.Core;
using Sim.Data;
using Sim.Setup;

namespace Game;

/// <summary>
/// The seats of a match that the computer plays, on the machine that hosts it (design 5.5): one
/// <see cref="AiPlayer"/> for each, with the personality the seat's setting names.
/// </summary>
/// <remarks>
/// The computer player reads the host's own match after a tick and gives its orders as
/// commands, which go where a person's go: to the host's side of the session, which schedules
/// them and sends them to every machine. No other machine runs it or knows its personality, and
/// the replay holds its commands like anyone's.
/// </remarks>
public sealed class ComputerSeats
{
    private readonly Match _match;
    private readonly HostSession _host;
    private readonly List<(AiPlayer Player, AiPersonalityDef Personality)> _players = [];
    private readonly List<Command> _commands = [];

    /// <param name="match">The match as the host runs it.</param>
    /// <param name="host">The host's side of the session, which takes the orders.</param>
    /// <param name="seats">The seats as the settings of the match have them, in the order of the players.</param>
    /// <exception cref="ArgumentException">A seat names a personality the match's data does not have.</exception>
    public ComputerSeats(Match match, HostSession host, IReadOnlyList<SeatSetting> seats)
    {
        _match = match;
        _host = host;
        for (var seat = 0; seat < seats.Count; seat++)
        {
            if (seats[seat].Kind != SeatKind.Computer)
            {
                continue;
            }

            var personality = match.Data.PersonalityOf(seats[seat].Personality ?? string.Empty)
                ?? throw new ArgumentException($"Seat {seat + 1} is set to the computer with the personality '{seats[seat].Personality}', which the data does not have.", nameof(seats));
            _players.Add((new AiPlayer(match, seat, personality), personality));
        }
    }

    /// <summary>The number of seats the computer plays.</summary>
    public int Count => _players.Count;

    /// <summary>The number of orders the computer has given so far, over all its seats.</summary>
    public int OrdersGiven { get; private set; }

    /// <summary>The personality of the computer in a seat, or null when the computer does not play it.</summary>
    public AiPersonalityDef? PersonalityOf(int seat)
    {
        foreach (var (player, personality) in _players)
        {
            if (player.Player == seat)
            {
                return personality;
            }
        }

        return null;
    }

    /// <summary>
    /// Lets every computer player think and hands its orders to the host. Call it after a tick
    /// the host's own match has run, never during one. A player thinks once in every
    /// <see cref="AiPlayer.ThinkInterval"/> ticks and does nothing in between, and one who is out
    /// of the match is not asked.
    /// </summary>
    public void Think()
    {
        if (_host.Phase != SessionPhase.Running)
        {
            return;
        }

        foreach (var (player, _) in _players)
        {
            if (!_match.Standings.IsPlaying(player.Player))
            {
                continue;
            }

            _commands.Clear();
            player.Think(_commands);

            // In the order of the list, which the host keeps: a round's gather orders come
            // before its build orders, and the simulation runs them in that order.
            foreach (var command in _commands)
            {
                if (_host.Submit(command))
                {
                    OrdersGiven++;
                }
            }
        }
    }
}
