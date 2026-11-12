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
/// A look is one mesh and, for each seat, one material, so the units of one chassis and one
/// owner are drawn alike and the renderer can draw them together. Workers, and any chassis
/// that <see cref="ChassisLookSet"/> has no look for, keep the stand-in shape.
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

    private static readonly StringName OwnerParameter = "owner";
    private static readonly StringName GrowParameter = "grow";

    // For every unit that wears a look, the chassis it was dressed as.
    private Dictionary<EntityId, int> _dressed = [];
    private Dictionary<EntityId, int> _before = [];
    private ShaderMaterial[] _materials = [];
    private ChassisLookSet _looks = null!;
    private float _handed = 1f;

    /// <summary>The looks of the match's chassis.</summary>
    public ChassisLookSet Looks => _looks;

    /// <summary>The number of units that wear a look after the last tick.</summary>
    public int Count => _dressed.Count;

    /// <summary>How many times its true size a look is drawn in this frame, measured on the ground.</summary>
    public float Growth { get; private set; } = 1f;

    /// <summary>The material that paints a look in the color of a seat.</summary>
    public ShaderMaterial MaterialOf(int player) => _materials[player % _materials.Length];

    /// <summary>The color a material of the looks paints the owner's parts in.</summary>
    public static Color OwnerOf(ShaderMaterial material) => material.GetShaderParameter(OwnerParameter).AsColor();

    /// <summary>The look a drawn unit wears, or null for a unit that is not drawn or keeps the stand-in shape.</summary>
    public ChassisLook? LookOf(EntityId entity) => _dressed.TryGetValue(entity, out var chassis) ? _looks.Of(chassis) : null;

    protected override void Attach()
    {
        _looks = ChassisLookSet.For(View.Match.Data);

        var shader = new Shader { Code = ShaderCode() };
        _materials = new ShaderMaterial[PlayerColors.Count];
        for (var player = 0; player < _materials.Length; player++)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter(OwnerParameter, PlayerColors.Of(player));
            _materials[player] = material;
        }
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
                && !View.World.TryDress(entity, look.Mesh, MaterialOf(body.Owner)))
            {
                continue;
            }

            _dressed.Add(entity, unit.Chassis);
        }
    }

    public override void EveryFrame(double delta)
    {
        Growth = GrowthAt(View.Camera.Distance, View.Camera.StartDistance, View.Camera.MaxDistance);
        if (Growth == _handed)
        {
            return;
        }

        _handed = Growth;
        foreach (var material in _materials)
        {
            material.SetShaderParameter(GrowParameter, Growth);
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

    // The mesh of a look carries its tones in the colors of its corners (LookShape.ToMesh): an
    // alpha of 0 marks a part in the owner's color. The colors are given as the interface has
    // them, and the renderer works in linear light.
    private static string ShaderCode()
    {
        static string Number(float value) => value.ToString("0.0###", CultureInfo.InvariantCulture);

        return $$"""
            shader_type spatial;

            uniform vec3 owner : source_color = vec3(1.0);

            // How many times its true size the look is drawn, on the ground.
            uniform float grow = 1.0;

            varying vec3 paint;

            vec3 to_linear(vec3 color) {
                return mix(pow((color + vec3(0.055)) / 1.055, vec3(2.4)), color / 12.92, lessThan(color, vec3(0.04045)));
            }

            void vertex() {
                VERTEX.xz *= grow;
                NORMAL = normalize(NORMAL / vec3(grow, 1.0, grow));
                paint = mix(owner * COLOR.rgb, to_linear(COLOR.rgb), COLOR.a);
            }

            void fragment() {
                ALBEDO = paint;
                ROUGHNESS = {{Number(1f)}};
            }
            """;
    }
}
