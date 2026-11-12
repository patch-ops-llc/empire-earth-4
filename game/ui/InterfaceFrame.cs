using Godot;

namespace Game;

/// <summary>
/// The frame of the interface round a match. It has four places, and it puts nothing of its own
/// into them: every readout, button and panel belongs to an <see cref="InterfacePart"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
/// <b>The top bar</b>, across the top of the window: <see cref="TopLeft"/> for what the player
/// has, as resources and supply, and <see cref="TopRight"/> for the match itself. Between them
/// stand the buttons of the full-screen panels. The interface is scaled with the window, so
/// the bar is 1,152 units wide in a window of 16 to 9 or narrower, whatever its size, and
/// wider only in a wider one. Everything in the bar has to fit in that: a readout is a word
/// and a number, and what can say less when the bar is full asks <see cref="TopBarRoomFor"/>.
/// </item>
/// <item>
/// <b>The bottom panel</b>, for the selection and its commands: <see cref="SelectionArea"/> for
/// what is selected, and beside it a button for every <see cref="SelectionCommand"/> that applies.
/// It is shown while there is something in it.
/// </item>
/// <item>
/// <b>The full-screen panel</b>: one of the panels added with <see cref="AddPanel"/> at a time,
/// over the world and the bottom panel, and under the top bar and the message line. While one
/// is open the pointer and the keys are its own: the camera, selection and orders rest.
/// </item>
/// <item>
/// <b>The message line</b>, under the top bar: <see cref="Say"/> shows a sentence to the player
/// for a while. Nothing of the frame lies under it: a full-screen panel begins below it, with
/// room kept for one message, so that the panel moves only when two are shown at once.
/// </item>
/// </list>
/// </remarks>
public partial class InterfaceFrame : CanvasLayer
{
    /// <summary>Closes the full-screen panel that is open. Named in the input map of project.godot.</summary>
    public static readonly StringName ClosePanelAction = "interface_close_panel";

    // The height of the top bar, which the message line and the full-screen panel start under.
    private const float BarHeight = 44f;

    // The room above the message line, measured from BarHeight, of which a top bar with a button
    // in it covers a little; the room between the message line and a full-screen panel; the room
    // between a full-screen panel and the other edges of the window; and the room inside the
    // panel's edge. They are small, so that the line kept for a message costs the panel no height.
    private const int MessageTop = 8;
    private const int MessageBottom = 4;
    private const int PanelMargin = 12;
    private const float PanelPadding = 12f;

    private const int MessageLimit = 5;
    private const double MessageFadeSeconds = 1.0;

    private readonly List<InterfacePart> _parts = [];
    private readonly List<(SelectionCommand Command, Button Button)> _commands = [];
    private readonly List<FramePanel> _panels = [];
    private readonly List<(Label Label, double Left)> _messages = [];

    private MatchView? _view;
    private PanelContainer _bottom = null!;
    private HBoxContainer _commandArea = null!;
    private PanelContainer _topBar = null!;
    private HBoxContainer _panelButtons = null!;
    private VBoxContainer _messageLines = null!;
    private Label _messageRoom = null!;
    private Control _panelShade = null!;
    private Control _panelPlace = null!;
    private Label _panelTitle = null!;
    private Control _panelContent = null!;
    private bool _commandsStale = true;

    /// <summary>The left of the top bar, filled from the left: what the player has.</summary>
    public HBoxContainer TopLeft { get; private set; } = null!;

    /// <summary>The right of the top bar: the match itself, who the player is in it and the way out.</summary>
    public HBoxContainer TopRight { get; private set; } = null!;

    /// <summary>The left of the bottom panel: what is selected. A part adds a control of its own and shows it while it has something to say.</summary>
    public VBoxContainer SelectionArea { get; private set; } = null!;

    /// <summary>The full-screen panel that is open, or null.</summary>
    public FramePanel? OpenPanel { get; private set; }

    /// <summary>True while a full-screen panel is open, and the pointer and the keys are its own.</summary>
    public bool IsPanelOpen => OpenPanel is not null;

