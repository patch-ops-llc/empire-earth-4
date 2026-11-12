using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>What the loader rejects in a map file: its shape, and the map rules of design 5.3 and 5.4.</summary>
public class MapTests
{
    private const string Path = SampleData.MapPath;

    [Fact]
    public void TheIdMustMatchTheFileName()
    {
        DataAssert.RejectedOnce(SampleData.WithMap(map => map.Id = "other"), Path, "$.id", "must be 'arena' to match the file name");
    }

    [Fact]
    public void TheSymmetryMustBeKnown()
    {
        DataAssert.RejectedOnce(SampleData.WithMap(map => map.Symmetry = "rotate_180"), Path, "$.symmetry", "unknown symmetry 'rotate_180'");
    }

    [Fact]
    public void TerrainNeedsOneRowPerTileOfHeight()
    {
        DataAssert.RejectedOnce(SampleData.WithMap(map => map.Rows.RemoveAt(15)), Path, "$.terrain", "must have one row per tile of height (16), found 15");
    }

    [Fact]
    public void ATerrainRowNeedsOneCharacterPerTileOfWidth()
    {
        var files = SampleData.WithMap(map => map.Rows[3] = new string('.', 39).ToCharArray());

        DataAssert.RejectedOnce(files, Path, "$.terrain[3]", "must have one character per tile of width (40), found 39");
    }

    [Fact]
    public void ATerrainCharacterMustBeKnown()
    {
        var files = SampleData.WithMap(map => map.SetBoth(5, 3, 'w'));

        DataAssert.RejectedOnce(files, Path, "$.terrain[3]", "unknown terrain character 'w' at x = 5");
    }

    [Fact]
    public void TerrainMustMirror()
    {
        var files = SampleData.WithMap(map => map.Rows[10][30] = 'F');

        DataAssert.RejectedOnce(files, Path, "$.terrain", "is not symmetric under mirror_x: tile (9, 10) is open ground but its mirror tile (30, 10) is forest");
    }

    [Fact]
    public void TerrainThatMirrorsTheOtherWayIsRejected()
    {
        var files = SampleData.WithMap(map => map.SetBoth(9, 1, 'F'));
        DataAssert.Valid(files);

        var flipped = SampleData.WithMap(map =>
        {
            map.SetBoth(9, 1, 'F');
            map.Symmetry = "mirror_y";
        });

        DataAssert.Rejected(flipped, Path, "$.terrain", "is not symmetric under mirror_y");
    }

    [Fact]
    public void TheSecondStartMustMirrorTheFirst()
    {
        var files = SampleData.WithMap(map => map.Starts[1] = """{ "x": 33, "y": 7 }""");

        DataAssert.RejectedOnce(files, Path, "$.starts[1]", "must mirror start 0 under mirror_x: expected x = 34, y = 7");
    }

    [Fact]
    public void AMapNeedsTwoStarts()
    {
        var files = SampleData.WithMap(map => map.Starts.RemoveAt(1));

        DataAssert.RejectedOnce(files, Path, "$.starts", "must have exactly 2 entries, found 1");
    }

    [Fact]
    public void StartsMustLeaveRoomForTwoTerritories()
    {
        var files = SampleData.WithMap(map =>
        {
            map.Starts[0] = """{ "x": 14, "y": 7 }""";
            map.Starts[1] = """{ "x": 24, "y": 7 }""";
            map.Deposits[0] = """{ "kind": "main", "x": 10, "y": 4 }""";
            map.Deposits[1] = """{ "kind": "main", "x": 28, "y": 4 }""";
            map.Deposits[2] = """{ "kind": "natural", "x": 2, "y": 12 }""";
            map.Deposits[3] = """{ "kind": "natural", "x": 36, "y": 12 }""";
            map.Deposits.RemoveRange(4, 2);
        });

        var result = DataLoader.Load(files);

        Assert.Contains(result.Errors, error => error.Location == "$.starts"
            && error.Message.Contains("too close: their centers must be at least 12 tiles apart", StringComparison.Ordinal));
    }

    [Fact]
    public void StartsExactlyTwoRadiiApartAreAccepted()
    {
        // Capitol centers at x = 14 and x = 26: exactly 12 tiles, twice the radius of 6.
        var files = SampleData.WithMap(map =>
        {
            map.Starts[0] = """{ "x": 13, "y": 7 }""";
            map.Starts[1] = """{ "x": 25, "y": 7 }""";
            map.Deposits[0] = """{ "kind": "main", "x": 10, "y": 4 }""";
            map.Deposits[1] = """{ "kind": "main", "x": 28, "y": 4 }""";
            map.Deposits[2] = """{ "kind": "natural", "x": 2, "y": 12 }""";
            map.Deposits[3] = """{ "kind": "natural", "x": 36, "y": 12 }""";
            map.Deposits[4] = """{ "kind": "contested", "x": 4, "y": 0 }""";
            map.Deposits[5] = """{ "kind": "contested", "x": 34, "y": 0 }""";
        });

        DataAssert.Valid(files);
    }

