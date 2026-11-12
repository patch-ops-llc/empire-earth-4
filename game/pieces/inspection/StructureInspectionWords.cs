using System.Globalization;
using System.Text;
using Sim.Data;
using Sim.Numerics;

namespace Game;

/// <summary>
/// One row of the inspection panel for a structure: a track, the tier in it when there is one
/// to name, and what that means for the structure.
/// </summary>
/// <param name="Name">What the row is about, in a word: "Protection", "Firepower", "Look" or "Banner".</param>
/// <param name="Track">The track, as its index in the data.</param>
/// <param name="Tier">The tier, or null for a row that names the track alone, as the banner's.</param>
/// <param name="Text">What stands beside the track.</param>
public readonly record struct StructureTierRow(string Name, int Track, int? Tier, string Text)
{
    /// <summary>The track with its tier, as the row shows it in the track's color: "Protection 2", or "Firepower" alone.</summary>
    public string Label(GameData data) =>
        Tier is { } tier ? StructureInspectionWords.Tier(data, Track, tier) : data.Tracks[Track].Name;
}

/// <summary>
/// What the inspection panel says of a structure (design 3.2 with 2.5): its rows, what a tier of
/// its owner's means for it, and what the player is told of one they only remember. The panel
/// makes its controls from what is worded here and adds no word of its own, so a test can ask
/// here what the panel shows for a structure that no match has.
/// </summary>
public static class StructureInspectionWords
{
    /// <summary>A track with a tier, as the panel names it in the track's color: "Protection 2".</summary>
    public static string Tier(GameData data, int track, int tier) =>
        string.Create(CultureInfo.InvariantCulture, $"{data.Tracks[track].Name} {tier}");

    /// <summary>
    /// What the owner's Protection tier does for a structure (design 2.5): its hit points under
    /// that tier, and the fortification rule. "2500 hit points · 50% less from attackers below
    /// Firepower 2", and for a structure whose look is of Protection, that the look shows it.
    /// </summary>
    public static string Protection(GameData data, in StructureFacts structure)
    {
        var text = $"{InspectionWords.Whole(structure.Maximum)} hit points · {Fortified(structure.Fortification, structure.Protection)}";
        return structure.LooksOfProtection(data) ? $"{text} · shown by its look" : text;
    }

    /// <summary>
    /// The fortification rule under a Protection tier (design 2.5): "50% less from attackers
    /// below Firepower 2". At tier 1 no attacker is below it, and the line says so.
    /// </summary>
    public static string Fortified(Fraction fortification, int protection) => protection > 1 && fortification < Fraction.One
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{((Fraction.One - fortification) * 100).RoundHalfUp()}% less from attackers below Firepower {protection}")
        : "full damage from every attacker";

    /// <summary>
    /// How a structure attacks under its owner's Firepower tier (design 2.5, the Tower): "10
    /// damage a second · range 7.0, a shot every 1 s · cannot hit Air". One that is building
    /// itself does not attack yet, and the line says so.
    /// </summary>
    public static string Attack(GameData data, in StructureFacts structure)
    {
        if (structure.Attack is not { } attack)
        {
            return "No attack";
        }

        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{InspectionWords.Whole(attack.DamagePerSecond)} damage a second · range {InspectionWords.Tiles(attack.Range)}, a shot every {InspectionWords.Seconds(attack.Attack.IntervalTicks)}");
        var spared = new List<string>();
        foreach (var line in attack.CannotHit)
        {
            if (line >= 0 && line < data.Lines.Count)
            {
                spared.Add(ResearchWords.NameOf(data.Lines[line].Id));
            }
        }

        if (spared.Count > 0)
        {
            text += $" · cannot hit {string.Join(" or ", spared)}";
        }

        return structure.IsFinished ? text : $"{text} · once it is finished";
    }

    /// <summary>What stands beside the tier a structure's look shows (design 3.2).</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="own">True for a structure of the local player's.</param>
    public static string Look(in StructureFacts structure, bool own) =>
        structure.IsRemembered ? "shown by its look when you last saw it"
        : own ? "shown by its look to anyone who sees it"
        : "shown by its look";

    /// <summary>What stands beside the track on a structure's banner (design 3.2): the one in research in its slot.</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="own">True for a structure of the local player's.</param>
    public static string Banner(in StructureFacts structure, bool own) =>
        structure.IsRemembered ? "was in research there when you last saw it, by its banner"
        : own ? "in research here: its banner shows that to anyone who sees it"
        : "in research there, by its banner";

    /// <summary>
    /// That the panel shows a memory, and how old it is: "As you last saw it, 15 s ago. It may
    /// have changed since, or be gone." For one that was building itself, how far it was then:
    /// "As you last saw it, 15 s ago, 40% built. It may have changed since, or be gone."
    /// </summary>
    /// <param name="ticksAgo">The ticks since the player last saw the structure.</param>
    /// <param name="percentBuilt">How far it had built itself then, or null for one that was finished.</param>
    public static string Memory(int ticksAgo, int? percentBuilt = null)
    {
        var built = percentBuilt is { } percent ? string.Create(CultureInfo.InvariantCulture, $", {percent}% built") : string.Empty;
        return $"As you last saw it, {ResearchWords.Duration(ticksAgo)} ago{built}. It may have changed since, or be gone.";
    }