    /// <summary>The parts, in the order they were added.</summary>
    public IReadOnlyList<InterfacePart> Parts => _parts;

    /// <summary>Builds the frame for a match. Call it once, when the frame is in the tree.</summary>
    public void Attach(MatchView view)
    {
        _view = view;
        Layer = 10;
        Build();
        view.Selection.Changed += () => _commandsStale = true;
    }

    /// <summary>Adds a part, which is called after every tick and in every frame, after the parts added before it.</summary>
    public void AddPart(InterfacePart part)
    {
        if (string.IsNullOrEmpty(part.Name))
        {
            part.Name = part.GetType().Name;
        }

        AddChild(part);
        _parts.Add(part);
        part.Begin(_view ?? throw new InvalidOperationException("A part is added once the frame has a match."));
    }

    /// <summary>Adds a command for the selection: a button in the bottom panel, shown while the command applies.</summary>
    public void AddCommand(SelectionCommand command)
    {
        var key = command.Action is { } action ? KeyOf(action) : null;
        var button = UiStyle.FrameButton(key is null ? command.Label : $"{command.Label} ({key})");
        button.Visible = false;
        button.Pressed += () => Run(command);
        _commandArea.AddChild(button);
        _commands.Add((command, button));
        _commandsStale = true;
    }

    /// <summary>
    /// Adds a full-screen panel, closed, and a button for it in the top bar. The frame gives it a
    /// title, a way to close it and a shade over the world. One panel is open at a time.
    /// </summary>
    /// <param name="title">The panel's name, on its button and at its top.</param>
    /// <param name="content">What the panel shows. The frame takes it as a child.</param>
    /// <param name="toggleAction">An action of the input map that opens and closes the panel, or null.</param>
    public FramePanel AddPanel(string title, Control content, StringName? toggleAction = null)
    {
        var panel = new FramePanel(this, title, content, toggleAction);
        content.Visible = false;
        _panelContent.AddChild(content);
        _panels.Add(panel);

        var key = toggleAction is null ? null : KeyOf(toggleAction);
        var button = UiStyle.FrameButton(key is null ? title : $"{title} ({key})");
        button.Pressed += panel.Toggle;
        _panelButtons.AddChild(button);
        return panel;
    }

    /// <summary>Closes the full-screen panel that is open, if one is.</summary>
    public void ClosePanel()
    {
        if (OpenPanel is { } panel)
        {
            panel.Content.Visible = false;
            OpenPanel = null;
            _panelShade.Visible = false;
            _panelPlace.Visible = false;
            KeepMessageRoom();
            GivePartsTheKeys(true);
        }
    }

    /// <summary>
    /// The width a control in the top bar can have before the bar is wider than the window, with
    /// everything else in the bar at the least width it needs. A part whose control can give
    /// way, as one that can say less, fits what it shows to this, so that the bar's buttons
    /// stay in the window when many parts fill it.
    /// </summary>
    public float TopBarRoomFor(Control control)
    {
        // The bar itself is never narrower than what is in it, so the window is what is measured.
        var others = _topBar.GetCombinedMinimumSize().X - control.GetCombinedMinimumSize().X;
        return Mathf.Max(0f, _topBar.GetViewportRect().Size.X - others);
    }

    /// <summary>Shows a sentence to the player in the message line. It goes away by itself.</summary>
    /// <param name="message">The sentence.</param>
    /// <param name="seconds">How long it stays.</param>
    public void Say(string message, double seconds = 6.0)
    {
        if (_messages.Count >= MessageLimit)
        {
            Drop(_messages[0].Label);
            _messages.RemoveAt(0);
        }

        var label = MessageLabel(message);
        _messageLines.AddChild(label);
        _messages.Add((label, seconds));
        KeepMessageRoom();

        if (OS.IsStdOutVerbose())
        {
            GD.Print($"Message: {message}");
        }
    }

    /// <summary>Lets every part read the match again. Call it after every tick, after the world view.</summary>
    public void AfterTick()
    {
        _commandsStale = true;
        foreach (var part in _parts)
        {
            part.AfterTick();
        }
    }

