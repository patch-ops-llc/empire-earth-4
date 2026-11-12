using System.Globalization;
using Sim.Data;
using Sim.Numerics;

namespace Game;

/// <summary>
/// The wording of the inspection panel for a structure (design 3.2 with 2.5): what a tier of its
/// owner's means for it, and what the player is told of one they only remember.
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
}