    /// <summary>
    /// The line under the headline of another player's structure: how far it has built itself
    /// while it is not finished, and for a memory that it is one. Empty for a finished structure
    /// the player sees.
    /// </summary>
    public static string State(in StructureFacts structure)
    {
        if (structure.IsRemembered)
        {
            // How long a memory had left to build says nothing by now.
            var total = Math.Max(structure.Structure.BuildTicks, 1);
            return Memory(structure.SeenAgo, structure.IsFinished ? null : Math.Clamp(structure.Built, 0, total) * 100 / total);
        }

        return structure.IsFinished ? string.Empty : BuildWords.Progress(structure.Structure, structure.Built) + ".";
    }

    /// <summary>The last line for a structure the player sees: what of design 3.2's list a structure does not have.</summary>
    public const string NoMobilityNoAccents = "A structure has no Mobility tier and no accents.";

    /// <summary>The last line for a memory: which tiers are missing, and when they are shown.</summary>
    public static string TiersWhileSeen(in StructureFacts structure) => structure.Structure.Attack is null
        ? "Its Protection tier is shown while you see it."
        : "Its Protection and Firepower tiers are shown while you see it.";

    /// <summary>The last line of the panel for another player's structure.</summary>
    public static string Note(in StructureFacts structure) =>
        structure.IsRemembered ? TiersWhileSeen(in structure) : NoMobilityNoAccents;

    /// <summary>
    /// The rows for one structure: its owner's Protection tier and, for one that attacks, the
    /// Firepower tier, while the player may know them; the tier its look shows, unless the
    /// Protection row has said it; and the track on its banner.
    /// </summary>
    /// <param name="data">The game data.</param>
    /// <param name="structure">The structure.</param>
    /// <param name="own">True for a structure of the local player's.</param>
    public static List<StructureTierRow> Rows(GameData data, in StructureFacts structure, bool own)
    {
        var roles = data.TrackRoles;
        var rows = new List<StructureTierRow>();
        if (structure.HasTiers)
        {
            rows.Add(new StructureTierRow("Protection", roles.Protection, structure.Protection, Protection(data, in structure)));
            if (structure.Attack is not null)
            {
                rows.Add(new StructureTierRow("Firepower", roles.Firepower, structure.Firepower, Attack(data, in structure)));
            }
        }

        // The look of a Tower shows its owner's Protection, which the row above has said.
        if (structure.Structure.Track is { } track && structure.LookTier > 0 && !(structure.HasTiers && track == roles.Protection))
        {
            rows.Add(new StructureTierRow("Look", track, structure.LookTier, Look(in structure, own)));
        }

        if (structure.Banner >= 0 && structure.Banner < data.Tracks.Count)
        {
            rows.Add(new StructureTierRow("Banner", structure.Banner, null, Banner(in structure, own)));
        }

        return rows;
    }

    /// <summary>
    /// The rows for several structures of one player: the Protection tier, which is their
    /// owner's and so the same for all, and the Firepower tier of the first that attacks.
    /// </summary>
    public static List<StructureTierRow> SharedRows(GameData data, IReadOnlyList<StructureFacts> structures)
    {
        var roles = data.TrackRoles;
        var rows = new List<StructureTierRow>();
        if (structures.Count == 0 || !structures[0].HasTiers)
        {
            return rows;
        }

        var first = structures[0];
        rows.Add(new StructureTierRow(
            "Protection",
            roles.Protection,
            first.Protection,
            $"{Fortified(first.Fortification, first.Protection)}, for every structure of yours"));
        foreach (var structure in structures)
        {
            if (structure.Attack is not null)
            {
                rows.Add(new StructureTierRow(
                    "Firepower",
                    roles.Firepower,
                    structure.Firepower,
                    $"{StructureWords.NameOf(structure.Structure)}: {Attack(data, in structure)}"));
                break;
            }
        }

        return rows;
    }

    /// <summary>
    /// Every line of text the panel shows for one structure, in reading order, each on a line of
    /// its own. For another player's: its name, its owner, its hit points, the line that says
    /// how far it is built or that it is a memory, its rows and the last line. For the player's
    /// own, whose name and hit points the selection's own line gives: its rows.
    /// </summary>
    /// <param name="data">The game data.</param>
    /// <param name="structure">The structure.</param>
    /// <param name="own">True for a structure of the local player's.</param>
    public static string Lines(GameData data, in StructureFacts structure, bool own)
    {
        var text = new StringBuilder();
        if (!own)
        {
            text.AppendLine(StructureWords.NameOf(structure.Structure));
            text.AppendLine(InspectionWords.Owner(structure.Owner));
            text.AppendLine(InspectionWords.Health(structure.HitPoints, structure.Maximum));
            if (State(in structure) is { Length: > 0 } state)
            {
                text.AppendLine(state);
            }
        }

        AppendRows(text, data, Rows(data, in structure, own));
        if (!own)
        {
            text.AppendLine(Note(in structure));
        }

        return text.ToString();
    }

    /// <summary>The lines of text of rows, as <see cref="Lines"/> has them: the track of each, and then what stands beside it.</summary>
    public static string Lines(GameData data, IReadOnlyList<StructureTierRow> rows)
    {
        var text = new StringBuilder();
        AppendRows(text, data, rows);
        return text.ToString();
    }

    private static void AppendRows(StringBuilder text, GameData data, IReadOnlyList<StructureTierRow> rows)
    {
        foreach (var row in rows)
        {
            text.AppendLine(row.Label(data));
            text.AppendLine(row.Text);
        }
    }
}
