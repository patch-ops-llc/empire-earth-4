using Sim;
using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tech;

namespace Tools.Battles;

/// <summary>What one army had at the start of a fight, what it kept and how much it fought. Prices are Materials and Energy added up.</summary>
/// <param name="Attacks">The attacks its units made: shots fired and melee hits.</param>
/// <param name="AttacksPossible">
/// The attacks its units had time for: one for every attack interval, begun or whole, of every
/// unit while it lived, counted from the first attack of the fight, so that the walk toward the
/// enemy is left out. The share made says how much of the army fought: a unit makes no attack
/// while nothing is in its reach, and none in a tick in which it moved.
/// </param>
/// <param name="OpeningAttacks">The attacks it made in the first <see cref="Battle.OpeningSeconds"/> seconds from the first attack of the fight.</param>
/// <param name="OpeningAttacksPossible">The attacks its units had time for in those seconds: how fast the army brings its numbers to bear.</param>
public sealed record ArmyResult(
    int Units,
    int UnitsKept,
    long Price,
    long PriceKept,
    int Supply,
    int SupplyCap,
    int Attacks,
    int AttacksPossible,
    int OpeningAttacks,
    int OpeningAttacksPossible)
{
    /// <summary>Army value kept, in percent: the price of the surviving units over the price of the starting units. Damaged units count in full.</summary>
    public double PercentKept => Price == 0 ? 0 : 100.0 * PriceKept / Price;

    public double PercentOfAttacksMade => AttacksPossible == 0 ? 0 : 100.0 * Attacks / AttacksPossible;

    public double PercentOfOpeningAttacksMade => OpeningAttacksPossible == 0 ? 0 : 100.0 * OpeningAttacks / OpeningAttacksPossible;
}

/// <summary>The shells of one fight: shots of a unit with a blast radius, and how many units each caught.</summary>
public sealed record ShellCount(int Landed, int Caught, int MostCaught)
{
    public static ShellCount None { get; } = new(0, 0, 0);

    public double AverageCaught => Landed == 0 ? 0 : (double)Caught / Landed;
}

/// <summary>One fight of a battle, one way round.</summary>
/// <param name="MassWest">True when the mass army stood in the west and was the first player.</param>
/// <param name="Winner">The army that still had units when the other had none. None when both fell in one tick, or when the time ran out.</param>
/// <param name="Ticks">How long the fight took.</param>
/// <param name="TimedOut">True when both armies still had units when the time ran out.</param>
public sealed record BattleResult(bool MassWest, Side? Winner, int Ticks, bool TimedOut, ArmyResult Mass, ArmyResult Elite, ShellCount Shells)
{
    public double Seconds => (double)Ticks / SimInfo.TicksPerSecond;

    public ArmyResult Of(Side side) => side == Side.Mass ? Mass : Elite;
}

/// <summary>
/// Fights one acceptance battle headless, one way round: sets the armies up on their arena as
/// the design fixes it, gives each side one attack-move order onto the other's starting
/// position, and runs the match until one army is gone.
/// </summary>
/// <remarks>
/// <para>
/// The match is the game's own: the same systems, and each player's blueprints worked out from
/// the data folder and the army's tiers. Nothing in a fight is random, so a battle gives the
/// same result in every run and on every machine.
/// </para>
/// <para>
/// <b>Both ways round.</b> With <c>massWest</c> the mass army stands in the west and is the
/// first player; without it, it stands in the east and is the second. So the two fights of a
/// battle swap the side of the map and the seat together: the simulation runs commands in the
/// order of the players and units act in the order of their IDs, and the seat decides both.
/// </para>
/// </remarks>
public static class Battle
{
    /// <summary>The opening of a fight, counted apart: this many seconds from its first attack.</summary>
    public const int OpeningSeconds = 10;

    /// <summary>A fight that has no winner by then is stopped: ten minutes of game time.</summary>
    public const int DefaultMostTicks = 10 * 60 * SimInfo.TicksPerSecond;

    /// <summary>
    /// What is wrong with a battle for this data, or null: a chassis the data does not have, or
    /// an army that a player with its tiers could not build in a match.
    /// </summary>
    public static string? Problem(GameData data, BattleDef battle)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(battle);

