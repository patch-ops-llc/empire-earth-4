using Godot;
using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Production;
using Sim.Research;
using Sim.Structures;

namespace Game;

/// <summary>
/// The smoke test's perks: about a minute and a half of play on the open ground south of the
/// ridge's middle, in which each of the four perks the view shows is brought about and the view
/// is asked what it showed. The part fails when a perk is not reached, when the view does not
/// show one the local player may know of, or when it shows one the player may not know of.
/// </summary>
/// <remarks>
/// <para>
/// The two seats are given tiers with the development command, since no match of the smoke test
/// is long enough to research them, and the local player's Logistics through the development
/// aid of this piece, as a tester raises it: the local player Manufacturing and Logistics, for Reserves
/// and Regeneration, and later Firepower and Mobility, for splash and Charge; the other seat
/// Manufacturing and Mobility, for Reserves and Charge.
/// </para>
/// <list type="bullet">
/// <item>Each seat gets a Barracks with the development placement and trains a pair of units in
/// it, since only a unit that was paid for gives a refund.</item>
/// <item>The other seat's pair, with two more, walks at the local pair and kills it. Their first
/// attacks are charged, in the local player's sight, and must be shown. Each of the local pair
/// gives a refund, which must be shown where it fell and in the top bar.</item>
/// <item>Local units made with the development spawn kill those four. The other seat's pair gives
/// its refunds in the local player's sight, and nothing of them may be shown. A unit nobody paid
/// for gives none.</item>
/// <item>The local survivors heal after five seconds. Each that is reported healing must carry
/// the mark. The part waits until one of them is whole again, and from here to its end no unit
/// may carry the mark that is whole, or that was not reported healing within the last second.</item>
/// <item>The local player is given Firepower and Mobility, and the survivors walk at a group of
/// the other seat's. On the way each whose charge is ready must carry the mark of the run-up.
/// Their charged attacks, the splashes and those the splashes reach must all be shown.</item>
/// <item>A unit of the other seat's that fires shells walks up to the local Barracks and shells
/// it from further than the Barracks sees. Its first shell is charged, and the local player does
/// not know: the shell must fly without the tail of a charged shot and land like any other.</item>
/// </list>
/// <para>
/// The part holds whatever ran before it, as long as nobody stands near its ground: it fails at
/// once, and says so, when a unit does. It needs <c>debug.spawn</c> on in
/// <c>game/data/rules.jsonc</c>, like the fight. It ends with the other seat's units dead and
/// the local survivors standing where the last of them fell.
/// </para>
/// </remarks>
public sealed class PerkSmoke : SmokePart
{
    private const int Length = 2400;

    // The tier two above a chassis of the first epoch, which gives level II of a track's accent,
    // and the tier one above, which gives level I.
    private const int LevelTwo = 3;
    private const int LevelOne = 2;

    // The ground the part uses, on ridge: the plain south of the middle, between the two bases.
    // The local Barracks in the west, the other seat's in the east, the first fight between
    // them, the second south of it, and the unit that fires shells from the south west.
    private const int LocalBarracksX = 27;
    private const int OtherBarracksX = 46;
    private const int BarracksY = 57;
    private const int MeetX = 41;
    private const int MeetY = 58;
    private const int SecondX = 34;
    private const int SecondY = 72;
    private const int ShellsX = 20;
    private const int ShellsY = 76;

    // Nobody may stand between these lines when the part begins: its ground and what is in
    // sight of it.
    private const int West = ShellsX - 6;
    private const int East = OtherBarracksX + 12;
    private const int North = BarracksY - 4;
    private const int South = ShellsY + 10;

    // The other seat's units beside its pair in the first fight, the local units of the second,
    // and the other seat's group they then walk at.
    private const int Extras = 2;
    private const int Avengers = 8;
    private const int Targets = 5;

    // How far short of the other seat's group the local units stop before they attack: further
    // than that group sees.
    private const int Short = 10;

    // How long a step may take before the part gives up on it, in ticks.
    private const int Patience = 900;

