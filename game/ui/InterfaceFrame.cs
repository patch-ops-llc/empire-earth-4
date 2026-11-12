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
/// stand the buttons of the full-screen panels.
/// </item>
/// <item>
/// <b>The bottom panel</b>, for the selection and its commands: <see cref="SelectionArea"/> for
/// what is selected, and beside it a button for every <see cref="SelectionCommand"/> that applies.
/// It is shown while there is something in it.
/// </item>
/// <item>
/// <b>The full-screen panel</b>: one of the panels added with <see cref="AddPanel"/> at a time,
/// over the world and under the top bar. While one is open the pointer and the keys are its own:
/// the camera, selection and orders rest.
/// </item>
/// <item><b>The message line</b>, under the top bar: <see cref="Say"/> shows a sentence to the player for a while.</item>
/// </list>
/// </remarks>
public partial class InterfaceFrame : CanvasLayer
{
    /// <summary>Closes the full-screen panel that is open. Named in the input map of project.godot.</summary>
    public static readonly StringName ClosePanelAction = "interface_close_panel";

    // The height of the top bar, which the message line and the full-screen panel start under.
    private const float BarHeight = 44f;

    private const int MessageLimit = 5;
    private const double MessageFadeSeconds = 1.0;

    private readonly List<InterfacePart> _parts = [];
    private readonly List<(SelectionCommand Command, Button Button)> _commands = [];
    private readonly List<FramePanel> _panels = [];
    private readonly List<(Label Label, double Left)> _messages = [];

    private MatchView _view = null!;
    private PanelContainer _bottom = null!;
    private HBoxContainer _commandArea = null!;
    private HBoxContainer _panelButtons = null!;
    private VBoxContainer _messageLines = null!;
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
        part.Begin(_view);
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
            _panelPlace.Visible = false;
        }
    }

    /// <summary>Shows a sentence to the player in the message line. It goes away by itself.</summary>
    /// <param name="message">The sentence.</param>
    /// <param name="seconds">How long it stays.</param>
    public void Say(string message, double seconds = 6.0)
    {
        if (_messages.Count >= MessageLimit)
        {
            _messages[0].Label.QueueFree();
            _messages.RemoveAt(0);
        }

        var label = new Label
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeStyleboxOverride("normal", UiStyle.PanelStyle(6f));
        label.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _messageLines.AddChild(label);
        _messages.Add((label, seconds));

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
        if (_view is null)
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
                button.Visible = command.Applies(_view);
            }
        }

        _bottom.Visible = HasVisibleChild(SelectionArea) || HasVisibleChild(_commandArea);
        FadeMessages(delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_view is null)
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
            if (command.Action is { } action && @event.IsActionPressed(action) && command.Applies(_view))
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
        _panelPlace.Visible = true;
    }

    private void Run(SelectionCommand command)
    {
        command.Run(_view);
        _commandsStale = true;
    }

    private void FadeMessages(double delta)
    {
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            var (label, left) = _messages[i];
            left -= delta;
            if (left <= 0.0)
            {
                label.QueueFree();
                _messages.RemoveAt(i);
                continue;
            }

            label.Modulate = new Color(1f, 1f, 1f, (float)Math.Min(1.0, left / MessageFadeSeconds));
            _messages[i] = (label, left);
        }
    }

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

        BuildPanelPlace(root);

        _messageLines = UiStyle.Column(4);
        _messageLines.Name = "Messages";
        _messageLines.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(_messageLines);
        _messageLines.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _messageLines.OffsetTop = BarHeight + 8f;

        var bar = new PanelContainer { Name = "TopBar", CustomMinimumSize = new Vector2(0f, BarHeight) };
        bar.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(5f, cornerRadius: 0));
        root.AddChild(bar);
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);

        var barRow = UiStyle.Row(16);
        bar.AddChild(barRow);
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

    private void BuildPanelPlace(Control root)
    {
        // The shade takes every click, so that nothing behind the panel can be selected or ordered.
        var shade = new ColorRect { Name = "PanelPlace", Color = new Color(0f, 0f, 0f, 0.55f), Visible = false };
        root.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        shade.OffsetTop = BarHeight;
        _panelPlace = shade;

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(side, 24);
        }

        shade.AddChild(margin);

        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(18f));
        margin.AddChild(box);

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
