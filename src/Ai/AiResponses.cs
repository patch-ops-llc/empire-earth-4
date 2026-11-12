using Sim.Data;

namespace Ai;

/// <summary>
/// The response table (design 5.5, item 4): the rows of its personality's file, judged every
/// round by what it remembers of the enemy, and what the rows in force ask of its research, its
/// army and where it fights.
/// </summary>
/// <remarks>
/// <para>
/// <b>When a row is in force.</b> A row comes into force when what it sees has held for the
/// reaction delay. It goes out of force when what it sees has not held for the time between two
/// scouts: what it knows is as old as its last look, so it does not drop a response, and march
/// its army back, in the round a count dips.
/// </para>
/// <para>
/// <b>Research.</b> A row names a track and until when. "So many tiers" are counted from the
/// tier it had when the row came into force.
/// </para>
/// </remarks>
internal sealed class AiResponses
{
    private readonly AiSight _sight;
    private readonly AiIntel _intel;
    private readonly IReadOnlyList<AiResponseDef> _rows;
    private readonly int _reactionTicks;
    private readonly int _lingerTicks;
    private readonly int _mainLine;
    private readonly AiReaction[] _reactions;
    private readonly bool[] _inForce;
    private readonly int[] _lastHeld;
    private readonly int[] _targetTier;
    private readonly List<AiSupplyShare> _armySupply = [];
    private readonly List<int> _researchFirst = [];

    public AiResponses(AiSight sight, AiIntel intel, AiPersonalityDef personality)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(intel);
        ArgumentNullException.ThrowIfNull(personality);
        _sight = sight;
        _intel = intel;
        _rows = personality.Scouting.Responses;
        _reactionTicks = personality.Fight.ReactionTicks;
        _lingerTicks = personality.Scouting.EveryTicks;
        _mainLine = MainLineOf(personality);
        _reactions = new AiReaction[_rows.Count];
        _inForce = new bool[_rows.Count];
        _lastHeld = new int[_rows.Count];
        _targetTier = new int[_rows.Count];
    }

    /// <summary>The lines the rows in force raise to a share of its army's supply, in the order of the file. A line is listed once, by the first row that names it.</summary>
    public IReadOnlyList<AiSupplyShare> ArmySupply => _armySupply;

    /// <summary>The tracks the rows in force research before any other, in the order of the file, each until its row says it is enough.</summary>
    public IReadOnlyList<int> ResearchFirst => _researchFirst;

    /// <summary>The kind of map marker it holds at and goes through, when a row in force names one.</summary>
    public MarkerKind? Passage { get; private set; }

    /// <summary>True when a row in force has it start no other research while it cannot pay for a catch-up tier.</summary>
    public bool CatchUpFirst { get; private set; }

    /// <summary>
    /// The main line of a personality's army: the one with the largest share, and of those that
    /// are level the first of the file. An index into the roster, or -1 for an army of no line.
    /// </summary>
    public static int MainLineOf(AiPersonalityDef personality)
    {
        ArgumentNullException.ThrowIfNull(personality);
        var main = new AiLineShare(-1, 0);
        foreach (var line in personality.Lines)
        {
            if (line.Share > main.Share)
            {
                main = line;
            }
        }

        return main.Line;
    }

    public bool IsInForce(int row) => _inForce[row];

    /// <summary>Judges every row by what it knows now. Call it once a round, after its memory was brought up to date.</summary>
    public void Update()
    {
        var tick = _sight.Tick;
        _armySupply.Clear();
        _researchFirst.Clear();
        Passage = null;
        CatchUpFirst = false;
        for (var row = 0; row < _rows.Count; row++)
        {
            var does = _rows[row].Does;
            var holds = Sees(_rows[row].Sees);
            if (holds)
            {
                _lastHeld[row] = tick;
            }

            if (_reactions[row].Holds(holds, tick, _reactionTicks) && !_inForce[row])
            {
                _inForce[row] = true;
                if (does.Research is { Until: AiResearchUntil.Tiers } counted)
                {
                    _targetTier[row] = _sight.Tech.Tiers[counted.Track] + counted.Amount;
                }
            }
            else if (_inForce[row] && !holds && tick - _lastHeld[row] >= _lingerTicks)
            {
                _inForce[row] = false;
            }

            if (!_inForce[row])
            {
                continue;
            }

            foreach (var share in does.ArmySupply)
            {
                if (!_armySupply.Exists(listed => listed.Line == share.Line))
                {
                    _armySupply.Add(share);
                }
            }

            if (does.Research is { } research && !IsEnough(row, research) && !_researchFirst.Contains(research.Track))
            {
                _researchFirst.Add(research.Track);
            }

            Passage ??= does.Passage;
            CatchUpFirst |= does.CatchUpFirst;
        }
    }

    private bool Sees(IReadOnlyList<AiSeesDef> anyOf)
    {
        foreach (var sees in anyOf)
        {
            if (Sees(sees))
            {
                return true;
            }
        }

        return false;
    }

    private bool Sees(AiSeesDef sees)
    {
        if (sees.Accents.Count > 0)
        {
            var found = false;
            foreach (var accent in sees.Accents)
            {
                found |= _intel.HasAccent(accent.Track, accent.Level, sees.OnMainLine);
            }

            if (!found)
            {
                return false;
            }
        }

        if (sees.Units > 0 && _intel.UnitsOfLine(sees.Line) < sees.Units)
        {
            return false;
        }

        if (sees.ArmyCountRatio is not null && !sees.IsOutnumbered(_intel.EnemyUnits, _sight.Fighters.Count))
        {
            return false;
        }

        return sees.EpochsAhead <= 0 || _intel.EnemyEpoch >= _sight.Tech.Epoch + sees.EpochsAhead;
    }

    private bool IsEnough(int row, AiResearchResponse research)
    {
        var tier = _sight.Tech.Tiers[research.Track];
        switch (research.Until)
        {
            case AiResearchUntil.Tiers:
                return tier >= _targetTier[row];
            case AiResearchUntil.AccentLevel:
                // With no chassis of its main line there is nothing for the accent to be on.
                return AiArmy.NewestUnlocked(_sight.Tech, _mainLine) is not { } newest || newest.AccentLevel(research.Track) >= research.Amount;
            default:
                return tier >= _intel.EnemyTier(research.Amount);
        }
    }
}
