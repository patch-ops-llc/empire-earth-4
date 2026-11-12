using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// The run-up of Charge (design 2.3): two arrowheads in the color of the Mobility track before
/// the nose of every unit whose next attack is a charged one, because it has walked the distance
/// of its perk since its last attack. They point the way the unit faces, and go when it attacks.
/// </summary>
/// <remarks>
/// <para>
/// This is read from the unit and not from an event, since nothing happens in the match when a
/// charge is ready: the unit's chassis carries Charge for its owner, and its count of tiles
/// walked has reached the distance. It is drawn for every unit the world draws, which are the
/// player's own and those of another player that the player sees, like a health bar.
/// </para>
/// <para>
/// The arrowheads are solid while the unit walks and paler while it stands, so that a force on
/// its way to a fight reads as one that is charging and a force at rest stays quiet. Like an
/// accent badge they are sized on the screen and can be read at the widest zoom.
/// </para>
/// </remarks>
public partial class ChargeMarks : WorldLayer
{
    /// <summary>The side of the square the arrowheads are drawn in, in tiles, while the camera is near.</summary>
    public const float Side = 0.42f;

    // How far before the unit's middle the middle of the arrowheads is, and how high over the ground.
    private const float Ahead = PlaceholderMeshes.UnitRadius + 0.3f;
    private const float Height = 0.3f;

    private const float Walking = 1f;
    private const float Standing = 0.3f;
    private const float AppearsPerSecond = 6f;
    private const float ChangesPerSecond = 4f;

    private readonly Dictionary<EntityId, Charged> _ready = [];
    private readonly List<EntityId> _gone = [];
    private PerkGlyphs _glyphs = null!;
    private Color _color;
    private int _pass;

    /// <summary>The number of units marked as ready to charge after the last tick.</summary>
    public int Count => _ready.Count;

    /// <summary>True while a unit is marked as ready to charge.</summary>
    public bool Marks(EntityId unit) => _ready.ContainsKey(unit);

    protected override void Attach()
    {
        var match = View.Match;
        _color = TrackColors.Of(match.Data, match.Data.TrackRoles.Mobility).Lightened(0.5f);
        _glyphs = PerkGlyphs.Under(this, "Arrowheads", InstancedShapes.MapArea(match.Map));
    }

    public override void AfterTick()
    {
        var match = View.Match;
        _pass++;
        foreach (var entity in View.World.Units)
        {
            if (!match.Combatants.TryGet(entity, out var unit)
                || !match.Bodies.TryGet(entity, out var body)
                || !match.PerkCounts.TryGet(entity, out var counts)
                || match.Combat.ProfileOf(body.Owner, unit.Chassis).Charge is not { } charge
                || counts.Walked < charge.Distance)
            {
                continue;
            }

            var place = WorldView.ToWorld(body.Position);
            if (!_ready.TryGetValue(entity, out var ready))
            {
                ready = new Charged { Place = place };
                _ready.Add(entity, ready);
            }

            ready.Walks = place != ready.Place;
            ready.Place = place;
            if (match.Movers.TryGet(entity, out var mover))
            {
                ready.Heading = CombatEffects.HeadingOf(mover.Facing);
            }

            ready.Pass = _pass;
        }

        foreach (var (entity, ready) in _ready)
        {
            if (ready.Pass != _pass)
            {
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _ready.Remove(entity);
        }

        _gone.Clear();
    }

    public override void EveryFrame(double delta)
    {
        _glyphs.Begin();
        if (_ready.Count > 0 && GetViewport().GetCamera3D() is { } camera)
        {
            var basis = camera.GlobalBasis;
            foreach (var (entity, ready) in _ready)
            {
                if (!View.World.TryGetDrawnPosition(entity, out var position))
                {
                    continue;
                }

                // It comes quickly, and changes slowly between walking and standing.
                var wanted = ready.Walks ? Walking : Standing;
                var rate = ready.Shown < Standing ? AppearsPerSecond : ChangesPerSecond;
                ready.Shown = Mathf.MoveToward(ready.Shown, wanted, rate * (float)delta);

                // The unit shape's nose is toward -Z before it is turned.
                var along = new Basis(Vector3.Up, ready.Heading) * Vector3.Forward;
                _glyphs.Add(
                    PerkGlyph.Chevrons,
                    position + (along * Ahead) + (Vector3.Up * Height),
                    Side,
                    new Color(_color.R, _color.G, _color.B, ready.Shown),
                    PerkGlyphs.TurnOnScreen(basis, along));
            }
        }

        _glyphs.End();
    }

    private sealed class Charged
    {
        /// <summary>Where the unit stood after the last tick, on the ground.</summary>
        public Vector3 Place { get; set; }

        /// <summary>True when the unit moved in the last tick.</summary>
        public bool Walks { get; set; }

        public float Heading { get; set; }

        /// <summary>How solid the mark is drawn, from 0 to 1.</summary>
        public float Shown { get; set; }

        public int Pass { get; set; }
    }
}
