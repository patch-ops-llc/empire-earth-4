using Sim.Core;
using Sim.Data;
using Sim.Setup;

namespace Ai.Tests;

/// <summary>
/// A match in which computer players play, run as a host runs one: after every tick each of
/// them thinks, and its commands are scheduled the command delay ahead.
/// </summary>
internal sealed class AiMatch
{
    private readonly List<AiPlayer> _players = [];
    private readonly List<Command> _commands = [];
    private readonly int _commandDelay;

    public AiMatch(GameData data, string map, int playerCount, int commandDelay = 1, ulong seed = 1)
    {
        var mapDef = data.Maps.Single(candidate => candidate.Id == map);
        var players = Enumerable.Range(0, playerCount).Select(start => new PlayerSetup(start)).ToList();
        Match = MatchSetup.Create(data, mapDef, seed, players);
        _commandDelay = commandDelay;
    }

    public Match Match { get; }

    /// <summary>Every command the computer players gave, with the tick it was scheduled for.</summary>
    public List<(int Tick, Command Command)> Given { get; } = [];

    public AiPlayer Add(int player, string personality)
    {
        var ai = new AiPlayer(Match, player, Match.Data.PersonalityOf(personality)!);
        _players.Add(ai);
        return ai;
    }

    public void Tick()
    {
        Match.Simulation.Tick();
        foreach (var ai in _players)
        {
            _commands.Clear();
            ai.Think(_commands);
            foreach (var command in _commands)
            {
                var tick = Match.World.Tick + _commandDelay - 1;
                Match.Simulation.Commands.Enqueue(tick, command);
                Given.Add((tick, command));
            }
        }
    }

    /// <summary>Runs until the condition holds, and says whether it did within the ticks given.</summary>
    public bool RunUntil(Func<Match, bool> condition, int mostTicks)
    {
        while (Match.World.Tick < mostTicks)
        {
            if (condition(Match))
            {
                return true;
            }

            Tick();
        }

        return condition(Match);
    }
}
