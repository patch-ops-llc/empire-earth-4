using Godot;
using Sim.Core;
using Sim.Data;

namespace Game;

/// <summary>
/// What a fight looks like on a structure (16b): smoke and then fire over one that has lost hit
/// points, a white flash and a spark for every hit, the fall of one that is destroyed, and the
/// line round one the player has just ordered an attack on.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is drawn over what the <see cref="StructureLayer"/> draws, from
/// <see cref="StructureLayer.CollectDrawn"/> and from the events of each tick that the player
/// may know of (design 5.3). So a structure the player has never seen shows nothing, and a
/// fight over a structure out of the player's sight leaves nothing on the screen.
/// </para>
/// <list type="bullet">
/// <item><b>Smoke and fire.</b> A structure with less than two thirds of its hit points smokes,
/// more the fewer it has, and under two fifths it burns. Only a structure the player sees now
/// does: a remembered one is a picture of when it was last seen, darkened as it was then, and
/// nothing moves on it.</item>
/// <item><b>A hit.</b> A structure the player sees flashes white in the tick it loses hit
/// points. A shot leaves a spark where it went into the structure, which the
/// <see cref="ShotLayer"/> knows; the spark of a melee hit is the <see cref="CombatEffects"/>'
/// own, at the point of the footprint the simulation names.</item>
/// <item><b>A structure destroyed.</b> Its shape sinks and fades in a cloud of dust, inside a
/// ring in its owner's color, and leaves rubble that fades after a while. This is drawn when
/// the player sees the structure removed in the tick its hit points ran out. A structure that
/// is destroyed out of the player's sight shows nothing: it stays on the screen as the player
/// remembers it, and that memory goes without a sound when the player sees its tiles again.</item>
/// </list>
/// <para>
/// Effects are for the eye only: nothing reads them, and a match runs the same with or without
/// them. Where they scatter, they scatter by the number of the structure and not by chance, so
/// that two machines draw the same.
/// </para>
/// </remarks>
public partial class StructureEffects : WorldLayer
{
    /// <summary>A structure with a smaller share of its hit points than this smokes.</summary>
    public const float SmokesBelow = 2f / 3f;

    /// <summary>A structure with a smaller share of its hit points than this burns.</summary>
    public const float BurnsBelow = 0.4f;

    // A structure is often hit many times a second, by everyone who attacks it. Its flash is
    // short and faint, and one that has only begun is not begun again, so that the structure
    // flickers under many hits and its darkened look can still be seen.
    private const float FlashSeconds = 0.12f;
    private const float FlashStrength = 0.4f;
    private const float FallSeconds = 1.3f;
    private const float RubbleSeconds = 9f;
    private const float MarkSeconds = 1.2f;

    // The line of the mark runs outside the tiles, where the structure does not hide it.
    private const float MarkOutside = 0.12f;
    private const float MarkWidth = 0.1f;
    private const int PuffsInAPlume = 5;

    // How much of the footprint the box takes that a shot is drawn going into.
    private const float BodyShare = 0.8f;

    private static readonly Color SparkColor = new(1f, 0.95f, 0.75f);
    private static readonly Color SmokeDark = new(0.13f, 0.12f, 0.12f);
    private static readonly Color SmokePale = new(0.5f, 0.49f, 0.48f);
    private static readonly Color Flame = new(1f, 0.52f, 0.1f);
    private static readonly Color FlameCore = new(1f, 0.88f, 0.35f);
    private static readonly Color DustColor = new(0.66f, 0.61f, 0.54f);
    private static readonly Color RubbleColor = new(0.21f, 0.2f, 0.19f);
    private static readonly Vector3 Wind = new(0.4f, 0f, 0.18f);

    private readonly StructureLayer _structures;
    private readonly ShotLayer? _shots;

    // What is drawn now, and what was drawn after the tick before this one: an event may be
    // about a structure that the structure layer has taken away already.
    private readonly List<DrawnStructure> _drawn = [];
    private readonly Dictionary<EntityId, DrawnStructure> _before = [];