        foreach (var side in new[] { Side.Mass, Side.Elite })
        {
            var army = battle.Army(side);
            if (army.Tiers.Count != data.Tracks.Count)
            {
                return $"the {Name(side)} army has {army.Tiers.Count} tiers and the data has {data.Tracks.Count} tracks";
            }

            foreach (var part in army.Parts)
            {
                if (data.Chassis.All(chassis => chassis.Id != part.Chassis))
                {
                    return $"the data has no chassis '{part.Chassis}'";
                }
            }

            PlayerTech tech;
            try
            {
                tech = TechResolver.Resolve(data, army.Tiers);
            }
            catch (ArgumentException exception)
            {
                return $"the tiers of the {Name(side)} army are not allowed: {exception.Message}";
            }

            var supply = 0;
            foreach (var part in army.Parts)
            {
                var blueprint = tech.Blueprints[ChassisIndex(data, part.Chassis)];
                if (!blueprint.Unlocked)
                {
                    return $"the {Name(side)} player has not unlocked '{part.Chassis}' with tiers {string.Join(", ", army.Tiers)}";
                }

                supply += part.Count * blueprint.Supply;
            }

            if (supply > tech.SupplyCap)
            {
                return $"the {Name(side)} army needs {supply} supply and its player's cap is {tech.SupplyCap}";
            }
        }

        return null;
    }

    public static BattleResult Fight(GameData data, BattleDef battle, bool massWest, int mostTicks = DefaultMostTicks)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentOutOfRangeException.ThrowIfLessThan(mostTicks, 1);

        var fight = Start(data, battle, massWest);
        var match = fight.Match;
        var shells = new ShellCounter();
        var living = new[] { fight.Units(Side.Mass).Length, fight.Units(Side.Elite).Length };
        while (match.World.Tick < mostTicks && living[0] > 0 && living[1] > 0)
        {
            match.Simulation.Tick();
            shells.AfterTick(match);
            fight.CountAttacks();
            living[0] = fight.Living(Side.Mass);
            living[1] = fight.Living(Side.Elite);
        }

        Side? winner = living[0] > 0 == living[1] > 0 ? null : living[0] > 0 ? Side.Mass : Side.Elite;
        return new BattleResult(
            massWest,
            winner,
            match.World.Tick,
            TimedOut: living[0] > 0 && living[1] > 0,
            fight.Result(Side.Mass),
            fight.Result(Side.Elite),
            shells.Count);
    }

    /// <summary>The match of a fight before its first tick: the armies in place and their one order each scheduled.</summary>
    public static Fight Start(GameData data, BattleDef battle, bool massWest)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(battle);
        if (Problem(data, battle) is { } problem)
        {
            throw new ArgumentException($"Battle {battle.Number} cannot be fought: {problem}.", nameof(battle));
        }

        var map = Arena.Create(battle.Arena, battle.BlockWidth);
        var match = MatchSetup.Create(data, map, seed: 1, [PlayerSetup.WithoutStart, PlayerSetup.WithoutStart]);
        var players = new int[2];
        players[(int)Side.Mass] = massWest ? 0 : 1;
        players[(int)Side.Elite] = massWest ? 1 : 0;

        // The first player's units get the lower IDs, whichever army it has.
        var units = new EntityId[2][];
        var chassis = new int[2][];
        var centers = new FixedVector2[2];
        foreach (var side in massWest ? new[] { Side.Mass, Side.Elite } : [Side.Elite, Side.Mass])
        {
            var army = battle.Army(side);
            var player = players[(int)side];
            var west = player == 0;
            match.Research.SetTiers(player, army.Tiers);
            var blueprints = match.Research.Tech(player).Blueprints;
            var block = (army.Block.Count, Spacing(blueprints[ChassisIndex(data, army.Block.Chassis)]));
            var behind = army.Behind is null ? (0, Fixed.One) : (army.Behind.Count, Spacing(blueprints[ChassisIndex(data, army.Behind.Chassis)]));
            var positions = Arena.Positions(map, west, battle.BlockWidth, block, behind);
            units[(int)side] = new EntityId[positions.Length];
            chassis[(int)side] = new int[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                var part = i < army.Block.Count ? army.Block : army.Behind!;
                chassis[(int)side][i] = ChassisIndex(data, part.Chassis);
                units[(int)side][i] = match.SpawnChassis(player, chassis[(int)side][i], positions[i]);
            }

            centers[(int)side] = Arena.BlockCenter(map, west, battle.BlockWidth, block);
        }

        match.RefreshDerivedState();
        foreach (var side in new[] { Side.Mass, Side.Elite })
        {
            var target = centers[side == Side.Mass ? (int)Side.Elite : (int)Side.Mass];
            match.Simulation.Commands.Enqueue(0, new Command(CommandType.AttackMove, players[(int)side], units[(int)side], target.X, target.Y));
        }

        return new Fight(match, players, units, chassis);
    }

    internal static string Name(Side side) => side == Side.Mass ? "mass" : "elite";

    private static int ChassisIndex(GameData data, string id) => data.Chassis.Single(chassis => chassis.Id == id).Index;

    private static Fixed Spacing(ChassisBlueprint blueprint) => blueprint.Line.Base.Spacing.ToFraction().ToFixed();
}

