using Godot;

namespace Game;

/// <summary>How one cell of the research grid looks, which says what its tier is to the player now.</summary>
public enum ResearchCellLook
{
    /// <summary>Not shown yet.</summary>
    None,

    /// <summary>The track has this tier.</summary>
    Reached,

    /// <summary>The next tier, and a click starts its research.</summary>
    Offered,

    /// <summary>The next tier, whose research the player has ordered and the match has not started yet.</summary>
    Ordered,

    /// <summary>The next tier, which cannot be started now.</summary>
    Blocked,

    /// <summary>The next tier, in research.</summary>
    Researching,

    /// <summary>A tier that comes after the next one.</summary>
    Later,

    /// <summary>A tier above the match's epoch cap.</summary>
    Closed,
}

/// <summary>What one cell of the research grid shows.</summary>
/// <param name="Look">What the tier is to the player now.</param>
/// <param name="Title">The first line.</param>
/// <param name="First">The first half of the second line, or empty.</param>
/// <param name="Second">The second half of the second line, or empty.</param>
/// <param name="FirstShort">Whether the first half is something the player lacks.</param>
/// <param name="SecondShort">Whether the second half is something the player lacks.</param>
/// <param name="Progress">For a tier in research, how far it is, from 0 to 1.</param>
public readonly record struct ResearchCellText(
    ResearchCellLook Look,
    string Title,
    string First = "",
    string Second = "",
    bool FirstShort = false,
    bool SecondShort = false,
    float Progress = 0f);

/// <summary>
/// One cell of the research grid: one tier of one track. It is a button, live only while a click
/// would start the research of its tier.
/// </summary>
public partial class ResearchCell : Button
{
    // The background of a tier that is closed for the whole match.
    private static readonly Color Closed = new(0.075f, 0.082f, 0.105f);

    private readonly Color _track;
    private ColorRect _fill = null!;
    private Label _title = null!;
    private HBoxContainer _second = null!;
    private Label _firstHalf = null!;
    private Label _secondHalf = null!;
    private ResearchCellText _shown = new(ResearchCellLook.None, string.Empty);

    /// <param name="track">The color of the cell's track.</param>
    public ResearchCell(Color track)
    {
        _track = track;
    }

    // Godot makes a node of every node class it knows, and needs this to do so.
    public ResearchCell()
        : this(Colors.White)
    {
    }

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        ClipContents = true;
        AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        // How far a research has come, drawn as the cell filling up from the left.
        _fill = new ColorRect
        {
            Color = new Color(_track, 0.55f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_fill);
        _fill.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide, LayoutPresetMode.Minsize, 2);

        var lines = UiStyle.Column(0);
        lines.MouseFilter = MouseFilterEnum.Ignore;
        lines.Alignment = BoxContainer.AlignmentMode.Center;
        AddChild(lines);
        lines.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 4);

        _title = Line();
        lines.AddChild(_title);

        _second = UiStyle.Row(8);
        _second.MouseFilter = MouseFilterEnum.Ignore;
        _second.Alignment = BoxContainer.AlignmentMode.Center;
        lines.AddChild(_second);
        _firstHalf = Line();
        _second.AddChild(_firstHalf);
        _secondHalf = Line();
        _second.AddChild(_secondHalf);

        Apply(_shown);
    }

    /// <summary>Shows the cell as the match has its tier now. Nothing is redrawn while nothing changes.</summary>
    public void Present(in ResearchCellText text)
    {
        if (text == _shown)
        {
            return;
        }

        var lookChanged = text.Look != _shown.Look;
        _shown = text;
        if (IsNodeReady())
        {
            Apply(text, lookChanged);
        }
    }

    private static Label Line()
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeConstantOverride("line_spacing", -2);
        return label;
    }

    private static StyleBoxFlat Box(Color fill, Color border, int width)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = border };
        style.SetBorderWidthAll(width);
        style.SetCornerRadiusAll(4);
        return style;
    }

    private void Apply(in ResearchCellText text, bool lookChanged = true)
    {
        _title.Text = text.Title;
        _title.Visible = text.Title.Length > 0;
        _firstHalf.Text = text.First;
        _firstHalf.Visible = text.First.Length > 0;
        _secondHalf.Text = text.Second;
        _secondHalf.Visible = text.Second.Length > 0;
        _second.Visible = _firstHalf.Visible || _secondHalf.Visible;

        // Alone in the cell, the first line has all of it to wrap in, and is cut with an ellipsis
        // where even that is too little. Above a second line it is one line.
        _title.SizeFlagsVertical = _second.Visible ? SizeFlags.ShrinkCenter : SizeFlags.ExpandFill;
        _title.AutowrapMode = _second.Visible ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart;
        _title.TextOverrunBehavior = _second.Visible ? TextServer.OverrunBehavior.NoTrimming : TextServer.OverrunBehavior.TrimEllipsis;

        var plain = text.Look == ResearchCellLook.Blocked ? ResearchColors.Faint : ResearchColors.Ink;
        _firstHalf.AddThemeColorOverride("font_color", text.FirstShort ? UiStyle.Warning : plain);
        _secondHalf.AddThemeColorOverride("font_color", text.SecondShort ? UiStyle.Warning : plain);

        _fill.Visible = text.Look == ResearchCellLook.Researching;
        _fill.AnchorRight = Math.Clamp(text.Progress, 0f, 1f);
        _fill.OffsetRight = 2f - (4f * _fill.AnchorRight);

        if (lookChanged)
        {
            Dress(text.Look);
        }
    }

    private void Dress(ResearchCellLook look)
    {
        Disabled = look != ResearchCellLook.Offered;
        MouseDefaultCursorShape = look == ResearchCellLook.Offered ? CursorShape.PointingHand : CursorShape.Arrow;

        StyleBoxFlat normal;
        var hover = (StyleBoxFlat?)null;
        var title = ResearchColors.Ink;
        switch (look)
        {
            case ResearchCellLook.Reached:
                normal = Box(_track.Darkened(0.45f), _track.Darkened(0.2f), 1);
                break;
            case ResearchCellLook.Offered:
                normal = Box(_track.Darkened(0.72f), _track, 2);
                hover = Box(_track.Darkened(0.5f), _track.Lightened(0.3f), 2);
                break;
            case ResearchCellLook.Ordered:
                normal = Box(_track.Darkened(0.72f), _track, 2);
                break;
            case ResearchCellLook.Researching:
                normal = Box(ResearchColors.Box, _track, 2);
                break;
            case ResearchCellLook.Blocked:
                normal = Box(ResearchColors.Box, _track.Darkened(0.45f), 1);
                title = UiStyle.Warning;
                break;
            case ResearchCellLook.Later:
                normal = Box(ResearchColors.Box, ResearchColors.Box.Lightened(0.08f), 1);
                title = ResearchColors.Faint;
                break;
            default:
                normal = Box(Closed, Closed, 0);
                title = ResearchColors.Faint;
                break;
        }

        _title.AddThemeColorOverride("font_color", title);
        _title.AddThemeFontSizeOverride("font_size", look is ResearchCellLook.Offered or ResearchCellLook.Ordered or ResearchCellLook.Researching ? 14 : 12);
        AddThemeStyleboxOverride("normal", normal);
        AddThemeStyleboxOverride("disabled", normal);
        AddThemeStyleboxOverride("hover", hover ?? normal);
        AddThemeStyleboxOverride("pressed", hover ?? normal);
    }
}
