using Godot;

namespace Game;

/// <summary>The colors and boxes the research panel is drawn with, apart from the tracks' own colors.</summary>
public static class ResearchColors
{
    /// <summary>Text that is there to be read.</summary>
    public static readonly Color Ink = new(0.93f, 0.94f, 0.96f);

    /// <summary>Text that stands back: headings, what is not available.</summary>
    public static readonly Color Faint = new(0.6f, 0.63f, 0.69f);

    /// <summary>Text that says something can be done now.</summary>
    public static readonly Color Good = new(0.55f, 0.85f, 0.55f);

    /// <summary>The background of a box inside the panel.</summary>
    public static readonly Color Box = new(0.13f, 0.145f, 0.18f);

    /// <summary>The background of what marks the player's epoch.</summary>
    public static readonly Color Epoch = new(0.2f, 0.26f, 0.36f);

    /// <summary>The trough of a progress bar.</summary>
    public static readonly Color Trough = new(0.05f, 0.055f, 0.07f);

    /// <summary>A box of one color with slightly rounded corners, as a progress bar and its trough have.</summary>
    public static StyleBoxFlat Bar(Color color)
    {
        var style = new StyleBoxFlat { BgColor = color };
        style.SetCornerRadiusAll(3);
        return style;
    }

    /// <summary>A box inside the panel, with room round what is in it.</summary>
    public static StyleBoxFlat Boxed(Color color, float left, float right, float topAndBottom)
    {
        var style = new StyleBoxFlat { BgColor = color };
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = left;
        style.ContentMarginRight = right;
        style.ContentMarginTop = topAndBottom;
        style.ContentMarginBottom = topAndBottom;
        return style;
    }
}
