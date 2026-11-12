namespace Sim.Data;

/// <summary>
/// How a personality scouts and what it does about what it has seen (design 5.5, item 4): how
/// often it sends a scout, and the rows of its response table, in the order of the file.
/// </summary>
/// <param name="EveryTicks">The ticks from one scout to the next, and from the start of the match to the first. Zero when it sends none.</param>
/// <param name="Responses">The rows of the table. Every row that holds is acted on; where two say something about the same thing, the earlier row goes first.</param>
public sealed record AiScoutingDef(int EveryTicks, IReadOnlyList<AiResponseDef> Responses)
{
    /// <summary>A personality with no <c>scouting</c> block: it sends no scout and has no row to act on.</summary>
    public static AiScoutingDef None { get; } = new(0, []);

    public bool SendsScouts => EveryTicks > 0;
}

/// <summary>One row of the response table: what it sees, and what it does while it sees it.</summary>
/// <param name="Sees">The row holds when any one of these does.</param>
/// <param name="Does">What it does while the row is in force.</param>
public sealed record AiResponseDef(IReadOnlyList<AiSeesDef> Sees, AiDoesDef Does);

/// <summary>An accent a unit shows as a badge: the track it belongs to, and its level, 1 or 2.</summary>
public readonly record struct AiAccent(int Track, int Level);

/// <summary>
/// One thing a row can see of the enemy, from the signals of design 3.2. Each part that is given
/// must hold. "The enemy's units" are the fighting units of other players that it has seen and
/// has not seen die.
/// </summary>
/// <param name="Accents">When not empty: one of the enemy's units carries one of these accents, at the level given or above it.</param>
/// <param name="OnMainLine">With <paramref name="Accents"/>: only units of the enemy's main line count, the line it has seen most units of.</param>
/// <param name="Line">With <paramref name="Units"/>: an index into <see cref="RuleSet.Lines"/>.</param>
/// <param name="Units">When above zero: the enemy has at least this many units of <paramref name="Line"/>.</param>
/// <param name="ArmyCountRatio">When given: the enemy's units are more than this many times its own.</param>
/// <param name="EpochsAhead">When above zero: the enemy's epoch, which is announced to everyone, is at least this far above its own.</param>
public sealed record AiSeesDef(
    IReadOnlyList<AiAccent> Accents,
    bool OnMainLine,
    int Line,
    int Units,
    DataNumber? ArmyCountRatio,
    int EpochsAhead)
{
    /// <summary>True when an army of <paramref name="enemy"/> units is more than the ratio times one of <paramref name="own"/>.</summary>
    public bool IsOutnumbered(long enemy, long own) =>
        ArmyCountRatio is { } ratio && enemy * AiNumbers.Pow10(ratio.Scale) > ratio.Unscaled * own;
}

/// <summary>What a row does while it is in force. Any part may be left out.</summary>
/// <param name="ArmySupply">Lines it raises to a share of its army's supply, whether its personality trains them or not.</param>
/// <param name="Research">A track it researches before the tracks its weights would choose, and until when.</param>
/// <param name="Passage">The kind of map marker it holds at and goes through, in place of its personality's.</param>
/// <param name="CatchUpFirst">True when it starts no other research while it cannot pay for a catch-up tier it is offered.</param>
public sealed record AiDoesDef(
    IReadOnlyList<AiSupplyShare> ArmySupply,
    AiResearchResponse? Research,
    MarkerKind? Passage,
    bool CatchUpFirst);

/// <summary>A line and the share of its army's supply that a row raises it to: above 0 and at most 1.</summary>
public readonly record struct AiSupplyShare(int Line, DataNumber Share)
{
    /// <summary>True when <paramref name="lineSupply"/> of an army's <paramref name="armySupply"/> is less than the share.</summary>
    public bool IsBelow(long lineSupply, long armySupply) =>
        lineSupply * AiNumbers.Pow10(Share.Scale) < Share.Unscaled * armySupply;
}

/// <summary>Until when a row researches its track.</summary>
public enum AiResearchUntil
{
    /// <summary>Until the track is so many tiers above where it was when the row came into force.</summary>
    Tiers,

    /// <summary>Until the newest chassis of its main line carries the track's accent at a level.</summary>
    AccentLevel,

    /// <summary>Until the track is at the tier it knows the enemy has in a track.</summary>
    EnemyTier,
}

/// <summary>A track a row researches first, and until when.</summary>
/// <param name="Track">An index into <see cref="GameData.Tracks"/>.</param>
/// <param name="Until">The kind of end.</param>
/// <param name="Amount">The tiers, the accent level, or the index of the enemy's track, by <paramref name="Until"/>.</param>
public readonly record struct AiResearchResponse(int Track, AiResearchUntil Until, int Amount);

internal static class AiNumbers
{
    public static long Pow10(int scale)
    {
        var value = 1L;
        for (var i = 0; i < scale; i++)
        {
            value *= 10;
        }

        return value;
    }
}
