using Godot;

namespace Game;

/// <summary>
/// The color of each seat, and its name for a sentence. Everything a player owns is drawn in the
/// player's color, whichever part of the view draws it.
/// </summary>
public static class PlayerColors
{
    // One color per seat. A match has at most eight players.
    private static readonly (Color Color, string Name)[] Seats =
    [
        (new(0.20f, 0.45f, 0.95f), "blue"),
        (new(0.90f, 0.22f, 0.20f), "red"),
        (new(0.95f, 0.80f, 0.20f), "yellow"),
        (new(0.25f, 0.75f, 0.70f), "teal"),
        (new(0.65f, 0.35f, 0.85f), "purple"),
        (new(0.95f, 0.55f, 0.15f), "orange"),
        (new(0.90f, 0.90f, 0.90f), "white"),
        (new(0.95f, 0.50f, 0.70f), "pink"),
    ];

    /// <summary>The number of colors. A seat beyond it shares the color of an earlier one.</summary>
    public static int Count => Seats.Length;

    public static Color Of(int player) => Seats[Wrap(player)].Color;

    public static string NameOf(int player) => Seats[Wrap(player)].Name;

    private static int Wrap(int player) => ((player % Seats.Length) + Seats.Length) % Seats.Length;
}
