using Godot;
using Sim;
using Sim.Core;

namespace Game;

/// <summary>
/// The mark of Regeneration (design 2.3): a small cross in the color of the Logistics track at
/// the left end of the health bar of every unit that is healing by itself.
/// </summary>
/// <remarks>
/// <para>
/// A unit is healing from the tick it is reported to have regenerated until a second and a
/// little have passed without another report, or until it is hit, or is whole again. Each
/// report is asked of the player's sight first, so the mark is on the player's own units and on
/// those of another player that regenerated where the player sees (design 5.3).
/// </para>
/// <para>
/// The mark is meant to be quiet. It stands still, comes and goes over a moment, and does not
/// flash with every report: an army of eighty that heals at once shows eighty small crosses
/// beside its bars and nothing that moves. Like an accent badge it is sized on the screen, so it
/// is a cross at the widest zoom too.
/// </para>
/// </remarks>
public partial class HealingMarks : WorldLayer
{
    /// <summary>The side of the square the cross is drawn in, in tiles, while the camera is near.</summary>
    public const float Side = 0.34f;

    // How long after a report a unit still counts as healing: the reports come once a second.
    private const int TicksKept = SimInfo.TicksPerSecond + 4;

    private const float AppearsPerSecond = 5f;
    private const float FadesPerSecond = 2.5f;

    // How far left of the bar's end the middle of the cross is, in sides of its square.
    private static readonly Vector2 Shift = new(-0.5f, 0f);

    private readonly Dictionary<EntityId, Healing> _healing = [];
    private readonly List<EntityId> _gone = [];
    private PerkGlyphs _glyphs = null!;
    private Color _color;

    /// <summary>The number of units marked as healing after the last tick.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var healing in _healing.Values)
            {
                count += healing.Until >= View.Match.World.Tick ? 1 : 0;
            }

            return count;
        }
    }

    /// <summary>The number of reports of a unit regenerating that the player was shown since the match began.</summary>
    public int ReportsShown { get; private set; }

    /// <summary>True while a unit is marked as healing.</summary>
    public bool Marks(EntityId unit) => _healing.TryGetValue(unit, out var healing) && healing.Until >= View.Match.World.Tick;

    protected override void Attach()
    {
        var match = View.Match;
        _color = TrackColors.Of(match.Data, match.Data.TrackRoles.Logistics).Lightened(0.5f);
        _glyphs = PerkGlyphs.Under(this, "Crosses", InstancedShapes.MapArea(match.Map));
    }

    public override void AfterTick()
    {
        var match = View.Match;
        var tick = match.World.Tick;
        foreach (var happened in match.Simulation.Events)
        {
            if (!View.Sight.Knows(happened))
            {
                continue;
            }

            switch (happened.Kind)
            {
                case SimEventKind.Regenerated:
                    ReportsShown++;
                    if (!_healing.TryGetValue(happened.Entity, out var healing))
                    {
                        healing = new Healing();
                        _healing.Add(happened.Entity, healing);
                    }

                    healing.Until = tick + TicksKept;
                    break;

                // A hit ends it: the unit heals again only after its perk's delay.
                case SimEventKind.MeleeHit:
                case SimEventKind.ShotHit:
                case SimEventKind.SplashHit:
                    if (_healing.TryGetValue(happened.Entity, out var hit))
                    {
                        hit.Until = tick - 1;
                    }

                    break;

                default:
                    break;
            }
        }

        foreach (var (entity, healing) in _healing)
        {
            if (!match.Health.TryGet(entity, out var health))
            {
                _gone.Add(entity);
            }
            else
            {
                // Whole again: the last report was the last. And a unit that has fewer hit
                // points than a tick ago was hit, whether the player was told of the hit or not.
                if (health.HitPoints >= health.Maximum || health.HitPoints < healing.HitPoints)
                {
                    healing.Until = Math.Min(healing.Until, tick - 1);
                }

                healing.HitPoints = health.HitPoints;
            }
        }

        foreach (var entity in _gone)
        {
            _healing.Remove(entity);
        }

        _gone.Clear();
    }

    public override void EveryFrame(double delta)
    {
        _glyphs.Begin();
        if (_healing.Count > 0 && GetViewport().GetCamera3D() is { } camera)
        {
            var tick = View.Match.World.Tick;
            var left = camera.GlobalBasis.X * ((HealthBars.Width / 2f) + HealthBars.Edge);
            var lift = Vector3.Up * (PlaceholderMeshes.UnitHeight + HealthBars.Lift);
            foreach (var (entity, healing) in _healing)
            {
                // A unit that is no longer drawn has gone out of the player's sight, and its mark with it.
                if (!View.World.TryGetDrawnPosition(entity, out var position))
                {
                    _gone.Add(entity);
                    continue;
                }

                var rate = healing.Until >= tick ? AppearsPerSecond : -FadesPerSecond;
                healing.Shown = Mathf.Clamp(healing.Shown + (rate * (float)delta), 0f, 1f);
                if (healing.Shown <= 0f)
                {
                    if (healing.Until < tick)
                    {
                        _gone.Add(entity);
                    }

                    continue;
                }

                _glyphs.Add(PerkGlyph.Plus, position + lift - left, Side, new Color(_color.R, _color.G, _color.B, healing.Shown), shift: Shift);
            }

            foreach (var entity in _gone)
            {
                _healing.Remove(entity);
            }

            _gone.Clear();
        }

        _glyphs.End();
    }

    private sealed class Healing
    {
        /// <summary>The last tick in which the unit counts as healing.</summary>
        public int Until { get; set; }

        /// <summary>The unit's hit points after the last tick, in thousandths.</summary>
        public long HitPoints { get; set; }

        /// <summary>How solid the mark is drawn, from 0 to 1.</summary>
        public float Shown { get; set; }
    }
}
