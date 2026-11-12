using System.Globalization;
using Sim.Core;
using Sim.Data;

namespace Game;

/// <summary>
/// The words the combat view uses for things the data names only by an ID: a chassis, a line,
/// and a count of hit points.
/// </summary>
public static class CombatWords
{
    /// <summary>The name of a chassis or a line for a sentence, from its ID: "man_at_arms" is "Man at arms".</summary>
    public static string NameOf(string id)
    {
        var words = id.Replace('_', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>What a unit is, in a word or two: its chassis, "Worker", or "Unit".</summary>
    public static string NameOf(MatchView view, EntityId entity)
    {
        var match = view.Match;
        if (match.Combatants.TryGet(entity, out var unit))
        {
            return NameOf(match.Data.Chassis[unit.Chassis].Id);
        }

        return match.Workers.Contains(entity) ? "Worker" : "Unit";
    }

    /// <summary>A chassis as a list names it: its name, its line and its epoch.</summary>
    public static string Describe(GameData data, ChassisDef chassis)
    {
        var line = data.Rules.Lines[chassis.Line];
        return string.Create(CultureInfo.InvariantCulture, $"{NameOf(chassis.Id)} ({NameOf(line).ToLowerInvariant()}, epoch {chassis.Epoch})");
    }

    /// <summary>
    /// Hit points as a whole number, from thousandths. They are rounded up, so that a unit that
    /// is still alive never shows none.
    /// </summary>
    public static string HitPoints(long thousandths) => ((thousandths + 999) / 1000).ToString(CultureInfo.InvariantCulture);
}
