using Net;
using Sim.Data;

namespace Game;

/// <summary>One thing the host can put in a seat, as the page of the match's settings lists it.</summary>
/// <param name="Label">The words of the entry.</param>
/// <param name="Kind">What takes the seat when the entry is chosen.</param>
/// <param name="Offered">False for an entry that is listed and cannot be chosen in this build.</param>
public sealed record SeatChoice(string Label, SeatKind Kind, bool Offered = true);

/// <summary>
/// What the host can put in a seat that is not its own. This list is the lobby's place for the
/// computer opponent (M21): an entry for each personality of the data goes where the entry that
/// cannot be chosen stands now, and the page of the settings needs no other change.
/// </summary>
public static class SeatChoices
{
    /// <summary>The entries, in the order the page lists them. The first is the one a seat has when nobody chooses.</summary>
    public static IReadOnlyList<SeatChoice> For(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return
        [
            new("A player who joins", SeatKind.Person),
            new("Nobody: its workers stand idle", SeatKind.Nobody),
            new("Computer opponent (not in this build)", SeatKind.Nobody, Offered: false),
        ];
    }

    /// <summary>The index of the entry a seat's setting is shown as.</summary>
    public static int IndexOf(IReadOnlyList<SeatChoice> choices, SeatSetting setting)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i].Offered && choices[i].Kind == setting.Kind)
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
        return setting with { Kind = choice.Kind };
    }
}