    private readonly HashSet<EntityId> _before = [];
    private readonly Dictionary<EntityId, int> _healedAt = [];
    private readonly Dictionary<EntityId, int> _reports = [];
    private bool _mended;
    private Step _step;
    private int _stepAt;
    private int _other;
    private ChassisDef? _paid;
    private ChassisDef? _melee;
    private ChassisDef? _shellsChassis;
    private StructureDef? _barracks;
    private int _logisticsBefore;
    private EntityId _localBarracks;
    private EntityId _otherBarracks;
    private EntityId[] _localPair = [];
    private EntityId[] _otherPair = [];
    private EntityId[] _otherGroup = [];
    private EntityId[] _avengers = [];
    private EntityId[] _dummies = [];
    private EntityId _shells;
    private Shown _base;
    private Counts _counts;
    private int _reactionsBefore;
    private int _marksOnTheWay;
    private bool _onTheWay;
    private int _tails;
    private int _chargedHitsBefore;
    private int _chargesBeforeWalk;
    private int _otherChargesKnownAtFirst;
    private int _unknownChargesBefore;
    private int _otherRefundsBefore;

    public override string Name => "Perks";

    public override int Ticks => Length;

    public override void GiveOrders(int tick, SmokeOrders orders)
    {
        if (Layers.Of(orders) is not { } layers)
        {
            orders.Fail("the view has no layer for the effects of perks, for the marks of Charge and of healing, or for refunds, or no part for the refund in the top bar or for the development tier.");
            return;
        }

        if (tick == 0)
        {
            Begin(orders, layers);
            return;
        }

        if (!Count(orders) || !ShownIsKnown(orders, layers) || (_step >= Step.Healing && !MarksOfHealingHold(tick, orders, layers)))
        {
            return;
        }

        switch (_step)
        {
            case Step.Laid:
                Train(tick, orders);
                break;
            case Step.Training:
                Trained(tick, orders);
                break;
            case Step.FirstFight:
                FirstFight(tick, orders, layers);
                break;
            case Step.SecondFight:
                SecondFight(tick, orders, layers);
                break;
            case Step.Healing:
                Healing(tick, orders);
                break;
            case Step.Tiers:
                MakeTargets(tick, orders);
                break;
            case Step.RunUp:
                RunUp(tick, orders, layers);
                break;
            case Step.Shelled:
                Shelled(tick, orders, layers);
                break;
            case Step.Hunt:
                Hunt(tick, orders);
                break;
            default:
                break;
        }

        if (!orders.Failed && _step != Step.Done && (tick - _stepAt > Patience || tick == Length - 1))
        {
            orders.Fail($"the part is still at its step '{_step}', which began at its tick {_stepAt}, at its tick {tick}. If a fight or a training takes longer now, the part needs more ticks.");
        }
    }

    private void Begin(SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        var data = match.Data;
        if (!data.Rules.Debug.Spawn || match.Players.Count < 2)
        {
            orders.Fail("the development spawn is off in the rules, or the match has one seat. The part needs both to give two seats their tiers, their Barracks and their units.");
            return;
        }

        // The pairs are of the chassis of the first epoch that costs the least, Energy first,
        // since a stockpile may have none left by now. The rest fight hand to hand.
        _paid = data.Chassis
            .OrderBy(chassis => chassis.Epoch)
            .ThenBy(chassis => LineOf(data, chassis).Base.Energy)
            .ThenBy(chassis => LineOf(data, chassis).Base.Materials)
            .ThenBy(chassis => chassis.Index)
            .FirstOrDefault();
        _melee = DevelopmentSpawn.FirstChassis(data, chassis => chassis.Attack.Kind == AttackKind.Melee && LineOf(data, chassis).Base.Spacing.ToFraction() <= Fraction.One);
        _shellsChassis = DevelopmentSpawn.FirstChassis(data, chassis => chassis.Attack.Kind == AttackKind.Ranged && chassis.Attack.BlastRadius is not null);
        _barracks = _paid is null ? null : data.Structures.FirstOrDefault(structure => structure.Trains.Contains(_paid.Line));
        if (_paid is null || _melee is null || _shellsChassis is null || _barracks is null)
        {
            orders.Fail("the data has no chassis, no melee chassis, no chassis that fires shells, or no structure that trains the cheapest chassis.");
            return;
        }

        // Nobody near: what the part counts must be its own, and nobody of the local player's
        // may see the unit that shells the Barracks.
        for (var i = 0; i < match.Bodies.Count; i++)
        {
            var position = WorldView.ToWorld(match.Bodies.ValueAt(i).Position);
            if (position.X > West && position.X < East && position.Z > North && position.Z < South)
            {
                orders.Fail(
                    $"{match.Bodies.EntityAt(i)} stands at {position}, on the ground the part uses or too close to it: from ({West}, {North}) to ({East}, {South}). " +
                    "The part needs other ground, or the part before it must leave this ground alone.");
                return;
            }
        }

        if (!match.HasRoomFor(_barracks, LocalBarracksX, BarracksY) || !match.HasRoomFor(_barracks, OtherBarracksX, BarracksY))
        {
            orders.Fail("a Barracks has no room where the part lays it. If the map changed, the part needs another place.");
            return;
        }

        for (var i = 0; i < match.Combatants.Count; i++)
        {
            _before.Add(match.Combatants.EntityAt(i));
        }

        _other = (orders.Player + 1) % match.Players.Count;
        _base = Shown.Of(layers);
        _reactionsBefore = layers.Readout.Reactions;

        var roles = data.TrackRoles;
        SetTier(orders, orders.Player, roles.Manufacturing, LevelTwo);

        // The local player's Logistics goes up the way a tester raises it: the track chosen in
        // the list of the development aid, and what the aid's key does.
        _logisticsBefore = match.Research.State.Tier(orders.Player, roles.Logistics);
        layers.Aid.Choose(roles.Logistics);
        if (_logisticsBefore < LevelOne && !layers.Aid.Press(DevelopmentTier.RaiseOwnAction))
        {
            orders.Fail("the development aid that raises a track sent no command for the local player's Logistics at its key.");
            return;
        }

        SetTier(orders, _other, roles.Manufacturing, LevelTwo);
        SetTier(orders, _other, roles.Mobility, LevelTwo);
        Place(orders, orders.Player, LocalBarracksX);
        Place(orders, _other, OtherBarracksX);
        To(Step.Laid, 0);
    }

