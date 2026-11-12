using System.Globalization;
using Godot;
using Sim.Core;
using Sim.Research;

namespace Game;

/// <summary>
/// Research in the interface (design 1.3): the full-screen panel with the grid of six tracks by
/// nine tiers, the line in the top bar that says what is in research, and the sentence that says
/// a research is complete. The player starts and cancels a research here.
/// </summary>
/// <remarks>
/// It reads the match after every tick and gives two commands through the session,
/// <see cref="CommandType.StartResearch"/> and <see cref="CommandType.CancelResearch"/>. What has
/// happened to a research it finds by comparing the tiers and the slots with those of the tick
/// before, so it works the same alone, hosting and joined.
/// </remarks>
public partial class ResearchPart : InterfacePart
{
    /// <summary>Opens and closes the research panel. Named in the input map of project.godot.</summary>
    public static readonly StringName ToggleAction = "research_toggle_panel";

    // An order runs a few ticks after it is given. Until the match shows it, or this many ticks
    // have passed, the cell or the button that gave it says so and takes no second click.
    private const int OrderShownTicks = 40;

    private const float DetailWidth = 300f;
    private const float TopRowHeight = 38f;


    private ResearchReading _reading = null!;
    private FramePanel _panel = null!;
    private ResearchGrid _grid = null!;
    private ResearchDetail _detail = null!;
    private ResearchSlotsStrip _slots = null!;
    private Label _epoch = null!;
    private Label _materials = null!;
    private Label _energy = null!;
    private Label _barEpoch = null!;
    private Label _barText = null!;
    private ProgressBar _barProgress = null!;

    // The track the detail is about: the one the pointer was on last.
    private int _pointed;

    private (int Track, int Tick) _started = (-1, 0);
    private (int Track, int Tick) _cancelled = (-1, 0);

    private int[] _tiersBefore = [];
    private ResearchSlot[] _slotsBefore = [];

    // The track the top bar's line is colored for, or -1 while it follows no research.
    private int _barTrack = -2;

    protected override void Attach()
    {
        _reading = new ResearchReading(View);
        _tiersBefore = new int[_reading.TrackCount];
        _slotsBefore = new ResearchSlot[ResearchState.SlotsPerPlayer];
        Remember();

        BuildBarLine();
        var content = BuildPanel();
        _panel = Frame.AddPanel("Research", content, InputMap.HasAction(ToggleAction) ? ToggleAction : null);
        _panel.Opened += ShowPanel;
    }

    public override void AfterTick()
    {
        SayWhatHappened();
        Remember();
        ForgetOrdersTheMatchShows();
        ShowBarLine();
        if (_panel.IsOpen)
        {
            ShowPanel();
        }
    }

