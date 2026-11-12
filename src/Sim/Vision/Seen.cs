using Sim.Core;
using Sim.Economy;
using Sim.Map;
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
/// A structure as a player may know it (design 5.3): one of its own, another player's that it
/// sees, or another player's that it remembers, as it was when last seen. A remembered structure
/// may have changed since, or be gone.
/// </summary>
/// <param name="Entity">The structure. An attack order may name it, also when it is remembered.</param>
/// <param name="Owner">The player it belongs to.</param>
/// <param name="Type">Its place in the roster, as in <see cref="Data.StructureDef.Index"/>.</param>
/// <param name="X">The x of the lowest tile it covers.</param>
/// <param name="Y">The y of the lowest tile it covers.</param>
/// <param name="Size">The tiles it covers along each side.</param>
/// <param name="HitPoints">Its hit points, in thousandths.</param>
/// <param name="MaxHitPoints">Its hit points at most, in thousandths.</param>
/// <param name="IsFinished">False while it is building itself.</param>
/// <param name="Built">The ticks it has built for, out of the <see cref="Data.StructureDef.BuildTicks"/> of its structure.</param>
/// <param name="LookTier">
/// Its owner's tier in the track whose look it shows (design 2.5 and 3.2), or zero for a
/// structure that looks the same at every tier.
/// </param>
/// <param name="Banner">
/// The track in research in the slot the structure gives, which its banner shows (design 3.2),
/// or <see cref="NoTrack"/>.
/// </param>
/// <param name="IsRemembered">
/// True when the player does not see it now: everything here is then as it was in
/// <see cref="SeenTick"/>.
/// </param>
/// <param name="SeenTick">The tick in which the player last saw it: the tick that runs next for one it sees now.</param>
public readonly record struct SeenStructure(
    EntityId Entity,
    int Owner,
    int Type,
    int X,
    int Y,
    int Size,
    long HitPoints,
    long MaxHitPoints,
    bool IsFinished,
    int Built,
    int LookTier,
    int Banner,
    bool IsRemembered,
    int SeenTick)
{
    /// <summary>The <see cref="Banner"/> of a structure that flies none.</summary>
    public const int NoTrack = RememberedStructure.NoTrack;

    /// <summary>The tiles it covers.</summary>
    public TileSquare Tiles => new(X, Y, Size);

    /// <summary>The middle of its footprint, where its body is.</summary>
    public FixedVector2 Center => Tiles.Center;
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
/// What inspecting a unit or a structure shows to anyone who sees it (design 3.2): its effective
/// Firepower, Protection and Mobility tiers and its accents. A structure has its owner's
/// Protection tier, its owner's Firepower tier when it attacks and zero when it does not, no
/// Mobility, which is zero, and no accents.
/// </summary>
public readonly record struct Inspection(int Firepower, int Protection, int Mobility, IReadOnlyList<Accent> Accents);
