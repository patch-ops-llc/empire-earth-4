using System.Globalization;
using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// Gives every fighting unit the world draws the look of its chassis (design 3.2): a shape of
/// its own in place of the stand-in, in its owner's color with pale, dark and wooden parts. A
/// player tells the line by the build of the shape and the epoch by what it wears and carries,
/// on their own units and on another player's while they see it.
/// </summary>
/// <remarks>
/// <para>
/// The world view makes, moves and removes the node of every unit, and draws only the units the
/// player may be shown. This layer changes what such a node looks like and nothing else: after
/// every tick it dresses the units that are new on the screen, before their first frame. A unit
/// that went out of sight lost its node, and gets its look again when it is seen again.
/// </para>
/// <para>
/// A look is one mesh, and each seat has one material for all its looks, so the units of one
/// chassis and one owner are drawn alike and the renderer can draw them together. Workers, and any chassis
/// that <see cref="ChassisLookSet"/> has no look for, keep the stand-in shape.
/// </para>
/// <para>
/// A unit in a look that is hit turns pale for a moment, and one that dies leaves its look
/// behind, darker, to sink into the ground. The combat effects ask this layer for both
/// (<see cref="Flash"/>, <see cref="Fall"/>) and draw the stand-in's when it says no.
/// </para>
/// <para>
/// <b>Far from the camera a look is drawn wider than it is</b>, so that the build of its shape,
/// which says its line, still reads where a tile is a dozen pixels. Up to the camera's starting
/// distance a look has its true size. From there to the widest zoom it grows evenly on the
/// ground, to <see cref="FarWidth"/> times its size, and not in height: the camera looks down
/// on it, and the health bar and the accent badges stand at a fixed height over the unit. It
/// still gets smaller on the screen as the camera leaves, only more slowly.
/// </para>
/// </remarks>
public partial class ChassisLooks : WorldLayer
{
    /// <summary>How many times its true size a look is drawn at the widest zoom, measured on the ground.</summary>
    public const float FarWidth = 1.45f;

    /// <summary>How pale a unit turns when it is hit: the share of white in its colors.</summary>
    public const float FlashShare = 0.75f;

    /// <summary>How dark the look that a dead unit leaves is: the share of its colors that is kept.</summary>
    public const float FallenShare = 0.65f;

    private static readonly StringName GrowParameter = "grow";
    private static readonly StringName GlowParameter = "glow";

    // For every unit that wears a look, the chassis it was dressed as.
    private Dictionary<EntityId, int> _dressed = [];
    private Dictionary<EntityId, int> _before = [];
    private ShaderMaterial[] _materials = [];
    private ShaderMaterial[] _flashMaterials = [];
    private ShaderMaterial[] _fallenMaterials = [];

    // The units that are pale now, with the seconds each has left, and the looks left by the dead.
    private readonly Dictionary<EntityId, double> _flashing = [];
    private readonly List<EntityId> _flashed = [];
    private readonly List<Fallen> _fallen = [];
    private ChassisLookSet _looks = null!;
    private float _handed = 1f;

    /// <summary>The looks of the match's chassis.</summary>
    public ChassisLookSet Looks => _looks;

    /// <summary>The number of units that wear a look after the last tick.</summary>
    public int Count => _dressed.Count;

    /// <summary>How many times its true size a look is drawn in this frame, measured on the ground.</summary>
    public float Growth { get; private set; } = 1f;

    /// <summary>The number of hits shown on a unit's own look since the match began.</summary>
    public int FlashesShown { get; private set; }

    /// <summary>The number of deaths that left the unit's own look behind since the match began.</summary>
    public int FallsShown { get; private set; }

    /// <summary>The material that paints a look in the color of a seat.</summary>
    public ShaderMaterial MaterialOf(int player) => _materials[player % _materials.Length];

    /// <summary>The material that paints the look of a unit of a seat while it is pale from a hit.</summary>
    public ShaderMaterial FlashMaterialOf(int player) => _flashMaterials[player % _flashMaterials.Length];

    /// <summary>The color a material of the looks paints a tone in.</summary>
    public static Color PaintOf(ShaderMaterial material, LookTone tone) => material.GetShaderParameter(ToneParameter(tone.Index)).AsColor();

