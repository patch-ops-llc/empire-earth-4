using Net;
using Sim.Data;

namespace Game;

/// <summary>One thing the host can put in a seat, as the page of the match's settings lists it.</summary>
/// <param name="Label">The words of the entry.</param>
/// <param name="Kind">What takes the seat when the entry is chosen.</param>
/// <param name="Personality">For the computer, the ID of its personality.</param>
/// <param name="Offered">False for an entry that is listed and cannot be chosen.</param>
public sealed record SeatChoice(string Label, SeatKind Kind, string? Personality = null, bool Offered = true);

/// <summary>
/// What the host can put in a seat that is not its own: a player who joins, nobody, or the
/// computer with one of the personalities of the data (design 5.5).
/// </summary>
public static class SeatChoices
{
    /// <summary>
    /// What the computer opponent does so far, for wherever it is offered. It is the first of
    /// its parts: it builds an economy, researches and trains an army, and does not attack.
    /// </summary>
    public const string ComputerNote = "The computer opponent builds up its base, researches and trains an army. It does not attack yet.";

    /// <summary>The entries, in the order the page lists them. The first is the one a seat has when nobody chooses.</summary>
    public static IReadOnlyList<SeatChoice> For(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var choices = new List<SeatChoice>
        {
            new("A player who joins", SeatKind.Person),
            new("Nobody: its workers stand idle", SeatKind.Nobody),
        };

        // The personalities come from the data, so a new file under ai/ is a new entry here.
        foreach (var personality in data.Personalities)
        {
            choices.Add(new SeatChoice(ComputerLabel(personality), SeatKind.Computer, personality.Id));
        }

        if (data.Personalities.Count == 0)
        {
            choices.Add(new SeatChoice("Computer (the data has no personality)", SeatKind.Computer, Offered: false));
        }

        return choices;
    }

    /// <summary>The computer with a personality, as an entry and a sentence name it: "Computer, Swarm".</summary>
    public static string ComputerLabel(AiPersonalityDef personality)
    {
        ArgumentNullException.ThrowIfNull(personality);
        return $"Computer, {personality.Name}";
    }

    /// <summary>The index of the entry a seat's setting is shown as.</summary>
    public static int IndexOf(IReadOnlyList<SeatChoice> choices, SeatSetting setting)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(setting);
        for (var i = 0; i < choices.Count; i++)
        {
            var same = choices[i].Kind == setting.Kind
                && (setting.Kind != SeatKind.Computer || string.Equals(choices[i].Personality, setting.Personality, StringComparison.Ordinal));
            if (choices[i].Offered && same)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>A seat's setting once an entry is chosen for it. The start it is set to stays.</summary>
    public static SeatSetting Apply(SeatChoice choice, SeatSetting setting)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(setting);
        return setting with { Kind = choice.Kind, Personality = choice.Personality };
    }
}
