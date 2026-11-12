using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Godot;
using Sim.Core;
using Sim.Data;

namespace Game;

/// <summary>What the inspection panel is showing of structures.</summary>
public enum StructureInspectionMode
{
    /// <summary>Nothing: no structure is selected or inspected.</summary>
    Nothing,

    /// <summary>One structure of the player's own: its tiers, under the line that names it.</summary>
    Own,

    /// <summary>Several structures of the player's own: the tiers they share.</summary>
    Several,

    /// <summary>One structure of another player that the player sees, with its tiers (design 3.2).</summary>
    Seen,

    /// <summary>One structure of another player that the player remembers, as it was when last seen.</summary>
    Remembered,
}

/// <summary>
/// The inspection panel for structures (design 3.2), in the bottom panel. A click on a structure
/// of another player shows it: while the player sees it, with its owner's Protection tier and,
/// for one that attacks, the Firepower tier it attacks with, which is what the design lets
/// anyone with sight know; once it is out of sight, as it was when last seen, and the panel
/// says so. A selected structure of the player's own shows its tiers too.
/// </summary>
/// <remarks>
/// <para>
/// Everything it shows is what the local player may know (design 5.3): it reads a structure
/// through <see cref="StructureFacts.TryRead"/>, and so the tiers of another player's from the
/// rules' own answer to an inspection. The rules give none for a structure the player only
/// remembers. A memory is then shown with what was seen of it: its hit points, how far it was
/// built, the tier its look showed and the track on its banner.
/// </para>
/// <para>
/// Another player's structure has the panel to itself, as another player's unit has. The
/// player's own keeps the short lines of the selection, which name it, and the parts that stand
/// with them, as a queue: its tiers are rows among those lines.
/// </para>
/// <para>
/// A tile of wall of another player's is left to the panel that describes walls. This panel
/// reads the match after every tick and gives no command.
/// </para>
/// </remarks>
public partial class StructureInspection : InterfacePart
{
    // The action a click that selects is given with. Named in the input map of project.godot and
    // read by the unit control, which selects with it. Here it finds a structure of another player.
    private static readonly StringName SelectAction = "unit_select";

    // A press that moves less than this many pixels before its release is a click, as for the unit control.
    private const float ClickSlack = 6f;

    private const float DetailWidth = 560f;

    // Wide enough for the longest track with a tier, so that the rows line up.
    private const float ChipWidth = 126f;

    private readonly InspectedStructureMark? _mark;
    private readonly List<StructureFacts> _structures = [];
    private VBoxContainer _detail = null!;
    private VBoxContainer _rows = null!;
    private HealthBar? _bar;
    private Label? _numbers;
    private Label? _state;
    private string _signature = string.Empty;
    private EntityId _other;
    private bool _pressed;
    private Vector2 _pressedAt;

    /// <param name="mark">The layer that marks the structure of another player the panel is about.</param>
    public StructureInspection(InspectedStructureMark mark) => _mark = mark;

    public StructureInspection()
    {
    }

    public StructureInspectionMode Mode { get; private set; }

    /// <summary>The structures the panel is about, as the player knows them.</summary>
    public IReadOnlyList<StructureFacts> Structures => _structures;

    /// <summary>Every line of text the panel shows now, in reading order, each on a line of its own.</summary>
    public string ShownText()
    {
        var text = new StringBuilder();
        if (Shown() is { } root)
        {
            Collect(root, text);
        }

        return text.ToString();
    }

    /// <summary>
    /// Every line of text the panel would show for a structure, whether it is in the match or
    /// not. The smoke test asks it what a structure shows under tiers that no match of the test
    /// reaches. The panel then shows again what it was showing.
    /// </summary>
    /// <param name="structure">The structure.</param>
    /// <param name="own">True to show it as one of the local player's, and false as another player's.</param>
    public string ShownFor(StructureFacts structure, bool own)
    {
        _structures.Clear();
        _structures.Add(structure);
        Mode = own ? StructureInspectionMode.Own
            : structure.IsRemembered ? StructureInspectionMode.Remembered
            : StructureInspectionMode.Seen;
        Build();
        Present();
        var text = new StringBuilder();
        Collect(own ? _rows : _detail, text);

        // What is built now is not what the signature stands for, so the panel builds again.
        _signature = string.Empty;
        Refresh();
        return text.ToString();
    }

    /// <summary>
    /// Shows a structure of another player, as a click on it does. It is shown while nothing of
    /// the player's own is selected, for as long as the player knows of it (design 5.3).
    /// </summary>
    public void Inspect(EntityId structure)
    {
        _other = structure;
        Refresh();
    }

    /// <summary>
    /// What a click at a point of the screen does here: it shows the structure of another
    /// player that is drawn there, and lets go of the one shown when there is none.
    /// </summary>
    public void ClickAt(Vector2 screen)
    {
        // A unit in front of the structure is what the click is on. A click on anything else
        // lets the structure go, as it clears a selection.
        var target = View.UnderPointer.At(screen, PointerTargets.EnemyUnit | PointerTargets.EnemyStructure);
        Inspect(target.Is(PointerTargets.EnemyStructure) ? target.Entity : EntityId.None);
    }