    public override void _Process(double delta)
    {
        if (_view is not { } view)
        {
            return;
        }

        foreach (var part in _parts)
        {
            part.EveryFrame(delta);
        }

        if (_commandsStale)
        {
            _commandsStale = false;
            foreach (var (command, button) in _commands)
            {
                button.Visible = command.Applies(view);
            }
        }

        // A full-screen panel covers the bottom panel, which is not shown round its edge.
        _bottom.Visible = !IsPanelOpen && (HasVisibleChild(SelectionArea) || HasVisibleChild(_commandArea));
        FadeMessages(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_view is not { } view)
        {
            return;
        }

        foreach (var panel in _panels)
        {
            if (panel.ToggleAction is { } action && @event.IsActionPressed(action))
            {
                panel.Toggle();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (IsPanelOpen)
        {
            if (@event.IsActionPressed(ClosePanelAction))
            {
                ClosePanel();
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        foreach (var (command, _) in _commands)
        {
            if (command.Action is { } action && @event.IsActionPressed(action) && command.Applies(view))
            {
                Run(command);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    internal void Open(FramePanel panel)
    {
        if (OpenPanel == panel)
        {
            return;
        }

        ClosePanel();
        OpenPanel = panel;
        _panelTitle.Text = panel.Title;
        panel.RaiseOpened();
        panel.Content.Visible = true;
        _panelShade.Visible = true;
        _panelPlace.Visible = true;
        KeepMessageRoom();
        GivePartsTheKeys(false);
    }

    private void Run(SelectionCommand command)
    {
        if (_view is { } view)
        {
            command.Run(view);
            _commandsStale = true;
        }
    }

    // While a full-screen panel is open, a key is the panel's and not the world's. The parts are
    // not asked about it: the controls of the panel take what is theirs before any part would.
    private void GivePartsTheKeys(bool theirs)
    {
        foreach (var part in _parts)
        {
            part.SetProcessUnhandledInput(theirs);
            part.SetProcessUnhandledKeyInput(theirs);
        }
    }

    private void FadeMessages(double delta)
    {
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            var (label, left) = _messages[i];
            left -= delta;
            if (left <= 0.0)
            {
                Drop(label);
                _messages.RemoveAt(i);
                KeepMessageRoom();
                continue;
            }

            label.Modulate = new Color(1f, 1f, 1f, (float)Math.Min(1.0, left / MessageFadeSeconds));
            _messages[i] = (label, left);
        }
    }

    private static Label MessageLabel(string message)
    {
        var label = new Label
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        label.AddThemeStyleboxOverride("normal", UiStyle.PanelStyle(6f));
        return label;
    }

    // Takes a message out of the line at once, and not at the end of the frame, so that the
    // line never holds a message more than it shows.
    private void Drop(Label label)
    {
        _messageLines.RemoveChild(label);
        label.QueueFree();
    }

    // While a full-screen panel is open and no message is shown, an unseen line keeps the room
    // of one message, so that the first message to come does not move the panel.
    private void KeepMessageRoom() => _messageRoom.Visible = IsPanelOpen && _messages.Count == 0;

    // The key an action is given with, as the player knows it, or null when it has none.
    private static string? KeyOf(StringName action)
    {
        if (!InputMap.HasAction(action))
        {
            return null;
        }

        foreach (var input in InputMap.ActionGetEvents(action))
        {
            if (input is InputEventKey key)
            {
                var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
                return OS.GetKeycodeString(code);
            }
        }

        return null;
    }

    private static bool HasVisibleChild(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is Control { Visible: true })
            {
                return true;
            }
        }

        return false;
    }

    private void Build()
    {
        // Nothing of the frame takes a click that is meant for the world, except its panels.
        var root = new Control { Name = "Frame", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Each is drawn over those before it: the bottom panel, then what stands under the top
        // bar, a full-screen panel among it, then the top bar.
        BuildBottomPanel(root);
        BuildUnderBar(root);
        BuildTopBar(root);
    }

    private void BuildTopBar(Control root)
    {
        _topBar = new PanelContainer { Name = "TopBar", CustomMinimumSize = new Vector2(0f, BarHeight) };
        _topBar.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(5f, cornerRadius: 0));
        root.AddChild(_topBar);
        _topBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);

        var barRow = UiStyle.Row(16);
        _topBar.AddChild(barRow);
        TopLeft = UiStyle.Row(16);
        TopLeft.Name = "TopLeft";
        barRow.AddChild(TopLeft);
        barRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _panelButtons = UiStyle.Row(8);
        _panelButtons.Name = "PanelButtons";
        barRow.AddChild(_panelButtons);
        TopRight = UiStyle.Row(12);
        TopRight.Name = "TopRight";
        barRow.AddChild(TopRight);
    }

    private void BuildBottomPanel(Control root)
    {
        _bottom = new PanelContainer { Name = "BottomPanel", Visible = false };
        _bottom.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(10f));
        root.AddChild(_bottom);
        _bottom.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.Minsize, 8);
        _bottom.GrowHorizontal = Control.GrowDirection.Both;
        _bottom.GrowVertical = Control.GrowDirection.Begin;

        var bottomRow = UiStyle.Row(16);
        _bottom.AddChild(bottomRow);
        SelectionArea = UiStyle.Column(4);
        SelectionArea.Name = "SelectionArea";
        SelectionArea.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bottomRow.AddChild(SelectionArea);
        _commandArea = UiStyle.Row(8);
        _commandArea.Name = "Commands";
        bottomRow.AddChild(_commandArea);
    }

    // Under the top bar the message line and the full-screen panel stand in one column, the
    // messages first, so that neither is drawn over the other: the panel begins where they end.
    private void BuildUnderBar(Control root)
    {
        // The shade and the panel's place take every click, so that nothing behind the panel
        // can be selected or ordered.
        _panelShade = new ColorRect { Name = "PanelShade", Color = new Color(0f, 0f, 0f, 0.55f), Visible = false };
        root.AddChild(_panelShade);
        _panelShade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panelShade.OffsetTop = BarHeight;

        var column = UiStyle.Column(0);
        column.Name = "UnderBar";
        column.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        column.OffsetTop = BarHeight;

        var messagePlace = new MarginContainer { Name = "MessagePlace", MouseFilter = Control.MouseFilterEnum.Ignore };
        messagePlace.AddThemeConstantOverride("margin_top", MessageTop);
        column.AddChild(messagePlace);
        _messageLines = UiStyle.Column(4);
        _messageLines.Name = "Messages";
        _messageLines.MouseFilter = Control.MouseFilterEnum.Ignore;
        messagePlace.AddChild(_messageLines);

        // As tall as a message, whatever the font, and never seen.
        _messageRoom = MessageLabel(" ");
        _messageRoom.Name = "MessageRoom";
        _messageRoom.Modulate = Colors.Transparent;
        _messageRoom.Visible = false;
        _messageLines.AddChild(_messageRoom);

        var place = new MarginContainer
        {
            Name = "PanelPlace",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        place.AddThemeConstantOverride("margin_top", MessageBottom);
        foreach (var side in new[] { "margin_left", "margin_right", "margin_bottom" })
        {
            place.AddThemeConstantOverride(side, PanelMargin);
        }

        column.AddChild(place);
        _panelPlace = place;

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(PanelPadding));
        place.AddChild(box);

        var stack = UiStyle.Column(12);
        box.AddChild(stack);

        var header = UiStyle.Row();
        stack.AddChild(header);
        _panelTitle = UiStyle.Title(string.Empty);
        _panelTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(_panelTitle);
        var close = UiStyle.FrameButton(KeyOf(ClosePanelAction) is { } key ? $"Close ({key})" : "Close");
        close.Pressed += ClosePanel;
        header.AddChild(close);

        _panelContent = new MarginContainer { Name = "PanelContent", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        stack.AddChild(_panelContent);
    }
}
