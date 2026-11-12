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
/// track tier the chassis uses for it, its armor, what an order for it makes, and its accents
/// with their level, what they do and where the unit stands with them now: whether its next
/// attack is charged, when it regains hit points, what would come back if it died (design 2.3).
/// Units of several kinds are a row each, with hit points, the three tiers behind the stats and
/// the accents. A click on a unit of another player shows that unit, with what the design lets
/// an opponent know: its effective Firepower, Protection and Mobility tiers and its accents,
/// and the armor that follows from them.
/// </summary>
/// <remarks>
/// It reads the match after every tick and gives no command. A research that finishes changes
/// the owner's blueprints, and the panel is built again in the tick it does. Between such
/// changes only the hit points and the lines that begin with "Now" are written anew.
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

    /// <summary>
    /// The most width, in the interface's units, that what the panel shows may need. The lines
    /// of sentences wrap, so what sets the width is the headline, the row of cards and the rows
    /// of a summary, which grow with the names in the data. The smoke test asks that none of
    /// them needs more for any chassis, and that a detail this wide and as tall as the tallest
    /// stands clear of everything else in the narrowest window.
    /// </summary>
    public const float WidestShown = 720f;

    // The least widths of a detail: of a unit of the player's own, of another player's unit,
    // which has two cards fewer, and of a worker.
    private const float DetailWidth = 600f;
    private const float EnemyWidth = 520f;
    private const float WorkerWidth = 420f;

    private readonly InspectedMark? _mark;
    private readonly List<HealthLine> _health = [];
    private readonly List<NowLine> _now = [];
    private List<UnitGroup> _groups = [];
    private VBoxContainer _root = null!;
    private string _signature = string.Empty;
    private EntityId _enemy;
    private bool _held;
    private int _mostRows = MostRows;
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
    /// Every line of text the panel would show for units of one kind in detail, the player's own
    /// or one of another player, whether they are in the match or not. The smoke test asks it
    /// what a unit with accents shows, which no match of the test reaches. The panel then shows
    /// again what it was showing.
    /// </summary>
    public string ShownFor(UnitGroup group, bool enemy)
    {
        Show(group, enemy);
        var text = new StringBuilder();
        Collect(_root, text);
        WidthNeeded = NeededByWhatIsBuilt();

        // What is built now is not what the signature stands for, so the panel builds again.
        _signature = string.Empty;
        Refresh();
        return text.ToString();
    }

    /// <summary>
    /// The width that what <see cref="ShownFor"/> last built needs at the least. A detail is
    /// given more than this when its least width is more.
    /// </summary>
    public float WidthNeeded { get; private set; }

    /// <summary>
    /// The width a summary of these kinds needs with a row for every one of them, which is as
    /// much as a summary of any <see cref="MostRows"/> of them can need: each of its columns is
    /// as wide as its widest cell.
    /// </summary>
    public float WidthOfSummary(IReadOnlyList<UnitGroup> kinds)
    {
        _groups = [.. kinds];
        Mode = InspectionMode.Mixed;
        _mostRows = kinds.Count;
        Build();
        Present();
        _mostRows = MostRows;
        var needed = NeededByWhatIsBuilt();

        _signature = string.Empty;
        Refresh();
        return needed;
    }

    /// <summary>
    /// Shows these kinds of units, one kind in detail as <see cref="ShownFor"/> builds it and
    /// several as a summary, and keeps showing them whatever is selected until
    /// <see cref="Release"/>. The smoke test holds the tallest and the widest the panel can
    /// show while it asks the frame whether anything covers anything.
    /// </summary>
    /// <param name="widest">Whether the panel is as wide as <see cref="WidestShown"/>, and not as wide as what it shows needs.</param>
    public void Hold(IReadOnlyList<UnitGroup> kinds, bool widest)
    {
        if (kinds.Count == 1)
        {
            Show(kinds[0], enemy: false);
        }
        else
        {
            _groups = [.. kinds];
            Mode = InspectionMode.Mixed;
            Build();
            Present();
        }

        if (widest)
        {
            _root.CustomMinimumSize = new Vector2(WidestShown, 0f);
        }

        _root.Visible = true;
        _held = true;
    }

    /// <summary>Ends a <see cref="Hold"/>: the panel shows the selection again.</summary>
    public void Release()
    {
        _held = false;
        _signature = string.Empty;
        Refresh();
    }

    private void Show(UnitGroup group, bool enemy)
    {
        _groups = [group];
        Mode = enemy ? InspectionMode.Enemy : group.Count == 1 ? InspectionMode.One : InspectionMode.Group;
        Build();
        Present();
    }

    private float NeededByWhatIsBuilt()
    {
        var needed = 0f;
        foreach (var child in _root.GetChildren())
        {
            if (child is Control control)
            {
                needed = Math.Max(needed, control.GetCombinedMinimumSize().X);
            }
        }

        return needed;
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
        if (_held)
        {
            Present();
            return;
        }

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

        Present();
        _root.Visible = Mode != InspectionMode.Nothing;
    }

    // Writes what changes from tick to tick: the hit points, and where the units stand with their perks.
    private void Present()
    {
        for (var i = 0; i < _health.Count && i < _groups.Count; i++)
        {
            _health[i].Present(_groups[i]);
        }

        // The lines of a detail are about its one group.
        if (_groups.Count > 0)
        {
            foreach (var line in _now)
            {
                line.Present(_groups[0]);
            }
        }
    }

    // Everything the panel is built from but the hit points and where the units stand with
    // their perks: when this changes, it is built again.
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
            text.Append(CultureInfo.InvariantCulture, $"|{first.Owner},{first.Kind},{group.Count},{blueprint},{first.SpeedPerTick.Raw},{(int)first.Work},{first.Builds ?? -1},{first.AtGenerator},{first.HasHealth}");
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
        _now.Clear();
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

        // What an order makes is the owner's to know: it follows from the build time and the price.
        var facts = string.Create(CultureInfo.InvariantCulture, $"{InspectionWords.Attack(blueprint)} · Sight {blueprint.Sight}");
        var line = InspectionUi.Text(enemy ? facts : $"{facts} · {InspectionWords.Order(blueprint)}", ResearchColors.Faint, 13);
        line.Name = "Facts";
        if (!enemy)
        {
            line.TooltipText = InspectionWords.OrderInFull(blueprint);
            line.MouseFilter = Control.MouseFilterEnum.Pass;
        }

        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _root.AddChild(line);

        // The armor follows from the Protection tier and the accents, which an opponent may see.
        if (first.Armor is { } armor)
        {
            var protects = InspectionUi.Text(InspectionWords.Armor(armor), ResearchColors.Faint, 13);
            protects.Name = "Armor";
            protects.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _root.AddChild(protects);
        }

        BuildAccents(data, blueprint, enemy);
    }

    private void BuildAccents(GameData data, ChassisBlueprint blueprint, bool enemy)
    {
        if (blueprint.Accents.Count == 0)
        {
            var none = InspectionUi.Text(
                enemy ? "No accents." : "No accents. A track that is one or two tiers ahead of this chassis's epoch gives one.",
                ResearchColors.Faint,
                13);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _root.AddChild(none);
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
            var lines = UiStyle.Column(0);
            lines.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(lines);
            var does = InspectionUi.Text(Sentence(InspectionWords.AccentPerks(data, blueprint, accent)), ResearchColors.Ink, 13);
            does.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            lines.AddChild(does);

            // Where a unit stands with a perk is in counts no opponent sees: how far it has
            // walked, when it was last damaged, what was paid for it.
            if (!enemy && data.AccentOf(accent.Track) is { } definition)
            {
                AddNowLines(lines, definition.PerksAt(accent.Level), blueprint.Perks);
            }
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
            also.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _root.AddChild(also);
        }
    }

    // A line under an accent for each of its perks that the unit keeps a count for.
    private void AddNowLines(Container lines, PerkSet ofAccent, PerkSet onUnit)
    {
        if (ofAccent.Charge is not null && onUnit.Charge is not null)
        {
            AddNowLine(lines, "Charge", InspectionWords.ChargeNow, group => group.Charged > 0);
        }

        if (ofAccent.Regeneration is not null && onUnit.Regeneration is not null)
        {
            AddNowLine(lines, "Regeneration", InspectionWords.RegenerationNow, group => group.Regaining > 0);
        }

        if (ofAccent.DeathRefund is not null && onUnit.DeathRefund is not null)
        {
            AddNowLine(lines, "Reserves", InspectionWords.RefundNow, group => group.Refund > 0);
        }
    }

    private void AddNowLine(Container lines, string perk, Func<UnitGroup, string> words, Func<UnitGroup, bool> lit)
    {
        var label = InspectionUi.Text(string.Empty, ResearchColors.Faint, 13);
        label.Name = $"Now{perk}";
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lines.AddChild(label);
        _now.Add(new NowLine(label, words, lit));
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
        var doing = !enemy && unit.Builds is { } builds && View.Match.Data.StructureOf(builds) is { } structure
            ? $"On its way to build the {StructureWords.NameOf(structure)}"
            : InspectionWords.Work(unit.Work, unit.AtGenerator);
        var line = InspectionUi.Text(Mode == InspectionMode.Group ? facts : $"{doing} · {facts}", ResearchColors.Faint, 13);
        line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _root.AddChild(line);

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
        grid.AddThemeConstantOverride("h_separation", 10);
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

        var rows = Math.Min(_groups.Count, _mostRows);
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
                _health.Add(HealthLine.AddTo(health, barWidth: 64f, withNote: false));
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

            var accents = UiStyle.Row(8);
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

    // Where the units stand with one perk now, written anew after every tick: lit while the perk
    // is at work or ready, faint otherwise.
    private readonly record struct NowLine(Label Label, Func<UnitGroup, string> Words, Func<UnitGroup, bool> Lit)
    {
        public void Present(UnitGroup group)
        {
            Label.Text = Words(group);
            Label.AddThemeColorOverride("font_color", Lit(group) ? ResearchColors.Good : ResearchColors.Faint);
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