    private void Train(int tick, SmokeOrders orders)
    {
        if (tick < _stepAt + 5)
        {
            return;
        }

        var match = orders.Match;
        var logistics = match.Research.State.Tier(orders.Player, match.Data.TrackRoles.Logistics);
        if (logistics < LevelOne)
        {
            orders.Fail(
                $"the local player's Logistics is at tier {logistics} five ticks after the key of the development aid was to raise it from tier {_logisticsBefore}. " +
                "The aid sent a command that raised nothing, or another track than the one in its list.");
            return;
        }

        _localBarracks = StructureAt(orders, LocalBarracksX);
        _otherBarracks = StructureAt(orders, OtherBarracksX);
        if (_localBarracks == EntityId.None || _otherBarracks == EntityId.None)
        {
            orders.Fail("a Barracks is not in the match five ticks after it was ordered.");
            return;
        }

        foreach (var (player, barracks) in new[] { (orders.Player, _localBarracks), (_other, _otherBarracks) })
        {
            var perks = match.Research.Tech(player).Blueprints[_paid!.Index].Perks;
            var check = match.Production.CheckTrain(player, barracks, _paid.Index);
            var made = match.Production.CountOf(player, _paid.Index);
            if (perks.DeathRefund is null || perks.Batch is null || check != TrainCheck.Allowed || made != 2)
            {
                orders.Fail(
                    $"player {player} cannot train a pair of {_paid.Id} with Reserves: Reserves {perks.DeathRefund is not null}, Batch {perks.Batch is not null}, " +
                    $"the order is '{check}' and would make {made}, with {match.Stockpiles.Materials(player) / Sim.Economy.Stockpiles.PerResource} Materials. " +
                    "The part needs the tiers it set, and a stockpile and supply that cover two.");
                return;
            }

            orders.SubmitFor(new Command(CommandType.Train, player, [barracks], contentId: _paid.Index));
        }

        To(Step.Training, tick);
    }

    private void Trained(int tick, SmokeOrders orders)
    {
        var match = orders.Match;
        _localPair = [.. NewUnits(orders, orders.Player)];
        _otherPair = [.. NewUnits(orders, _other)];
        if (_localPair.Length < 2 || _otherPair.Length < 2)
        {
            return;
        }

        if (_localPair.Concat(_otherPair).Any(unit => match.PerkCounts.Get(unit).Paid <= Fraction.Zero))
        {
            orders.Fail("a unit that came out of a Barracks has nothing paid for it, so it would give no refund.");
            return;
        }

        // The other seat's pair and a few more walk at the local pair, which goes to meet them
        // away from its Barracks, so that they do not go on to the Barracks afterwards.
        for (var i = 0; i < Extras; i++)
        {
            Spawn(orders, _other, _melee!, OtherBarracksX - 2, BarracksY + i);
        }

        orders.Submit(new Command(CommandType.Move, orders.Player, _localPair, MiddleOf(MeetX - 2), MiddleOf(MeetY)));
        To(Step.FirstFight, tick);
    }

