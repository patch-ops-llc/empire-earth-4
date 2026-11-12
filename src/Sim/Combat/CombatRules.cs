using Sim.Data;
using Sim.Movement;
using Sim.Numerics;
using Sim.Research;
using Sim.Tech;

namespace Sim.Combat;

/// <summary>
/// The combat numbers of a match, worked out from the data and from each player's tech: who can hit
/// whom, the counter multipliers, melee reach, and what each player's chassis attack with. Not
/// state: the same data and the same tiers give the same numbers on every machine.
/// </summary>
/// <remarks>
/// <para>
/// A line is named by its roster index, and <see cref="NoLine"/> stands for a target that belongs
/// to no line: a worker, which no counter names (design 4.1).
/// </para>
/// <para>
/// <b>Where the tech comes from.</b> Combat asks the match's research for a player's
/// <see cref="PlayerTech"/> each time it needs it (<see cref="MatchResearch.Tech"/>), and keeps
/// nothing but the attack profiles it made from the table it was last handed. So it can never
/// fight with an old table, and research needs to tell it nothing when a tier lands or a snapshot
/// is restored.
/// </para>
/// </remarks>
public sealed class CombatRules
{
    /// <summary>The line of a target that has none: a worker.</summary>
    public const int NoLine = -1;

    /// <summary>
    /// A unit without a target looks for one every this many ticks, on the ticks where the tick
    /// number plus its ID is a multiple of it (design 2.4).
    /// </summary>
    public const int LookInterval = 4;

    private readonly GameData _data;
    private readonly int _lineCount;
    private readonly bool[] _cannotHit;
    private readonly Fraction[] _multipliers;
    private readonly Fixed[] _reach;
    private readonly Fixed[] _reachOfWorker;
    private readonly Fixed[] _longestReach;
    private readonly Func<int, PlayerTech> _techOf;
    private readonly PlayerTech?[] _profiled;
    private readonly AttackProfile[][] _profiles;

    /// <summary>Works out the rules for a match.</summary>
    /// <param name="data">The loaded game data.</param>
    /// <param name="playerCount">The number of players.</param>
    /// <param name="techOf">
    /// Gives a player's tech as it is now: in a match, <see cref="MatchResearch.Tech"/>. It must
    /// hand out another object whenever the tech has changed, as research does.
    /// </param>
    public CombatRules(GameData data, int playerCount, Func<int, PlayerTech> techOf)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(techOf);
        ArgumentOutOfRangeException.ThrowIfNegative(playerCount);
        _data = data;
        _techOf = techOf;
        _lineCount = data.Rules.Lines.Count;
        _cannotHit = new bool[_lineCount * _lineCount];
        _multipliers = new Fraction[_lineCount * _lineCount];
        _reach = new Fixed[_lineCount * _lineCount];
        _reachOfWorker = new Fixed[_lineCount];
        _longestReach = new Fixed[_lineCount];
        Array.Fill(_multipliers, Fraction.One);

        // A worker has no line to carry its spacing: it is in the worker's profile.
        var workerSpacing = data.Resources.Worker.Spacing.ToFraction();
        foreach (var line in data.Lines)
        {
            foreach (var counter in line.Counters)
            {
                _multipliers[(line.Index * _lineCount) + counter.Line] = counter.Multiplier.ToFraction();
            }

            foreach (var other in line.CannotHit)
            {
                _cannotHit[(line.Index * _lineCount) + other] = true;
            }

            var spacing = line.Base.Spacing.ToFraction();
            var reach = Reach(Max(spacing, workerSpacing));
            _reachOfWorker[line.Index] = reach;
            _longestReach[line.Index] = reach;
            foreach (var other in data.Lines)
            {
                reach = Reach(Max(spacing, other.Base.Spacing.ToFraction()));
                _reach[(line.Index * _lineCount) + other.Index] = reach;
                _longestReach[line.Index] = Fixed.Max(_longestReach[line.Index], reach);
            }
        }

