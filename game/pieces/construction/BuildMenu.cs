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
    // covers as little of the world above the bottom panel as it can, and in rows of four where
    // it has not.
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

    /// <summary>The number of entries.</summary>
    public int EntryCount => _entries.Count;

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

        _placing = entry;
        Refresh();
        return true;
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

        _site = _placing is { } entry ? SiteUnderPointer(entry.Structure) : null;
        if (_placing is not null && _site is { } site)
        {
            _preview.Show(_placing.Structure, site);
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
                Choose(entry.Structure);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event.IsActionPressed(InterfaceFrame.ClosePanelAction))
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
        else if (_placing is { } placing && @event.IsActionPressed(BackAction))
        {
            StopPlacing();
            GetViewport().SetInputAsHandled();
        }
        else if (_placing is not null && @event.IsActionPressed(PlaceAction))
        {
            // A click that places nothing is still the menu's: it must not clear the selection.
            if (SiteUnderPointer(_placing.Structure) is { } site)
            {
                Place(_placing.Structure, site.X, site.Y, more: Input.IsActionPressed(PlaceMoreAction));
            }

            GetViewport().SetInputAsHandled();
        }
    }

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
            if (entry.Structure.Index == structure.Index)
            {
                return entry;
            }
        }

        return null;
    }

    // The site a click would place the structure at, or null while the pointer is over a part
    // of the interface or not over the ground.
    private Placement? SiteUnderPointer(StructureDef structure)
    {
        var viewport = GetViewport();
        if (viewport.GuiGetHoveredControl() is not null
            || viewport.GetCamera3D() is not { } camera
            || !UnderPointer.TryGroundPoint(camera, viewport.GetMousePosition(), out var ground))
        {
            return null;
        }

        return Placement.At(BuildChecks.For(View), structure, ground);
    }

    private void Refresh()
    {
        var checks = BuildChecks.For(View);
        foreach (var entry in _entries)
        {
            var problem = checks.Structure(entry.Structure);
            entry.Problem = problem;
            entry.Button.Disabled = problem != BuildProblem.None;
            entry.Name.AddThemeColorOverride("font_color", problem == BuildProblem.None ? ResearchColors.Ink : ResearchColors.Faint);
            entry.Line.Text = BuildWords.EntryLine(checks, entry.Structure, problem);
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
        else if (_site is not { } site)
        {
            text = $"Point at the ground where the {StructureWords.NameOf(entry.Structure)} should stand.";
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

        var structure = entry.Structure;
        var where = structure.TerritoryRadius is { } radius
            ? $"It can stand outside your territory and claims the ground {radius} tiles round it."
            : "It stands inside your territory.";
        var text = $"{StructureWords.NameOf(structure)}: {BuildWords.Facts(structure)}. {where}";
        if (BuildWords.WhyNot(BuildChecks.For(View), structure, entry.Problem) is { } why)
        {
            text += $" {why}";
        }

        _detail.Text = text;
    }

    // Over the bottom panel, in the middle, wherever the bottom panel begins now.
    private void PlacePanel()
    {
        var window = _panel.GetViewportRect().Size;
        var columns = WidthOf(_entries.Count) + (2f * (PanelPadding + GapBesideWindow)) <= window.X ? Math.Max(_entries.Count, 1) : FewColumns;
        if (_grid.Columns != columns)
        {
            // What is said about an entry fits one line under a whole row, and two under a short one.
            _grid.Columns = columns;
            _detail.CustomMinimumSize = new Vector2(WidthOf(columns), DetailLineHeight * (columns > FewColumns ? 1 : 2));
        }

        var size = _panel.GetCombinedMinimumSize();
        _panel.Size = size;
        _panel.Position = new Vector2(
            Mathf.Round((window.X - size.X) / 2f),
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
            var entry = MakeEntry(structure);
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

    private Entry MakeEntry(StructureDef structure)
    {
        StringName action = EntryActionStart + structure.Id;
        var key = InterfaceFrame.KeyOf(action);
        var name = StructureWords.NameOf(structure);

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

        var entry = new Entry(structure, key is null ? null : action, button, title, line, mark);
        button.Pressed += () => Choose(structure);
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

    private sealed class Entry(StructureDef structure, StringName? action, Button button, Label name, Label line, Panel mark)
    {
        public StructureDef Structure { get; } = structure;

        public StringName? Action { get; } = action;

        public Button Button { get; } = button;

        public Label Name { get; } = name;

        public Label Line { get; } = line;

        public Panel Mark { get; } = mark;

        public BuildProblem Problem { get; set; }
    }
}