    private void FirstFight(int tick, SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        if (tick == _stepAt + 5)
        {
            _otherGroup = [.. NewUnits(orders, _other)];
            if (_otherGroup.Length != 2 + Extras)
            {
                orders.Fail($"the other seat has {_otherGroup.Length} units of the part and not its pair and {Extras} more, five ticks after those were ordered.");
                return;
            }

            orders.SubmitFor(new Command(CommandType.AttackMove, _other, _otherGroup, MiddleOf(MeetX - 2), MiddleOf(MeetY)));
            return;
        }

        if (tick < _stepAt + 5)
        {
            return;
        }

        _tails += layers.Effects.ChargedShotCount;
        if (_localPair.Any(match.World.Entities.IsAlive))
        {
            return;
        }

        // Both of the local pair are dead: each gave a refund, and both were shown.
        if (_counts.LocalRefunds != 2 || layers.Refunds.RefundsShown - _base.Refunds != 2 || layers.Readout.Reactions == _reactionsBefore)
        {
            orders.Fail(
                $"the local pair died and gave {_counts.LocalRefunds} refunds; the view showed {layers.Refunds.RefundsShown - _base.Refunds} where they fell, " +
                $"and the top bar showed a refund {layers.Readout.Reactions - _reactionsBefore} times. Each of the two must give one, and both must be shown.");
            return;
        }

        // They came a long way: their first attacks were charged, in the local pair's sight.
        _otherChargesKnownAtFirst = _counts.KnownCharges;
        if (_otherChargesKnownAtFirst == 0 || layers.Effects.ChargedHitsShown == _base.ChargedHits)
        {
            orders.Fail(
                $"the other seat's units walked up and killed the local pair, and the local player was told of {_otherChargesKnownAtFirst} charged attacks " +
                $"and shown {layers.Effects.ChargedHitsShown - _base.ChargedHits} landing. At least one must be made in sight, and be shown landing.");
            return;
        }

        // The other seat's pair shoots: its charged shots flew in sight, each with its tail.
        if (!match.Combat.ProfileOf(_other, _paid!.Index).Melee && _tails == 0)
        {
            orders.Fail("the other seat's pair fired charged shots in the local player's sight, and none was drawn with the tail of a charged shot.");
            return;
        }

        // Those who avenge the pair: made west of where it fell, free of charge.
        for (var i = 0; i < Avengers; i++)
        {
            Spawn(orders, orders.Player, _melee!, MeetX - 8 + (i / 4), MeetY - 2 + (i % 4));
        }

        _otherRefundsBefore = _counts.OtherRefunds;
        To(Step.SecondFight, tick);
    }

    private void SecondFight(int tick, SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        if (tick == _stepAt + 5)
        {
            _avengers = [.. NewUnits(orders, orders.Player).Except(_localPair)];
            if (_avengers.Length != Avengers)
            {
                orders.Fail($"the local player has {_avengers.Length} of the {Avengers} units ordered five ticks ago.");
                return;
            }

            orders.Submit(new Command(CommandType.AttackMove, orders.Player, _avengers, MiddleOf(MeetX), MiddleOf(MeetY)));
            return;
        }

        if (tick < _stepAt + 5 || _otherGroup.Any(match.World.Entities.IsAlive))
        {
            return;
        }

        // The other seat's pair died in the local player's sight and gave its refunds, of which
        // the local player is told nothing. The check after every tick has held that nothing of
        // them was shown; here the part makes sure that there was something not to show.
        if (_counts.OtherRefunds - _otherRefundsBefore != 2 || _counts.LocalRefunds != 2)
        {
            orders.Fail(
                $"the other seat's pair died and gave {_counts.OtherRefunds - _otherRefundsBefore} refunds, and the local player's units have given {_counts.LocalRefunds} in all. " +
                "The pair must give two, and the local units that nobody paid for none.");
            return;
        }

        _avengers = Alive(orders, _avengers);
        if (_avengers.Length < 2)
        {
            orders.Fail($"{_avengers.Length} of the local units are alive after the second fight. The part needs two or more to go on.");
            return;
        }

        To(Step.Healing, tick);
    }

