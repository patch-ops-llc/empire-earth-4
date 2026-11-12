using System.Globalization;
using Godot;
using Sim.Research;

namespace Game;

/// <summary>
/// The grid of the research panel (design 1.3): a row for each track and a column for each tier.
/// A cell says what its tier is to the player now, and the cell of a tier that can be researched
/// is a button that starts it.
/// </summary>
public partial class ResearchGrid : GridContainer
{
    private const float RowHeadWidth = 118f;
    private const float RowHeight = 44f;

    // A tier above the match's epoch cap can never be researched, so its column is kept narrow.
    private const float ClosedWidth = 24f;
    private const float OpenWidth = 56f;

    // The background of the head of the row the detail is about.
    private static readonly Color PointedColor = new(0.2f, 0.225f, 0.28f);

    private ResearchReading _reading = null!;
    private ResearchCell[][] _cells = [];
    private PanelContainer[] _rowHeads = [];
    private Label[] _rowTiers = [];
    private PanelContainer[] _columnHeads = [];
    private Label[] _columnNames = [];
    private int _pointedShown = -2;
    private int _epochShown = -1;

    /// <summary>Raised with a track when the pointer comes onto its row.</summary>
    public event Action<int>? Pointed;

    /// <summary>Raised with a track when the player clicks the cell that starts its next tier.</summary>
    public event Action<int>? Chosen;

    /// <summary>Makes the cells. Call it once, before the first <see cref="Present"/>.</summary>
    public void Build(ResearchReading reading)
    {
        _reading = reading;
        var data = reading.Data;
        var tiers = data.TierCount;
        var cap = reading.EpochCap;

        Columns = tiers + 1;
        AddThemeConstantOverride("h_separation", 4);
        AddThemeConstantOverride("v_separation", 4);

        AddChild(new Control { CustomMinimumSize = new Vector2(RowHeadWidth, 0f), MouseFilter = MouseFilterEnum.Ignore });
        _columnHeads = new PanelContainer[tiers];
        _columnNames = new Label[tiers];
        for (var tier = 1; tier <= tiers; tier++)
        {
            var open = tier <= cap;
            var name = Small(open ? string.Create(CultureInfo.InvariantCulture, $"{tier} {data.EpochNames[tier - 1]}") : ResearchWords.Number(tier), 12);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.ClipText = true;
            name.AddThemeColorOverride("font_color", ResearchColors.Faint);
            var head = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            head.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            head.AddChild(name);
            Fit(head, open, vertical: false);
            AddChild(head);
            _columnHeads[tier - 1] = head;
            _columnNames[tier - 1] = name;
        }

        _cells = new ResearchCell[reading.TrackCount][];
        _rowHeads = new PanelContainer[reading.TrackCount];
        _rowTiers = new Label[reading.TrackCount];
        for (var track = 0; track < reading.TrackCount; track++)
        {
            // The handlers below are kept by the cells, so each needs a track of its own to name.
            var own = track;
            var color = TrackColors.Of(data, track);

            var head = new PanelContainer { CustomMinimumSize = new Vector2(RowHeadWidth, RowHeight), SizeFlagsVertical = SizeFlags.ExpandFill };
            head.MouseEntered += () => Pointed?.Invoke(own);
            var lines = UiStyle.Column(0);
            lines.Alignment = BoxContainer.AlignmentMode.Center;
            head.AddChild(lines);
            var name = Small(data.Tracks[track].Name, 15);
            name.AddThemeColorOverride("font_color", color);
            lines.AddChild(name);
            var tierLine = Small(string.Empty, 11);
            tierLine.AddThemeColorOverride("font_color", ResearchColors.Faint);
            lines.AddChild(tierLine);
            AddChild(head);
            _rowHeads[track] = head;
            _rowTiers[track] = tierLine;

            _cells[track] = new ResearchCell[tiers];
            for (var tier = 1; tier <= tiers; tier++)
            {
                var cell = new ResearchCell(color) { Name = $"{data.Tracks[track].Id}_{tier}" };
                Fit(cell, tier <= cap, vertical: true);
                cell.MouseEntered += () => Pointed?.Invoke(own);
                cell.Pressed += () => Chosen?.Invoke(own);
                AddChild(cell);
                _cells[track][tier - 1] = cell;
            }
        }
    }

