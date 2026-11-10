using Sim.Data;
using Sim.Numerics;

namespace Sim.Tech;

/// <summary>
/// The stat and price formulas of design 2.3, one function per quantity. Each multiplies exact
/// fractions and leaves the single rounding to its caller.
/// </summary>
internal static class UnitFormulas
{
    /// <summary>Hit points and damage are kept in thousandths.</summary>
    public const int Thousandths = 1_000;

    /// <summary>Rule 1: the player's tier, limited to the window around the chassis's epoch.</summary>
    public static int EffectiveTier(GameData data, int tier, int epoch)
    {
        var window = data.Rules.EffectiveTierWindow;
        return Math.Clamp(tier, epoch - window, epoch + window);
    }

    public static Fraction HitPoints(GameData data, LineDef line, int effectiveProtection) =>
        line.Base.HitPoints.ToFraction() * data.Rules.PerTier.HitPoints.ToFraction().Pow(effectiveProtection - 1);

    public static Fraction DamagePerSecond(GameData data, LineDef line, int effectiveFirepower) =>
        line.Base.DamagePerSecond.ToFraction() * data.Rules.PerTier.Damage.ToFraction().Pow(effectiveFirepower - 1);

    /// <summary>Damage per second times the attack interval (design 2.4).</summary>
    public static Fraction DamagePerHit(Fraction damagePerSecond, AttackDef attack) =>
        damagePerSecond * attack.IntervalTicks / SimInfo.TicksPerSecond;

    /// <summary>Tiles per tick, rounded once to the distance unit.</summary>
    public static Fixed SpeedPerTick(GameData data, LineDef line, int effectiveMobility) =>
        (line.Base.Speed.ToFraction() * data.Rules.PerTier.Speed.ToFraction().Pow(effectiveMobility - 1) / SimInfo.TicksPerSecond).ToFixed();

    /// <summary>
    /// What a line's price is multiplied by. <paramref name="qualityOffsets"/> is the sum of the
    /// Firepower, Protection and Mobility offsets.
    /// </summary>
    public static Fraction CostMultiplier(GameData data, int epoch, int qualityOffsets, int manufacturingOffset) =>
        data.Rules.PerStep.QualityCost.ToFraction().Pow(epoch - 1 + qualityOffsets)
        * data.Rules.PerStep.ManufacturingCost.ToFraction().Pow(manufacturingOffset);

    /// <summary>A price in whole resources, rounded once. Materials and Energy are each rounded on their own.</summary>
    public static int Price(int linePrice, Fraction costMultiplier) => checked((int)(costMultiplier * linePrice).RoundHalfUp());

    /// <summary>The exact build time in ticks, before rounding.</summary>
    public static Fraction BuildTicks(GameData data, LineDef line, int manufacturingOffset) =>
        data.Rules.PerStep.ManufacturingTime.ToFraction().Pow(manufacturingOffset) * line.Base.BuildTicks;

    /// <summary>A distance in tiles, rounded once to the distance unit.</summary>
    public static Fixed Distance(DataNumber tiles, DataNumber? multiplier = null) =>
        (multiplier is { } factor ? tiles.ToFraction() * factor.ToFraction() : tiles.ToFraction()).ToFixed();
}