    // What holds of the mark of healing in every tick from the second fight's end on: a unit
    // that is whole carries none, and a unit carries it only within a little over a second of
    // a report that it is healing. False when the part has failed.
    private bool MarksOfHealingHold(int tick, SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        foreach (var happened in match.Simulation.Events)
        {
            if (happened.Kind == SimEventKind.Regenerated && Array.IndexOf(_avengers, happened.Entity) >= 0)
            {
                _healedAt[happened.Entity] = tick;
                _reports[happened.Entity] = _reports.GetValueOrDefault(happened.Entity) + 1;
            }
        }

        foreach (var unit in _avengers)
        {
            if (!match.Health.TryGet(unit, out var health))
            {
                continue;
            }

            var marked = layers.Healing.Marks(unit);
            var whole = health.HitPoints >= health.Maximum;
            var since = _healedAt.TryGetValue(unit, out var at) ? tick - at : -1;
            if (marked && (whole || since < 0 || since > Sim.SimInfo.TicksPerSecond + 5))
            {
                orders.Fail($"{unit} has {health.HitPoints} of {health.Maximum} hit points, was last reported healing {since} ticks ago, and carries the mark of healing.");
                return false;
            }

            // Nobody is left to hit them in the step that looks at the healing. So there a
            // unit reported healing within the last second carries the mark until it is whole.
            if (_step == Step.Healing && !marked && !whole && since >= 0 && since <= Sim.SimInfo.TicksPerSecond)
            {
                orders.Fail($"{unit} has {health.HitPoints} of {health.Maximum} hit points, was reported healing {since} ticks ago, and does not carry the mark of healing.");
                return false;
            }

            if (whole && _reports.ContainsKey(unit))
            {
                _mended = true;
            }
        }

        return true;
    }

    private void Healing(int tick, SmokeOrders orders)
    {
        // Three reports for one unit are two seconds of healing, with the mark on it all the
        // while. And the step waits for a unit that was healing to be whole again: the check
        // of every tick has then held that its mark went with its last hit point. The wait is
        // here because the tiers that come next take the accent that heals from these units.
        if (!_mended || !_reports.Values.Any(reports => reports >= 3))
        {
            return;
        }

        var match = orders.Match;
        var roles = match.Data.TrackRoles;
        SetTier(orders, orders.Player, roles.Firepower, LevelTwo);
        SetTier(orders, orders.Player, roles.Mobility, LevelTwo);
        To(Step.Tiers, tick);
    }

    private void MakeTargets(int tick, SmokeOrders orders)
    {
        if (tick < _stepAt + 5)
        {
            return;
        }

        var match = orders.Match;
        var profile = match.Combat.ProfileOf(orders.Player, _melee!.Index);
        if (profile.Charge is null || profile.SplashRadius <= Fixed.Zero)
        {
            orders.Fail("the local player's melee chassis has no Charge or no splash five ticks after the tiers that give them were ordered.");
            return;
        }

        // A group of the other seat's, out of sight to the south, close enough together for a splash to reach.
        for (var i = 0; i < Targets; i++)
        {
            Spawn(orders, _other, _melee!, SecondX + (i % 2), SecondY + (i / 2));
        }

        To(Step.RunUp, tick);
    }

