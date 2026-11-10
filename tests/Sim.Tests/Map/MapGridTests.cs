using Sim.Data;
using Sim.Map;
using Sim.Numerics;

namespace Sim.Tests.Map;

public class MapGridTests
{
    private static readonly MapDef Sample = TestMaps.FromRows(
        "....",
        ".F#.",
        "....");

    private static FixedVector2 Point(string x, string y) => new(Fraction.Parse(x).ToFixed(), Fraction.Parse(y).ToFixed());

    [Fact]
    public void TheGridHasTheSizeOfTheMap()
    {
        var grid = new MapGrid(Sample);

        Assert.Equal(4, grid.Width);
        Assert.Equal(3, grid.Height);
    }

    [Fact]
    public void OnlyOpenGroundIsPassable()
    {
        var grid = new MapGrid(Sample);

        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(Sample.TerrainAt(x, y) == TerrainKind.Open, grid.IsPassable(x, y));
            }
        }

        Assert.False(grid.IsPassable(1, 1));
        Assert.False(grid.IsPassable(2, 1));
        Assert.True(grid.IsPassable(3, 1));
    }

    [Fact]
    public void NothingOutsideTheMapIsPassable()
    {
        var grid = new MapGrid(Sample);

        Assert.True(grid.Contains(0, 0));
        Assert.True(grid.Contains(3, 2));
        foreach (var (x, y) in ((int, int)[])[(-1, 0), (0, -1), (4, 0), (0, 3), (int.MinValue, 0), (0, int.MaxValue)])
        {
            Assert.False(grid.Contains(x, y));
            Assert.False(grid.IsPassable(x, y));
        }
    }

    [Fact]
    public void APointBelongsToTheTileItsCoordinatesRoundDownTo()
    {
        var grid = new MapGrid(Sample);

        Assert.True(grid.IsPassable(Point("0", "0")));
        Assert.True(grid.IsPassable(Point("0.99", "1.5")));
        Assert.False(grid.IsPassable(Point("1", "1.5")));
        Assert.False(grid.IsPassable(Point("2.99", "1.99")));
        Assert.True(grid.IsPassable(Point("3", "1")));
        Assert.True(grid.IsPassable(Point("1.5", "2")));

        Assert.True(grid.Contains(Point("3.99", "2.99")));
        Assert.False(grid.Contains(Point("4", "1")));
        Assert.False(grid.Contains(Point("1", "3")));
        Assert.False(grid.Contains(new FixedVector2(-Fixed.Epsilon, Fixed.One)));
        Assert.False(grid.IsPassable(new FixedVector2(Fixed.One, -Fixed.Epsilon)));
    }

    [Fact]
    public void ATileCenterIsHalfATileIn()
    {
        Assert.Equal(Point("0.5", "0.5"), MapGrid.TileCenter(0, 0));
        Assert.Equal(Point("95.5", "7.5"), MapGrid.TileCenter(95, 7));
    }

    [Fact]
    public void AMapWhoseTilesDoNotMatchItsSizeIsRejected()
    {
        var broken = Sample with { Width = 5 };

        Assert.Throws<ArgumentException>(() => new MapGrid(broken));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapGrid(Sample with { Width = 0, Tiles = [] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapGrid(Sample with { Height = MapGrid.MaxSize + 1 }));
        Assert.Throws<ArgumentNullException>(() => new MapGrid(null!));
    }

    [Fact]
    public void TheLargestMapCanBeBuilt()
    {
        var grid = new MapGrid(TestMaps.Open(MapGrid.MaxSize, 2));

        Assert.True(grid.IsPassable(MapGrid.MaxSize - 1, 1));
        Assert.True(grid.IsPassable(MapGrid.TileCenter(MapGrid.MaxSize - 1, 1)));
    }
}
