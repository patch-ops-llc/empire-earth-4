using System.Globalization;
using System.Text;
using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Vision;

namespace Game;

/// <summary>What the wall panel is showing.</summary>
public enum WallInspectionMode
{
    /// <summary>Nothing: the selection is not wall tiles, and no wall of another player is looked at.</summary>
    Nothing,

    /// <summary>One tile of the player's own wall.</summary>
    One,

    /// <summary>Several tiles of the player's own wall.</summary>
    Several,

    /// <summary>One tile of another player's wall, as the player sees or remembers it.</summary>
    Other,
}

/// <summary>
/// The inspection panel for walls, in the bottom panel: what a selected tile of wall is. One
/// tile is shown with its hit points, how far it is built and the wall it is a piece of.
/// Several are shown together. A click on a tile of another player's wall shows that tile as
/// the player knows it.
/// </summary>
/// <remarks>
/// <para>
/// Everything it shows is what the local player may know (design 5.3): it reads a tile from the
/// view's <see cref="PlayerSight"/>, so a tile of another player's that is out of sight is
/// shown as it was last seen, and says so, and one the player has never seen cannot be clicked
/// at all. The wall a tile is a piece of is counted from the pieces the player knows of.
/// </para>
/// <para>
/// It reads the match after every tick and gives no command. The command that cancels a tile
/// that is still building itself is the build menu's, and applies to a selected tile of wall
/// as to any structure.
/// </para>
/// </remarks>
public partial class WallInspection : InterfacePart
{
    // The action a click that selects is given with. Named in the input map of project.godot and
    // read by the unit control, which selects with it. Here it finds a wall of another player.
    private static readonly StringName SelectAction = "unit_select";

    // A press that moves less than this many pixels before its release is a click, as for the unit control.
    private const float ClickSlack = 6f;

    private const float PanelWidth = 460f;

    private readonly StructureLayer _structures;
    private readonly InspectedWallMark? _mark;
    private readonly List<SeenStructure> _tiles = [];
    private VBoxContainer _root = null!;
    private ColorRect _swatch = null!;
    private Label _title = null!;
    private Label _owner = null!;
    private HealthBar _bar = null!;
    private Label _numbers = null!;
    private Label _state = null!;
    private Label _wall = null!;
    private Label _facts = null!;
    private EntityId _other;
    private bool _pressed;
    private Vector2 _pressedAt;

    /// <summary>Makes the panel.</summary>
    /// <param name="structures">The layer that draws structures, which knows which wall tiles are one wall.</param>
    /// <param name="mark">The layer that marks the tile of another player's wall the panel is about, or null.</param>
    public WallInspection(StructureLayer structures, InspectedWallMark? mark = null)
    {
        _structures = structures;
        _mark = mark;
    }

    public WallInspectionMode Mode { get; private set; }

    /// <summary>The tiles the panel is about, as the player knows them.</summary>
    public IReadOnlyList<SeenStructure> Tiles => _tiles;