    private void RunUp(int tick, SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        if (tick == _stepAt + 5)
        {
            _dummies = [.. NewUnits(orders, _other).Except(_otherGroup)];
            _avengers = Alive(orders, _avengers);
            if (_dummies.Length != Targets || _dummies.Any(orders.View.Sight.TrulySees))
            {
                orders.Fail($"the other seat has {_dummies.Length} of the {Targets} units ordered five ticks ago, or the local player sees one before the walk.");
                return;
            }

            _chargedHitsBefore = layers.Effects.ChargedHitsShown;
            _chargesBeforeWalk = _counts.KnownCharges;

            // They walk most of the way first, past the other seat's Barracks, which a unit
            // that fights on the way would stop at.
            orders.Submit(new Command(CommandType.Move, orders.Player, _avengers, MiddleOf(SecondX + 1), MiddleOf(SecondY - Short)));
            _onTheWay = true;
            return;
        }

        if (tick < _stepAt + 5)
        {
            return;
        }

        if (_onTheWay && _avengers.All(unit => !match.Combatants.TryGet(unit, out var fighter) || fighter.Order == UnitOrder.Idle))
        {
            _onTheWay = false;
            orders.Submit(new Command(CommandType.AttackMove, orders.Player, _avengers, MiddleOf(SecondX + 1), MiddleOf(SecondY)));
        }

        // The run-up: a unit is marked exactly while its charge is ready.
        var profile = match.Combat.ProfileOf(orders.Player, _melee!.Index);
        foreach (var unit in _avengers)
        {
            if (!match.PerkCounts.TryGet(unit, out var counts) || !match.World.Entities.IsAlive(unit))
            {
                continue;
            }

            var ready = counts.Walked >= profile.Charge!.Distance;
            if (ready != layers.Run.Marks(unit))
            {
                orders.Fail($"{unit} has walked {counts.Walked} tiles since its last attack, of the {profile.Charge.Distance} a charge needs, and carries the mark of the run-up: {layers.Run.Marks(unit)}.");
                return;
            }

            _marksOnTheWay += ready ? 1 : 0;
        }

        if (_dummies.Any(match.World.Entities.IsAlive))
        {
            return;
        }

        var charges = _counts.KnownCharges - _chargesBeforeWalk;
        var landed = layers.Effects.ChargedHitsShown - _chargedHitsBefore;
        GD.Print(
            $"Smoke test, perks: {_counts.LocalRefunds} refunds shown and {_counts.OtherRefunds} of the other seat's not, {_counts.KnownHeals} reports of healing, " +
            $"{_otherChargesKnownAtFirst} charged attacks of the other seat's seen, {charges} of the local player's with {landed} shown landing, " +
            $"{_counts.KnownSplashes} splashes that reached {_counts.KnownSplashHits}, and the run-up marked in {_marksOnTheWay} unit ticks.");
        if (_marksOnTheWay == 0 || charges == 0 || landed != charges)
        {
            orders.Fail($"on the walk the run-up was marked in {_marksOnTheWay} unit ticks, {charges} charged attacks were made and {landed} were shown landing. Each must be marked, made and shown.");
            return;
        }

        if (_counts.KnownSplashes == 0 || _counts.KnownSplashHits == 0)
        {
            orders.Fail($"the fight had {_counts.KnownSplashes} splashes, which reached {_counts.KnownSplashHits} units. The part needs a splash that reaches someone.");
            return;
        }

        // The unit that fires shells, far to the south west of the local Barracks and of everyone.
        _avengers = Alive(orders, _avengers);
        _unknownChargesBefore = _counts.UnknownCharges;
        _chargedHitsBefore = layers.Effects.ChargedHitsShown;
        Spawn(orders, _other, _shellsChassis!, ShellsX, ShellsY);
        To(Step.Shelled, tick);
    }

    private void Shelled(int tick, SmokeOrders orders, Layers layers)
    {
        var match = orders.Match;
        if (tick == _stepAt + 5)
        {
            _shells = NewUnits(orders, _other).Except(_otherGroup).Except(_dummies).FirstOrDefault();
            if (_shells == EntityId.None || match.Combat.ProfileOf(_other, _shellsChassis!.Index).Charge is null)
            {
                orders.Fail("the other seat's unit that fires shells is not in the match five ticks after it was ordered, or it has no Charge.");
                return;
            }

            var middle = _barracks!.Footprint / 2f;
            orders.SubmitFor(new Command(
                CommandType.AttackMove,
                _other,
                [_shells],
                Fixed.FromRaw((int)((LocalBarracksX + middle) * Fixed.RawOne)),
                Fixed.FromRaw((int)((BarracksY + middle) * Fixed.RawOne))));
            return;
        }

        if (tick < _stepAt + 5)
        {
            return;
        }

        // The local player never sees the unit, so its charge is its own: the shell flies and
        // lands like any other.
        if (orders.View.Sight.TrulySees(_shells) || layers.Effects.ChargedShotCount != 0 || layers.Effects.ChargedHitsShown != _chargedHitsBefore)
        {
            orders.Fail(
                $"the local player sees the unit that shells the Barracks: {orders.View.Sight.TrulySees(_shells)}, or a shell is drawn with the tail of a charged shot: {layers.Effects.ChargedShotCount}, " +
                $"or was shown landing as one: {layers.Effects.ChargedHitsShown - _chargedHitsBefore}. The part has it fire from out of sight, and nothing may tell the player of its charge.");
            return;
        }

        var landed = false;
        foreach (var happened in match.Simulation.Events)
        {
            landed |= happened.Kind == SimEventKind.Blast && happened.Player == _other && orders.View.Sight.Seen.Knows(happened);
        }

        if (!landed)
        {
            return;
        }

        if (_counts.UnknownCharges == _unknownChargesBefore)
        {
            orders.Fail("the first shell on the Barracks landed, and it was not fired with Charge. The part has the unit walk further than a charge needs.");
            return;
        }

        // The survivors go and kill it, so that the part leaves no fight behind.
        var place = match.Bodies.Get(_shells).Position;
        orders.Submit(new Command(CommandType.AttackMove, orders.Player, _avengers, place.X, place.Y));
        To(Step.Hunt, tick);
    }

