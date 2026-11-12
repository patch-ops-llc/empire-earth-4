using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Godot;
using Sim.Core;
using Sim.Data;
using Sim.Tech;

namespace Game;

/// <summary>What the inspection panel is showing.</summary>
public enum InspectionMode
{
    /// <summary>Nothing: no unit is selected or inspected, and the panel is not shown.</summary>
    Nothing,

    /// <summary>One unit of the player's own, in detail.</summary>
    One,

    /// <summary>Several units of the player's own, all of one kind, in detail.</summary>
    Group,

    /// <summary>Units of the player's own of several kinds: a row for each kind.</summary>
    Mixed,

    /// <summary>One unit of another player, with what an opponent may know of it (design 3.2).</summary>
    Enemy,
}

/// <summary>
/// The inspection panel (design 3.2), in the bottom panel: what the selected units are. One unit,
/// or several of one kind, is shown in detail: its chassis, its hit points, each stat with the
/// track tier the chassis uses for it, and its accents with their level and what they do. Units
/// of several kinds are a row each, with hit points, the three tiers behind the stats and the
/// accents. A click on a unit of another player shows that unit, with what the design lets an
/// opponent know: its effective Firepower, Protection and Mobility tiers and its accents.
/// </summary>
/// <remarks>
/// It reads the match after every tick and gives no command. A research that finishes changes
/// the owner's blueprints, and the panel is built again in the tick it does. Between such
/// changes only the hit points are written anew.
/// </remarks>
public partial class InspectionPart : InterfacePart
{
    /// <summary>The most kinds a summary has a row for. The rest are counted in one line.</summary>
    public const int MostRows = 6;

    // The action a click that selects is given with. Named in the input map of project.godot and
    // read by the unit control, which selects with it. Here it finds a unit of another player.
    private static readonly StringName SelectAction = "unit_select";

    // A press that moves less than this many pixels before its release is a click, as for the unit control.
    private const float ClickSlack = 6f;

    private const float DetailWidth = 600f;
    private const float EnemyWidth = 520f;
    private const float WorkerWidth = 420f;

    private readonly InspectedMark? _mark;
    private readonly List<HealthLine> _health = [];
    private List<UnitGroup> _groups = [];
    private VBoxContainer _root = null!;
    private string _signature = string.Empty;
    private EntityId _enemy;
    private bool _pressed;
    private Vector2 _pressedAt;

    /// <param name="mark">The layer that rings the unit of another player the panel is about.</param>
    public InspectionPart(InspectedMark mark) => _mark = mark;

    public InspectionPart()
    {
    }

    public InspectionMode Mode { get; private set; }

    /// <summary>The units the panel is about, as the rows it shows, in their order.</summary>
    public IReadOnlyList<UnitGroup> Groups => _groups;

    /// <summary>Every line of text the panel shows now, in reading order, each on a line of its own.</summary>
    public string ShownText()
    {
        var text = new StringBuilder();
        if (_root.Visible)
        {
            Collect(_root, text);
        }

        return text.ToString();
    }

    /// <summary>
    /// Shows a unit of another player, as a click on it does. It is shown while nothing of the
    /// player's own is selected, until it dies or the player no longer sees it (design 5.3).
    /// </summary>
    public void Inspect(EntityId unit)
    {
        _enemy = unit;
        Refresh();
    }

