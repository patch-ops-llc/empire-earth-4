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
/// A line is named by its roster index, and <see cref="NoLine"/> stands for what belongs to no
/// line: a worker, which no counter names (design 4.1), a structure as a target, and a Tower as
/// an attacker, which has no counter of its own.
/// </para>
/// <para>
/// <b>Structures</b> (design 2.5). A structure is named by its roster index. Damage to one takes
/// the attacking line's multiplier against structures and the fortification rule. A structure
/// with an attack of its own, the Tower, has a <see cref="TurretProfile"/> for each player.
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
    /// <summary>The line of what has none: a worker, a structure, and a Tower when it attacks.</summary>
    public const int NoLine = -1;

    private readonly GameData _data;
    private readonly int _lineCount;
    private readonly bool[] _cannotHit;
    private readonly Fraction[] _multipliers;
    private readonly Fraction[] _structureMultipliers;
    private readonly StructurePick[] _structurePicks;
    private readonly Fixed[] _reach;
    private readonly Fixed[] _reachOfWorker;
    private readonly Fixed[] _longestReach;
    private readonly Func<int, PlayerTech> _techOf;
    private readonly PlayerTech?[] _profiled;
    private readonly AttackProfile[][] _profiles;
    private readonly ArmorProfile[][] _armor;
    private readonly TurretProfile?[][] _turrets;

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
        var constants = data.Rules.Combat;
        LookInterval = constants.LookIntervalTicks;
        UnitReachMargin = constants.UnitReachMargin.ToFraction();
        StructureReach = constants.StructureReach.ToFraction().ToFixed();
        StructureReachSquared = FixedVector2.SquareRaw(StructureReach);
        Fortification = constants.Fortification.ToFraction();

        _lineCount = data.Rules.Lines.Count;
        _cannotHit = new bool[_lineCount * _lineCount];
        _multipliers = new Fraction[_lineCount * _lineCount];
        _structureMultipliers = new Fraction[_lineCount];
        Array.Fill(_structureMultipliers, Fraction.One);
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

            if (line.StructureCounter is { } againstStructures)
            {
                _structureMultipliers[line.Index] = againstStructures.ToFraction();
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

        _structurePicks = new StructurePick[data.Rules.Structures.Count];
        var largestFootprint = 0;
        foreach (var structure in data.Structures)
        {
            _structurePicks[structure.Index] =
                structure.Wall is not null ? StructurePick.Never
                : structure.Attack is not null ? StructurePick.LikeAUnit
                : StructurePick.Last;
            largestFootprint = Math.Max(largestFootprint, structure.Footprint);
        }

        // Half the diagonal of a square is 0.7072 of its side at most; three quarters is above it.
        FurthestEdge = Fixed.FromRaw(largestFootprint * (Fixed.RawOne / 4) * 3);

        _profiled = new PlayerTech?[playerCount];
        _profiles = new AttackProfile[playerCount][];
        _armor = new ArmorProfile[playerCount][];
        _turrets = new TurretProfile?[playerCount][];
    }

    /// <summary>
    /// A unit or a Tower without a target looks for one every this many ticks, on the ticks where
    /// the tick number plus its ID is a multiple of it (design 2.4).
    /// </summary>
    public int LookInterval { get; }

    /// <summary>
    /// What is added to the spacing between a melee attacker and a unit to give its reach
    /// (design 2.4). A value of the rule set, the same for every chassis.
    /// </summary>
    public Fraction UnitReachMargin { get; }

    /// <summary>
    /// Melee reach against a structure, from the attacker's center to the nearest edge of the
    /// footprint (design 2.4). A value of the rule set, the same for every chassis.
    /// </summary>
    public Fixed StructureReach { get; }

    /// <summary>The square of <see cref="StructureReach"/>, in the unit of <see cref="FixedVector2.DistanceSquaredRaw"/>.</summary>
    public long StructureReachSquared { get; }

    /// <summary>
    /// The fortification rule (design 2.5): what multiplies the damage a structure takes from an
    /// attacker whose effective Firepower is below the Protection tier of the structure's owner.
    /// </summary>
    public Fraction Fortification { get; }

    /// <summary>
    /// No structure of the data has its center further than this from the nearest point of its
    /// footprint. A search by centers for structures within a distance of their edges looks this
    /// much further.
    /// </summary>
    public Fixed FurthestEdge { get; }

    /// <summary>What a player's units of a chassis attack with, under the player's tech as it is now.</summary>
    public AttackProfile ProfileOf(int player, int chassis)
    {
        Refresh(player);
        return _profiles[player][chassis];
    }

    /// <summary>What a player's units of a chassis take a hit with, under the player's tech as it is now.</summary>
    public ArmorProfile ArmorOf(int player, int chassis)
    {
        Refresh(player);
        return _armor[player][chassis];
    }

    /// <summary>
    /// What a player's structures of one kind attack with when finished, under the player's tech
    /// as it is now, or null for a structure that does not attack.
    /// </summary>
    /// <param name="player">The owner.</param>
    /// <param name="structure">The structure's place in the roster, as in <see cref="StructureDef.Index"/>.</param>
    public TurretProfile? TurretOf(int player, int structure)
    {
        Refresh(player);
        return _turrets[player][structure];
    }

    /// <summary>How units pick a structure by themselves. A structure the data has no file for is never picked.</summary>
    /// <param name="structure">The structure's place in the roster.</param>
    public StructurePick PickOf(int structure) => _structurePicks[structure];

    /// <summary>The roster index of a chassis's line.</summary>
    public int LineOf(int chassis) => _data.Chassis[chassis].Line;

    /// <summary>
    /// Whether units of a line can hit a target of another line. Every line can hit a worker and
    /// a structure.
    /// </summary>
    public bool CanHit(int attackerLine, int targetLine) =>
        targetLine == NoLine || attackerLine == NoLine || !_cannotHit[(attackerLine * _lineCount) + targetLine];

    /// <summary>
    /// The counter multiplier of an attacking line against a unit of a line: 1 where the data
    /// names none, and for a Tower, which has no counters.
    /// </summary>
    public Fraction Multiplier(int attackerLine, int targetLine) =>
        targetLine == NoLine || attackerLine == NoLine ? Fraction.One : _multipliers[(attackerLine * _lineCount) + targetLine];

    /// <summary>The multiplier of an attacking line against every structure: 1 where the data names none, and for a Tower.</summary>
    public Fraction StructureMultiplier(int attackerLine) =>
        attackerLine == NoLine ? Fraction.One : _structureMultipliers[attackerLine];

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
    /// The damage a hit does to a unit of a chassis, in thousandths of a hit point: the attacker's
    /// side, times the counter multiplier of the two lines, times what the target's accents take
    /// off (design 2.3). Exact fractions, rounded once.
    /// </summary>
    /// <remarks>
    /// Hardened I takes its share off area damage: the blast of a shell, for everyone in it, and
    /// the splash of a hit, for everyone but the target of that hit. Hardened II, plating, takes
    /// its share off any hit whose attacker's effective Firepower is below the target's effective
    /// Protection. The two multiply. The target's side is read when the hit lands: its owner's
    /// tech as it is now.
    /// </remarks>
    /// <param name="attackerSide">The attacker's damage of one hit, fixed when it attacked.</param>
    /// <param name="attackerLine">The attacker's line, or <see cref="NoLine"/> for a Tower.</param>
    /// <param name="attackerFirepower">The attacker's effective Firepower, fixed when it attacked. A Tower's is its owner's tier.</param>
    /// <param name="owner">The player the target belongs to.</param>
    /// <param name="chassis">The target's chassis.</param>
    /// <param name="area">True for area damage: a blast or a splash.</param>
    public long DamageToUnit(Fraction attackerSide, int attackerLine, int attackerFirepower, int owner, int chassis, bool area)
    {
        var armor = ArmorOf(owner, chassis);
        var damage = attackerSide * Multiplier(attackerLine, LineOf(chassis));
        if (area)
        {
            damage *= armor.AreaDamage;
        }

        if (attackerFirepower < armor.Protection)
        {
            damage *= armor.Plating;
        }

        return damage.RoundHalfUp(1000);
    }

    /// <summary>
    /// The damage a hit does to a structure, in thousandths of a hit point: the attacker's side,
    /// times its line's multiplier against structures, times <see cref="Fortification"/> when the
    /// attacker's effective Firepower is below the Protection tier the structure's owner has now
    /// (design 2.5). Exact fractions, rounded once.
    /// </summary>
    /// <param name="attackerSide">The attacker's damage of one hit, fixed when it attacked.</param>
    /// <param name="attackerLine">The attacker's line, or <see cref="NoLine"/> for a Tower.</param>
    /// <param name="attackerFirepower">The attacker's effective Firepower, fixed when it attacked. A Tower's is its owner's tier.</param>
    /// <param name="owner">The player the structure belongs to.</param>
    public long DamageToStructure(Fraction attackerSide, int attackerLine, int attackerFirepower, int owner)
    {
        var damage = attackerSide * StructureMultiplier(attackerLine);
        if (attackerFirepower < _techOf(owner).Tiers[_data.TrackRoles.Protection])
        {
            damage *= Fortification;
        }

        return damage.RoundHalfUp(1000);
    }

    /// <summary>
    /// The hit points of a player's worker: those of the worker's profile at Protection tier 1,
    /// times the hit point factor of the rule set for each Protection tier of its owner above it
    /// (design 4.1).
    /// </summary>
    public Health WorkerHealth(int player)
    {
        var protection = _techOf(player).Tiers[_data.TrackRoles.Protection];
        return Health.Full(HitPointRules.AtProtectionTier(_data, _data.Resources.Worker.HitPoints.ToFraction(), protection));
    }

    private Fixed Reach(Fraction spacing) => (spacing + UnitReachMargin).ToFixed();

    private static Fraction Max(Fraction a, Fraction b) => a >= b ? a : b;

    /// <summary>Makes a player's profiles again when research has handed out another tech table.</summary>
    private void Refresh(int player)
    {
        var tech = _techOf(player);
        if (ReferenceEquals(tech, _profiled[player]))
        {
            return;
        }

        var profiles = new AttackProfile[tech.Blueprints.Count];
        var armor = new ArmorProfile[tech.Blueprints.Count];
        for (var i = 0; i < profiles.Length; i++)
        {
            profiles[i] = ProfileFor(tech.Blueprints[i]);
            armor[i] = ArmorFor(tech.Blueprints[i]);
        }

        var turrets = new TurretProfile?[_structurePicks.Length];
        foreach (var blueprint in tech.Structures)
        {
            if (blueprint.Attack is { } attack)
            {
                turrets[blueprint.Structure.Index] = TurretFor(blueprint.Structure, attack);
            }
        }

        _profiles[player] = profiles;
        _armor[player] = armor;
        _turrets[player] = turrets;
        _profiled[player] = tech;
    }

    private TurretProfile TurretFor(StructureDef structure, StructureAttackBlueprint attack)
    {
        var speed = attack.Attack.ProjectileSpeed
            ?? throw new InvalidOperationException($"Structure '{structure.Id}' attacks and has no projectile speed.");
        var cannotHit = new bool[_lineCount];
        foreach (var line in attack.CannotHit)
        {
            cannotHit[line] = true;
        }

        return new TurretProfile(cannotHit)
        {
            Damage = attack.DamagePerHit,
            Firepower = attack.EffectiveFirepower,
            IntervalTicks = attack.Attack.IntervalTicks,
            Range = attack.Range,
            RangeSquared = FixedVector2.SquareRaw(attack.Range),
            ShotSpeed = Mover.SpeedPerTick(speed.ToFraction()),
        };
    }

    private ArmorProfile ArmorFor(ChassisBlueprint blueprint) => new()
    {
        Protection = blueprint.EffectiveTiers[_data.TrackRoles.Protection],
        AreaDamage = blueprint.Perks.AreaDamageReduction is { } hardened ? hardened.Multiplier.ToFraction() : Fraction.One,
        Plating = blueprint.Perks.Plating is { } plating ? plating.Multiplier.ToFraction() : Fraction.One,
    };

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
            Charge = blueprint.Perks.Charge is { } charge
                ? new ChargeProfile(charge.Distance.ToFraction().ToFixed(), charge.Multiplier.ToFraction())
                : null,
            Range = melee ? Fixed.Zero : range,
            RangeSquared = melee ? 0 : FixedVector2.SquareRaw(range),
            MinRangeSquared = !melee && blueprint.MinRange is { } minRange ? FixedVector2.SquareRaw(minRange) : 0,
            ShotSpeed = shotSpeed,
            Blast = !melee && blueprint.BlastRadius is { } blast ? blast : Fixed.Zero,
            SplashShare = blueprint.Perks.Splash is { } splash ? splash.Share.ToFraction() : Fraction.Zero,
            SplashRadius = blueprint.Perks.Splash is { } reach ? reach.Radius.ToFraction().ToFixed() : Fixed.Zero,
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

    /// <summary>Charge (design 2.3), when the chassis carries it.</summary>
    public required ChargeProfile? Charge { get; init; }

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
    /// The blast radius of a ranged attack, with the blast radius bonus: where its shot lands,
    /// every enemy within this distance takes the damage. Zero for an attack that hits one target.
    /// </summary>
    public required Fixed Blast { get; init; }

    /// <summary>
    /// The splash of the attack (design 2.3, Overgunned II): the share of <see cref="Damage"/>
    /// that every other enemy within <see cref="SplashRadius"/> of the target takes with each
    /// hit. Zero for an attack without splash, and for one with a <see cref="Blast"/>, which
    /// gets a larger blast instead.
    /// </summary>
    public required Fraction SplashShare { get; init; }

    /// <summary>How far the splash reaches from the target's center, in tiles. Zero for an attack without splash.</summary>
    public required Fixed SplashRadius { get; init; }

    /// <summary>
    /// How far the unit looks for targets: its sight or its weapon range, whichever is larger, with
    /// accents included (design 2.4).
    /// </summary>
    public required Fixed Acquisition { get; init; }

    public required long AcquisitionSquared { get; init; }
}