    private void Hunt(int tick, SmokeOrders orders)
    {
        if (orders.Match.World.Entities.IsAlive(_shells))
        {
            return;
        }

        _avengers = Alive(orders, _avengers);
        if (_avengers.Length > 0)
        {
            orders.Submit(new Command(CommandType.Stop, orders.Player, _avengers));
        }

        To(Step.Done, tick);
    }

    // Counts the events of the tick before this one, by whether the local player may know of them.
    private bool Count(SmokeOrders orders)
    {
        var seen = orders.View.Sight.Seen;
        var refunded = false;
        foreach (var happened in orders.Match.Simulation.Events)
        {
            var known = seen.Knows(happened);
            switch (happened.Kind)
            {
                case SimEventKind.OrderRefused:
                case SimEventKind.TrainRefused:
                    orders.Fail($"the simulation refused an order of the part for player {happened.Player}: {happened.Kind}, {happened.Amount}.");
                    return false;
                case SimEventKind.Splash:
                    _counts.KnownSplashes += known ? 1 : 0;
                    break;
                case SimEventKind.SplashHit:
                    _counts.KnownSplashHits += known ? 1 : 0;
                    break;
                case SimEventKind.ChargedAttack:
                    _counts.KnownCharges += known ? 1 : 0;
                    _counts.UnknownCharges += known ? 0 : 1;
                    break;
                case SimEventKind.Regenerated:
                    _counts.KnownHeals += known ? 1 : 0;
                    break;
                case SimEventKind.ReservesRefunded when happened.Player == orders.Player:
                    _counts.LocalRefunds++;
                    _counts.LocalMaterials += happened.Amount;
                    refunded = true;
                    break;
                case SimEventKind.ReservesRefunded:
                    _counts.OtherRefunds++;
                    break;
                default:
                    break;
            }
        }

        _counts.RefundTicks += refunded ? 1 : 0;
        return true;
    }

    // What the view has shown since the part began is what the local player may know of, no more and no less.
    private bool ShownIsKnown(SmokeOrders orders, Layers layers)
    {
        var shown = Shown.Of(layers);
        var start = _base;
        if (shown.Splashes - start.Splashes == _counts.KnownSplashes
            && shown.SplashHits - start.SplashHits == _counts.KnownSplashHits
            && shown.Heals - start.Heals == _counts.KnownHeals
            && shown.Refunds - start.Refunds == _counts.LocalRefunds
            && shown.Materials - start.Materials == _counts.LocalMaterials
            && layers.Readout.Reactions - _reactionsBefore == _counts.RefundTicks
            && shown.Charges - start.Charges == _counts.KnownCharges)
        {
            return true;
        }

        orders.Fail(
            "the view has not shown exactly what the local player may know of. Shown and known: " +
            $"splashes {shown.Splashes - start.Splashes} and {_counts.KnownSplashes}, units a splash reached {shown.SplashHits - start.SplashHits} and {_counts.KnownSplashHits}, " +
            $"charged attacks {shown.Charges - start.Charges} and {_counts.KnownCharges}, reports of healing {shown.Heals - start.Heals} and {_counts.KnownHeals}, " +
            $"refunds {shown.Refunds - start.Refunds} and {_counts.LocalRefunds}, their Materials {shown.Materials - start.Materials} and {_counts.LocalMaterials}, " +
            $"ticks in which the top bar showed a refund {layers.Readout.Reactions - _reactionsBefore} and {_counts.RefundTicks}.");
        return false;
    }

    private void To(Step step, int tick)
    {
        _step = step;
        _stepAt = tick;
    }

    private static LineDef LineOf(GameData data, ChassisDef chassis) => data.Lines.First(line => line.Index == chassis.Line);

    private static Fixed MiddleOf(int tile) => Fixed.FromRaw((tile * Fixed.RawOne) + (Fixed.RawOne / 2));