/// <summary>A fight that is set up: its match, and which player and which units each army is.</summary>
public sealed class Fight
{
    private readonly int[] _players;
    private readonly EntityId[][] _units;
    private readonly int[][] _chassis;
    private readonly int[] _attacks = new int[2];
    private readonly int[] _openingAttacks = new int[2];
    private readonly int[][] _ticksFought;
    private readonly int[][] _openingTicksFought;
    private readonly bool[][] _wasAlive;
    private int _ticksOfFighting;

    internal Fight(Match match, int[] players, EntityId[][] units, int[][] chassis)
    {
        Match = match;
        _players = players;
        _units = units;
        _chassis = chassis;
        _ticksFought = [new int[units[0].Length], new int[units[1].Length]];
        _openingTicksFought = [new int[units[0].Length], new int[units[1].Length]];
        _wasAlive = [new bool[units[0].Length], new bool[units[1].Length]];
        Array.Fill(_wasAlive[0], true);
        Array.Fill(_wasAlive[1], true);
    }

    public Match Match { get; }

    public int Player(Side side) => _players[(int)side];

    /// <summary>The units an army started with: its block rank by rank from the front, then the row behind.</summary>
    public ReadOnlySpan<EntityId> Units(Side side) => _units[(int)side];

    public int Living(Side side)
    {
        var living = 0;
        foreach (var unit in _units[(int)side])
        {
            living += Match.World.Entities.IsAlive(unit) ? 1 : 0;
        }

        return living;
    }

    /// <summary>Counts the attacks of the tick that has just run, and the attacks each army's living units had time for in it.</summary>
    internal void CountAttacks()
    {
        var attacks = new int[2];
        foreach (var simEvent in Match.Simulation.Events)
        {
            if (simEvent.Kind is SimEventKind.ShotFired or SimEventKind.MeleeHit)
            {
                attacks[simEvent.Player == _players[(int)Side.Mass] ? (int)Side.Mass : (int)Side.Elite]++;
            }
        }

        if (_ticksOfFighting == 0 && attacks[0] + attacks[1] == 0)
        {
            return;
        }

        _ticksOfFighting++;
        var opening = _ticksOfFighting <= Battle.OpeningSeconds * SimInfo.TicksPerSecond;

        // A unit that began the tick alive had the tick to attack in, also when it fell in it.
        for (var side = 0; side < 2; side++)
        {
            _attacks[side] += attacks[side];
            _openingAttacks[side] += opening ? attacks[side] : 0;
            for (var i = 0; i < _units[side].Length; i++)
            {
                _ticksFought[side][i] += _wasAlive[side][i] ? 1 : 0;
                _openingTicksFought[side][i] += opening && _wasAlive[side][i] ? 1 : 0;
                _wasAlive[side][i] = Match.World.Entities.IsAlive(_units[side][i]);
            }
        }
    }

    public ArmyResult Result(Side side)
    {
        var player = _players[(int)side];
        var blueprints = Match.Research.Tech(player).Blueprints;
        var (kept, price, priceKept, supply, attacksPossible, openingAttacksPossible) = (0, 0L, 0L, 0, 0, 0);
        for (var i = 0; i < _units[(int)side].Length; i++)
        {
            // A unit that fell is in no store any more, so its chassis is kept from the start.
            var unit = _units[(int)side][i];
            var blueprint = blueprints[_chassis[(int)side][i]];
            var unitPrice = blueprint.Materials + blueprint.Energy;
            price += unitPrice;
            supply += blueprint.Supply;
            var interval = blueprint.Attack.IntervalTicks;
            attacksPossible += (_ticksFought[(int)side][i] + interval - 1) / interval;
            openingAttacksPossible += (_openingTicksFought[(int)side][i] + interval - 1) / interval;
            if (Match.World.Entities.IsAlive(unit))
            {
                kept++;
                priceKept += unitPrice;
            }
        }

        return new ArmyResult(
            _units[(int)side].Length,
            kept,
            price,
            priceKept,
            supply,
            Match.Research.Tech(player).SupplyCap,
            _attacks[(int)side],
            attacksPossible,
            _openingAttacks[(int)side],
            openingAttacksPossible);
    }
}