    private static Label Chip(string name, Color color)
    {
        var label = new Label { Name = name, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeStyleboxOverride("normal", ResearchColors.Boxed(color, 10f, 10f, 3f));
        return label;
    }

    // The top bar: the player's epoch, and what is in research with how far it is.
    private void BuildBarLine()
    {
        var row = UiStyle.Row(8);
        row.Name = "Research";
        Frame.TopLeft.AddChild(row);

        _barEpoch = new Label { Name = "Epoch" };
        row.AddChild(_barEpoch);
        _barText = new Label { Name = "InResearch" };
        row.AddChild(_barText);
        _barProgress = new ProgressBar
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Step = 0.0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(90f, 10f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            Visible = false,
        };
        _barProgress.AddThemeStyleboxOverride("background", ResearchColors.Bar(ResearchColors.Trough));
        row.AddChild(_barProgress);
    }

    private VBoxContainer BuildPanel()
    {
        var content = UiStyle.Column(8);
        content.Name = "Research";

        // The row is as high as a slot with its button, so that nothing below moves when a research starts.
        var top = UiStyle.Row(8);
        top.CustomMinimumSize = new Vector2(0f, TopRowHeight);
        content.AddChild(top);
        _epoch = Chip("Epoch", ResearchColors.Epoch);
        top.AddChild(_epoch);
        _materials = Chip("Materials", ResearchColors.Box);
        top.AddChild(_materials);
        _energy = Chip("Energy", ResearchColors.Box);
        top.AddChild(_energy);
        _slots = new ResearchSlotsStrip { Name = "Slots", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _slots.CancelChosen += Cancel;
        top.AddChild(_slots);

        var middle = UiStyle.Row(10);
        middle.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.AddChild(middle);
        _grid = new ResearchGrid { Name = "Grid", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.Build(_reading);
        _grid.Pointed += track =>
        {
            if (track != _pointed)
            {
                _pointed = track;
                ShowPanel();
            }
        };
        _grid.Chosen += Start;
        middle.AddChild(_grid);
        _detail = new ResearchDetail { Name = "Detail", CustomMinimumSize = new Vector2(DetailWidth, 0f) };
        middle.AddChild(_detail);

        var legend = new Label
        {
            Name = "Legend",
            Text = Legend(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        legend.AddThemeFontSizeOverride("font_size", 12);
        legend.AddThemeColorOverride("font_color", ResearchColors.Faint);
        content.AddChild(legend);
        return content;
    }

    private string Legend()
    {
        var data = _reading.Data;
        var words = "Point at a row to see what its next tier costs and changes. A bright cell starts that research when you click it. M is Materials, E is Energy.";
        var cap = _reading.EpochCap;
        if (cap < data.TierCount)
        {
            var closed = cap + 1 == data.TierCount
                ? string.Create(CultureInfo.InvariantCulture, $"Tier {data.TierCount} is")
                : string.Create(CultureInfo.InvariantCulture, $"Tiers {cap + 1} to {data.TierCount} are");
            words += string.Create(CultureInfo.InvariantCulture, $" {closed} closed in this match: its epoch cap is {cap}, {ResearchWords.Epoch(data, cap)}.");
        }

        return words;
    }

    private void Start(int track)
    {
        if (_reading.Check(track) != ResearchStartCheck.Allowed)
        {
            return;
        }

        if (View.Client.Submit(new Command(CommandType.StartResearch, View.Player, contentId: track)))
        {
            _started = (track, _reading.Tick);
            _pointed = track;
            ShowPanel();
        }
    }

    private void Cancel(int track)
    {
        if (!_reading.TryGetSlot(track, out _))
        {
            return;
        }

        if (View.Client.Submit(new Command(CommandType.CancelResearch, View.Player, contentId: track)))
        {
            _cancelled = (track, _reading.Tick);
            ShowPanel();
        }
    }

    // A research the player ordered is shown as ordered until the match has it, and one the
    // player cancelled until the match has let it go. Neither waits for ever: the match may
    // have dropped the order.
    private void ForgetOrdersTheMatchShows()
    {
        var tick = _reading.Tick;
        if (_started.Track >= 0 && (_reading.TryGetSlot(_started.Track, out _) || tick - _started.Tick > OrderShownTicks))
        {
            _started = (-1, 0);
        }

        if (_cancelled.Track >= 0 && (!_reading.TryGetSlot(_cancelled.Track, out _) || tick - _cancelled.Tick > OrderShownTicks))
        {
            _cancelled = (-1, 0);
        }
    }

    // What the last tick did to the player's research, found by comparing with the tick before:
    // a slot that held a research and holds it no longer has finished it if the track is at
    // that tier now, and has given it up otherwise.
    private void SayWhatHappened()
    {
        var data = _reading.Data;
        foreach (var before in _slotsBefore)
        {
            if (!before.IsBusy || (_reading.TryGetSlot(before.Track, out var now) && now.Tier == before.Tier))
            {
                continue;
            }

            var track = data.Tracks[before.Track];
            if (_reading.Tier(before.Track) >= before.Tier && _tiersBefore[before.Track] < before.Tier)
            {
                Frame.Say(
                    string.Create(CultureInfo.InvariantCulture, $"Research complete: {track.Name} is now tier {before.Tier}, {track.TierNames[before.Tier - 1]}."),
                    seconds: 10.0);
            }
            else
            {
                Frame.Say(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Research stopped: {track.Name} {before.Tier}. Its price, {ResearchWords.Price(before.Materials, before.Energy)}, is back in your stockpile."));
            }
        }
    }

    private void Remember()
    {
        for (var track = 0; track < _tiersBefore.Length; track++)
        {
            _tiersBefore[track] = _reading.Tier(track);
        }

        for (var number = 0; number < _slotsBefore.Length; number++)
        {
            _slotsBefore[number] = _reading.Slot(number);
        }
    }

    private void ShowBarLine()
    {
        var data = _reading.Data;
        var epoch = _reading.Epoch;
        _barEpoch.Text = string.Create(CultureInfo.InvariantCulture, $"Epoch {epoch}, {ResearchWords.Epoch(data, epoch)}");

        // The research that finishes first is the one the bar follows.
        var first = -1;
        var busy = 0;
        for (var number = 0; number < ResearchState.SlotsPerPlayer; number++)
        {
            var slot = _reading.Slot(number);
            if (slot.IsBusy)
            {
                busy++;
                if (first < 0 || slot.TicksLeft < _reading.Slot(first).TicksLeft)
                {
                    first = number;
                }
            }
        }

        _barProgress.Visible = first >= 0;
        if (first < 0)
        {
            var free = false;
            for (var track = 0; track < _reading.TrackCount && !free; track++)
            {
                free = _reading.Check(track) == ResearchStartCheck.Allowed;
            }

            // The line asks for attention only while something can be started.
            _barText.Text = free ? "Research: a slot is free" : "Research: none";
            _barText.AddThemeColorOverride("font_color", free ? UiStyle.Warning : ResearchColors.Faint);
            _barTrack = -1;
            return;
        }

        var leading = _reading.Slot(first);
        var name = data.Tracks[leading.Track].Name;
        var more = busy > 1 ? string.Create(CultureInfo.InvariantCulture, $", and {busy - 1} more") : string.Empty;
        _barText.Text = string.Create(CultureInfo.InvariantCulture, $"Researching {name} {leading.Tier}: {ResearchWords.Duration(leading.TicksLeft)} left{more}");
        if (_barTrack != leading.Track)
        {
            _barTrack = leading.Track;
            _barText.AddThemeColorOverride("font_color", TrackColors.Of(data, leading.Track));
            _barProgress.AddThemeStyleboxOverride("fill", ResearchColors.Bar(TrackColors.Of(data, leading.Track)));
        }

        _barProgress.Value = leading.Ticks > 0 ? 1.0 - ((double)leading.TicksLeft / leading.Ticks) : 0.0;
    }

    private void ShowPanel()
    {
        var data = _reading.Data;
        var epoch = _reading.Epoch;
        _epoch.Text = string.Create(CultureInfo.InvariantCulture, $"Your epoch: {epoch}, {ResearchWords.Epoch(data, epoch)}");
        _materials.Text = string.Create(CultureInfo.InvariantCulture, $"Materials {_reading.Materials}");
        _energy.Text = string.Create(CultureInfo.InvariantCulture, $"Energy {_reading.Energy}");
        _slots.Present(_reading, _cancelled.Track);
        _grid.Present(_pointed, _started.Track);
        _detail.Present(_reading, _pointed);
    }
}
