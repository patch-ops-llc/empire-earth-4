using Sim.Core;
using Sim.Data;
using Sim.Serialization;
using Sim.Setup;

namespace Ai.Tests;

/// <summary>
/// A match in which computer players play, run as a host runs one: after every tick each of
/// them thinks, and its commands are scheduled the command delay ahead. The match is recorded
/// as a replay while it runs, from nothing but those commands.
/// </summary>
internal sealed class AiMatch
{
    private readonly List<AiPlayer> _players = [];
    private readonly List<Command> _commands = [];
    private readonly List<Command> _scheduled = [];
    private readonly ByteWriter _replay = new();
    private readonly ReplayRecorder _recorder;
    private readonly int _commandDelay;

    public AiMatch(GameData data, string map, int playerCount, int commandDelay = 1, ulong seed = 1)
    {
        var players = Enumerable.Range(0, playerCount).Select(start => new PlayerSetup(start)).ToList();
        var description = new MatchDescription("ai tests", data.ContentHash, map, seed, players);
        Match = description.CreateMatch(data);
        _recorder = new ReplayRecorder(_replay, description, Match.World);
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
        _recorder.AfterTick();

        var tick = Match.World.Tick + _commandDelay - 1;
        _scheduled.Clear();
        foreach (var ai in _players)
        {
            _commands.Clear();
            ai.Think(_commands);
            foreach (var command in _commands)
            {
                Match.Simulation.Commands.Enqueue(tick, command);
                Given.Add((tick, command));
                _scheduled.Add(command);
            }
        }

        _recorder.RecordCommands(tick, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_scheduled));
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

    /// <summary>Ends the recording where the match stands, and gives the replay as a file would hold it.</summary>
    public byte[] FinishReplay()
    {
        _recorder.Finish();
        return _replay.ToArray();
    }

    /// <summary>The structures of a player that hold territory: its Capitol and its Outposts, finished or not.</summary>
    public int TerritoryStructuresOf(int player)
    {
        var count = 0;
        for (var i = 0; i < Match.Structures.Count; i++)
        {
            if (Match.Bodies.TryGet(Match.Structures.EntityAt(i), out var body)
                && body.Owner == player
                && Match.Data.StructureOf(Match.Structures.ValueAt(i).Type)?.TerritoryRadius is not null)
            {
                count++;
            }
        }

        return count;
    }
}
