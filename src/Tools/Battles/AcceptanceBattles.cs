namespace Tools.Battles;

/// <summary>The two armies of every acceptance battle: the swarm player's and the elite player's of design example B.</summary>
public enum Side
{
    Mass,
    Elite,
}

/// <summary>The ground a battle is fought on.</summary>
public enum ArenaKind
{
    /// <summary>Open ground on every side of both armies.</summary>
    OpenField,

    /// <summary>A corridor between cliffs, as wide as the blocks, that holds both armies.</summary>
    Chokepoint,
}

/// <summary>Units of one chassis in an army, by the chassis's ID in the data.</summary>
public sealed record ArmyPart(string Chassis, int Count);

/// <summary>
/// One army: the tiers of its player, its block, and what stands in one row behind the block.
/// </summary>
/// <param name="Tiers">One tier for each track, in the order of the data: Firepower, Protection, Mobility, Manufacturing, Logistics, Extraction.</param>
/// <param name="Block">The units of the block, placed rank by rank from the front.</param>
/// <param name="Behind">The units of the one row behind the block, or none.</param>
public sealed record ArmyDef(IReadOnlyList<int> Tiers, ArmyPart Block, ArmyPart? Behind = null)
{
    public IEnumerable<ArmyPart> Parts => Behind is null ? [Block] : [Block, Behind];
}

/// <summary>
/// What a battle is tuned toward (design 3.1): who wins, and how much of its army value the
/// winner keeps, in percent. A part that is null asks for nothing.
/// </summary>
public sealed record Target(Side? Winner, int? LeastKept, int? MostKept, string Text)
{
    /// <summary>
    /// True when one fight meets the target. A fight with no winner meets none: the design's
    /// targets all name a winner or say "either side may win".
    /// </summary>
    public bool IsMetBy(BattleResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Winner is not { } winner || (Winner is { } wanted && wanted != winner))
        {
            return false;
        }

        // Whole numbers on both sides, so that a band's edge is judged exactly.
        var army = result.Of(winner);
        return (LeastKept is not { } least || army.PriceKept * 100 >= least * army.Price)
            && (MostKept is not { } most || army.PriceKept * 100 <= most * army.Price);
    }
}

/// <summary>One acceptance battle of design 3.1.</summary>
/// <param name="Number">Its number in the design's table.</param>
/// <param name="BlockWidth">How many units wide each block is.</param>
/// <param name="Expected">What the design expects with the starting numbers, from its rough model.</param>
public sealed record BattleDef(
    int Number,
    string Name,
    ArenaKind Arena,
    int BlockWidth,
    ArmyDef Mass,
    ArmyDef Elite,
    Target Target,
    string Expected)
{
    /// <summary>True for a fight that is not one of the design's battles and has no target: it is fought for what it counts.</summary>
    public bool IsMeasure { get; init; }

    public ArmyDef Army(Side side) => side == Side.Mass ? Mass : Elite;
}

/// <summary>
/// The four acceptance battles as the design fixes them (section 3.1), between the two armies
/// of example B at first-playable tiers (section 2.6). The numbers a battle is fought with (hit
/// points, damage, prices) are not here: they come from the data folder, through the same
/// blueprints a match uses.
/// </summary>
public static class AcceptanceBattles
{
    /// <summary>The front ranks of the two armies start this many tiles apart.</summary>
    public const int TilesBetweenFronts = 15;

    /// <summary>The width of a block, in units, in the open.</summary>
    public const int OpenWidth = 10;

    /// <summary>The width of the chokepoint, in tiles, and of a block in it, in units.</summary>
    public const int ChokepointWidth = 6;

    /// <summary>The narrowest and the widest chokepoint the test can be fought in: what the design allows a map (section 5.3).</summary>
    public const int NarrowestChokepoint = 4;

    /// <inheritdoc cref="NarrowestChokepoint"/>
    public const int WidestChokepoint = 8;

    private const string Auxiliary = "auxiliary";
    private const string Grenadier = "grenadier";
    private const string Onager = "onager";
    private const string Legionary = "legionary";

    /// <summary>The swarm player of example B at first-playable tiers.</summary>
    public static IReadOnlyList<int> MassTiers { get; } = [1, 1, 2, 4, 4, 2];

    /// <summary>The elite player of example B at first-playable tiers.</summary>
    public static IReadOnlyList<int> EliteTiers { get; } = [4, 4, 2, 1, 1, 2];

    public static IReadOnlyList<BattleDef> All { get; } =
    [
        new(
            1,
            "Equal resources, open field",
            ArenaKind.OpenField,
            OpenWidth,
            Mass(80),
            Elite(20),
            new Target(Side.Mass, 30, 60, "mass wins and keeps 30% to 60% of its army value"),
            "met"),
        new(
            2,
            "Equal supply, open field",
            ArenaKind.OpenField,
            OpenWidth,
            Mass(40),
            Elite(20),
            new Target(Side.Elite, 70, null, "elite wins and keeps at least 70%"),
            "met"),
        new(
            3,
            "Equal resources, chokepoint 6 tiles wide",
            ArenaKind.Chokepoint,
            ChokepointWidth,
            Mass(80),
            Elite(20),
            new Target(Side.Elite, null, null, "elite wins"),
            "probably missed: mass wins"),
        new(
            4,
            "Equal resources, elite with Artillery, open field",
            ArenaKind.OpenField,
            OpenWidth,
            Mass(80),
            Elite(14, onagers: 2),
            new Target(null, null, 35, "either side may win, and keeps at most 35%"),
            "close: elite wins and keeps about 28%"),
    ];

    /// <summary>
    /// A fight that is not one of the design's four and has no target: battle 1 with the elite
    /// player's Legionaries in place of its Grenadiers. At the elite player's tiers a Legionary
    /// is two tiers of Firepower ahead of its chassis and so Overgunned II (design 2.3): every
    /// hit of it splashes, which no unit of the four battles does. It is fought to count what
    /// splash reaches in the layout the design counts blasts in, a square grid at 1 tile.
    /// </summary>
    public static BattleDef SplashMeasure { get; } = new(
        5,
        "Not a battle of the design: what splash reaches, open field",
        ArenaKind.OpenField,
        OpenWidth,
        Mass(80),
        new ArmyDef(EliteTiers, new ArmyPart(Legionary, 20)),
        new Target(null, null, null, "none; it is fought to count what the splash of the Legionaries reaches"),
        "nothing")
    {
        IsMeasure = true,
    };

    /// <summary>
    /// The four battles with the chokepoint of battle 3 another width than the design's 6: the
    /// corridor and both blocks in it are that many tiles and units wide. For tuning the test,
    /// which the design names as the second thing to adjust when its target is missed.
    /// </summary>
    public static IReadOnlyList<BattleDef> WithChokepoint(int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, NarrowestChokepoint);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, WidestChokepoint);
        return
        [
            .. All.Select(battle => battle.Arena == ArenaKind.Chokepoint
                ? battle with { Name = $"Equal resources, chokepoint {width} tiles wide", BlockWidth = width }
                : battle),
        ];
    }

    private static ArmyDef Mass(int auxiliaries) => new(MassTiers, new ArmyPart(Auxiliary, auxiliaries));

    private static ArmyDef Elite(int grenadiers, int onagers = 0) =>
        new(EliteTiers, new ArmyPart(Grenadier, grenadiers), onagers == 0 ? null : new ArmyPart(Onager, onagers));
}