    /// <summary>Every line of text the panel shows now, in reading order, each on a line of its own.</summary>
    public string ShownText()
    {
        var text = new StringBuilder();
        if (_root.Visible)
        {
            foreach (var label in new[] { _title, _owner, _numbers, _state, _wall, _facts })
            {
                if (label.Visible && label.Text.Length > 0)
                {
                    text.AppendLine(label.Text);
                }
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Shows a tile of another player's wall, as a click on it does. It is shown while nothing
    /// of the player's own is selected, for as long as the player knows of it.
    /// </summary>
    public void Inspect(EntityId tile)
    {
        _other = tile;
        Refresh();
    }

    protected override void Attach()
    {
        _root = UiStyle.Column(4);
        _root.Name = "WallInspection";
        _root.Visible = false;
        _root.CustomMinimumSize = new Vector2(PanelWidth, 0f);
        Frame.SelectionDetail.AddChild(_root);

        var header = UiStyle.Row(8);
        header.Name = "Header";
        _root.AddChild(header);
        _swatch = InspectionUi.Swatch(View.Player);
        header.AddChild(_swatch);
        _title = InspectionUi.Text(string.Empty, ResearchColors.Ink, 18);
        header.AddChild(_title);
        _owner = InspectionUi.Text(string.Empty, ResearchColors.Faint, 13);
        header.AddChild(_owner);

        var health = UiStyle.Row(8);
        health.Name = "Health";
        health.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        health.Alignment = BoxContainer.AlignmentMode.End;
        header.AddChild(health);
        _bar = new HealthBar(130f);
        health.AddChild(_bar);
        _numbers = InspectionUi.Text(string.Empty, ResearchColors.Ink, 13);
        health.AddChild(_numbers);

        _state = InspectionUi.Text(string.Empty, ResearchColors.Ink, 13);
        _wall = InspectionUi.Text(string.Empty, ResearchColors.Faint, 13);
        _facts = InspectionUi.Text(string.Empty, ResearchColors.Faint, 13);
        _facts.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _root.AddChild(_state);
        _root.AddChild(_wall);
        _root.AddChild(_facts);

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
                // A unit in front of the wall is what the click is on. A click on anything
                // else lets the tile go, as it clears a selection.
                var target = View.UnderPointer.At(_pressedAt, PointerTargets.EnemyUnit | PointerTargets.EnemyStructure);
                Inspect(target.Is(PointerTargets.EnemyStructure) ? target.Entity : EntityId.None);
            }
        }
    }

    private void Refresh()
    {
        var match = View.Match;
        _tiles.Clear();
        Mode = WallInspectionMode.Nothing;
        if (View.Selection.Count > 0)
        {
            foreach (var entity in View.Selection.Entities)
            {
                if (!TryReadWall(entity, out var tile) || tile.Owner != View.Player)
                {
                    _tiles.Clear();
                    break;
                }

                _tiles.Add(tile);
            }

            Mode = _tiles.Count switch
            {
                0 => WallInspectionMode.Nothing,
                1 => WallInspectionMode.One,
                _ => WallInspectionMode.Several,
            };
        }
        else if (TryReadWall(_other, out var other) && other.Owner != View.Player)
        {
            _tiles.Add(other);
            Mode = WallInspectionMode.Other;
        }
        else
        {
            _other = EntityId.None;
        }

        if (_mark is not null)
        {
            _mark.Tile = Mode == WallInspectionMode.Other ? (_tiles[0].X, _tiles[0].Y) : null;
        }

        _root.Visible = Mode != WallInspectionMode.Nothing;
        if (Mode != WallInspectionMode.Nothing && match.Walls.Wall is { } wall)
        {
            Present(wall);
        }
    }

    // A tile of wall as the player knows it. False for anything else, and for a tile the player does not know of.
    private bool TryReadWall(EntityId entity, out SeenStructure tile) =>
        View.Sight.TryGetStructure(entity, out tile) && View.Match.WallMap.IsWall(tile.Type);

    private void Present(StructureDef wall)
    {
        var first = _tiles[0];
        long hitPoints = 0;
        long maximum = 0;
        var unfinished = 0;
        var damaged = 0;
        foreach (var tile in _tiles)
        {
            hitPoints += tile.HitPoints;
            maximum += tile.MaxHitPoints;
            unfinished += tile.IsFinished ? 0 : 1;
            damaged += tile.HitPoints < tile.MaxHitPoints ? 1 : 0;
        }

        var name = StructureWords.NameOf(wall);
        _swatch.Color = PlayerColors.Of(first.Owner);
        _title.Text = _tiles.Count == 1 ? name : $"{WallWords.Tiles(_tiles.Count)} of {name.ToLowerInvariant()}";
        _owner.Visible = Mode == WallInspectionMode.Other;
        _owner.Text = InspectionWords.Owner(first.Owner);
        _owner.AddThemeColorOverride("font_color", PlayerColors.Of(first.Owner).Lightened(0.25f));
        _bar.Present(hitPoints, maximum);
        _numbers.Text = InspectionWords.Health(hitPoints, maximum);

        _state.Text = StateOf(wall, first, unfinished, damaged);
        _wall.Visible = _tiles.Count == 1;
        _wall.Text = PieceOf(first);
        _facts.Text = Mode == WallInspectionMode.Other
            ? "No unit walks through it. Units you order past it break it down on their way."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"No unit walks through it, yours neither. Sight {wall.Sight} · {ResearchWords.Price(wall.Materials, wall.Energy)} a tile.");
    }

    private string StateOf(StructureDef wall, in SeenStructure first, int unfinished, int damaged)
    {
        if (_tiles.Count > 1)
        {
            var finished = _tiles.Count - unfinished;
            var parts = new List<string>();
            if (finished > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{finished} finished"));
            }

            if (unfinished > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{unfinished} building {(unfinished == 1 ? "itself" : "themselves")}"));
            }

            if (damaged > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{damaged} damaged"));
            }

            return string.Join(", ", parts) + ".";
        }

        var state = first.IsFinished ? "Finished." : BuildWords.Progress(wall, first.Built) + ".";
        if (first.IsRemembered)
        {
            // What the player remembers may have changed since, or be gone.
            var ago = Math.Max(0, View.Match.World.Tick - first.SeenTick);
            state += $" As you last saw it, {ResearchWords.Duration(ago)} ago.";
        }

        return state;
    }

    // The wall a tile is a piece of, counted from the pieces the player knows of.
    private string PieceOf(in SeenStructure tile)
    {
        var length = _structures.Walls.LengthOfWallAt(tile.Owner, tile.X, tile.Y);
        var whose = Mode == WallInspectionMode.Other ? "that you know of" : "of yours";
        return length <= 1
            ? "It stands alone."
            : string.Create(CultureInfo.InvariantCulture, $"A piece of a wall of {length} tiles {whose}.");
    }
}