    [Fact]
    public void AStartingCapitolMustStandOnOpenGround()
    {
        var files = SampleData.WithMap(map => map.SetBoth(5, 8, 'F'));

        var result = DataLoader.Load(files);

        Assert.Equal(
            [
                "maps/arena.jsonc: $.starts[0]: the starting Capitol must stand on open ground, but tile (5, 8) is forest",
                "maps/arena.jsonc: $.starts[1]: the starting Capitol must stand on open ground, but tile (34, 8) is forest",
            ],
            result.Errors.Select(error => error.ToString()));
    }

    [Fact]
    public void AFootprintMustFitOnTheMap()
    {
        var files = SampleData.WithMap(map =>
        {
            map.Deposits[4] = """{ "kind": "contested", "x": 15, "y": 15 }""";
            map.Deposits[5] = """{ "kind": "contested", "x": 23, "y": 15 }""";
        });

        DataAssert.Rejected(files, Path, "$.deposits[4]", "the deposit covers 2 by 2 tiles from (15, 15) and does not fit on the map");
    }

    [Fact]
    public void DepositsMustNotOverlap()
    {
        var files = SampleData.WithMap(map =>
        {
            map.Deposits[4] = """{ "kind": "contested", "x": 13, "y": 11 }""";
            map.Deposits[5] = """{ "kind": "contested", "x": 25, "y": 11 }""";
        });

        DataAssert.Rejected(files, Path, "$.deposits[4]", "overlaps the deposit at $.deposits[2]");
    }

    [Fact]
    public void ADepositMustNotOverlapAStartingCapitol()
    {
        var files = SampleData.WithMap(map =>
        {
            map.Deposits[0] = """{ "kind": "main", "x": 5, "y": 8 }""";
            map.Deposits[1] = """{ "kind": "main", "x": 33, "y": 8 }""";
        });

        var result = DataLoader.Load(files);

        Assert.Equal(
            [
                "maps/arena.jsonc: $.deposits[0]: overlaps the starting Capitol of start 0",
                "maps/arena.jsonc: $.deposits[1]: overlaps the starting Capitol of start 1",
            ],
            result.Errors.Select(error => error.ToString()));
    }

    [Fact]
    public void ADepositMustHaveAMirrorImageOfTheSameKind()
    {
        var files = SampleData.WithMap(map => map.Deposits[5] = """{ "kind": "contested", "x": 23, "y": 3 }""");

        var result = DataLoader.Load(files);

        Assert.Equal(
            [
                "maps/arena.jsonc: $.deposits[4]: has no mirror image under mirror_x: expected a contested deposit at x = 23, y = 2",
                "maps/arena.jsonc: $.deposits[5]: has no mirror image under mirror_x: expected a contested deposit at x = 15, y = 3",
            ],
            result.Errors.Select(error => error.ToString()));
    }

