using Godot;
using Sim.Core;

namespace Game;

/// <summary>
/// Draws the look a structure has by tier (design 2.5 and 3.2): on every finished structure
/// that shows a track, a turret with one storey for each tier its owner has in that track, in
/// the track's color. When the owner's research raises the track, every such structure gains a
/// storey in the same tick.
/// </summary>
/// <remarks>
/// What is drawn is read from <see cref="StructureLooks"/> after every tick, not from events, so
/// the layer shows whatever that seam lists: today every structure as it is, and under the fog
/// of war what the player sees or remembers.
/// </remarks>
public partial class TierMarkLayer : WorldLayer
{
    private readonly List<StructureLook> _looks = [];
    private readonly Dictionary<EntityId, Mark> _marks = [];
    private readonly Dictionary<(int Structure, int Tier), ArrayMesh> _meshes = [];
    private readonly List<EntityId> _gone = [];
    private Material _paint = null!;
    private int _pass;

    /// <summary>The number of structures whose tier is drawn.</summary>
    public int Count => _marks.Count;

    /// <summary>The tier drawn on a structure, or zero when none is.</summary>
    public int TierShown(EntityId structure) => _marks.TryGetValue(structure, out var mark) ? mark.Tier : 0;

    protected override void Attach() =>
        _paint = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 1f };

    public override void AfterTick()
    {
        StructureLooks.Collect(View, _looks);
        _pass++;
        foreach (var look in _looks)
        {
            if (!look.IsFinished || look.LookTier < 1 || look.Def.Track is not { } track)
            {
                continue;
            }

            if (!_marks.TryGetValue(look.Entity, out var mark))
            {
                mark = new Mark(new MeshInstance3D { Name = $"Tier{look.Entity.Value}", MaterialOverride = _paint });
                AddChild(mark.Node);
                _marks.Add(look.Entity, mark);
            }

            if (mark.Tier != look.LookTier)
            {
                mark.Tier = look.LookTier;
                mark.Node.Mesh = MeshOf(look, track);
            }

            mark.Node.Position = look.Middle;
            mark.Pass = _pass;
        }

        foreach (var (entity, mark) in _marks)
        {
            if (mark.Pass != _pass)
            {
                mark.Node.QueueFree();
                _gone.Add(entity);
            }
        }

        foreach (var entity in _gone)
        {
            _marks.Remove(entity);
        }

        _gone.Clear();
    }

    private ArrayMesh MeshOf(in StructureLook look, int track)
    {
        var key = (look.Def.Index, look.LookTier);
        if (!_meshes.TryGetValue(key, out var mesh))
        {
            mesh = TierShapes.Turret(look.Def, look.LookTier, TrackColors.Of(View.Match.Data, track));
            _meshes.Add(key, mesh);
        }

        return mesh;
    }

    private sealed class Mark(MeshInstance3D node)
    {
        public MeshInstance3D Node { get; } = node;

        public int Tier { get; set; }

        public int Pass { get; set; }
    }
}
