using Sim.Data;
using Sim.Numerics;

namespace Tools.Battles;

/// <summary>
/// The ground of an acceptance battle and where the armies stand on it (design 3.1): small maps
/// built in code, because a map file is held to the rules of a match map, with its deposits and
/// starts, and an arena has neither.
/// </summary>
/// <remarks>
/// <para>
/// Both arenas are <see cref="Length"/> tiles from west to east. The armies face each other
/// along that axis, their front ranks <see cref="AcceptanceBattles.TilesBetweenFronts"/> tiles
/// apart and the same distance from the middle of the map, so that swapping their sides is a
/// mirror image.
/// </para>
/// <para>
/// The open field is <see cref="OpenHeight"/> tiles from north to south: room for a block 10
/// wide to be passed on both flanks by an army with a range of 10 tiles. The chokepoint is a
/// corridor <see cref="AcceptanceBattles.ChokepointWidth"/> tiles wide between two bands of
/// cliff, which runs the whole length of the map: both blocks stand in it, 6 wide, so that no
/// more than the ranks within range ever fight. That is the reading of "chokepoint 6 tiles wide"
/// that the design's rough model used. The width can be set, for tuning the test (design 3.1: for
/// test 3, adjust the range of Line infantry first, then the width of the test chokepoint).
/// </para>
/// </remarks>
public static class Arena
{
    public const string OpenFieldId = "arena-open-field";

    public const string ChokepointId = "arena-chokepoint";

    /// <summary>Tiles from west to east, in both arenas.</summary>
    public const int Length = 72;

    /// <summary>Tiles from north to south in the open field.</summary>
    public const int OpenHeight = 48;

    /// <summary>The rows of cliff on each side of the corridor.</summary>
    public const int CliffRows = 2;

    /// <param name="chokepointWidth">The width of the corridor, in tiles. The open field does not use it.</param>
    public static MapDef Create(ArenaKind kind, int chokepointWidth = AcceptanceBattles.ChokepointWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chokepointWidth, 1);
        if (kind == ArenaKind.OpenField)
        {
            return new MapDef(
                OpenFieldId,
                Length,
                OpenHeight,
                MapSymmetry.MirrorX,
                new TerrainKind[Length * OpenHeight],
                [],
                [],
                [new MapMarker(MarkerKind.OpenField, new TileRect(0, 0, Length, OpenHeight), MapAxis.Y)]);
        }

        var height = chokepointWidth + (2 * CliffRows);
        var tiles = new TerrainKind[Length * height];
        for (var y = 0; y < height; y++)
        {
            if (y >= CliffRows && y < height - CliffRows)
            {
                continue;
            }

            for (var x = 0; x < Length; x++)
            {
                tiles[(y * Length) + x] = TerrainKind.Cliff;
            }
        }

        return new MapDef(
            ChokepointId,
            Length,
            height,
            MapSymmetry.MirrorX,
            tiles,
            [],
            [],
            [new MapMarker(MarkerKind.Chokepoint, new TileRect(0, CliffRows, Length, chokepointWidth), MapAxis.Y)]);
    }

    /// <summary>
    /// Where the units of one army stand at the start: the block in a square grid at its spacing,
    /// rank by rank from the front, and one row behind it for what follows the block. A rank that
    /// is not full, and the row behind, are centered on the block's axis.
    /// </summary>
    /// <param name="map">The arena.</param>
    /// <param name="west">True for the army on the west side, which faces east.</param>
    /// <param name="width">How many units wide the block is.</param>
    /// <param name="block">The number of units in the block and the spacing of their line, in tiles.</param>
    /// <param name="behind">The same for the row behind the block. A count of zero for none.</param>
    /// <returns>The positions of the block's units, then those of the row behind.</returns>
    public static FixedVector2[] Positions(MapDef map, bool west, int width, (int Count, Fixed Spacing) block, (int Count, Fixed Spacing) behind)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);

        var positions = new FixedVector2[block.Count + behind.Count];
        var axis = Fixed.FromInt(map.Height) / 2;
        var middle = Fixed.FromInt(map.Width) / 2;
        var toFront = Fixed.FromInt(AcceptanceBattles.TilesBetweenFronts) / 2;
        var front = west ? middle - toFront : middle + toFront;
        var back = west ? -1 : 1;

        var ranks = (block.Count + width - 1) / width;
        for (var i = 0; i < block.Count; i++)
        {
            var rank = i / width;
            var inRank = Math.Min(width, block.Count - (rank * width));
            positions[i] = new FixedVector2(front + (block.Spacing * (back * rank)), Across(axis, i % width, inRank, block.Spacing));
        }

        // One row behind the last rank, at the larger spacing of the two lines.
        var depth = (block.Spacing * Math.Max(0, ranks - 1)) + Fixed.Max(block.Spacing, behind.Spacing);
        for (var i = 0; i < behind.Count; i++)
        {
            positions[block.Count + i] = new FixedVector2(front + (depth * back), Across(axis, i, behind.Count, behind.Spacing));
        }

        return positions;
    }

    /// <summary>The middle of the ground an army's block stands on at the start: where the other army is sent.</summary>
    public static FixedVector2 BlockCenter(MapDef map, bool west, int width, (int Count, Fixed Spacing) block)
    {
        var positions = Positions(map, west, width, block, (0, Fixed.One));
        var first = positions[0];
        var last = positions[^1];
        return new FixedVector2((first.X + last.X) / 2, Fixed.FromInt(map.Height) / 2);
    }

    private static Fixed Across(Fixed axis, int column, int inRank, Fixed spacing) =>
        axis + ((spacing * ((2 * column) - (inRank - 1))) / 2);
}
