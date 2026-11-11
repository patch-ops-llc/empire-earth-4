using Sim.Combat;
using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Setup;
using Sim.Tech;
using Sim.Tests.Setup;
using Sim.Tests.Tech;

namespace Sim.Tests.Combat;

/// <summary>
/// Matches for combat tests: built by the real match setup from the design's own numbers
/// (<see cref="DesignData"/>), on a map made in code, with players who start with nothing. Each
/// test places its armies itself with <see cref="Spawn"/>, which is
/// <see cref="CombatSpawn.SpawnChassis"/>, before the first tick.
/// </summary>
/// <remarks>
/// Every player has every track at tier 1, so the chassis of epoch 1 fight with the values of the
/// design's tables: Hoplite (Guard, melee), Peltast (Line infantry, range 5), Hippeus
/// (Cavalry/Armor, melee) and Ballista (Artillery, range 9, minimum range 3).
/// </remarks>
internal static class CombatMatches
{
    public const string Hoplite = "hoplite";
    public const string Peltast = "peltast";
    public const string Hippeus = "hippeus";
    public const string Ballista = "ballista";

    public static GameData Data => DesignData.Data;

    public static Match Create(MapDef map, int players = 2, ulong seed = 1) =>
        MatchSetup.Create(Data, map, seed, [.. Enumerable.Repeat(PlayerSetup.WithoutStart, players)]);

    public static int Chassis(string id) => Data.Chassis.Single(chassis => chassis.Id == id).Index;

    public static int Line(string id) => Data.Lines.Single(line => line.Id == id).Index;

    public static EntityId Spawn(this Match match, int player, string chassis, string x, string y) =>
        match.SpawnChassis(player, Chassis(chassis), TestMatches.Point(x, y));

    /// <summary>A worker: it can be hit, belongs to no line, and never acts on its own.</summary>
    public static EntityId SpawnWorker(this Match match, int player, string x, string y) =>
        match.SpawnWorker(player, TestMatches.Point(x, y), MatchSetup.StartingWorkerSpeed);

    /// <summary>Gives a player other tiers than the starting ones. Call it before placing that player's units.</summary>
    public static void SetTiers(this Match match, int player, params int[] tiers) =>
        match.Combat.SetTech(player, TechResolver.Resolve(Data, tiers));

    public static ChassisBlueprint Blueprint(this Match match, int player, string chassis) =>
        match.Combat.TechOf(player).Blueprints[Chassis(chassis)];

    /// <summary>Queues an attack order for the next tick to run.</summary>
    public static void OrderAttack(this Match match, int player, EntityId target, params EntityId[] entities) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, AttackCommand(player, target, entities));

    /// <summary>Queues an attack-move order for the next tick to run.</summary>
    public static void OrderAttackMove(this Match match, int player, FixedVector2 target, params EntityId[] entities) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, AttackMoveCommand(player, target, entities));

    /// <summary>Queues a stop order for the next tick to run.</summary>
    public static void OrderStop(this Match match, int player, params EntityId[] entities) =>
        match.Simulation.Commands.Enqueue(match.World.Tick, new Command(CommandType.Stop, player, entities));

    public static Command AttackCommand(int player, EntityId target, params EntityId[] entities) =>
        new(CommandType.Attack, player, entities, targetEntity: target);

    public static Command AttackMoveCommand(int player, FixedVector2 target, params EntityId[] entities) =>
        new(CommandType.AttackMove, player, entities, target.X, target.Y);

    public static Command MoveCommand(int player, FixedVector2 target, params EntityId[] entities) =>
        new(CommandType.Move, player, entities, target.X, target.Y);

    public static void Run(this Match match, int ticks) => match.Simulation.Run(ticks);

    public static bool IsAlive(this Match match, EntityId entity) => match.World.Entities.IsAlive(entity);

    /// <summary>Hit points in thousandths.</summary>
    public static long HitPointsOf(this Match match, EntityId entity) => match.Health.Get(entity).HitPoints;

    public static void SetHitPoints(this Match match, EntityId entity, long thousandths) =>
        match.Health.Get(entity).HitPoints = thousandths;

    public static Combatant UnitOf(this Match match, EntityId entity) => match.Combatants.Get(entity);

    public static Targeting Targeting(this Match match) =>
        new(match.Grid, match.Bodies, match.Combatants, match.Health, match.SpatialIndex, match.Combat);

    /// <summary>The exact distance between two entities, squared, in the unit of <see cref="FixedVector2.DistanceSquaredRaw"/>.</summary>
    public static long DistanceSquared(this Match match, EntityId a, EntityId b) =>
        FixedVector2.DistanceSquaredRaw(match.PositionOf(a), match.PositionOf(b));

    /// <summary>The square of a distance given as decimal text, in the same unit.</summary>
    public static long Squared(string distance) => FixedVector2.SquareRaw(Fraction.Parse(distance).ToFixed());
}