    /// <summary>The look a drawn unit wears, or null for a unit that is not drawn or keeps the stand-in shape.</summary>
    public ChassisLook? LookOf(EntityId entity) => _dressed.TryGetValue(entity, out var chassis) ? _looks.Of(chassis) : null;

    protected override void Attach()
    {
        _looks = ChassisLookSet.For(View.Match.Data);

        var shader = new Shader { Code = ShaderCode(_looks.Tones.Count) };
        _materials = new ShaderMaterial[PlayerColors.Count];
        _flashMaterials = new ShaderMaterial[PlayerColors.Count];
        _fallenMaterials = new ShaderMaterial[PlayerColors.Count];
        for (var player = 0; player < _materials.Length; player++)
        {
            _materials[player] = Material(shader, player, 1f, 0f);
            _flashMaterials[player] = Material(shader, player, 1f, FlashShare);
            _fallenMaterials[player] = Material(shader, player, FallenShare, 0f);
        }
    }

    // The material of a seat: every tone as that seat has it, with a share of its light kept
    // and a share of white mixed in. The colors are worked out here and handed over one by
    // one, as the interface has them, so that each renderer takes them into the light it
    // works in. A shader that does that sum itself, or is handed the colors as a list, comes
    // out darker under the Compatibility renderer than under the other two.
    private ShaderMaterial Material(Shader shader, int player, float kept, float white)
    {
        var owner = PlayerColors.Of(player);
        var material = new ShaderMaterial { Shader = shader };
        foreach (var tone in _looks.Tones)
        {
            material.SetShaderParameter(ToneParameter(tone.Index), LookTone.Scaled(tone.For(owner), kept, white));
        }

        material.SetShaderParameter(GlowParameter, white * 0.5f);
        return material;
    }

    private static StringName ToneParameter(int index) => $"tone_{index.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Turns a unit pale for some seconds, as one that was hit. False, with nothing done, for a
    /// unit that is not drawn or has no look: its flash is the caller's to draw.
    /// </summary>
    public bool Flash(EntityId entity, double seconds)
    {
        var match = View.Match;
        if (!match.Combatants.TryGet(entity, out var unit)
            || _looks.Of(unit.Chassis) is not { } look
            || !match.Bodies.TryGet(entity, out var body)
            || !View.World.TryDress(entity, look.Mesh, FlashMaterialOf(body.Owner)))
        {
            return false;
        }

        _flashing[entity] = seconds;
        FlashesShown++;
        return true;
    }

    /// <summary>
    /// Leaves the look of a unit that died where it stood, darker, to sink into the ground over
    /// some seconds. False, with nothing done, for a unit that wore no look when it was last
    /// drawn: what it leaves is the caller's to draw.
    /// </summary>
    /// <param name="entity">The unit that died.</param>
    /// <param name="player">Its owner.</param>
    /// <param name="at">Where it stood.</param>
    /// <param name="heading">The way it faced, as a turn about the upright axis in radians.</param>
    /// <param name="seconds">How long the look takes to sink.</param>
    public bool Fall(EntityId entity, int player, Vector3 at, float heading, double seconds)
    {
        if ((!_dressed.TryGetValue(entity, out var chassis) && !_before.TryGetValue(entity, out chassis))
            || _looks.Of(chassis) is not { } look)
        {
            return false;
        }

        var node = new MeshInstance3D
        {
            Mesh = look.Mesh,
            MaterialOverride = _fallenMaterials[player % _fallenMaterials.Length],
            Position = at,
            Rotation = new Vector3(0f, heading, 0f),
        };
        AddChild(node);
        _fallen.Add(new Fallen(node, seconds));
        FallsShown++;
        return true;
    }

    public override void AfterTick()
    {
        (_before, _dressed) = (_dressed, _before);
        _dressed.Clear();

        var match = View.Match;
        foreach (var entity in View.World.Units)
        {
            if (!match.Combatants.TryGet(entity, out var unit)
                || _looks.Of(unit.Chassis) is not { } look
                || !match.Bodies.TryGet(entity, out var body))
            {
                continue;
            }

            // A unit that was drawn in this look a tick ago still wears it: its node is the same.
            if ((!_before.TryGetValue(entity, out var worn) || worn != unit.Chassis)
                && !View.World.TryDress(entity, look.Mesh, _flashing.ContainsKey(entity) ? FlashMaterialOf(body.Owner) : MaterialOf(body.Owner)))
            {
                continue;
            }

            _dressed.Add(entity, unit.Chassis);
        }
    }

