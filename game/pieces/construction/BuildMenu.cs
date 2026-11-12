using Godot;
using Sim.Construction;
using Sim.Core;
using Sim.Data;
using Sim.Map;

namespace Game;

/// <summary>
/// The build menu and the placing of a structure (design 2.5, 5.4). With a worker of the
/// player's own selected, the menu opens over the bottom panel and lists the structures a
/// worker places. Choosing one puts its shape under the pointer, and a click on the ground
/// sends the selected workers the order to build it there.
/// </summary>
/// <remarks>
/// <para>
/// An entry says what the structure costs, and is dark when it cannot be ordered: locked behind
/// research, at the limit of how many the player may have, or beyond what the player has, each
/// as the rules of construction say. The line under the entries says more
/// about the entry under the pointer or the one being placed.
/// </para>
/// <para>
/// While a structure is being placed, the select button places it and the order button goes
/// back to the menu, so neither selects nor orders: this part takes them before the unit
/// control does. The click is taken only for a site the rules accept. Too near an enemy's territory is a warning and
/// not a refusal, because that rule is judged when the worker arrives. This part decides
/// nothing about the match: the order goes through the session like any other, and the
/// simulation judges it again on the tick it runs.
/// </para>
/// <para>
/// What is not placed with one click has an entry of its own kind, a <see cref="BuildTool"/>,
/// which another piece adds: a run of wall tiles is drawn by pressing on its first tile and
/// letting go on its last. While such an entry is chosen the same buttons are the tool's.
/// </para>
/// <para>
/// Everything this part and the preview ask of the rules, they ask through
/// <see cref="BuildChecks"/>, which is where a fogged view gets its answers from the player's
/// own picture of the match in place of the true state.
/// </para>
/// </remarks>
public partial class BuildMenu : InterfacePart
{
    /// <summary>Opens the menu. Named in the input map of project.godot, as are the entries' own actions.</summary>
    public static readonly StringName OpenAction = "build_menu";

    // An entry's action is this and the ID of its structure, as "build_barracks". A structure
    // with no such action in the input map is chosen with the pointer alone.
    private const string EntryActionStart = "build_";

    // The select button places, the key that adds to a selection places more than one, and the
    // order button goes back, so that placing uses the buttons the player already has in hand.
    private static readonly StringName PlaceAction = "unit_select";
    private static readonly StringName PlaceMoreAction = "unit_select_add";
    private static readonly StringName BackAction = "unit_order";

    // The order of the entries, which is the order of the roster in the rules file. A structure
    // that is not named here comes after these.
    private static readonly string[] EntryOrder = ["capitol", "outpost", "generator", "barracks", "works", "academy", "tower"];

    // The entries stand in one row where the window has the width for it, so that the menu
    // covers as little of the world above the bottom panel as it can; in two rows where it has
    // the width for half of them; and in rows of four where it has not.
    private const int FewColumns = 4;
    private const float EntryWidth = 126f;
    private const float EntryHeight = 50f;
    private const int EntryGap = 6;
    private const float PanelPadding = 10f;
    private const float GapAboveBottomPanel = 8f;
    private const float GapBesideWindow = 8f;
    private const float DetailLineHeight = 21f;

    private static readonly Color Chosen = new(0.55f, 1f, 0.55f);

    private readonly PlacementPreview _preview;
    private readonly List<Entry> _entries = [];
    private readonly List<EntityId> _workers = [];
    private PanelContainer _panel = null!;
    private Label _hint = null!;
    private Label _detail = null!;
    private GridContainer _grid = null!;
    private Entry? _placing;
    private Entry? _hovered;
    private Placement? _site;

    /// <summary>Makes the menu, which shows the structure being placed in this layer.</summary>
    public BuildMenu(PlacementPreview preview) => _preview = preview;

    /// <summary>True while the menu is shown.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The structure being placed, or null.</summary>
    public StructureDef? Placing => _placing?.Structure;

    /// <summary>The tool that is chosen, or null.</summary>
    public BuildTool? Tool => _placing?.Tool;

    /// <summary>
    /// The number of structures of the roster the menu lists. Its tools and the structures
    /// another piece added are entries too, and are not counted here.
    /// </summary>
    public int EntryCount => _entries.Count - ToolCount - AddedCount;

    /// <summary>The number of structures another piece added after those of the roster.</summary>
    public int AddedCount { get; private set; }