        _profiled = new PlayerTech?[playerCount];
        _profiles = new AttackProfile[playerCount][];
    }

    /// <summary>
    /// What is added to the spacing between a melee attacker and a unit to give its reach: a quarter
    /// of a tile (design 2.4). A value of the rule set, the same for every chassis.
    /// </summary>
    public static Fraction UnitReachMargin => new(1, 4);

    /// <summary>
    /// Melee reach against a structure, from the attacker's center to the nearest edge of the
    /// footprint: three quarters of a tile (design 2.4). Nothing uses it until structures exist.
    /// </summary>
    public static Fraction StructureReach => new(3, 4);

    /// <summary>What a player's units of a chassis attack with, under the player's tech as it is now.</summary>
    public AttackProfile ProfileOf(int player, int chassis)
    {
        var tech = _techOf(player);
        if (!ReferenceEquals(tech, _profiled[player]))
        {
            var profiles = new AttackProfile[tech.Blueprints.Count];
            for (var i = 0; i < profiles.Length; i++)
            {
                profiles[i] = ProfileFor(tech.Blueprints[i]);
            }

            _profiles[player] = profiles;
            _profiled[player] = tech;
        }

        return _profiles[player][chassis];
    }

    /// <summary>The roster index of a chassis's line.</summary>
    public int LineOf(int chassis) => _data.Chassis[chassis].Line;

    /// <summary>Whether units of a line can hit a target of another line. Every line can hit a worker.</summary>
    public bool CanHit(int attackerLine, int targetLine) =>
        targetLine == NoLine || !_cannotHit[(attackerLine * _lineCount) + targetLine];

    /// <summary>The counter multiplier of an attacking line against a target's line: 1 where the data names none.</summary>
    public Fraction Multiplier(int attackerLine, int targetLine) =>
        targetLine == NoLine ? Fraction.One : _multipliers[(attackerLine * _lineCount) + targetLine];

    /// <summary>
    /// The melee reach of a line against a unit of another line: the larger spacing of the two plus
    /// <see cref="UnitReachMargin"/>.
    /// </summary>
    public Fixed Reach(int attackerLine, int targetLine) =>
        targetLine == NoLine ? _reachOfWorker[attackerLine] : _reach[(attackerLine * _lineCount) + targetLine];

    /// <summary>The square of <see cref="Reach"/>, in the unit of <see cref="FixedVector2.DistanceSquaredRaw"/>.</summary>
    public long ReachSquared(int attackerLine, int targetLine) => FixedVector2.SquareRaw(Reach(attackerLine, targetLine));

    /// <summary>
    /// The damage a hit does, in thousandths of a hit point: the attacker's side times the target's
    /// side, as exact fractions, rounded once (design 2.4).
    /// </summary>
    public long DamageOf(Fraction attackerSide, int attackerLine, int targetLine) =>
        (attackerSide * Multiplier(attackerLine, targetLine)).RoundHalfUp(1000);

    /// <summary>
    /// The hit points of a player's worker: those of the worker's profile at Protection tier 1,
    /// times the hit point factor of the rule set for each Protection tier of its owner above it
    /// (design 4.1).
    /// </summary>
    public Health WorkerHealth(int player) => Health.Full(WorkerHitPoints(_techOf(player)));

    /// <summary>The maximum hit points, in thousandths, of a worker whose owner has this tech.</summary>
    public long WorkerHitPoints(PlayerTech tech)
    {
        ArgumentNullException.ThrowIfNull(tech);
        var protection = tech.Tiers[_data.TrackRoles.Protection];
        return HitPointRules.AtProtectionTier(_data, _data.Resources.Worker.HitPoints.ToFraction(), protection);
    }

    private static Fixed Reach(Fraction spacing) => (spacing + UnitReachMargin).ToFixed();

    private static Fraction Max(Fraction a, Fraction b) => a >= b ? a : b;

    private AttackProfile ProfileFor(ChassisBlueprint blueprint)
    {
        var attack = blueprint.Attack;
        var line = blueprint.Line.Index;
        var melee = attack.Kind == AttackKind.Melee;
        var sight = Fixed.FromInt(blueprint.Sight);
        Fixed range;
        Fixed shotSpeed;
        if (melee)
        {
            range = _longestReach[line];
            shotSpeed = Fixed.Zero;
        }
        else
        {
            range = blueprint.Range
                ?? throw new InvalidOperationException($"Chassis '{blueprint.Chassis.Id}' attacks at range and has no range.");
            var speed = attack.ProjectileSpeed
                ?? throw new InvalidOperationException($"Chassis '{blueprint.Chassis.Id}' attacks at range and has no projectile speed.");
            shotSpeed = Mover.SpeedPerTick(speed.ToFraction());
        }

        var acquisition = Fixed.Max(sight, range);
        return new AttackProfile
        {
            Line = line,
            Melee = melee,
            Damage = blueprint.DamagePerHit,
            Firepower = blueprint.EffectiveTiers[_data.TrackRoles.Firepower],
            IntervalTicks = attack.IntervalTicks,
            Rate = melee && blueprint.Perks.AttackRateBonus is { } bonus ? bonus.Multiplier.ToFraction() : Fraction.One,
            Range = melee ? Fixed.Zero : range,
            RangeSquared = melee ? 0 : FixedVector2.SquareRaw(range),
            MinRangeSquared = !melee && blueprint.MinRange is { } minRange ? FixedVector2.SquareRaw(minRange) : 0,
            ShotSpeed = shotSpeed,
            Acquisition = acquisition,
            AcquisitionSquared = FixedVector2.SquareRaw(acquisition),
        };
    }
}

/// <summary>
/// What one player's units of one chassis attack with, taken from the player's blueprint of it
/// (design 2.3 and 2.4). Squares are in the unit of <see cref="FixedVector2.DistanceSquaredRaw"/>.
/// </summary>
public sealed class AttackProfile
{
    internal AttackProfile()
    {
    }

    /// <summary>The roster index of the chassis's line.</summary>
    public required int Line { get; init; }

    /// <summary>True for a melee attack, which lands in the tick it is made. A ranged attack fires a <see cref="Shot"/>.</summary>
    public required bool Melee { get; init; }

    /// <summary>The damage of one hit before anything the target applies, exactly.</summary>
    public required Fraction Damage { get; init; }

    /// <summary>The chassis's effective Firepower tier.</summary>
    public required int Firepower { get; init; }

    /// <summary>The wait between two attacks, in ticks.</summary>
    public required int IntervalTicks { get; init; }

    /// <summary>What one tick of waiting counts toward <see cref="IntervalTicks"/>: 1, or more with the melee attack-rate bonus.</summary>
    public required Fraction Rate { get; init; }

    /// <summary>The range of a ranged attack, with the range bonus. Zero for a melee attack, whose reach depends on the target: see <see cref="CombatRules.Reach"/>.</summary>
    public required Fixed Range { get; init; }

    /// <summary>The square of <see cref="Range"/>.</summary>
    public required long RangeSquared { get; init; }

    /// <summary>The square of the distance inside which a ranged attack cannot fire. Zero when there is none.</summary>
    public required long MinRangeSquared { get; init; }

    /// <summary>The distance a shot covers in one tick. Zero for a melee attack.</summary>
    public required Fixed ShotSpeed { get; init; }

    /// <summary>
    /// How far the unit looks for targets: its sight or its weapon range, whichever is larger, with
    /// accents included (design 2.4).
    /// </summary>
    public required Fixed Acquisition { get; init; }

    public required long AcquisitionSquared { get; init; }
}