    /// <summary>Shows the grid as the match stands.</summary>
    /// <param name="pointed">The track whose row is marked as the one the detail is about, or -1.</param>
    /// <param name="ordered">The track whose research the player has just ordered, or -1.</param>
    public void Present(int pointed, int ordered)
    {
        var data = _reading.Data;
        var cap = _reading.EpochCap;
        var materials = _reading.Materials;
        var energy = _reading.Energy;

        var epoch = _reading.Epoch;
        if (epoch != _epochShown)
        {
            _epochShown = epoch;
            for (var tier = 1; tier <= _columnHeads.Length; tier++)
            {
                var own = tier == epoch;
                _columnHeads[tier - 1].AddThemeStyleboxOverride("panel", own ? HeadBox(ResearchColors.Epoch, ResearchColors.Epoch) : new StyleBoxEmpty());
                _columnNames[tier - 1].AddThemeColorOverride("font_color", own ? ResearchColors.Ink : ResearchColors.Faint);
            }
        }

        for (var track = 0; track < _cells.Length; track++)
        {
            var tierNow = _reading.Tier(track);
            var names = data.Tracks[track].TierNames;
            _rowTiers[track].Text = string.Create(CultureInfo.InvariantCulture, $"Tier {tierNow} · {ResearchWords.Raises(data, track)}");

            for (var tier = 1; tier <= names.Count; tier++)
            {
                ResearchCellText text;
                if (tier > cap)
                {
                    text = new ResearchCellText(ResearchCellLook.Closed, string.Empty);
                }
                else if (tier <= tierNow)
                {
                    text = new ResearchCellText(ResearchCellLook.Reached, names[tier - 1]);
                }
                else if (tier == tierNow + 1)
                {
                    text = Next(track, ordered == track, materials, energy);
                }
                else
                {
                    text = new ResearchCellText(ResearchCellLook.Later, names[tier - 1]);
                }

                _cells[track][tier - 1].Present(text);
            }
        }

        if (pointed != _pointedShown)
        {
            _pointedShown = pointed;
            for (var track = 0; track < _rowHeads.Length; track++)
            {
                var color = TrackColors.Of(data, track);
                _rowHeads[track].AddThemeStyleboxOverride("panel", track == pointed ? HeadBox(PointedColor, color) : HeadBox(ResearchColors.Box, color.Darkened(0.35f)));
            }
        }
    }

    // The cell of a track's next tier.
    private ResearchCellText Next(int track, bool ordered, long materials, long energy)
    {
        if (_reading.TryGetSlot(track, out var slot))
        {
            var progress = slot.Ticks > 0 ? 1f - ((float)slot.TicksLeft / slot.Ticks) : 0f;
            return new ResearchCellText(ResearchCellLook.Researching, "Researching", $"{ResearchWords.Duration(slot.TicksLeft)} left", Progress: progress);
        }

        if (_reading.Tech.Research[track].Price is not { } price)
        {
            return new ResearchCellText(ResearchCellLook.Blocked, "Out of reach", "epoch too low");
        }

        var first = string.Create(CultureInfo.InvariantCulture, $"{price.Materials} M");
        var second = string.Create(CultureInfo.InvariantCulture, $"{price.Energy} E");
        if (ordered)
        {
            return new ResearchCellText(ResearchCellLook.Ordered, "Starting", first, second);
        }

        return _reading.Check(track) switch
        {
            ResearchStartCheck.Allowed => new ResearchCellText(ResearchCellLook.Offered, "Research", first, second),
            ResearchStartCheck.TooExpensive => new ResearchCellText(
                ResearchCellLook.Blocked, "Too expensive", first, second, FirstShort: materials < price.Materials, SecondShort: energy < price.Energy),
            ResearchStartCheck.NoFreeSlot => new ResearchCellText(ResearchCellLook.Blocked, _reading.OpenSlots() == 0 ? "No slot" : "Slot busy", first, second),
            _ => new ResearchCellText(ResearchCellLook.Blocked, "Not now", first, second),
        };
    }

    private static void Fit(Control control, bool open, bool vertical)
    {
        control.CustomMinimumSize = new Vector2(open ? OpenWidth : ClosedWidth, vertical ? RowHeight : 0f);
        control.SizeFlagsHorizontal = open ? SizeFlags.ExpandFill : SizeFlags.Fill;
        control.SizeFlagsVertical = vertical ? SizeFlags.ExpandFill : SizeFlags.Fill;
    }

    private static Label Small(string text, int size)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeConstantOverride("line_spacing", -2);
        return label;
    }

    // The box of a row's or a column's head, with a bar of the track's color on its left.
    private static StyleBoxFlat HeadBox(Color fill, Color bar)
    {
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = bar, BorderWidthLeft = 4 };
        style.SetCornerRadiusAll(4);
        style.ContentMarginLeft = 10f;
        style.ContentMarginRight = 4f;
        style.ContentMarginTop = 2f;
        style.ContentMarginBottom = 2f;
        return style;
    }
}