    private readonly Dictionary<int, ArrayMesh> _meshes = [];
    private readonly Dictionary<int, ArrayMesh> _outlines = [];
    private readonly Dictionary<EntityId, Flash> _flashes = [];
    private readonly List<EntityId> _flashed = [];
    private readonly List<Fall> _falls = [];
    private readonly List<Puff> _puffs = [];
    private InstancedShapes _balls = null!;
    private InstancedShapes _rings = null!;
    private InstancedShapes _blocks = null!;
    private MeshInstance3D _mark = null!;
    private StandardMaterial3D _markColor = null!;
    private double _markLeft;
    private double _time;

    /// <summary>Makes the effects over the structures a layer draws. The shots, when given, say where one went into a structure.</summary>
    public StructureEffects(StructureLayer structures, ShotLayer? shots)
    {
        _structures = structures;
        _shots = shots;
    }

    /// <summary>The number of times a structure was shown flashing for a loss of hit points since the match began.</summary>
    public int HitsShown { get; private set; }

    /// <summary>The number of shots shown going into a structure since the match began.</summary>
    public int SparksShown { get; private set; }

    /// <summary>The number of structures shown falling since the match began.</summary>
    public int FallsShown { get; private set; }

    /// <summary>The number of structures that smoked in the last frame.</summary>
    public int SmokingCount { get; private set; }

    /// <summary>The number of structures that burned in the last frame.</summary>
    public int BurningCount { get; private set; }

    /// <summary>True when this structure smokes as it is drawn now: the player sees it, with few enough hit points.</summary>
    public bool Smokes(EntityId entity) => HealthSeenNow(entity) < SmokesBelow;

    /// <summary>True when this structure burns as it is drawn now: the player sees it, with few enough hit points.</summary>
    public bool Burns(EntityId entity) => HealthSeenNow(entity) < BurnsBelow;

    /// <summary>The structure the attack mark is round, or none when it is not shown.</summary>
    public EntityId Marked { get; private set; }

    protected override void Attach()
    {
        var area = InstancedShapes.MapArea(View.Match.Map);
        var fading = InstancedShapes.Material(seeThrough: true);
        _blocks = InstancedShapes.Under(this, "Rubble", new BoxMesh { Size = Vector3.One }, fading, area);
        _rings = InstancedShapes.Under(this, "Rings", PlaceholderMeshes.Ring(1f, Colors.White), fading, area);
        _balls = InstancedShapes.Under(this, "SmokeAndDust", new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 10, Rings = 5 }, fading, area);
        _markColor = new StandardMaterial3D
        {
            AlbedoColor = CombatOrders.AttackMark,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _mark = new MeshInstance3D
        {
            Name = "AttackMark",
            Visible = false,
            MaterialOverride = _markColor,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_mark);
    }

    /// <summary>
    /// Shows the mark of an attack order round a structure the layer draws, as it stands or as a
    /// memory: a line round its tiles, in the color of the attack order, that closes on it and goes.
    /// </summary>
    public void MarkAttacked(EntityId entity)
    {
        _drawn.Clear();
        _structures.CollectDrawn(_drawn);
        foreach (var structure in _drawn)
        {
            if (structure.Entity != entity)
            {
                continue;
            }

            if (!_outlines.TryGetValue(structure.Size, out var outline))
            {
                outline = MarkLine(structure.Size);
                _outlines.Add(structure.Size, outline);
            }

            _mark.Mesh = outline;
            _mark.Position = structure.Middle + (Vector3.Up * 0.08f);
            _mark.Visible = true;
            _markLeft = MarkSeconds;
            Marked = entity;
            return;
        }
    }

