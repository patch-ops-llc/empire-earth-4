using Sim.Data;
using Sim.Movement;
using Sim.Numerics;
using Sim.Tech;

namespace Sim.Combat;

/// <summary>
/// The combat numbers of a match, worked out once from the data and from each player's tech: who
/// can hit whom, the counter multipliers, melee reach, and what each player's chassis attack with.
/// Not state: the same data and the same tiers give the same numbers on every machine.
/// </summary>
/// <remarks>
/// A line is named by its roster index, and <see cref="NoLine"/> stands for a target that belongs
/// to no line: a worker, which no counter names (design 4.1).
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

    /// <summary>
    /// The hit points of a worker at Protection tier 1, before the owner's Protection tiers
    /// multiply them (design 4.1). It stands here until the worker's profile is in the data.
    /// </summary>
    public const int WorkerHitPoints = 50;

    private readonly GameData _data;
    private readonly int _lineCount;
    private readonly bool[] _cannotHit;
    private readonly Fraction[] _multipliers;
    private readonly long[] _reachSquared;
    private readonly long[] _reachSquaredOfWorker;
    private readonly Fixed[] _longestReach;
    private readonly PlayerTech[] _tech;
    private readonly AttackProfile[][] _profiles;

    /// <summary>Works out the rules for a match in which every player starts with every track at the data's starting epoch.</summary>
    public CombatRules(GameData data, int playerCount)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(playerCount);
        _data = data;
        _lineCount = data.Rules.Lines.Count;
        _cannotHit = new bool[_lineCount * _lineCount];
        _multipliers = new Fraction[_lineCount * _lineCount];
        _reachSquared = new long[_lineCount * _lineCount];
        _reachSquaredOfWorker = new long[_lineCount];
        _longestReach = new Fixed[_lineCount];
        Array.Fill(_multipliers, Fraction.One);

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
            var reach = Reach(Max(spacing, WorkerSpacing));
            _reachSquaredOfWorker[line.Index] = FixedVector2.SquareRaw(reach);
            _longestReach[line.Index] = reach;
            foreach (var other in data.Lines)
            {
                reach = Reach(Max(spacing, other.Base.Spacing.ToFraction()));
                _reachSquared[(line.Index * _lineCount) + other.Index] = FixedVector2.SquareRaw(reach);
                _longestReach[line.Index] = Fixed.Max(_longestReach[line.Index], reach);
            }
        }

        var tiers = new int[data.Tracks.Count];
        Array.Fill(tiers, data.Rules.Match.StartEpoch);
        _tech = new PlayerTech[playerCount];
        _profiles = new AttackProfile[playerCount][];
        for (var player = 0; player < playerCount; player++)
        {
            SetTech(player, TechResolver.Resolve(data, tiers));
        }
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

    /// <summary>The spacing of a worker, which has no line to carry it: 1 tile (design 2.4).</summary>
    public static Fraction WorkerSpacing => Fraction.One;

    /// <summary>The tech a player's units fight with.</summary>
    public PlayerTech TechOf(int player) => _tech[player];

    /// <summary>
    /// Gives a player new tech, which every unit of that player fights with from then on. The match
    /// setup calls it once per player; research in a match calls it when a tier is reached.
    /// </summary>
    public void SetTech(int player, PlayerTech tech)
    {
        ArgumentNullException.ThrowIfNull(tech);
        var profiles = new AttackProfile[tech.Blueprints.Count];
        for (var chassis = 0; chassis < profiles.Length; chassis++)
        {
            profiles[chassis] = ProfileFor(tech.Blueprints[chassis]);
        }

        _tech[player] = tech;
        _profiles[player] = profiles;
    }

    /// <summary>What a player's units of a chassis attack with.</summary>
    public AttackProfile ProfileOf(int player, int chassis) => _profiles[player][chassis];

    /// <summary>The roster index of a chassis's line.</summary>
    public int LineOf(int chassis) => _data.Chassis[chassis].Line;

    /// <summary>Whether units of a line can hit a target of another line. Every line can hit a worker.</summary>
    public bool CanHit(int attackerLine, int targetLine) =>
        targetLine == NoLine || !_cannotHit[(attackerLine * _lineCount) + targetLine];

    /// <summary>The counter multiplier of an attacking line against a target's line: 1 where the data names none.</summary>
    public Fraction Multiplier(int attackerLine, int targetLine) =>
        targetLine == NoLine ? Fraction.One : _multipliers[(attackerLine * _lineCount) + targetLine];

    /// <summary>
    /// The square of the melee reach of a line against a unit of another line: the larger spacing of
    /// the two plus <see cref="UnitReachMargin"/>, in the unit of <see cref="FixedVector2.DistanceSquaredRaw"/>.
    /// </summary>
    public long ReachSquared(int attackerLine, int targetLine) =>
        targetLine == NoLine ? _reachSquaredOfWorker[attackerLine] : _reachSquared[(attackerLine * _lineCount) + targetLine];

    /// <summary>
    /// The damage a hit does, in thousandths of a hit point: the attacker's side times the target's
    /// side, as exact fractions, rounded once (design 2.4).
    /// </summary>
    public long DamageOf(Fraction attackerSide, int attackerLine, int targetLine) =>
        (attackerSide * Multiplier(attackerLine, targetLine)).RoundHalfUp(1000);

    /// <summary>
    /// The hit points of a player's worker: <see cref="WorkerHitPoints"/> at Protection tier 1, times
    /// the hit point factor of the rule set for each tier above it, rounded once (design 4.1).
    /// </summary>
    public Health WorkerHealth(int player)
    {
        var protection = _tech[player].Tiers[_data.TrackRoles.Protection];
        var hitPoints = WorkerHitPoints * _data.Rules.PerTier.HitPoints.ToFraction().Pow(protection - 1);
        return Health.Full(hitPoints.RoundHalfUp(1000));
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

    /// <summary>The square of the range of a ranged attack, with the range bonus. Melee reach depends on the target: see <see cref="CombatRules.ReachSquared"/>.</summary>
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
