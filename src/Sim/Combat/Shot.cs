using Sim.Core;
using Sim.Numerics;

namespace Sim.Combat;

/// <summary>
/// A ranged attack on its way (design 2.4). A shot is an entity of its own with no
/// <see cref="Map.Body"/>, so it is never found as a target and never selected. It follows its
/// target and hits it; nothing blocks it.
/// </summary>
/// <remarks>
/// A shot is settled in two steps. What is here is the attacker's side, fixed when the shot was
/// fired, which is why the shot still lands after its attacker has died. The target's side is
/// applied on impact, and the result is rounded once.
/// </remarks>
public struct Shot : IStateValue
{
    /// <summary>The name of the store that holds every shot in flight.</summary>
    public const string StoreName = "shots";

    public FixedVector2 Position { get; set; }

    /// <summary>The entity it follows. A shot whose target is gone is lost.</summary>
    public EntityId Target { get; set; }

    /// <summary>The distance it covers in one tick, in tiles.</summary>
    public Fixed Speed { get; set; }

    /// <summary>The attacker's damage of one hit, exactly, before anything the target applies.</summary>
    public Fraction Damage { get; set; }

    /// <summary>The player whose unit fired it.</summary>
    public int Owner { get; set; }

    /// <summary>The roster index of the attacker's line, which decides the counter multiplier on impact.</summary>
    public int Line { get; set; }

    /// <summary>The attacker's effective Firepower tier when it fired, for the rules that compare it on impact.</summary>
    public int Firepower { get; set; }

    public void Walk(IStateWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        Position = new FixedVector2(walker.Field(Position.X), walker.Field(Position.Y));
        Target = walker.Field(Target);
        Speed = walker.Field(Speed);
        Damage = walker.Field(Damage);
        Owner = walker.Field(Owner);
        Line = walker.Field(Line);
        Firepower = walker.Field(Firepower);
    }
}