    protected override void Attach()
    {
        _detail = UiStyle.Column(5);
        _detail.Name = "StructureInspection";
        _detail.Visible = false;
        _detail.CustomMinimumSize = new Vector2(DetailWidth, 0f);
        Frame.SelectionDetail.AddChild(_detail);

        // Under the lines that name a selected structure and say how far it is built, and above
        // whatever else stands in the selection area.
        _rows = UiStyle.Column(3);
        _rows.Name = "StructureTiers";
        _rows.Visible = false;
        var area = Frame.SelectionArea;
        area.AddChild(_rows);
        if ((area.GetNodeOrNull("SelectedFoundation") ?? area.GetNodeOrNull("SelectedStructure")) is { } line)
        {
            area.MoveChild(_rows, line.GetIndex() + 1);
        }

        View.Selection.Changed += Refresh;
    }

    public override void AfterTick() => Refresh();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(SelectAction))
        {
            _pressed = true;
            _pressedAt = GetViewport().GetMousePosition();
        }
        else if (@event.IsActionReleased(SelectAction) && _pressed)
        {
            _pressed = false;
            if (GetViewport().GetMousePosition().DistanceTo(_pressedAt) < ClickSlack)
            {
                ClickAt(_pressedAt);
            }
        }
    }

    private void Refresh()
    {
        _structures.Clear();
        Mode = StructureInspectionMode.Nothing;
        if (View.Selection.Count > 0)
        {
            foreach (var entity in View.Selection.Entities)
            {
                if (!StructureFacts.TryRead(View, entity, out var own) || own.Owner != View.Player)
                {
                    _structures.Clear();
                    break;
                }

                _structures.Add(own);
            }

            Mode = _structures.Count switch
            {
                0 => StructureInspectionMode.Nothing,
                1 => StructureInspectionMode.Own,
                _ => StructureInspectionMode.Several,
            };
        }
        else if (StructureFacts.TryRead(View, _other, out var other) && other.Owner != View.Player && other.Structure.Wall is null)
        {
            _structures.Add(other);
            Mode = other.IsRemembered ? StructureInspectionMode.Remembered : StructureInspectionMode.Seen;
        }
        else
        {
            _other = EntityId.None;
        }

        if (_mark is not null)
        {
            _mark.Structure = Mode is StructureInspectionMode.Seen or StructureInspectionMode.Remembered ? _other : EntityId.None;
        }

        var signature = Signature();
        if (signature != _signature)
        {
            _signature = signature;
            Build();
        }

        Present();
        _detail.Visible = Mode is StructureInspectionMode.Seen or StructureInspectionMode.Remembered;
        _rows.Visible = Mode is StructureInspectionMode.Own or StructureInspectionMode.Several;
    }

    private VBoxContainer? Shown() => _detail.Visible ? _detail : _rows.Visible ? _rows : null;

    // Writes what changes from tick to tick: the hit points, how far the structure is built and
    // how long ago it was seen.
    private void Present()
    {
        if (_structures.Count == 0)
        {
            return;
        }

        var first = _structures[0];
        _bar?.Present(first.HitPoints, first.Maximum);
        if (_numbers is not null)
        {
            _numbers.Text = InspectionWords.Health(first.HitPoints, first.Maximum);
        }

        if (_state is not null)
        {
            _state.Text = StructureInspectionWords.State(in first);
            _state.Visible = _state.Text.Length > 0;
        }
    }

    // Everything the panel is built from but what Present writes: when this changes, it is built again.
    private string Signature()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{Mode}");
        foreach (var structure in _structures)
        {
            // A research that finishes replaces every blueprint of its player, so the blueprint
            // of the attack stands for everything in it.
            var attack = structure.Attack is null ? 0 : RuntimeHelpers.GetHashCode(structure.Attack);
            text.Append(
                CultureInfo.InvariantCulture,
                $"|{structure.Entity},{structure.Owner},{structure.Structure.Index},{structure.Maximum},{structure.IsFinished},{structure.IsRemembered},{structure.LookTier},{structure.Banner},{structure.HasTiers},{structure.Protection},{structure.Firepower},{attack}");
        }

        return text.ToString();
    }

    private void Build()
    {
        Empty(_detail);
        Empty(_rows);
        _bar = null;
        _numbers = null;
        _state = null;
        switch (Mode)
        {
            case StructureInspectionMode.Own:
                AddRows(_rows, _structures[0], own: true);
                break;
            case StructureInspectionMode.Several:
                AddSharedRows();
                break;
            case StructureInspectionMode.Seen:
            case StructureInspectionMode.Remembered:
                BuildDetail(_structures[0]);
                break;
            default:
                break;
        }
    }

    private void BuildDetail(StructureFacts structure)
    {
        var header = UiStyle.Row(8);
        header.Name = "Header";
        _detail.AddChild(header);
        header.AddChild(InspectionUi.Swatch(structure.Owner));
        header.AddChild(InspectionUi.Text(StructureWords.NameOf(structure.Structure), ResearchColors.Ink, 18));
        header.AddChild(InspectionUi.Text(InspectionWords.Owner(structure.Owner), PlayerColors.Of(structure.Owner).Lightened(0.25f), 13));

        // The hit points stand at the right end of the headline, as a unit's do.
        var health = UiStyle.Row(8);
        health.Name = "Health";
        health.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        health.Alignment = BoxContainer.AlignmentMode.End;
        header.AddChild(health);
        _bar = new HealthBar(130f);
        health.AddChild(_bar);
        _numbers = InspectionUi.Text(string.Empty, ResearchColors.Ink, 13);
        health.AddChild(_numbers);

        _state = InspectionUi.Text(string.Empty, structure.IsRemembered ? UiStyle.Warning : ResearchColors.Ink, 13);
        _state.Name = "State";
        _detail.AddChild(_state);

        AddRows(_detail, structure, own: false);

        var last = InspectionUi.Text(
            structure.IsRemembered ? StructureInspectionWords.TiersWhileSeen(in structure) : StructureInspectionWords.NoMobilityNoAccents,
            ResearchColors.Faint,
            13);
        last.Name = "Note";
        _detail.AddChild(last);
    }

    // A row for each tier the player may know of a structure, and one for its banner.
    private void AddRows(Container into, StructureFacts structure, bool own)
    {
        var data = View.Match.Data;
        var roles = data.TrackRoles;
        if (structure.HasTiers)
        {
            into.AddChild(Row(data, "Protection", roles.Protection, structure.Protection, StructureInspectionWords.Protection(data, in structure)));
            if (structure.Attack is not null)
            {
                into.AddChild(Row(data, "Firepower", roles.Firepower, structure.Firepower, StructureInspectionWords.Attack(data, in structure)));
            }
        }

        // The look of a Tower shows its owner's Protection, which the row above has said.
        if (structure.Structure.Track is { } track && structure.LookTier > 0 && !(structure.HasTiers && track == roles.Protection))
        {
            into.AddChild(Row(data, "Look", track, structure.LookTier, StructureInspectionWords.Look(in structure, own)));
        }

        if (structure.Banner >= 0 && structure.Banner < data.Tracks.Count)
        {
            into.AddChild(Row(data, "Banner", structure.Banner, null, StructureInspectionWords.Banner(in structure, own)));
        }
    }

    // What several structures of the player's own share: the Protection tier, which is their
    // owner's, and the Firepower tier of those that attack.
    private void AddSharedRows()
    {
        var data = View.Match.Data;
        var roles = data.TrackRoles;
        var first = _structures[0];
        if (!first.HasTiers)
        {
            return;
        }

        _rows.AddChild(Row(
            data,
            "Protection",
            roles.Protection,
            first.Protection,
            $"{StructureInspectionWords.Fortified(first.Fortification, first.Protection)}, for every structure of yours"));
        foreach (var structure in _structures)
        {
            if (structure.Attack is not null)
            {
                _rows.AddChild(Row(
                    data,
                    "Firepower",
                    roles.Firepower,
                    structure.Firepower,
                    $"{StructureWords.NameOf(structure.Structure)}: {StructureInspectionWords.Attack(data, in structure)}"));
                break;
            }
        }
    }

    // A track in its color, with a tier when there is one to name, and what that means for the structure.
    private static HBoxContainer Row(GameData data, string name, int track, int? tier, string text)
    {
        var color = TrackColors.Of(data, track);
        var style = ResearchColors.Boxed(ResearchColors.Box, 8f, 8f, 1f);
        style.BorderColor = color;
        style.BorderWidthLeft = 3;

        var row = UiStyle.Row(8);
        row.Name = name;
        var chip = new PanelContainer { Name = "Tier", CustomMinimumSize = new Vector2(ChipWidth, 0f) };
        chip.AddThemeStyleboxOverride("panel", style);
        chip.AddChild(InspectionUi.Text(
            tier is { } has ? StructureInspectionWords.Tier(data, track, has) : data.Tracks[track].Name,
            color,
            13));
        row.AddChild(chip);
        row.AddChild(InspectionUi.Text(text, ResearchColors.Ink, 13));
        return row;
    }

    private static void Empty(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            root.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void Collect(Node node, StringBuilder text)
    {
        if (node is Label { Visible: true } label && label.Text.Length > 0)
        {
            text.AppendLine(label.Text);
        }

        foreach (var child in node.GetChildren())
        {
            if (child is not CanvasItem { Visible: false })
            {
                Collect(child, text);
            }
        }
    }
}