    public override void EveryFrame(double delta)
    {
        EndFlashes(delta);
        Sink(delta);

        Growth = GrowthAt(View.Camera.Distance, View.Camera.StartDistance, View.Camera.MaxDistance);
        if (Growth == _handed)
        {
            return;
        }

        _handed = Growth;
        for (var player = 0; player < _materials.Length; player++)
        {
            _materials[player].SetShaderParameter(GrowParameter, Growth);
            _flashMaterials[player].SetShaderParameter(GrowParameter, Growth);
            _fallenMaterials[player].SetShaderParameter(GrowParameter, Growth);
        }
    }

    // A unit whose flash is over gets its own colors back, if it is still drawn in its look.
    private void EndFlashes(double delta)
    {
        if (_flashing.Count == 0)
        {
            return;
        }

        foreach (var entity in _flashing.Keys)
        {
            _flashed.Add(entity);
        }

        foreach (var entity in _flashed)
        {
            var left = _flashing[entity] - delta;
            if (left > 0)
            {
                _flashing[entity] = left;
                continue;
            }

            _flashing.Remove(entity);
            if (_dressed.TryGetValue(entity, out var chassis)
                && _looks.Of(chassis) is { } look
                && View.Match.Bodies.TryGet(entity, out var body))
            {
                View.World.TryDress(entity, look.Mesh, MaterialOf(body.Owner));
            }
        }

        _flashed.Clear();
    }

    // The look a dead unit left gets flatter until nothing of it is above the ground.
    private void Sink(double delta)
    {
        for (var i = _fallen.Count - 1; i >= 0; i--)
        {
            var fallen = _fallen[i];
            fallen.Age += delta;
            if (fallen.Age >= fallen.Life)
            {
                fallen.Node.QueueFree();
                _fallen.RemoveAt(i);
                continue;
            }

            var gone = (float)(fallen.Age / fallen.Life);
            fallen.Node.Scale = new Vector3(1f, Mathf.Lerp(1f, 0.02f, gone), 1f);
        }
    }

    /// <summary>How many times its true size a look is drawn when the camera is this far away, measured on the ground.</summary>
    /// <param name="distance">The camera's distance, in tiles.</param>
    /// <param name="normal">The distance up to which a look has its true size: the one the camera starts at.</param>
    /// <param name="widest">The distance at which a look is largest: the camera's widest zoom.</param>
    public static float GrowthAt(float distance, float normal, float widest)
    {
        var far = widest > normal ? Mathf.Clamp((distance - normal) / (widest - normal), 0f, 1f) : 0f;
        return Mathf.Lerp(1f, FarWidth, far);
    }

    // The mesh of a look carries the number of each corner's tone as its first texture
    // coordinate (LookShape.ToMesh), and the material has a color for each number.
    private static string ShaderCode(int tones)
    {
        var colors = new System.Text.StringBuilder();
        var choice = new System.Text.StringBuilder();
        for (var i = 0; i < tones; i++)
        {
            var name = ToneParameter(i).ToString();
            colors.Append(CultureInfo.InvariantCulture, $"uniform vec3 {name} : source_color = vec3(1.0);\n");
            choice.Append(CultureInfo.InvariantCulture, $"    if (tone == {i}) {{ paint = {name}; }}\n");
        }

        return $$"""
            shader_type spatial;

            {{colors}}
            // How many times its true size the look is drawn, on the ground.
            uniform float grow = 1.0;

            // Light of its own, for a unit that was hit.
            uniform float glow = 0.0;

            varying vec3 paint;

            void vertex() {
                VERTEX.xz *= grow;
                NORMAL = normalize(NORMAL / vec3(grow, 1.0, grow));
                int tone = int(UV.x + 0.5);
                paint = vec3(1.0);
            {{choice}}}

            void fragment() {
                ALBEDO = paint;
                ROUGHNESS = 1.0;
                EMISSION = vec3(glow);
            }
            """;
    }

    private sealed class Fallen(MeshInstance3D node, double life)
    {
        public MeshInstance3D Node { get; } = node;

        public double Life { get; } = life;

        public double Age { get; set; }
    }
}
