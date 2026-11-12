using Sim.Core;
using Sim.Research;

namespace Tools.Replay.Scripts;

/// <summary>
/// Research orders (M14): mostly a research on some track, which the simulation starts when the
/// player has a free slot and the price and drops otherwise; sometimes a cancel, usually of the
/// research the player has under way; and research on tracks that do not exist and from players
/// who are not in the match. Research orders need no units.
/// </summary>
/// <remarks>
/// It has no clock of its own: it takes one turn in three of <see cref="SharedTurns"/>, which
/// says why.
/// </remarks>
[OrderScript("research")]
internal sealed class ResearchOrders : OrderScript
{
    private const int Kinds = 50;

    private readonly SharedTurns _turns;
    private readonly DeterministicRandom _random;
    private readonly int _firstKind;

    public ResearchOrders(ScriptSetup setup)
        : base(setup)
    {
        _turns = SharedTurns.Of(setup);
        _random = _turns.Random;
        _firstKind = _turns.Widen(Kinds);
    }

    public override void AddOrdersFor(int tick, List<Command> commands)
    {
        if (_turns.IsTurn(tick, out var player, out var kind) && kind >= _firstKind)
        {
            commands.Add(ResearchOrder(player, kind - _firstKind));
        }
    }

    private Command ResearchOrder(int player, int kind)
    {
        var tracks = Match.Data.Tracks.Count;
        if (kind < 30)
        {
            return new Command(CommandType.StartResearch, player, contentId: _random.NextInt(tracks));
        }

        if (kind < 42)
        {
            // The research under way three times in four, so that cancels are not nearly all dropped.
            var track = _random.NextInt(tracks);
            var slot = Match.Research.State.Slot(player, ResearchState.PlayerSlot);
            return new Command(CommandType.CancelResearch, player, contentId: slot.IsBusy && _random.NextInt(4) != 0 ? slot.Track : track);
        }

        if (kind < 47)
        {
            var noTrack = _random.NextInt(2) == 0 ? tracks + _random.NextInt(1000) : -1 - _random.NextInt(1000);
            return new Command(_random.NextInt(2) == 0 ? CommandType.StartResearch : CommandType.CancelResearch, player, contentId: noTrack);
        }

        // From nobody in the match, with fields that research does not read.
        var nobody = Match.Players.Count + _random.NextInt(5);
        var point = AnyPoint(_random);
        return new Command(CommandType.StartResearch, nobody, [new EntityId(1)], point.X, point.Y, new EntityId(2), _random.NextInt(tracks));
    }
}
