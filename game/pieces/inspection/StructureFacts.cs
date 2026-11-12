using Sim.Core;
using Sim.Data;
using Sim.Numerics;
using Sim.Tech;
using Sim.Vision;

namespace Game;

/// <summary>
/// What one structure is, as the inspection panel shows it (design 3.2): whose it is, its hit
/// points, how far it is built, the tier its look shows, the track on its banner, and, while
/// the player may know them, its owner's Protection tier and the Firepower tier it attacks with.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is what the local player may know (design 5.3). The structure is read from
/// the view's <see cref="PlayerSight"/>: one of another player's that the player does not see
/// now is as it was when last seen, and one the player has never seen is not read at all.
/// </para>
/// <para>
/// The tiers are those the rules hand out for a structure the player sees
/// (<see cref="PlayerView.Inspect"/>), which the player's own always are. The rules keep no tier
/// for a structure the player only remembers, so a memory has none: <see cref="HasTiers"/> is
/// false, and what is left is what the player saw of it, its look and its banner among it.
/// </para>
/// </remarks>
public readonly record struct StructureFacts
{
    public required EntityId Entity { get; init; }

    public required int Owner { get; init; }

    public required StructureDef Structure { get; init; }

    /// <summary>Hit points, in thousandths: now, or when the player last saw the structure.</summary>
    public long HitPoints { get; init; }

    /// <summary>Hit points when undamaged, in thousandths: now, or when the player last saw the structure.</summary>
    public long Maximum { get; init; }

    /// <summary>False while the structure is building itself, as far as the player knows.</summary>
    public bool IsFinished { get; init; }

    /// <summary>The ticks it has built itself for, as far as the player knows.</summary>
    public int Built { get; init; }

    /// <summary>True for a structure of another player's that the player does not see now.</summary>
    public bool IsRemembered { get; init; }

    /// <summary>The ticks since the player last saw a remembered structure. Zero for one seen now.</summary>
    public int SeenAgo { get; init; }

    /// <summary>
    /// The owner's tier in the track the structure's look shows (design 2.5), now or when last
    /// seen. Zero for a structure that looks the same at every tier.
    /// </summary>
    public int LookTier { get; init; }

    /// <summary>The track on the structure's banner, which is in research in its slot, or <see cref="SeenStructure.NoTrack"/>.</summary>
    public required int Banner { get; init; }

    /// <summary>True when the tiers below are known: for a structure the player sees.</summary>
    public bool HasTiers { get; init; }

    /// <summary>The owner's Protection tier, which an attacker's Firepower is measured against. Meaningful with <see cref="HasTiers"/>.</summary>
    public int Protection { get; init; }

    /// <summary>The owner's Firepower tier for a structure that attacks, and zero for one that does not. Meaningful with <see cref="HasTiers"/>.</summary>
    public int Firepower { get; init; }

    /// <summary>How the structure attacks under its owner's tiers, or null for one that does not attack or whose tiers are not known.</summary>
    public StructureAttackBlueprint? Attack { get; init; }

    /// <summary>What is left of a hit from an attacker whose Firepower is below <see cref="Protection"/> (design 2.5).</summary>
    public Fraction Fortification { get; init; }

    /// <summary>True when the structure's look shows the owner's Protection tier, as a Tower's does.</summary>
    public bool LooksOfProtection(GameData data) => Structure.Track == data.TrackRoles.Protection;

    /// <summary>
    /// Reads a structure as the local player knows it. False for anything that is no structure,
    /// and for a structure of another player's that the player neither sees nor remembers.
    /// </summary>
    public static bool TryRead(MatchView view, EntityId entity, out StructureFacts facts)
    {
        facts = default;
        var match = view.Match;
        if (!view.Sight.TryGetStructure(entity, out var seen) || match.Data.StructureOf(seen.Type) is not { } structure)
        {
            return false;
        }

        // The rules answer for a structure the player sees, their own among them. What follows
        // from the tiers they give, as a Tower's damage, is in the owner's blueprint.
        var blueprint = match.Research.Tech(seen.Owner).StructureOf(seen.Type);
        if (view.Sight.Seen.Inspect(seen.Entity, out var tiers))
        {
            facts = Of(structure, in seen, blueprint, match.Combat.Fortification, match.World.Tick) with
            {
                HasTiers = true,
                Protection = tiers.Protection,
                Firepower = tiers.Firepower,
            };
            return true;
        }

        // DEVELOPMENT ONLY. With the switch the fog is off the screen, and a structure the
        // player does not see is shown as it stands.
        var lifted = view.Sight.FogOff && !seen.IsRemembered;
        facts = Of(structure, in seen, lifted ? blueprint : null, match.Combat.Fortification, match.World.Tick);
        return true;
    }

    /// <summary>What <see cref="TryRead"/> makes of a structure as a player knows it.</summary>
    /// <param name="structure">The structure of the roster it is.</param>
    /// <param name="seen">The structure as the player sees or remembers it.</param>
    /// <param name="built">Its owner's blueprint of it when the player may know its tiers, and null otherwise.</param>
    /// <param name="fortification">What is left of a hit from an attacker below the owner's Protection tier.</param>
    /// <param name="tick">The world's tick, which the time since it was last seen is counted to.</param>
    public static StructureFacts Of(StructureDef structure, in SeenStructure seen, StructureBlueprint? built, Fraction fortification, int tick) => new()
    {
        Entity = seen.Entity,
        Owner = seen.Owner,
        Structure = structure,
        HitPoints = seen.HitPoints,
        Maximum = seen.MaxHitPoints,
        IsFinished = seen.IsFinished,
        Built = seen.Built,
        IsRemembered = seen.IsRemembered,
        SeenAgo = seen.IsRemembered ? Math.Max(0, tick - seen.SeenTick) : 0,
        LookTier = seen.LookTier,
        Banner = seen.Banner,
        HasTiers = built is not null,
        Protection = built?.ProtectionTier ?? 0,
        Firepower = built?.Attack?.EffectiveFirepower ?? 0,
        Attack = built?.Attack,
        Fortification = fortification,
    };
}