/// <summary>
/// What one player's units of one chassis take a hit with, taken from the player's blueprint of
/// it (design 2.3): the target's side of the two Protection accents.
/// </summary>
public sealed class ArmorProfile
{
    internal ArmorProfile()
    {
    }

    /// <summary>The chassis's effective Protection tier, which plating compares with the attacker's effective Firepower.</summary>
    public required int Protection { get; init; }

    /// <summary>What multiplies area damage, the blast of a shell and the splash of a hit: less than 1 with Hardened I, else 1.</summary>
    public required Fraction AreaDamage { get; init; }

    /// <summary>
    /// What multiplies the damage of a hit whose attacker's effective Firepower is below
    /// <see cref="Protection"/>: less than 1 with Hardened II, else 1.
    /// </summary>
    public required Fraction Plating { get; init; }
}

/// <summary>How units and Towers pick a structure by themselves (design 2.4, "What may be picked").</summary>
public enum StructurePick
{
    /// <summary>Never: a wall or a gate, which is attacked only on an attack order.</summary>
    Never = 0,

    /// <summary>
    /// Only by a unit on an attack-move order, and only when it can pick no unit and no Tower: a
    /// structure that does not attack.
    /// </summary>
    Last,

    /// <summary>Like an enemy unit: a structure with an attack of its own, the Tower.</summary>
    LikeAUnit,
}

