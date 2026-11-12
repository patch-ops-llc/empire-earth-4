using Sim.Core;
using Sim.Economy;
using Sim.Numerics;
using Sim.Tech;

namespace Sim.Vision;

/// <summary>
/// A unit as a player may know it: one of its own, or another player's that it sees. What an
/// enemy unit is doing, its order and its target, is not here: nobody sees that.
/// </summary>
/// <param name="Entity">The unit.</param>
/// <param name="Owner">The player it belongs to.</param>
/// <param name="Position">Its center, in tiles.</param>
/// <param name="Facing">Where it faces. Zero for something that cannot move.</param>
/// <param name="Chassis">The chassis, as its <see cref="Data.ChassisDef.Index"/>, or <see cref="NoChassis"/>.</param>
/// <param name="IsWorker">True for a worker, which is not of a chassis.</param>
/// <param name="HitPoints">Its hit points now, in thousandths. Zero for something that cannot be damaged.</param>
/// <param name="MaxHitPoints">Its hit points at most, in thousandths.</param>
public readonly record struct SeenUnit(
    EntityId Entity,
    int Owner,
    FixedVector2 Position,
    Angle Facing,
    int Chassis,
    bool IsWorker,
    long HitPoints,
    long MaxHitPoints)
{
    /// <summary>The <see cref="Chassis"/> of a unit that has none, such as a worker.</summary>
    public const int NoChassis = -1;
}

/// <summary>
/// A shot in flight as a player may know it: one its own unit fired, or another player's while it
/// is over a tile the player sees.
/// </summary>
/// <param name="Entity">The shot, the same entity that its <see cref="SimEventKind.ShotFired"/> named.</param>
/// <param name="Owner">The player whose unit fired it.</param>
/// <param name="Position">Where it is, in tiles.</param>
/// <param name="Line">The roster index of the line that fired it.</param>
public readonly record struct SeenShot(EntityId Entity, int Owner, FixedVector2 Position, int Line);

/// <summary>
/// A deposit or a Generator as a player may know it: a deposit on ground it has explored, or one
/// of its own Generators.
/// </summary>
/// <param name="Entity">The work site, which a <see cref="CommandType.Gather"/> names.</param>
/// <param name="Kind">A deposit or a Generator.</param>
/// <param name="X">The x of the lowest tile it covers.</param>
/// <param name="Y">The y of the lowest tile it covers.</param>
/// <param name="Size">The tiles it covers along each side.</param>
public readonly record struct SeenWorkSite(EntityId Entity, WorkSiteKind Kind, int X, int Y, int Size);

/// <summary>
/// What inspecting a unit shows to anyone who sees it (design 3.2): its effective Firepower,
/// Protection and Mobility tiers and its accents.
/// </summary>
public readonly record struct Inspection(int Firepower, int Protection, int Mobility, IReadOnlyList<Accent> Accents);