    public override void AfterTick()
    {
        var events = View.Match.Simulation.Events;
        foreach (var happened in events)
        {
            if (!View.Sight.Knows(happened) || !_before.TryGetValue(happened.Entity, out var structure))
            {
                continue;
            }

            switch (happened.Kind)
            {
                case SimEventKind.ShotHit when !structure.IsRemembered:
                    SparksShown++;
                    _puffs.Add(new Puff(PuffKind.Spark, WhereItWentIn(in structure, happened.Other), default, 0.25f));
                    break;

                // A structure whose hit points ran out is reported dead before it is reported
                // removed. One that is taken away for another reason, as a foundation whose
                // builder cancels it, does not fall.
                case SimEventKind.StructureRemoved when HasDied(happened.Entity):
                    FallsShown++;
                    AddFall(in structure);
                    break;

                default:
                    break;
            }
        }

        // A structure flashes when the player sees it lose hit points: it was seen a tick ago
        // and is seen now with fewer. One that comes back into sight with fewer does not, for
        // nobody saw the hit.
        _drawn.Clear();
        _structures.CollectDrawn(_drawn);
        foreach (var structure in _drawn)
        {
            if (!structure.IsRemembered
                && _before.TryGetValue(structure.Entity, out var was)
                && !was.IsRemembered
                && structure.Health < was.Health)
            {
                HitsShown++;
                AddFlash(in structure);
            }
        }

        _before.Clear();
        foreach (var structure in _drawn)
        {
            _before[structure.Entity] = structure;
        }

        bool HasDied(EntityId entity)
        {
            foreach (var happened in events)
            {
                if (happened.Kind == SimEventKind.Death && happened.Entity == entity)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public override void EveryFrame(double delta)
    {
        _time += delta;
        _balls.Begin();
        _rings.Begin();
        _blocks.Begin();

        DrawSmoke();
        DrawFlashes((float)delta);
        DrawFalls((float)delta);
        DrawPuffs((float)delta);
        DrawMark(delta);

        _balls.End();
        _rings.End();
        _blocks.End();
    }

    // The share of its hit points a structure is drawn with, or all of them for one that is not
    // drawn or is drawn as a memory, on which nothing moves.
    private float HealthSeenNow(EntityId entity)
    {
        _drawn.Clear();
        _structures.CollectDrawn(_drawn);
        foreach (var structure in _drawn)
        {
            if (structure.Entity == entity)
            {
                return structure.IsRemembered ? 1f : structure.Health;
            }
        }

        return 1f;
    }

    // Smoke rises from one, two or three places on a damaged structure, each a string of puffs
    // that grow and thin out as they rise and drift. Nothing is kept of them: where each puff
    // is follows from the time alone.
    private void DrawSmoke()
    {
        SmokingCount = 0;
        BurningCount = 0;
        _drawn.Clear();
        _structures.CollectDrawn(_drawn);
        foreach (var structure in _drawn)
        {
            if (structure.IsRemembered || structure.Health >= SmokesBelow)
            {
                continue;
            }

            SmokingCount++;
            var burns = structure.Health < BurnsBelow;
            BurningCount += burns ? 1 : 0;
            var plumes = structure.Size == 1 ? 1 : structure.Health < 0.2f ? 3 : burns ? 2 : 1;
            for (var plume = 0; plume < plumes; plume++)
            {
                var seed = (structure.Entity.Value * 31) + plume;
                var foot = structure.Middle + new Vector3(
                    (Scatter(seed) - 0.5f) * structure.Size * 0.45f,
                    structure.Height * Mathf.Lerp(0.55f, 0.95f, Scatter(seed + 7)),
                    (Scatter(seed + 13) - 0.5f) * structure.Size * 0.45f);
                var scale = 0.75f + (0.15f * structure.Size);
                for (var puff = 0; puff < PuffsInAPlume; puff++)
                {
                    var age = Mathf.PosMod((float)(_time * 0.42) + (puff / (float)PuffsInAPlume) + Scatter(seed + 3), 1f);
                    var at = foot + (Vector3.Up * (1.7f * age)) + (Wind * age * age);
                    var solid = 0.55f * (1f - age) * Mathf.Min(1f, age * 8f);
                    AddBall(at, scale * Mathf.Lerp(0.22f, 0.75f, age), SmokeDark.Lerp(SmokePale, age), solid);
                }

                if (burns)
                {
                    var flicker = 0.5f + (0.5f * Mathf.Sin((float)(_time * 19.0) + (seed * 1.7f)));
                    AddBall(foot + (Vector3.Up * 0.08f), scale * Mathf.Lerp(0.3f, 0.42f, flicker), Flame, 0.85f);
                    AddBall(foot + (Vector3.Up * 0.14f), scale * Mathf.Lerp(0.14f, 0.22f, 1f - flicker), FlameCore, 0.9f);
                }
            }
        }
    }

    // A white copy of the structure's shape, just outside it, that fades.
    private void AddFlash(in DrawnStructure structure)
    {
        if (_flashes.TryGetValue(structure.Entity, out var flash))
        {
            if (flash.Age > FlashSeconds / 2f)
            {
                flash.Age = 0f;
            }

            return;
        }

        if (MeshOf(structure.Type) is not { } mesh)
        {
            return;
        }

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, FlashStrength),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var node = new MeshInstance3D
        {
            Name = $"Flash{structure.Entity.Value}",
            Mesh = mesh,
            MaterialOverride = material,
            Position = structure.Middle,
            Scale = new Vector3(1.05f, 1.03f, 1.05f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        _flashes.Add(structure.Entity, new Flash(node, material));
    }

    private void DrawFlashes(float delta)
    {
        foreach (var (entity, flash) in _flashes)
        {
            flash.Age += delta;
            if (flash.Age >= FlashSeconds)
            {
                flash.Node.QueueFree();
                _flashed.Add(entity);
                continue;
            }

            flash.Material.AlbedoColor = new Color(1f, 1f, 1f, FlashStrength * (1f - (flash.Age / FlashSeconds)));
        }

        foreach (var entity in _flashed)
        {
            _flashes.Remove(entity);
        }

        _flashed.Clear();
    }

    // The structure's shape, scorched, which sinks and fades; dust that rises from its tiles;
    // a ring in its owner's color; and the rubble it leaves.
    private void AddFall(in DrawnStructure structure)
    {
        if (_flashes.Remove(structure.Entity, out var flash))
        {
            flash.Node.QueueFree();
        }

        MeshInstance3D? node = null;
        StandardMaterial3D? walls = null;
        StandardMaterial3D? roof = null;
        if (MeshOf(structure.Type) is { } mesh)
        {
            walls = Fading(StructureLayer.Worn(StructureLayer.WallColorOf(structure.Owner), 0f));
            roof = Fading(StructureLayer.Worn(PlayerColors.Of(structure.Owner), 0f, roof: true));
            node = new MeshInstance3D
            {
                Name = $"Fall{structure.Entity.Value}",
                Mesh = mesh,
                Position = structure.Middle,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            node.SetSurfaceOverrideMaterial(StructureShapes.WallsSurface, walls);
            node.SetSurfaceOverrideMaterial(StructureShapes.RoofSurface, roof);
            AddChild(node);
        }

        _falls.Add(new Fall(structure, node, walls, roof));

        var seed = structure.Entity.Value * 131;
        var clouds = 5 + (4 * structure.Size);
        for (var i = 0; i < clouds; i++)
        {
            var at = structure.Middle + new Vector3(
                (Scatter(seed + (i * 3)) - 0.5f) * structure.Size,
                structure.Height * 0.5f * Scatter(seed + (i * 3) + 1),
                (Scatter(seed + (i * 3) + 2) - 0.5f) * structure.Size);
            var away = new Vector3(at.X - structure.Middle.X, 0f, at.Z - structure.Middle.Z) * 0.9f;
            _puffs.Add(new Puff(PuffKind.Dust, at, away + (Vector3.Up * Mathf.Lerp(0.5f, 1.3f, Scatter(seed + i + 50))), Mathf.Lerp(0.9f, 1.6f, Scatter(seed + i + 90)))
            {
                Age = -0.45f * Scatter(seed + i + 70),
                Size = 0.5f + (0.25f * structure.Size),
            });
        }
    }

    private void DrawFalls(float delta)
    {
        for (var i = _falls.Count - 1; i >= 0; i--)
        {
            var fall = _falls[i];
            fall.Age += delta;
            var structure = fall.Structure;
            if (fall.Node is { } node)
            {
                var gone = fall.Age / FallSeconds;
                if (gone >= 1f)
                {
                    node.QueueFree();
                    fall.Node = null;
                }
                else
                {
                    // It gives way slowly and then all at once, and leans as it goes.
                    var sunk = gone * gone;
                    node.Scale = new Vector3(1f, Mathf.Lerp(1f, 0.05f, sunk), 1f);
                    node.Rotation = new Vector3(0.12f * sunk * (Scatter(structure.Entity.Value) - 0.5f), 0f, 0.2f * sunk * (Scatter(structure.Entity.Value + 5) - 0.5f));
                    var solid = 1f - Mathf.Clamp((gone - 0.45f) / 0.55f, 0f, 1f);
                    fall.Walls!.AlbedoColor = WithAlpha(fall.Walls.AlbedoColor, solid);
                    fall.Roof!.AlbedoColor = WithAlpha(fall.Roof.AlbedoColor, solid);
                }
            }

            if (fall.Age < 0.8f)
            {
                var spread = fall.Age / 0.8f;
                var reach = structure.Size * Mathf.Lerp(0.45f, 1.05f, spread);
                _rings.Add(
                    new Transform3D(Basis.FromScale(new Vector3(reach, 0.25f, reach)), structure.Middle + (Vector3.Up * 0.05f)),
                    WithAlpha(PlayerColors.Of(structure.Owner), 1f - spread));
            }

            if (fall.Age >= RubbleSeconds)
            {
                _falls.RemoveAt(i);
                continue;
            }

            DrawRubble(in structure, fall.Age);
        }
    }

    // A dark patch on the structure's tiles with a few blocks on it, which comes as the
    // structure sinks and fades at the end.
    private void DrawRubble(in DrawnStructure structure, float age)
    {
        var solid = 0.9f * Mathf.Min(Mathf.Clamp(age / FallSeconds, 0f, 1f), Mathf.Clamp((RubbleSeconds - age) / 2.5f, 0f, 1f));
        var size = structure.Size * 0.84f;
        _blocks.Add(new Transform3D(Basis.FromScale(new Vector3(size, 0.07f, size)), structure.Middle + (Vector3.Up * 0.035f)), WithAlpha(RubbleColor, solid));

        var seed = structure.Entity.Value * 17;
        var blocks = 2 + (2 * structure.Size);
        for (var i = 0; i < blocks; i++)
        {
            var side = Mathf.Lerp(0.14f, 0.3f, Scatter(seed + (i * 5)));
            var at = structure.Middle + new Vector3(
                (Scatter(seed + (i * 5) + 1) - 0.5f) * size * 0.85f,
                0.07f + (side * 0.35f),
                (Scatter(seed + (i * 5) + 2) - 0.5f) * size * 0.85f);
            var turned = new Basis(Vector3.Up, Scatter(seed + (i * 5) + 3) * Mathf.Tau).Scaled(new Vector3(side * 1.4f, side * 0.7f, side));
            _blocks.Add(new Transform3D(turned, at), WithAlpha(RubbleColor.Lightened(0.12f + (0.2f * Scatter(seed + (i * 5) + 4))), solid));
        }
    }

    private void DrawPuffs(float delta)
    {
        for (var i = _puffs.Count - 1; i >= 0; i--)
        {
            var puff = _puffs[i];
            puff.Age += delta;
            if (puff.Age >= puff.Life)
            {
                _puffs.RemoveAt(i);
                continue;
            }

            // A cloud of dust waits its turn, so that the dust comes in a wave and not at once.
            if (puff.Age < 0f)
            {
                continue;
            }

            var gone = puff.Age / puff.Life;
            switch (puff.Kind)
            {
                case PuffKind.Spark:
                    AddBall(puff.At, Mathf.Lerp(0.16f, 0.55f, gone), SparkColor, 1f - gone);
                    break;

                default:
                    // Fast at first and slower as it spreads.
                    var eased = 1f - ((1f - gone) * (1f - gone));
                    AddBall(puff.At + (puff.Way * eased), puff.Size * Mathf.Lerp(0.5f, 1.5f, eased), DustColor, 0.6f * (1f - gone));
                    break;
            }
        }
    }

    private void DrawMark(double delta)
    {
        if (Marked == EntityId.None)
        {
            return;
        }

        _markLeft -= delta;
        if (_markLeft <= 0.0)
        {
            _mark.Visible = false;
            Marked = EntityId.None;
            return;
        }

        // It closes on the structure, stays there and fades.
        var gone = 1f - (float)(_markLeft / MarkSeconds);
        var size = Mathf.Lerp(1.5f, 1f, Mathf.Min(gone * 5f, 1f));
        _mark.Scale = new Vector3(size, 1f, size);
        _markColor.AlbedoColor = WithAlpha(CombatOrders.AttackMark, Mathf.Min(1f, 2.5f * (1f - gone)));
    }

    // A flat line round a square of tiles, a little outside them.
    private static ArrayMesh MarkLine(int size)
    {
        var inner = (size / 2f) + MarkOutside;
        var outer = inner + MarkWidth;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        tool.SetNormal(Vector3.Up);
        AddFlat(tool, -outer, -outer, outer, -inner);
        AddFlat(tool, -outer, inner, outer, outer);
        AddFlat(tool, -outer, -inner, -inner, inner);
        AddFlat(tool, inner, -inner, outer, inner);
        return tool.Commit();
    }

    private static void AddFlat(SurfaceTool tool, float fromX, float fromZ, float toX, float toZ)
    {
        tool.AddVertex(new Vector3(fromX, 0f, fromZ));
        tool.AddVertex(new Vector3(toX, 0f, fromZ));
        tool.AddVertex(new Vector3(toX, 0f, toZ));

        tool.AddVertex(new Vector3(fromX, 0f, fromZ));
        tool.AddVertex(new Vector3(toX, 0f, toZ));
        tool.AddVertex(new Vector3(fromX, 0f, toZ));
    }

    // Where the last stretch of a shot, as it is drawn, goes into the structure. A shot that is
    // not drawn, or that began inside, is shown landing on the roof.
    private Vector3 WhereItWentIn(in DrawnStructure structure, EntityId shot)
    {
        var half = structure.Size * BodyShare / 2f;
        var low = structure.Middle - new Vector3(half, 0f, half);
        var high = structure.Middle + new Vector3(half, structure.Height, half);
        if (_shots is not null
            && _shots.TryGetLanding(shot, out var from, out var to)
            && (to - from).LengthSquared() > 0.0001f
            && StructurePicker.TryEnter(from, to - from, low, high, out var distance)
            && distance > 0f
            && distance <= 1f)
        {
            return from + ((to - from) * distance);
        }

        return structure.Middle + (Vector3.Up * structure.Height);
    }

    private ArrayMesh? MeshOf(int type)
    {
        if (_meshes.TryGetValue(type, out var mesh))
        {
            return mesh;
        }

        if (View.Match.Data.StructureOf(type) is not StructureDef structure)
        {
            return null;
        }

        mesh = StructureShapes.MeshOf(structure);
        _meshes.Add(type, mesh);
        return mesh;
    }

    private void AddBall(Vector3 center, float size, Color color, float alpha) =>
        _balls.Add(new Transform3D(Basis.FromScale(Vector3.One * size), center), WithAlpha(color, alpha));

    private static StandardMaterial3D Fading(Color color)
    {
        var material = PlaceholderMeshes.Flat(color);
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return material;
    }

    private static Color WithAlpha(Color color, float alpha) => new(color.R, color.G, color.B, alpha);

    // A number from 0 to 1 that follows from a whole number and looks like chance.
    private static float Scatter(int seed)
    {
        unchecked
        {
            var mixed = (uint)seed * 2654435761u;
            mixed ^= mixed >> 15;
            mixed *= 2246822519u;
            mixed ^= mixed >> 13;
            return (mixed & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private enum PuffKind
    {
        Spark,
        Dust,
    }

    private sealed class Puff(PuffKind kind, Vector3 at, Vector3 way, float life)
    {
        public PuffKind Kind { get; } = kind;

        public Vector3 At { get; } = at;

        /// <summary>How far it goes in its life.</summary>
        public Vector3 Way { get; } = way;

        public float Life { get; } = life;

        public float Age { get; set; }

        public float Size { get; init; } = 1f;
    }

    private sealed class Flash(MeshInstance3D node, StandardMaterial3D material)
    {
        public MeshInstance3D Node { get; } = node;

        public StandardMaterial3D Material { get; } = material;

        public float Age { get; set; }
    }

    private sealed class Fall(DrawnStructure structure, MeshInstance3D? node, StandardMaterial3D? walls, StandardMaterial3D? roof)
    {
        public DrawnStructure Structure { get; } = structure;

        /// <summary>The sinking shape, or null once it has gone and only the rubble is left.</summary>
        public MeshInstance3D? Node { get; set; } = node;

        public StandardMaterial3D? Walls { get; } = walls;

        public StandardMaterial3D? Roof { get; } = roof;

        public float Age { get; set; }
    }
}