    protected override void Attach()
    {
        _root = UiStyle.Column(6);
        _root.Name = "Inspection";
        _root.Visible = false;
        Frame.SelectionDetail.AddChild(_root);
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
            var pointer = GetViewport().GetMousePosition();
            if (pointer.DistanceTo(_pressedAt) < ClickSlack)
            {
                // A click on anything else lets the unit go, as it clears a selection.
                var target = View.UnderPointer.At(_pressedAt, PointerTargets.EnemyUnit);
                Inspect(target.Is(PointerTargets.EnemyUnit) ? target.Entity : EntityId.None);
            }
        }
    }

    private void Refresh()
    {
        var match = View.Match;
        if (View.Selection.Count > 0)
        {
            _groups = UnitGroup.Of(match, View.Selection.Entities);
            Mode = _groups.Count switch
            {
                0 => InspectionMode.Nothing,
                1 => _groups[0].Count == 1 ? InspectionMode.One : InspectionMode.Group,
                _ => InspectionMode.Mixed,
            };
        }
        else if (View.Sight.ShowsUnit(_enemy) && UnitFacts.TryRead(match, _enemy, out var enemy) && enemy.Owner != View.Player)
        {
            _groups = [new UnitGroup(enemy, 1, enemy.HitPoints, enemy.Maximum, enemy.HitPoints < enemy.Maximum ? 1 : 0)];
            Mode = InspectionMode.Enemy;
        }
        else
        {
            _enemy = EntityId.None;
            _groups = [];
            Mode = InspectionMode.Nothing;
        }

        if (_mark is not null)
        {
            _mark.Unit = Mode == InspectionMode.Enemy ? _enemy : EntityId.None;
        }

        var signature = Signature();
        if (signature != _signature)
        {
            _signature = signature;
            Build();
        }

        for (var i = 0; i < _health.Count && i < _groups.Count; i++)
        {
            _health[i].Present(_groups[i]);
        }

        _root.Visible = Mode != InspectionMode.Nothing;
    }

    // Everything the panel is built from but the hit points: when this changes, it is built again.
    private string Signature()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{Mode}");
        foreach (var group in _groups)
        {
            var first = group.First;

            // A research that finishes replaces every blueprint of its player, so the blueprint
            // itself stands for everything in it.
            var blueprint = first.Blueprint is null ? 0 : RuntimeHelpers.GetHashCode(first.Blueprint);
            text.Append(CultureInfo.InvariantCulture, $"|{first.Owner},{first.Kind},{group.Count},{blueprint},{first.SpeedPerTick.Raw},{(int)first.Work},{first.Builds ?? -1},{first.WallTilesLeft},{first.AtGenerator},{first.HasHealth}");
            if (first.IsWorker)
            {
                text.Append(CultureInfo.InvariantCulture, $",{ProtectionOf(first.Owner)}");
            }
        }

        return text.ToString();
    }

    private void Build()
    {
        foreach (var child in _root.GetChildren())
        {
            _root.RemoveChild(child);
            child.QueueFree();
        }

        _health.Clear();
        switch (Mode)
        {
            case InspectionMode.One:
            case InspectionMode.Group:
            case InspectionMode.Enemy:
                BuildDetail(_groups[0], Mode == InspectionMode.Enemy);
                break;
            case InspectionMode.Mixed:
                BuildSummary();
                break;
            default:
                break;
        }
    }

    private void BuildDetail(UnitGroup group, bool enemy)
    {
        var data = View.Match.Data;
        var first = group.First;
        _root.CustomMinimumSize = new Vector2(first.Blueprint is null ? WorkerWidth : enemy ? EnemyWidth : DetailWidth, 0f);

        var header = UiStyle.Row(8);
        header.Name = "Header";
        _root.AddChild(header);
        header.AddChild(InspectionUi.Swatch(first.Owner));
        header.AddChild(InspectionUi.Text(InspectionWords.Counted(group), ResearchColors.Ink, 18));
        if (first.Blueprint is { } named)
        {
            header.AddChild(InspectionUi.Text(InspectionWords.LineAndEpoch(data, named), ResearchColors.Faint, 13));
        }

        if (enemy)
        {
            header.AddChild(InspectionUi.Text(InspectionWords.Owner(first.Owner), PlayerColors.Of(first.Owner).Lightened(0.25f), 13));
        }

        // The hit points stand at the right end of the headline, which keeps the panel a row lower.
        if (first.HasHealth)
        {
            var health = UiStyle.Row(8);
            health.Name = "Health";
            health.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            health.Alignment = BoxContainer.AlignmentMode.End;
            header.AddChild(health);
            _health.Add(HealthLine.AddTo(health, barWidth: 130f, withNote: true));
        }

        if (first.Blueprint is not { } blueprint)
        {
            BuildWithoutChassis(first, enemy);
            return;
        }

        var roles = data.TrackRoles;
        var cards = UiStyle.Row(6);
        cards.Name = "Stats";
        _root.AddChild(cards);
        var damage = blueprint.DamagePerSecond > 0 ? InspectionWords.Whole(blueprint.DamagePerSecond) : "None";
        cards.AddChild(InspectionUi.StatCard(data, "Damage a second", damage, blueprint, roles.Firepower, OwnTier(first, roles.Firepower, enemy)));
        cards.AddChild(InspectionUi.StatCard(data, "Hit points", InspectionWords.Whole(first.Maximum), blueprint, roles.Protection, OwnTier(first, roles.Protection, enemy)));
        cards.AddChild(InspectionUi.StatCard(data, "Speed", InspectionWords.Speed(first.SpeedPerTick), blueprint, roles.Mobility, OwnTier(first, roles.Mobility, enemy)));
        if (!enemy)
        {
            // The price and the supply would tell an opponent more than the design lets them know.
            cards.AddChild(InspectionUi.StatCard(data, "Price", InspectionWords.Price(blueprint.Materials, blueprint.Energy), blueprint, roles.Manufacturing, OwnTier(first, roles.Manufacturing, enemy)));
            cards.AddChild(InspectionUi.StatCard(data, "Supply", ResearchWords.Number(blueprint.Supply), blueprint, roles.Logistics, OwnTier(first, roles.Logistics, enemy)));
        }

        var attack = InspectionWords.Attack(blueprint);
        _root.AddChild(InspectionUi.Text(
            string.Create(CultureInfo.InvariantCulture, $"{attack} · Sight {blueprint.Sight}"),
            ResearchColors.Faint,
            13));

        BuildAccents(data, blueprint, enemy);
    }

    private void BuildAccents(GameData data, ChassisBlueprint blueprint, bool enemy)
    {
        if (blueprint.Accents.Count == 0)
        {
            _root.AddChild(InspectionUi.Text(
                enemy ? "No accents." : "No accents. A track that is one or two tiers ahead of this chassis's epoch gives one.",
                ResearchColors.Faint,
                13));
            return;
        }

        foreach (var accent in blueprint.Accents)
        {
            var row = UiStyle.Row(10);
            row.Name = "Accent";
            _root.AddChild(row);
            var name = InspectionUi.AccentName(data, accent, 14);
            name.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
            row.AddChild(name);
            var does = InspectionUi.Text(Sentence(InspectionWords.AccentPerks(data, blueprint, accent)), ResearchColors.Ink, 13);
            does.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            does.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(does);
        }

        // A track that is ahead and gives no accent would look like a mistake without the rule
        // behind it (design 2.3, rule 3): a unit carries the two tracks that are furthest ahead.
        var ahead = AheadWithoutAccent(data, blueprint, enemy);
        if (ahead.Count > 0)
        {
            var also = InspectionUi.Text(
                $"{string.Join(" and ", ahead)} {(ahead.Count == 1 ? "is" : "are")} ahead too. A unit carries two accents at most: its two tracks furthest ahead.",
                ResearchColors.Faint,
                12);
            also.Name = "AheadToo";
            _root.AddChild(also);
        }
    }

    // The tracks ahead of the chassis's epoch that give this unit no accent, of those the viewer may see.
    private static List<string> AheadWithoutAccent(GameData data, ChassisBlueprint blueprint, bool enemy)
    {
        var roles = data.TrackRoles;
        int[] tracks = enemy
            ? [roles.Firepower, roles.Protection, roles.Mobility]
            : [roles.Firepower, roles.Protection, roles.Mobility, roles.Manufacturing, roles.Logistics];
        var ahead = new List<string>();
        if (blueprint.Accents.Count < data.Rules.AccentSlots)
        {
            return ahead;
        }

        foreach (var track in tracks)
        {
            if (blueprint.Offsets[track] > 0 && blueprint.AccentLevel(track) == 0)
            {
                ahead.Add(data.Tracks[track].Name);
            }
        }

        return ahead;
    }

    // A worker, which is not a chassis (design 4.1), or a unit the panel knows nothing more of.
    private void BuildWithoutChassis(UnitFacts unit, bool enemy)
    {
        if (!unit.IsWorker)
        {
            return;
        }

        var worker = View.Match.Data.Resources.Worker;
        var facts = string.Create(CultureInfo.InvariantCulture, $"Speed {InspectionWords.Speed(unit.SpeedPerTick)} · Sight {worker.Sight}");
        if (!enemy)
        {
            facts += string.Create(CultureInfo.InvariantCulture, $" · Supply {worker.Supply}");
        }

        // Where another player means to build is that player's to know.
        var doing = InspectionWords.Work(unit.Work, unit.AtGenerator);
        if (!enemy && unit.Builds is { } builds && View.Match.Data.StructureOf(builds) is { } structure)
        {
            doing = $"On its way to build the {StructureWords.NameOf(structure)}";
        }
        else if (!enemy && unit.WallTilesLeft > 0)
        {
            doing = unit.WallTilesLeft == 1
                ? "Laying a wall: 1 tile to go"
                : string.Create(CultureInfo.InvariantCulture, $"Laying a wall: {unit.WallTilesLeft} tiles to go");
        }

        _root.AddChild(InspectionUi.Text(Mode == InspectionMode.Group ? facts : $"{doing} · {facts}", ResearchColors.Faint, 13));

        var why = enemy
            ? "A worker is not a chassis: it has no tiers and no accents."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"A worker is not a chassis: it has no tiers and no accents. Its hit points follow your Protection, now tier {ProtectionOf(unit.Owner)}.");
        var note = InspectionUi.Text(why, ResearchColors.Faint, 13);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _root.AddChild(note);
    }

    private void BuildSummary()
    {
        var data = View.Match.Data;
        var roles = data.TrackRoles;
        _root.CustomMinimumSize = Vector2.Zero;

        var grid = new GridContainer { Name = "Kinds", Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 5);
        _root.AddChild(grid);
        var units = 0;
        foreach (var group in _groups)
        {
            units += group.Count;
        }

        foreach (var heading in new[] { string.Create(CultureInfo.InvariantCulture, $"{units} units"), "Hit points", "Tiers in use", "Accents" })
        {
            grid.AddChild(InspectionUi.Text(heading, ResearchColors.Faint, 11));
        }

        var rows = Math.Min(_groups.Count, MostRows);
        for (var i = 0; i < rows; i++)
        {
            var group = _groups[i];
            var first = group.First;

            var name = UiStyle.Row(8);
            name.AddChild(InspectionUi.Swatch(first.Owner));
            name.AddChild(InspectionUi.Text(InspectionWords.Counted(group), ResearchColors.Ink, 14));
            grid.AddChild(name);

            var health = UiStyle.Row(8);
            grid.AddChild(health);
            if (first.HasHealth)
            {
                _health.Add(HealthLine.AddTo(health, barWidth: 90f, withNote: false));
            }
            else
            {
                _health.Add(default);
            }

            if (first.Blueprint is not { } blueprint)
            {
                grid.AddChild(InspectionUi.Text(first.IsWorker ? "Not a chassis" : string.Empty, ResearchColors.Faint, 13));
                grid.AddChild(new Control());
                continue;
            }

            var tiers = UiStyle.Row(4);
            tiers.AddChild(InspectionUi.TierChip(data, blueprint, roles.Firepower));
            tiers.AddChild(InspectionUi.TierChip(data, blueprint, roles.Protection));
            tiers.AddChild(InspectionUi.TierChip(data, blueprint, roles.Mobility));
            grid.AddChild(tiers);

            var accents = UiStyle.Row(12);
            foreach (var accent in blueprint.Accents)
            {
                var badge = InspectionUi.AccentName(data, accent, 13);
                badge.TooltipText = Sentence(InspectionWords.AccentPerks(data, blueprint, accent));
                accents.AddChild(badge);
            }

            if (blueprint.Accents.Count == 0)
            {
                accents.AddChild(InspectionUi.Text("None", ResearchColors.Faint, 13));
            }

            grid.AddChild(accents);
        }

        if (_groups.Count > rows)
        {
            var more = 0;
            for (var i = rows; i < _groups.Count; i++)
            {
                more += _groups[i].Count;
            }

            _root.AddChild(InspectionUi.Text(
                string.Create(CultureInfo.InvariantCulture, $"And {more} more of {_groups.Count - rows} other kinds."),
                ResearchColors.Faint,
                13));
        }
    }

    // The owner's own tier in a track, to be shown beside the tier the chassis uses. An
    // opponent's own tiers are not shown: only what its units use (design 3.2).
    private int OwnTier(UnitFacts unit, int track, bool enemy) =>
        enemy ? 0 : View.Match.Research.Tech(unit.Owner).Tiers[track];

    private int ProtectionOf(int player) =>
        View.Match.Research.Tech(player).Tiers[View.Match.Data.TrackRoles.Protection];

    // The rest of a sentence as a sentence.
    private static string Sentence(string rest) =>
        rest.Length == 0 ? rest : char.ToUpperInvariant(rest[0]) + rest[1..] + ".";

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

    // The hit points of one row: the bar, the numbers, and for a group how many are damaged.
    private readonly record struct HealthLine(HealthBar? Bar, Label? Numbers, Label? Note)
    {
        public static HealthLine AddTo(Container row, float barWidth, bool withNote)
        {
            var bar = new HealthBar(barWidth);
            row.AddChild(bar);
            var numbers = InspectionUi.Text(string.Empty, ResearchColors.Ink, 13);
            row.AddChild(numbers);
            Label? note = null;
            if (withNote)
            {
                note = InspectionUi.Text(string.Empty, ResearchColors.Faint, 13);
                row.AddChild(note);
            }

            return new HealthLine(bar, numbers, note);
        }

        public void Present(UnitGroup group)
        {
            if (Bar is null || Numbers is null)
            {
                return;
            }

            Bar.Present(group.HitPoints, group.Maximum);
            Numbers.Text = InspectionWords.Health(group.HitPoints, group.Maximum);
            if (Note is not null)
            {
                Note.Text = group.Count == 1 || group.Damaged == 0
                    ? string.Empty
                    : string.Create(CultureInfo.InvariantCulture, $"{group.Damaged} of {group.Count} damaged");
            }
        }
    }
}
