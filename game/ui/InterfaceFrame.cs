using Godot;

namespace Game;

/// <summary>
/// The frame of the interface round a match. It has five places for the game and two for the
/// aids of development, and it puts nothing of its own into them: every readout, button and
/// panel belongs to an <see cref="InterfacePart"/>. The frame decides where each place stands,
/// so that no two of them cover one another in any window.
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
/// what is selected, <see cref="SelectionDetail"/> for the one part that describes it in full,
/// and beside them a button for every <see cref="SelectionCommand"/> that applies. It is shown
/// while there is something in it.
/// </item>
/// <item>
/// <b>The full-screen panel</b>: one of the panels added with <see cref="AddPanel"/> at a time,
/// over the world and the bottom panel, and under the top bar and the message line. While one
/// is open the pointer and the keys are its own: the camera, selection and orders rest.
/// </item>
/// <item>
/// <b>The rows under the top bar</b>, at its left: <see cref="RowsUnderBar"/> for a row of a
/// part's own that is always at hand, as the row of the control groups is.
/// </item>
/// <item>
/// <b>The message line</b>, under the rows, in the middle: <see cref="Say"/> shows a sentence to
/// the player for a while. A sentence wider than the middle of the window is written on more
/// lines. Nothing of the frame lies under the line: a full-screen panel begins below it, with
/// room kept for one message, so that the panel moves only when two are shown at once.
/// </item>
/// <item>
/// <b>The development aids</b>, in a column at either edge of the window:
/// <see cref="AddDevelopmentAid"/> for what a tester needs and the game does not have, at the
/// left, and <see cref="DevelopmentReadouts"/> for what a developer reads, at the right. See
/// below for where a column may stand.
/// </item>
/// </list>
/// <para>
/// <b>Where the development aids stand.</b> There is one rule, and the frame keeps it, so that an
/// aid cannot land on a panel of the game whatever it holds. A column is
/// <see cref="DevelopmentWidth"/> wide and begins under the rows. It ends where the first thing
/// of the game begins that reaches into its width: the bottom panel, the part in the bottom
/// right corner, and every control a part has named with <see cref="KeepClear"/>. An aid is as
/// wide as its column and as high as it needs. When the aids of a column need more height than
/// the column has, the column scrolls. What the game shows in the middle under the top bar, as
/// the messages are, stays between the columns: <see cref="MiddleWidth"/>.
/// </para>
/// <para>
/// A part that shows a control of its own over the world, outside the places above, names it
/// with <see cref="KeepClear"/>. <see cref="LayoutProblems"/> then says whether anything on the
/// screen covers anything else, which the smoke test asks with the interface at its fullest.
/// </para>
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

    /// <summary>The width of a column of development aids, in the interface's units.</summary>
    public const float DevelopmentWidth = 264f;

    // The room between the edge of the window and what stands at it, and between two things of
    // the frame that stand one over the other.
    private const float EdgeGap = 8f;

    // The rows under the top bar keep the width of the camera's edge-scroll border from the
    // window's edge, so that the pointer on them never scrolls the view.
    private const int RowsLeft = 16;

    private const int MessageLimit = 5;
    private const double MessageFadeSeconds = 1.0;

    private readonly List<InterfacePart> _parts = [];
    private readonly List<(SelectionCommand Command, Button Button)> _commands = [];
    private readonly List<FramePanel> _panels = [];
    private readonly List<(Label Label, double Left, float Width)> _messages = [];
    private readonly List<Control> _clear = [];

    private MatchView? _view;
    private Control _root = null!;
    private Control _rowsPlace = null!;
    private ScrollContainer _aidsColumn = null!;
    private ScrollContainer _readoutsColumn = null!;
    private PanelContainer _bottom = null!;
    private Control? _corner;
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

    /// <summary>
    /// The rows under the top bar, at its left: a part adds a row of its own, which is shown
    /// while it has something in it. The message line stands under the rows, so that a long
    /// message is never written over one. They are not shown while a full-screen panel is open.
    /// </summary>
    public VBoxContainer RowsUnderBar { get; private set; } = null!;

    /// <summary>
    /// The column of the development aids, at the left edge. They are not part of the game, and
    /// each is a box that says so: <see cref="AddDevelopmentAid"/> makes one. The frame decides
    /// where the column stands and how high it is. It is not shown while a full-screen panel is open.
    /// </summary>
    public VBoxContainer DevelopmentAids { get; private set; } = null!;

    /// <summary>
    /// The column at the right edge for what a developer reads and a tester does not need, as
    /// the readout of frame time is. It stands by the same rule as <see cref="DevelopmentAids"/>.
    /// </summary>
    public VBoxContainer DevelopmentReadouts { get; private set; } = null!;

    /// <summary>
    /// The place of the part that describes the selection in full. While it shows something, the
    /// short lines of <see cref="SelectionArea"/> are not shown, because it says what they say:
    /// a piece that knows only its own line about the selection keeps adding it there, and it is
    /// seen where nothing says more.
    /// </summary>
    public VBoxContainer SelectionDetail { get; private set; } = null!;

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
    /// Adds a development aid: a box in the column at the left edge, marked as an aid by its
    /// border and its title. Returns the column inside it, under the title, for the aid to fill.
    /// What it holds is as wide as the box: a line of text that is longer has to wrap.
    /// </summary>
    /// <param name="title">The title, which says that this is an aid and what for.</param>
    /// <param name="about">What the aid does and that it is not part of the game, shown when the pointer rests on the box.</param>
    public VBoxContainer AddDevelopmentAid(string title, string about)
    {
        var box = new PanelContainer { Name = "DevelopmentAid", TooltipText = about };
        var style = UiStyle.PanelStyle(8f);
        style.BorderColor = UiStyle.Warning;
        style.SetBorderWidthAll(1);
        box.AddThemeStyleboxOverride("panel", style);
        DevelopmentAids.AddChild(box);

        var column = UiStyle.Column(4);
        box.AddChild(column);
        var heading = new Label { Text = title, ClipText = true };
        heading.AddThemeFontSizeOverride("font_size", 15);
        heading.AddThemeColorOverride("font_color", UiStyle.Warning);
        column.AddChild(heading);
        return column;
    }

    /// <summary>
    /// Names a control that a part shows over the world by itself, outside the places of the
    /// frame, as a menu over the bottom panel is. The development aids end where it begins, and
    /// <see cref="LayoutProblems"/> knows whose it is.
    /// </summary>
    public void KeepClear(Control control) => _clear.Add(control);

    /// <summary>
    /// The width in the middle of the window that the development aids leave free: all of it but
    /// the gap at either edge while no column of aids is shown. What a part shows in the middle
    /// under the top bar, as a banner, is no wider than this.
    /// </summary>
    public float MiddleWidth => Mathf.Max(0f, _root.Size.X - (2f * Mathf.Max(RoomOf(DevelopmentAids), RoomOf(DevelopmentReadouts))));

    /// <summary>
    /// How far down the middle of the window is free, in the interface's units: to a gap above
    /// the first thing of the game that reaches into it from below, as the bottom panel and a
    /// menu over it do, or to the window's lower edge.
    /// </summary>
    public float MiddleEnd
    {
        get
        {
            var window = _root.Size;
            var side = (window.X - MiddleWidth) / 2f;
            var end = window.Y - EdgeGap;
            foreach (var taken in TakenByTheGame())
            {
                if (taken.End.X > side && taken.Position.X < window.X - side && taken.GetCenter().Y >= window.Y / 2f)
                {
                    end = Mathf.Min(end, taken.Position.Y - EdgeGap);
                }
            }

            return end;
        }
    }

    /// <summary>
    /// The height a part needs under the messages for what it shows there, as the epoch banners
    /// are: zero while it shows nothing. The messages leave that much room above the bottom
    /// panel, by giving up their oldest. The part sets it every frame it shows something.
    /// </summary>
    public float RoomUnderMessages { get; set; }

    /// <summary>Takes every sentence off the message line at once, as the end of a match does.</summary>
    public void DropMessages()
    {
        foreach (var (label, _, _) in _messages)
        {
            Drop(label);
        }

        _messages.Clear();
        KeepMessageRoom();
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

    /// <summary>
    /// How far down the window the message line reaches now, in the interface's units: the top
    /// bar's lower edge when it says nothing. Something a part shows under the bar, over the
    /// world, begins here so that it does not cover a message.
    /// </summary>
    public float MessagesEnd => _messageLines.GetGlobalRect().End.Y;

    /// <summary>
    /// Where the bottom panel begins now, in the interface's units: the lower edge of the window
    /// while the panel is not shown. Something a part shows over the world above the bottom panel
    /// ends here, so that neither covers the other.
    /// </summary>
    public float BottomPanelStart => _bottom.Visible ? _bottom.GetGlobalRect().Position.Y : _bottom.GetViewportRect().Size.Y;

    /// <summary>
    /// The width of the window that is free above the bottom panel, from the left edge: all of
    /// it, or as far as the part in the bottom right corner begins while that is shown.
    /// Something a part shows over the world above the bottom panel stays inside this width, so
    /// that the corner part does not cover it.
    /// </summary>
    public float FreeWidthAboveBottomPanel =>
        _corner is not null && _corner.IsVisibleInTree() ? _corner.GetGlobalRect().Position.X : _bottom.GetViewportRect().Size.X;

    /// <summary>
    /// Names the control that stands in the bottom right corner of the window, over the world
    /// and taller than the bottom panel, as the minimap does: see <see cref="FreeWidthAboveBottomPanel"/>.
    /// </summary>
    public void TakeBottomRightCorner(Control corner) => _corner = corner;

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
        var width = UiStyle.WidthOf(label);
        UiStyle.FitWidth(label, width, MiddleWidth);
        _messages.Add((label, seconds, width));
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

        if (_commandsStale)
        {
            _commandsStale = false;
            foreach (var (command, button) in _commands)
            {
                button.Visible = command.Applies(view);
            }
        }

        // A full-screen panel covers the bottom panel, which is not shown round its edge. The
        // panel is placed before the parts are called, which stand what they show against it.
        var detailed = HasVisibleChild(SelectionDetail);
        SelectionDetail.Visible = detailed;
        SelectionArea.Visible = !detailed;
        _bottom.Visible = !IsPanelOpen && (detailed || HasVisibleChild(SelectionArea) || HasVisibleChild(_commandArea));
        PlaceBottomPanel();
        _rowsPlace.Visible = !IsPanelOpen && RowsUnderBar.GetCombinedMinimumSize().Y > 0f;

        foreach (var part in _parts)
        {
            part.EveryFrame(delta);
        }

        PlaceDevelopmentAids();
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
            var (label, left, width) = _messages[i];
            left -= delta;
            if (left <= 0.0)
            {
                Drop(label);
                _messages.RemoveAt(i);
                KeepMessageRoom();
                continue;
            }

            label.Modulate = new Color(1f, 1f, 1f, (float)Math.Min(1.0, left / MessageFadeSeconds));
            _messages[i] = (label, left, width);
        }

        // A message stays between the columns of the development aids, on as many lines as it needs.
        var middle = MiddleWidth;
        foreach (var (label, _, width) in _messages)
        {
            UiStyle.FitWidth(label, width, middle);
        }

        // The messages end above what the game shows under them, with the room a part has asked
        // for in between. The oldest go first, and the newest stays whatever its height.
        var end = MiddleEnd - RoomUnderMessages;
        while (_messages.Count > 1 && _messageLines.GlobalPosition.Y + _messageLines.GetCombinedMinimumSize().Y > end)
        {
            Drop(_messages[0].Label);
            _messages.RemoveAt(0);
        }
    }


    // The bottom panel stands in the middle of the window's lower edge. Where it would reach the
    // part in the bottom right corner it stands as far left as clears that part.
    private void PlaceBottomPanel()
    {
        if (!_bottom.Visible)
        {
            return;
        }

        var window = _root.Size;
        var size = _bottom.GetCombinedMinimumSize();
        var x = (window.X - size.X) / 2f;
        var end = FreeWidthAboveBottomPanel - EdgeGap;
        if (x + size.X > end)
        {
            x = Mathf.Max(EdgeGap, end - size.X);
        }

        _bottom.Size = size;
        _bottom.Position = new Vector2(Mathf.Round(x), Mathf.Round(window.Y - EdgeGap - size.Y));
    }

    // The one rule for the development aids: see the remarks of the class.
    private void PlaceDevelopmentAids()
    {
        var window = _root.Size;
        var top = (_rowsPlace.Visible ? _rowsPlace.GetGlobalRect().End.Y : BarHeight) + EdgeGap;
        PlaceColumn(_aidsColumn, DevelopmentAids, EdgeGap, top, window);
        PlaceColumn(_readoutsColumn, DevelopmentReadouts, window.X - EdgeGap - DevelopmentWidth, top, window);
    }

    private void PlaceColumn(ScrollContainer column, VBoxContainer aids, float left, float top, Vector2 window)
    {
        column.Visible = !IsPanelOpen && HasVisibleChild(aids);
        if (!column.Visible)
        {
            return;
        }

        // What reaches into the column's width from above pushes its top down, and what reaches
        // into it from below lifts its end.
        var bottom = window.Y - EdgeGap;
        foreach (var taken in TakenByTheGame())
        {
            if (taken.End.X + EdgeGap <= left || taken.Position.X - EdgeGap >= left + DevelopmentWidth)
            {
                continue;
            }

            if (taken.GetCenter().Y < window.Y / 2f)
            {
                top = Mathf.Max(top, taken.End.Y + EdgeGap);
            }
            else
            {
                bottom = Mathf.Min(bottom, taken.Position.Y - EdgeGap);
            }
        }

        var height = Mathf.Clamp(aids.GetCombinedMinimumSize().Y, 0f, Mathf.Max(0f, bottom - top));
        column.Position = new Vector2(left, top);
        column.Size = new Vector2(DevelopmentWidth, Mathf.Floor(height));
    }

    // The rectangles of what the game shows over the world outside the middle of the top: the
    // bottom panel, the corner part and what the parts have named.
    private IEnumerable<Rect2> TakenByTheGame()
    {
        if (_bottom.Visible)
        {
            yield return _bottom.GetGlobalRect();
        }

        if (_corner is not null && IsInstanceValid(_corner) && _corner.IsVisibleInTree())
        {
            yield return _corner.GetGlobalRect();
        }

        foreach (var control in _clear)
        {
            if (IsInstanceValid(control) && control.IsVisibleInTree())
            {
                yield return control.GetGlobalRect();
            }
        }
    }

    // The width a column of development aids takes from the window's edge while it shows something.
    private float RoomOf(VBoxContainer aids) =>
        !IsPanelOpen && HasVisibleChild(aids) ? EdgeGap + DevelopmentWidth + EdgeGap : EdgeGap;

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

    /// <summary>
    /// Every box the interface shows now, with its rectangle in the interface's units: each
    /// panel, button and boxed line of text that is not inside another box. A box that a
    /// scrolling place shows a part of is that part.
    /// </summary>
    public List<(string Name, Rect2 Rect)> Boxes()
    {
        var boxes = new List<(string, Rect2)>();
        foreach (var child in GetChildren())
        {
            CollectBoxes(child, null, boxes);
        }

        return boxes;
    }

    /// <summary>
    /// What is wrong with where the boxes of the interface stand now, a sentence for each: a box
    /// that reaches outside the window, and two boxes of which one lies over the other. While a
    /// full-screen panel is open it lies over the world and what is shown there, and only the
    /// window is asked about. The list is empty when nothing is wrong.
    /// </summary>
    public List<string> LayoutProblems()
    {
        const float Slack = 0.5f;
        var problems = new List<string>();
        var window = new Rect2(Vector2.Zero, _root.Size).Grow(Slack);
        var boxes = Boxes();
        foreach (var (name, rect) in boxes)
        {
            if (!window.Encloses(rect))
            {
                problems.Add($"{name} at {Describe(rect)} reaches outside the window of {_root.Size.X:F0} by {_root.Size.Y:F0}.");
            }
        }

        if (IsPanelOpen)
        {
            return problems;
        }

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var shared = boxes[i].Rect.Intersection(boxes[j].Rect);
                if (shared.Size.X > Slack && shared.Size.Y > Slack)
                {
                    problems.Add($"{boxes[j].Name} at {Describe(boxes[j].Rect)} lies over {boxes[i].Name} at {Describe(boxes[i].Rect)}.");
                }
            }
        }

        return problems;
    }

    private static string Describe(Rect2 rect) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{rect.Position.X:F0},{rect.Position.Y:F0} to {rect.End.X:F0},{rect.End.Y:F0}");

    private void CollectBoxes(Node node, Rect2? clip, List<(string Name, Rect2 Rect)> boxes)
    {
        if (node is Control control)
        {
            // The shade behind a full-screen panel is not a box: it is the world, darkened.
            if (!control.Visible || control == _panelShade)
            {
                return;
            }

            var rect = control.GetGlobalRect();
            if (IsBox(control))
            {
                var shown = clip is { } inside ? rect.Intersection(inside) : rect;
                if (shown.HasArea())
                {
                    boxes.Add((GetPathTo(control).ToString(), shown));
                }

                return;
            }

            if (control is ScrollContainer || control.ClipContents)
            {
                clip = clip is { } outer ? rect.Intersection(outer) : rect;
            }
        }
        else if (node is not InterfacePart)
        {
            // A popup or a timer of a part is not on the frame's own canvas.
            return;
        }

        foreach (var child in node.GetChildren())
        {
            CollectBoxes(child, clip, boxes);
        }
    }

    // What draws a box round itself: a panel, a button, and a line of text with a box of its own.
    private static bool IsBox(Control control) =>
        control is PanelContainer or Panel or BaseButton
        || (control is Label label && label.HasThemeStyleboxOverride("normal") && label.Text.Trim().Length > 0);

    /// <summary>The key an action is given with, as the player knows it, or null when it has none.</summary>
    public static string? KeyOf(StringName action)
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
        _root = root;

        // Each is drawn over those before it: the bottom panel, the development aids, then what
        // stands under the top bar, a full-screen panel among it, then the top bar.
        BuildBottomPanel(root);
        (_aidsColumn, DevelopmentAids) = BuildDevelopmentColumn(root, "DevelopmentAids");
        (_readoutsColumn, DevelopmentReadouts) = BuildDevelopmentColumn(root, "DevelopmentReadouts");
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

        var bottomRow = UiStyle.Row(16);
        _bottom.AddChild(bottomRow);
        SelectionArea = UiStyle.Column(4);
        SelectionArea.Name = "SelectionArea";
        SelectionArea.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        bottomRow.AddChild(SelectionArea);
        SelectionDetail = UiStyle.Column(4);
        SelectionDetail.Name = "SelectionDetail";
        SelectionDetail.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        SelectionDetail.Visible = false;
        bottomRow.AddChild(SelectionDetail);
        _commandArea = UiStyle.Row(8);
        _commandArea.Name = "Commands";
        bottomRow.AddChild(_commandArea);
    }

    // A column of development aids: its boxes one under another, as wide as the column, in a
    // place that scrolls when the frame can give it less height than they need. The frame sets
    // where it stands in every frame.
    private static (ScrollContainer Column, VBoxContainer Aids) BuildDevelopmentColumn(Control root, string name)
    {
        var column = new ScrollContainer
        {
            Name = $"{name}Column",
            Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        root.AddChild(column);

        // Only the boxes take clicks: between them a click is the world's.
        var aids = UiStyle.Column(8);
        aids.Name = name;
        aids.MouseFilter = Control.MouseFilterEnum.Ignore;
        aids.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        column.AddChild(aids);
        return (column, aids);
    }

    // Under the top bar the rows of the parts, the message line and the full-screen panel stand
    // in one column, in that order, so that none is drawn over another: each begins where the
    // one before it ends.
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

        var rowsPlace = new MarginContainer { Name = "RowsPlace", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        rowsPlace.AddThemeConstantOverride("margin_top", MessageTop);
        rowsPlace.AddThemeConstantOverride("margin_left", RowsLeft);
        rowsPlace.AddThemeConstantOverride("margin_right", RowsLeft);
        column.AddChild(rowsPlace);
        _rowsPlace = rowsPlace;
        RowsUnderBar = UiStyle.Column(4);
        RowsUnderBar.Name = "RowsUnderBar";
        RowsUnderBar.MouseFilter = Control.MouseFilterEnum.Ignore;
        rowsPlace.AddChild(RowsUnderBar);

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