    [Fact]
    public void AMainDepositMustLieWhollyInsideAStartingTerritory()
    {
        // The Capitol's center is (5, 8). The tile (9, 3) has its center 6.5 tiles away, outside the
        // radius of 6, while the other three tiles of the deposit are inside.
        var files = SampleData.WithMap(map =>
        {
            map.Deposits[0] = """{ "kind": "main", "x": 8, "y": 3 }""";
            map.Deposits[1] = """{ "kind": "main", "x": 30, "y": 3 }""";
        });

        var result = DataLoader.Load(files);

        Assert.Equal(
            [
                "maps/arena.jsonc: $.deposits: start 0 needs 1 main deposits inside its territory, found 0",
                "maps/arena.jsonc: $.deposits: start 1 needs 1 main deposits inside its territory, found 0",
                "maps/arena.jsonc: $.deposits[0]: a main deposit must lie wholly inside a starting territory (every tile center within 6 tiles of a starting Capitol's center)",
                "maps/arena.jsonc: $.deposits[1]: a main deposit must lie wholly inside a starting territory (every tile center within 6 tiles of a starting Capitol's center)",
            ],
            result.Errors.Select(error => error.ToString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AMainDepositWhoseFarthestTileIsInsideIsAccepted()
    {
        // The farthest tile, (8, 3), has its center 3.5 across and 4.5 up from the Capitol's center:
        // a squared distance of 32.5 against a squared radius of 36.
        var files = SampleData.WithMap(map =>
        {
            map.Deposits[0] = """{ "kind": "main", "x": 7, "y": 3 }""";
            map.Deposits[1] = """{ "kind": "main", "x": 31, "y": 3 }""";
        });

        DataAssert.Valid(files);
    }

    [Theory]
    [InlineData(10, 29, true)]
    [InlineData(11, 28, false)]
    public void ATileWhoseCenterIsExactlyOnTheRadiusIsInside(int x, int mirroredX, bool inside)
    {
        // With a footprint and deposits of one tile, the Capitol's center is (4.5, 7.5) and the tile
        // (10, 7) has its center exactly 6 tiles from it, on the radius. The tile (11, 7) is 7 away.
        var files = SampleData.WithMap(map =>
        {
            map.Starts[1] = """{ "x": 35, "y": 7 }""";
            map.Deposits[0] = $$"""{ "kind": "main", "x": {{x}}, "y": 7 }""";
            map.Deposits[1] = $$"""{ "kind": "main", "x": {{mirroredX}}, "y": 7 }""";
            map.Deposits[3] = """{ "kind": "natural", "x": 27, "y": 12 }""";
            map.Deposits[5] = """{ "kind": "contested", "x": 24, "y": 2 }""";
        });
        files.Edit(SampleData.CapitolPath, "\"footprint\": 2", "\"footprint\": 1");
        files.Edit(SampleData.RulesPath, "\"deposit_size\": 2", "\"deposit_size\": 1");

        if (inside)
        {
            DataAssert.Valid(files);
        }
        else
        {
            DataAssert.Rejected(files, Path, "$.deposits[0]", "a main deposit must lie wholly inside a starting territory");
        }
    }

    [Fact]
    public void ADepositThatIsNotMainMustLieOutsideEveryStartingTerritory()
    {
        var files = SampleData.WithMap(map =>
        {
            map.Deposits[2] = """{ "kind": "natural", "x": 9, "y": 11 }""";
            map.Deposits[3] = """{ "kind": "natural", "x": 29, "y": 11 }""";
        });

        DataAssert.Rejected(files, Path, "$.deposits[2]", "a natural deposit must lie outside every starting territory, but 1 of its 4 tiles are inside the territory of start 0");
    }

    [Fact]
    public void EachStartNeedsItsNaturalDeposits()
    {
        var files = SampleData.WithMap(map =>
        {
            map.Deposits[2] = """{ "kind": "contested", "x": 12, "y": 12 }""";
            map.Deposits[3] = """{ "kind": "contested", "x": 26, "y": 12 }""";
        });

        var result = DataLoader.Load(files);

        Assert.Equal(
            [
                "maps/arena.jsonc: $.deposits: start 0 needs 1 natural deposits nearest to it, found 0",
                "maps/arena.jsonc: $.deposits: start 1 needs 1 natural deposits nearest to it, found 0",
                "maps/arena.jsonc: $.deposits: needs 2 contested deposits, found 4",
            ],
            result.Errors.Select(error => error.ToString()));
    }

    [Fact]
    public void ANaturalDepositMustBeNearerToOneStart()
    {
        var files = SampleData.WithMap(map =>
        {
            map.Deposits[2] = """{ "kind": "natural", "x": 19, "y": 9 }""";
            map.Deposits[3] = """{ "kind": "natural", "x": 19, "y": 11 }""";
        });

        DataAssert.Rejected(files, Path, "$.deposits[2]", "a natural deposit must be nearer to one start than to the other");
    }

    [Fact]
    public void TheNumberOfContestedDepositsIsChecked()
    {
        var files = SampleData.WithMap(map => map.Deposits.RemoveRange(4, 2));

        DataAssert.RejectedOnce(files, Path, "$.deposits", "needs 2 contested deposits, found 0");
    }

    [Theory]
    [InlineData(0, "needs at least one chokepoint marker")]
    [InlineData(1, "needs at least one open_field marker")]
    public void AMapNeedsBothKindsOfMarker(int removed, string message)
    {
        var files = SampleData.WithMap(map => map.Markers.RemoveAt(removed));

        DataAssert.RejectedOnce(files, Path, "$.markers", message);
    }

    [Fact]
    public void AMarkerMustHaveAMirrorImage()
    {
        var files = SampleData.WithMap(map => map.Markers[1] = """{ "kind": "open_field", "x": 18, "y": 8, "size_x": 2, "size_y": 6, "across": "y" }""");

        DataAssert.RejectedOnce(files, Path, "$.markers[1]", "has no mirror image under mirror_x: expected the same marker at x = 20, y = 8");
    }

    [Fact]
    public void AMarkerMustCoverOnlyOpenGround()
    {
        var files = SampleData.WithMap(map => map.Markers[1] = """{ "kind": "open_field", "x": 19, "y": 8, "size_x": 2, "size_y": 7, "across": "y" }""");

        DataAssert.RejectedOnce(files, Path, "$.markers[1]", "the marker must stand on open ground, but tile (19, 14) is cliff");
    }

    [Fact]
    public void AnOpenFieldMustBeWideEnough()
    {
        var files = SampleData.WithMap(map => map.Markers[1] = """{ "kind": "open_field", "x": 19, "y": 8, "size_x": 2, "size_y": 5, "across": "y" }""");

        DataAssert.RejectedOnce(files, Path, "$.markers[1]", "an open field must be at least 6 tiles wide, found 5");
    }

    [Fact]
    public void AChokepointMustSpanTheWholePassage()
    {
        var files = SampleData.WithMap(map => map.Markers[0] = """{ "kind": "chokepoint", "x": 19, "y": 2, "size_x": 2, "size_y": 2, "across": "y" }""");

        DataAssert.RejectedOnce(files, Path, "$.markers[0]", "the passage is wider than the marker: tile (19, 4) beside it is open ground");
    }

    [Fact]
    public void AChokepointMustNotBeTooWide()
    {
        var files = SampleData.WithMap(map => map.Markers[0] = """{ "kind": "chokepoint", "x": 19, "y": 8, "size_x": 2, "size_y": 6, "across": "y" }""");

        DataAssert.RejectedOnce(files, Path, "$.markers[0]", "a chokepoint must be 2 to 4 tiles wide, found 6");
    }

    [Fact]
    public void AChokepointMustNotBeTooNarrow()
    {
        var files = SampleData.WithMap(map =>
        {
            map.SetBoth(19, 2, '#');
            map.SetBoth(19, 3, '#');
            map.Markers[0] = """{ "kind": "chokepoint", "x": 19, "y": 4, "size_x": 2, "size_y": 1, "across": "y" }""";
        });

        DataAssert.RejectedOnce(files, Path, "$.markers[0]", "a chokepoint must be 2 to 4 tiles wide, found 1");
    }

    [Fact]
    public void AChokepointMeasuredAlongTheWrongAxisIsRejected()
    {
        var files = SampleData.WithMap(map => map.Markers[0] = """{ "kind": "chokepoint", "x": 19, "y": 2, "size_x": 2, "size_y": 3, "across": "x" }""");

        DataAssert.RejectedOnce(files, Path, "$.markers[0]", "the passage is wider than the marker: tile (18, 2) beside it is open ground");
    }

    [Fact]
    public void AMapMayMirrorTopToBottom()
    {
        var files = SampleData.WithMap(map =>
        {
            // Turn the sample into a map that is the same above and below the middle row pair:
            // no terrain features, starts at the top and bottom.
            for (var y = 0; y < MapBuilder.Height; y++)
            {
                map.Rows[y] = new string('.', MapBuilder.Width).ToCharArray();
            }

            for (var x = 0; x < MapBuilder.Width; x++)
            {
                if (x is < 4 or > 6)
                {
                    map.Rows[7][x] = '#';
                    map.Rows[8][x] = '#';
                }
            }

            map.Symmetry = "mirror_y";
            map.Starts[0] = """{ "x": 20, "y": 0 }""";
            map.Starts[1] = """{ "x": 20, "y": 14 }""";
            map.Deposits.Clear();
            map.Deposits.AddRange(
            [
                """{ "kind": "main", "x": 24, "y": 0 }""",
                """{ "kind": "main", "x": 24, "y": 14 }""",
                """{ "kind": "natural", "x": 34, "y": 2 }""",
                """{ "kind": "natural", "x": 34, "y": 12 }""",
                """{ "kind": "contested", "x": 0, "y": 0 }""",
                """{ "kind": "contested", "x": 0, "y": 14 }""",
            ]);
            map.Markers.Clear();
            map.Markers.Add("""{ "kind": "chokepoint", "x": 4, "y": 7, "size_x": 3, "size_y": 2, "across": "x" }""");
            map.Markers.Add("""{ "kind": "open_field", "x": 8, "y": 0, "size_x": 6, "size_y": 2, "across": "x" }""");
            map.Markers.Add("""{ "kind": "open_field", "x": 8, "y": 14, "size_x": 6, "size_y": 2, "across": "x" }""");
        });

        DataAssert.Valid(files);
    }
}
