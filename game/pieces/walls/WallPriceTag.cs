using Godot;

namespace Game;

/// <summary>
/// The tag beside the pointer while a run of wall is drawn: how many tiles the order would lay
/// and what it costs, and under it why it cannot be laid or which tiles are left out. The wall
/// tool says what it shows.
/// </summary>
public partial class WallPriceTag : InterfacePart
{
    // Below and to the right of the pointer, clear of the tile it points at.
    private static readonly Vector2 Offset = new(22f, 20f);
    private const float Edge = 8f;

    private PanelContainer _panel = null!;
    private Label _price = null!;
    private Label _note = null!;

    /// <summary>What the first line says now, or null while the tag is not shown.</summary>
    public string? Price => _panel.Visible ? _price.Text : null;

    /// <summary>What the second line says now, or null while it says nothing.</summary>
    public string? Note => _panel.Visible && _note.Visible ? _note.Text : null;

    /// <summary>Shows the tag.</summary>
    /// <param name="price">What the run lays and costs.</param>
    /// <param name="note">What else there is to say, or null.</param>
    /// <param name="warn">True when the order would not be taken.</param>
    public void Show(string price, string? note, bool warn)
    {
        _price.Text = price;
        _price.AddThemeColorOverride("font_color", warn ? UiStyle.Warning : ResearchColors.Ink);
        _note.Text = note ?? string.Empty;
        _note.Visible = note is not null;
        _note.AddThemeColorOverride("font_color", warn ? UiStyle.Warning : ResearchColors.Faint);
        _panel.Visible = true;
        Place();
    }

    /// <summary>Shows nothing.</summary>
    public void Clear() => _panel.Visible = false;

    protected override void Attach()
    {
        _panel = new PanelContainer { Name = "WallPriceTag", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(7f));
        AddChild(_panel);

        var lines = UiStyle.Column(0);
        lines.MouseFilter = Control.MouseFilterEnum.Ignore;
        _panel.AddChild(lines);
        _price = InspectionUi.Text(string.Empty, ResearchColors.Ink, 15);
        _note = InspectionUi.Text(string.Empty, ResearchColors.Faint, 13);
        _price.MouseFilter = Control.MouseFilterEnum.Ignore;
        _note.MouseFilter = Control.MouseFilterEnum.Ignore;
        lines.AddChild(_price);
        lines.AddChild(_note);
    }

    public override void EveryFrame(double delta)
    {
        if (_panel.Visible)
        {
            Place();
        }
    }

    // Beside the pointer, and inside the window.
    private void Place()
    {
        var size = _panel.GetCombinedMinimumSize();
        var window = _panel.GetViewportRect().Size;
        var at = _panel.GetViewport().GetMousePosition() + Offset;
        _panel.Size = size;
        _panel.Position = new Vector2(
            Mathf.Round(Mathf.Clamp(at.X, Edge, Mathf.Max(Edge, window.X - size.X - Edge))),
            Mathf.Round(Mathf.Clamp(at.Y, Edge, Mathf.Max(Edge, window.Y - size.Y - Edge))));
    }
}