    // Gives a seat a tier in a track with the development command.
    private static void SetTier(SmokeOrders orders, int player, int track, int tier)
    {
        // The development command never lowers a track, and a part before this one may have raised it.
        if (orders.Match.Research.State.Tier(player, track) < tier)
        {
            orders.SubmitFor(DebugSetTierHandler.CommandFor(player, track, tier));
        }
    }

    private void Place(SmokeOrders orders, int player, int x) =>
        orders.SubmitFor(new Command(CommandType.DebugPlace, player, targetX: Fixed.FromInt(x), targetY: Fixed.FromInt(BarracksY), contentId: _barracks!.Index));

    private static void Spawn(SmokeOrders orders, int player, ChassisDef chassis, int x, int y)
    {
        var point = new FixedVector2(MiddleOf(x), MiddleOf(y));
        if (!orders.Match.Grid.IsPassable(point))
        {
            orders.Fail($"no unit can stand at {point}, where the part makes one. If the map changed, the part needs another place.");
            return;
        }

        orders.SubmitFor(new Command(CommandType.DebugSpawn, player, targetX: point.X, targetY: point.Y, contentId: chassis.Index));
    }

    // The living fighting units of a player that were not in the match when the part began, in ID order.
    private List<EntityId> NewUnits(SmokeOrders orders, int player)
    {
        var match = orders.Match;
        var units = new List<EntityId>();
        for (var i = 0; i < match.Combatants.Count; i++)
        {
            var entity = match.Combatants.EntityAt(i);
            if (!_before.Contains(entity) && match.World.Entities.IsAlive(entity) && match.Bodies.TryGet(entity, out var body) && body.Owner == player)
            {
                units.Add(entity);
            }
        }

        units.Sort();
        return units;
    }

    private static EntityId[] Alive(SmokeOrders orders, EntityId[] units) =>
        Array.FindAll(units, orders.Match.World.Entities.IsAlive);

    private static EntityId StructureAt(SmokeOrders orders, int x)
    {
        var structures = orders.Match.Structures;
        for (var i = 0; i < structures.Count; i++)
        {
            var tiles = structures.ValueAt(i).Tiles;
            if (tiles.X == x && tiles.Y == BarracksY && orders.Match.World.Entities.IsAlive(structures.EntityAt(i)))
            {
                return structures.EntityAt(i);
            }
        }

        return EntityId.None;
    }

    private enum Step
    {
        Laid,
        Training,
        FirstFight,
        SecondFight,
        Healing,
        Tiers,
        RunUp,
        Shelled,
        Hunt,
        Done,
    }

    // What the part has counted in the events since it began.
    private struct Counts
    {
        public int KnownSplashes;
        public int KnownSplashHits;
        public int KnownCharges;
        public int UnknownCharges;
        public int KnownHeals;
        public int LocalRefunds;
        public long LocalMaterials;
        public int OtherRefunds;
        public int RefundTicks;
    }

    // What the view says it has shown since the match began.
    private readonly record struct Shown(int Splashes, int SplashHits, int Charges, int ChargedHits, int Heals, int Refunds, long Materials)
    {
        public static Shown Of(Layers layers) => new(
            layers.Effects.SplashesShown,
            layers.Effects.SplashHitsShown,
            layers.Effects.ChargesShown,
            layers.Effects.ChargedHitsShown,
            layers.Healing.ReportsShown,
            layers.Refunds.RefundsShown,
            layers.Refunds.MaterialsShown);
    }

    private sealed record Layers(PerkEffects Effects, ChargeMarks Run, HealingMarks Healing, RefundNotes Refunds, RefundReadout Readout, DevelopmentTier Aid)
    {
        public static Layers? Of(SmokeOrders orders)
        {
            var world = orders.View.World;
            return world.GetNodeOrNull<PerkEffects>(nameof(PerkEffects)) is { } effects
                && world.GetNodeOrNull<ChargeMarks>(nameof(ChargeMarks)) is { } run
                && world.GetNodeOrNull<HealingMarks>(nameof(HealingMarks)) is { } healing
                && world.GetNodeOrNull<RefundNotes>(nameof(RefundNotes)) is { } refunds
                && orders.View.Interface.Parts.OfType<RefundReadout>().FirstOrDefault() is { } readout
                && orders.View.Interface.Parts.OfType<DevelopmentTier>().FirstOrDefault() is { } aid
                ? new Layers(effects, run, healing, refunds, readout, aid)
                : null;
        }
    }
}