/// <summary>
/// What one player's structures of one kind attack with (design 2.5, the Tower), taken from the
/// player's blueprint of the structure. Range is measured from the nearest edge of the footprint.
/// </summary>
public sealed class TurretProfile
{
    private readonly bool[] _cannotHit;

    internal TurretProfile(bool[] cannotHit)
    {
        _cannotHit = cannotHit;
    }

    /// <summary>The damage of one hit before anything the target applies, exactly. It follows the owner's Firepower tier.</summary>
    public required Fraction Damage { get; init; }

    /// <summary>The owner's Firepower tier, which is the attack's effective Firepower.</summary>
    public required int Firepower { get; init; }

    /// <summary>The wait between two attacks, in ticks.</summary>
    public required int IntervalTicks { get; init; }

    /// <summary>The range, which is also how far it looks for targets (design 8.4, row 16).</summary>
    public required Fixed Range { get; init; }

    /// <summary>The square of <see cref="Range"/>, in the unit of <see cref="FixedVector2.DistanceSquaredRaw"/>.</summary>
    public required long RangeSquared { get; init; }

    /// <summary>The distance a shot covers in one tick.</summary>
    public required Fixed ShotSpeed { get; init; }

    /// <summary>
    /// Whether it can hit a target of a line, under its owner's Protection tier. It can always hit
    /// what has no line: a worker and a structure.
    /// </summary>
    public bool CanHit(int line) => line == CombatRules.NoLine || !_cannotHit[line];
}

/// <summary>
/// Charge (design 2.3): "the first attack after moving 4 or more tiles deals +50% damage".
/// </summary>
/// <param name="Distance">The distance a unit must have walked since its last attack, in tiles.</param>
/// <param name="Multiplier">What the attacker's side of all damage of that one attack is multiplied by.</param>
public sealed record ChargeProfile(Fixed Distance, Fraction Multiplier);