    /// <summary>
    /// DEVELOPMENT ONLY. What places a structure in place of the selected workers' order, or
    /// null, which is how the game has it. A development aid sets it, to place for a seat
    /// nobody plays. Asked on every click that places; returns true when it placed the
    /// structure, and false to leave the click to the order.
    /// </summary>
    public Func<StructureDef, int, int, bool>? PlacedInstead { get; set; }

    /// <summary>The number of tools the menu lists after its structures.</summary>
    public int ToolCount { get; private set; }

    /// <summary>What the menu last showed as in the way of using a tool, or null for one it does not list.</summary>
    public BuildProblem? ProblemShownFor(BuildTool tool) => EntryOf(tool)?.Problem;

    /// <summary>What the menu's line of advice says now.</summary>
    public string HintShown => _hint.Text;

    /// <summary>
    /// Lists a tool after the structures and the tools added before it. Its key is the action
    /// "build_" and its ID, if the input map has one.
    /// </summary>
    public void AddTool(BuildTool tool)
    {
        var entry = MakeEntry(tool.Id, tool.Name, null, tool);
        _entries.Add(entry);
        _grid.AddChild(entry.Button);
        ToolCount++;
        if (IsOpen)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Lists a structure that is not in the roster, as the gate is not (18b), after the entries
    /// added before it. It is placed with one click, as the roster's are. Its key is the action
    /// "build_" and its ID, if the input map has one. Does nothing for a structure a worker
    /// does not place, and for one that is listed already.
    /// </summary>
    public void AddStructure(StructureDef structure)
    {
        if (!BuildChecks.For(View).Lists(structure) || EntryOf(structure) is not null)
        {
            return;
        }

        var entry = MakeEntry(structure.Id, StructureWords.NameOf(structure), structure, null);
        _entries.Add(entry);
        _grid.AddChild(entry.Button);
        AddedCount++;
        if (IsOpen)
        {
            Refresh();
        }
    }

    /// <summary>The tool of a kind that the menu offers, or null when it has none.</summary>
    public T? ToolOf<T>()
        where T : BuildTool
    {
        foreach (var entry in _entries)
        {
            if (entry.Tool is T tool)
            {
                return tool;
            }
        }

        return null;
    }

    /// <summary>What the menu last showed as in the way of ordering a structure, or null for one it does not list.</summary>
    public BuildProblem? ProblemShownFor(StructureDef structure) => EntryOf(structure)?.Problem;

    /// <summary>True when the selection holds a worker of the local player's, which is when the menu can open.</summary>
    public static bool HasWorker(MatchView view)
    {
        foreach (var entity in view.Selection.Entities)
        {
            if (IsOwnWorker(view, entity))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Shows the menu, if a worker of the player's is selected.</summary>
    public void Open()
    {
        if (IsOpen || !HasWorker(View))
        {
            return;
        }

        IsOpen = true;
        _panel.Visible = true;
        Refresh();
        PlacePanel();
    }

    /// <summary>Hides the menu and stops placing.</summary>
    public void Close()
    {
        LeaveTool();
        IsOpen = false;
        _placing = null;
        _hovered = null;
        _site = null;
        _panel.Visible = false;
        _preview.Clear();
    }

    /// <summary>
    /// Starts placing a structure: its shape follows the pointer from now on. Returns false, and
    /// says why, for a structure that cannot be ordered now.
    /// </summary>
    public bool Choose(StructureDef structure)
    {
        if (!IsOpen || EntryOf(structure) is not { } entry)
        {
            return false;
        }

        var checks = BuildChecks.For(View);
        if (BuildWords.WhyNot(checks, structure, checks.Structure(structure)) is { } why)
        {
            Frame.Say($"{StructureWords.NameOf(structure)}: {why}");
            return false;
        }

        LeaveTool();
        _placing = entry;
        Refresh();
        return true;
    }

    /// <summary>
    /// Chooses a tool: the pointer is the tool's from now on. Returns false, and says why, for
    /// one that cannot be used now.
    /// </summary>
    public bool Choose(BuildTool tool)
    {
        if (!IsOpen || EntryOf(tool) is not { } entry)
        {
            return false;
        }

        if (tool.WhyNot(View, tool.Problem(View)) is { } why)
        {
            Frame.Say($"{tool.Name}: {why}");
            return false;
        }

        LeaveTool();
        _placing = entry;
        _site = null;
        _preview.Clear();
        Refresh();
        return true;
    }

    /// <summary>
    /// Places with the chosen tool as a press at one point of the ground, a drag to another and
    /// a release there do. The points are map x and y in tiles. Returns true when an order was
    /// sent.
    /// </summary>
    /// <param name="from">Where the select button goes down.</param>
    /// <param name="to">Where it is let go.</param>
    /// <param name="more">True to go on with the same tool, false to close the menu.</param>
    public bool PlaceWithTool(Vector2 from, Vector2 to, bool more = false)
    {
        if (_placing?.Tool is not { } tool)
        {
            return false;
        }

        ReadWorkers();
        tool.Point(View, from, _workers);
        tool.Press(View);
        tool.Point(View, to, _workers);
        return ReleaseTool(tool, more);
    }

    /// <summary>
    /// Orders the selected workers to build the structure with its lowest tile at (x, y), as a
    /// click there does. Returns true when the order was sent.
    /// </summary>
    /// <param name="structure">The structure.</param>
    /// <param name="x">The x of the lowest tile of the footprint.</param>
    /// <param name="y">The y of the lowest tile of the footprint.</param>
    /// <param name="more">True to go on placing the same structure, false to close the menu.</param>
    public bool Place(StructureDef structure, int x, int y, bool more = false)
    {
        if (PlacedInstead is { } instead && instead(structure, x, y))
        {
            if (!more)
            {
                Close();
            }

            return true;
        }

        var checks = BuildChecks.For(View);
        var name = StructureWords.NameOf(structure);
        if (BuildWords.WhyNot(checks, structure, checks.Structure(structure)) is { } why)
        {
            Frame.Say($"{name}: {why}");
            StopPlacing();
            return false;
        }

        var site = Placement.Of(checks, structure, x, y);
        if (!site.IsAccepted)
        {
            Frame.Say($"The {name} cannot go there: {BuildWords.SiteProblem(site.Problem)}.");
            return false;
        }

        ReadWorkers();
        if (_workers.Count == 0)
        {
            Close();
            return false;
        }

        var point = MapGrid.TileCenter(x, y);
        if (!View.Client.Submit(new Command(CommandType.Build, View.Player, [.. _workers], point.X, point.Y, contentId: structure.Index)))
        {
            return false;
        }

        if (!more)
        {
            Close();
        }

        return true;
    }

    protected override void Attach()
    {
        BuildPanel();
        Frame.AddCommand(new SelectionCommand
        {
            Label = "Build",
            Action = OpenAction,
            Applies = HasWorker,
            Run = _ => Toggle(),
        });
        View.Selection.Changed += () =>
        {
            if (IsOpen && !HasWorker(View))
            {
                Close();
            }
        };
    }

    public override void AfterTick()
    {
        if (!IsOpen)
        {
            return;
        }

        if (!HasWorker(View))
        {
            Close();
            return;
        }

        Refresh();
        if (_placing is { Problem: not BuildProblem.None })
        {
            // The price went on something else, or the last one allowed is under way.
            StopPlacing();
        }
    }

    public override void EveryFrame(double delta)
    {
        if (!IsOpen)
        {
            return;
        }

        if (Frame.IsPanelOpen)
        {
            Close();
            return;
        }

        if (_placing?.Tool is { } tool)
        {
            // A release over the interface, or while another window had the focus, never arrives.
            if (tool.IsHeld && !Input.IsActionPressed(PlaceAction))
            {
                tool.Back(View);
            }

            ReadWorkers();
            tool.Point(View, GroundUnderPointer(), _workers);
        }

        // While a tool is held, as while a run of wall is drawn, the menu is out of the way of
        // the ground behind it. It is back when the tool is let go or its work is dropped.
        _panel.Visible = _placing?.Tool is not { IsHeld: true };

        _site = _placing?.Structure is { } placing ? SiteUnderPointer(placing) : null;
        if (_placing?.Structure is { } structure && _site is { } site)
        {
            _preview.Show(structure, site);
        }
        else
        {
            _preview.Clear();
        }

        ShowHint();
        ShowDetail();
        PlacePanel();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen)
        {
            return;
        }

        foreach (var entry in _entries)
        {
            if (entry.Action is { } action && @event.IsActionPressed(action))
            {
                ChooseEntry(entry);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (_placing?.Tool is { } tool)
        {
            ToolInput(tool, @event);
        }
        else if (@event.IsActionPressed(InterfaceFrame.ClosePanelAction))
        {
            if (_placing is null)
            {
                Close();
            }
            else
            {
                StopPlacing();
            }

            GetViewport().SetInputAsHandled();
        }
        else if (_placing is not null && @event.IsActionPressed(BackAction))
        {
            StopPlacing();
            GetViewport().SetInputAsHandled();
        }
        else if (_placing?.Structure is { } placing && @event.IsActionPressed(PlaceAction))
        {
            // A click that places nothing is still the menu's: it must not clear the selection.
            if (SiteUnderPointer(placing) is { } site)
            {
                Place(placing, site.X, site.Y, more: Input.IsActionPressed(PlaceMoreAction));
            }

            GetViewport().SetInputAsHandled();
        }
    }

    // The buttons while a tool is chosen: the tool says what each does.
    private void ToolInput(BuildTool tool, InputEvent @event)
    {
        if (@event.IsActionPressed(InterfaceFrame.ClosePanelAction) || @event.IsActionPressed(BackAction))
        {
            if (!tool.Back(View))
            {
                StopPlacing();
            }

            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(PlaceAction))
        {
            // A press that begins nothing is still the menu's: it must not clear the selection.
            ReadWorkers();
            tool.Point(View, GroundUnderPointer(), _workers);
            tool.Press(View);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionReleased(PlaceAction) && tool.IsHeld)
        {
            ReadWorkers();
            tool.Point(View, GroundUnderPointer(), _workers);
            ReleaseTool(tool, Input.IsActionPressed(PlaceMoreAction));
            GetViewport().SetInputAsHandled();
        }
    }

    private bool ReleaseTool(BuildTool tool, bool more)
    {
        ReadWorkers();
        if (_workers.Count == 0)
        {
            Close();
            return false;
        }

        if (!tool.Release(View, _workers))
        {
            return false;
        }

        if (!more)
        {
            Close();
        }

        return true;
    }

    private bool ChooseEntry(Entry entry) =>
        entry.Tool is { } tool ? Choose(tool) : entry.Structure is { } structure && Choose(structure);

    // The tool that was chosen shows nothing more.
    private void LeaveTool() => _placing?.Tool?.Leave(View);

    private static bool IsOwnWorker(MatchView view, EntityId entity) =>
        view.Match.World.Entities.IsAlive(entity)
        && view.Match.Workers.Contains(entity)
        && view.Match.Bodies.TryGet(entity, out var body)
        && body.Owner == view.Player;

    private void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    // Back from placing to the menu.
    private void StopPlacing()
    {
        LeaveTool();
        _placing = null;
        _site = null;
        _preview.Clear();
        if (IsOpen)
        {
            Refresh();
        }
    }

    private void ReadWorkers()
    {
        _workers.Clear();
        foreach (var entity in View.Selection.Entities)
        {
            if (IsOwnWorker(View, entity))
            {
                _workers.Add(entity);
            }
        }

        // The selection keeps no order of its own, and an order must not depend on one.
        _workers.Sort();
    }

    private Entry? EntryOf(StructureDef structure)
    {
        foreach (var entry in _entries)
        {
            if (entry.Structure?.Index == structure.Index)
            {
                return entry;
            }
        }

        return null;
    }

    private Entry? EntryOf(BuildTool tool)
    {
        foreach (var entry in _entries)
        {
            if (entry.Tool == tool)
            {
                return entry;
            }
        }

        return null;
    }

    // The site a click would place the structure at, or null while the pointer is over a part
    // of the interface or not over the ground.
    private Placement? SiteUnderPointer(StructureDef structure) =>
        GroundUnderPointer() is { } ground ? Placement.At(BuildChecks.For(View), structure, ground) : null;

    // The point of the ground under the pointer, as map x and y in tiles, or null while the
    // pointer is over a part of the interface or not over the ground.
    private Vector2? GroundUnderPointer()
    {
        var viewport = GetViewport();
        if (viewport.GuiGetHoveredControl() is not null
            || viewport.GetCamera3D() is not { } camera
            || !UnderPointer.TryGroundPoint(camera, viewport.GetMousePosition(), out var ground))
        {
            return null;
        }

        return ground;
    }

    private void Refresh()
    {
        var checks = BuildChecks.For(View);
        foreach (var entry in _entries)
        {
            var problem = entry.Tool is { } tool ? tool.Problem(View) : checks.Structure(entry.Structure!);
            entry.Problem = problem;
            entry.Button.Disabled = problem != BuildProblem.None;
            entry.Name.AddThemeColorOverride("font_color", problem == BuildProblem.None ? ResearchColors.Ink : ResearchColors.Faint);
            entry.Line.Text = entry.Tool is { } named ? named.EntryLine(View, problem) : BuildWords.EntryLine(checks, entry.Structure!, problem);
            entry.Line.AddThemeColorOverride("font_color", problem == BuildProblem.CannotAfford ? UiStyle.Warning : ResearchColors.Faint);
            entry.Mark.Visible = entry == _placing;
        }
    }

    private void ShowHint()
    {
        var warn = false;
        string text;
        if (_placing is not { } entry)
        {
            text = "Pick a structure to place.";
        }
        else if (entry.Tool is { } tool)
        {
            (text, warn) = tool.Hint(View);
        }
        else if (_site is not { } site)
        {
            text = $"Point at the ground where the {entry.Title} should stand.";
        }
        else if (!site.IsAccepted)
        {
            text = $"It cannot go here: {BuildWords.SiteProblem(site.Problem)}.";
            warn = true;
        }
        else if (site.NearEnemy)
        {
            text = "Its territory would overlap an enemy's: the worker will be turned away unless that changes.";
            warn = true;
        }
        else
        {
            text = "Click to place it. Shift-click places more, one for each selected worker.";
        }

        _hint.Text = text;
        _hint.AddThemeColorOverride("font_color", warn ? UiStyle.Warning : ResearchColors.Faint);
    }

    private void ShowDetail()
    {
        if ((_hovered ?? _placing) is not { } entry)
        {
            _detail.Text = InterfaceFrame.KeyOf(InterfaceFrame.ClosePanelAction) is { } key
                ? $"{key} or a right-click goes back."
                : "A right-click goes back.";
            return;
        }

        if (entry.Tool is { } tool)
        {
            _detail.Text = $"{tool.Name}: {tool.WhyNot(View, entry.Problem) ?? tool.Detail(View)}";
            return;
        }

        // What is in the way of ordering it takes the place of where it may stand, which does
        // not matter until it can be ordered, so that the line stays as short as its room.
        var structure = entry.Structure!;
        var where = structure.TerritoryRadius is { } radius
            ? $"It can stand outside your territory and claims the ground {radius} tiles round it."
            : structure.IsGate ? "In your territory. Open to your units only."
            : "It stands inside your territory.";
        var text = $"{StructureWords.NameOf(structure)}: {BuildWords.Facts(structure)}. {BuildWords.WhyNot(BuildChecks.For(View), structure, entry.Problem) ?? where}";
        _detail.Text = text;
    }

    // Over the bottom panel, wherever that begins now, in the middle of the width that is free
    // there: the window's, less the minimap's while a minimap stands in the corner.
    private void PlacePanel()
    {
        var free = Frame.FreeWidthAboveBottomPanel;
        var beside = 2f * (PanelPadding + GapBesideWindow);
        var oneRow = WidthOf(_entries.Count) + beside <= free;

        // Two rows hold whatever the menu lists while the window has the width for half of it.
        var halved = Math.Max(FewColumns, (_entries.Count + 1) / 2);
        var columns = oneRow ? Math.Max(_entries.Count, 1)
            : WidthOf(halved) + beside <= free ? halved
            : FewColumns;
        if (_grid.Columns != columns)
        {
            // What is said about an entry fits one line under a whole row, and two under a short one.
            _grid.Columns = columns;
            _detail.CustomMinimumSize = new Vector2(WidthOf(columns), DetailLineHeight * (oneRow ? 1 : 2));
        }

        var size = _panel.GetCombinedMinimumSize();
        _panel.Size = size;
        _panel.Position = new Vector2(
            Mathf.Round(Mathf.Max(GapBesideWindow, (free - size.X) / 2f)),
            Mathf.Round(Frame.BottomPanelStart - GapAboveBottomPanel - size.Y));
    }

    private void BuildPanel()
    {
        _panel = new PanelContainer { Name = "BuildMenuPanel", Visible = false };
        _panel.AddThemeStyleboxOverride("panel", UiStyle.PanelStyle(PanelPadding));
        AddChild(_panel);

        var column = UiStyle.Column(6);
        _panel.AddChild(column);

        var header = UiStyle.Row(12);
        column.AddChild(header);
        header.AddChild(new Label { Text = "Build" });
        _hint = InspectionUi.Text(string.Empty, ResearchColors.Faint, 14);
        _hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _hint.HorizontalAlignment = HorizontalAlignment.Right;
        _hint.ClipText = true;
        header.AddChild(_hint);

        _grid = new GridContainer { Columns = FewColumns };
        _grid.AddThemeConstantOverride("h_separation", EntryGap);
        _grid.AddThemeConstantOverride("v_separation", EntryGap);
        column.AddChild(_grid);
        foreach (var structure in ListedStructures())
        {
            var entry = MakeEntry(structure.Id, StructureWords.NameOf(structure), structure, null);
            _entries.Add(entry);
            _grid.AddChild(entry.Button);
        }

        // The same room always, so that the panel does not change height as the pointer moves.
        _detail = InspectionUi.Text(string.Empty, ResearchColors.Faint, 14);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detail.VerticalAlignment = VerticalAlignment.Top;
        _detail.ClipText = true;
        _detail.CustomMinimumSize = new Vector2(WidthOf(FewColumns), DetailLineHeight * 2f);
        column.AddChild(_detail);
    }

    // The width of a row of this many entries.
    private static float WidthOf(int entries) => (entries * EntryWidth) + (Math.Max(entries - 1, 0) * EntryGap);

    // The structures a worker places, in the order of the menu.
    private List<StructureDef> ListedStructures()
    {
        var checks = BuildChecks.For(View);
        var listed = new List<StructureDef>();
        foreach (var structure in View.Match.Data.Structures)
        {
            if (checks.Lists(structure))
            {
                listed.Add(structure);
            }
        }

        // The roster's own order decides between two the menu's order does not name.
        listed.Sort((a, b) => PlaceOf(a).CompareTo(PlaceOf(b)) is var byPlace and not 0 ? byPlace : a.Index.CompareTo(b.Index));
        return listed;

        static int PlaceOf(StructureDef structure)
        {
            var place = Array.IndexOf(EntryOrder, structure.Id);
            return place < 0 ? EntryOrder.Length : place;
        }
    }

    // An entry for a structure, or for a tool.
    private Entry MakeEntry(string id, string name, StructureDef? structure, BuildTool? tool)
    {
        StringName action = EntryActionStart + id;
        var key = InterfaceFrame.KeyOf(action);

        var button = UiStyle.FrameButton(string.Empty);
        button.Name = $"Build{name}";
        button.CustomMinimumSize = new Vector2(EntryWidth, EntryHeight);

        var lines = UiStyle.Column(0);
        lines.MouseFilter = Control.MouseFilterEnum.Ignore;
        lines.Alignment = BoxContainer.AlignmentMode.Center;
        button.AddChild(lines);
        lines.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.Minsize, 8);
        var title = InspectionUi.Text(key is null ? name : $"{name} ({key})", ResearchColors.Ink, 15);
        var line = InspectionUi.Text(string.Empty, ResearchColors.Faint, 13);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        line.MouseFilter = Control.MouseFilterEnum.Ignore;
        lines.AddChild(title);
        lines.AddChild(line);

        // A line round the entry that is being placed.
        var mark = new Panel { Name = "Chosen", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = Chosen };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(4);
        mark.AddThemeStyleboxOverride("panel", style);
        button.AddChild(mark);
        mark.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var entry = new Entry(name, structure, tool, key is null ? null : action, button, title, line, mark);
        button.Pressed += () => ChooseEntry(entry);
        button.MouseEntered += () => _hovered = entry;
        button.MouseExited += () =>
        {
            if (_hovered == entry)
            {
                _hovered = null;
            }
        };
        return entry;
    }

    private sealed class Entry(string title, StructureDef? structure, BuildTool? tool, StringName? action, Button button, Label name, Label line, Panel mark)
    {
        /// <summary>What the entry is called in a sentence.</summary>
        public string Title { get; } = title;

        /// <summary>The structure the entry places, or null for a tool.</summary>
        public StructureDef? Structure { get; } = structure;

        /// <summary>The tool the entry is, or null for a structure.</summary>
        public BuildTool? Tool { get; } = tool;

        public StringName? Action { get; } = action;

        public Button Button { get; } = button;

        public Label Name { get; } = name;

        public Label Line { get; } = line;

        public Panel Mark { get; } = mark;

        public BuildProblem Problem { get; set; }
    }
}
