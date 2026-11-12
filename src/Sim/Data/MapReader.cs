namespace Sim.Data;

internal static class MapReader
{
    public const char OpenTile = '.';
    public const char ForestTile = 'F';
    public const char CliffTile = '#';

    public static MapDef Read(JsonValue root, string expectedId, MapRules rules, MapStartRules capitol)
    {
        var errorsBefore = root.ErrorCount;
        var file = root.Object();
        file.Optional(DataRules.SchemaKey)?.String();

        var idValue = file.Required("id");
        var id = idValue.Id();
        if (id.Length > 0 && id != expectedId)
        {
            idValue.Error($"must be '{expectedId}' to match the file name, found '{id}'");
        }

        var width = file.Required("width").Integer(DataRules.MinMapSize, DataRules.MaxMapSize);
        var height = file.Required("height").Integer(DataRules.MinMapSize, DataRules.MaxMapSize);
        var symmetry = ReadSymmetry(file.Required("symmetry"));
        var tiles = ReadTerrain(file.Required("terrain"), width, height, root.ErrorCount == errorsBefore);

        var starts = new List<MapStart>();
        foreach (var startValue in file.Required("starts").Array(2, 2))
        {
            var start = startValue.Object();
            starts.Add(new MapStart(ReadCoordinate(start.Required("x")), ReadCoordinate(start.Required("y"))));
            start.End();
        }

        var deposits = new List<MapDeposit>();
        foreach (var depositValue in file.Required("deposits").Array())
        {
            var deposit = depositValue.Object();
            deposits.Add(new MapDeposit(
                ReadDepositKind(deposit.Required("kind")),
                ReadCoordinate(deposit.Required("x")),
                ReadCoordinate(deposit.Required("y"))));
            deposit.End();
        }

        var markers = new List<MapMarker>();
        foreach (var markerValue in file.Required("markers").Array())
        {
            var marker = markerValue.Object();
            var kind = ReadMarkerKind(marker.Required("kind"));
            var area = new TileRect(
                ReadCoordinate(marker.Required("x")),
                ReadCoordinate(marker.Required("y")),
                marker.Required("size_x").Integer(1, DataRules.MaxMapSize),
                marker.Required("size_y").Integer(1, DataRules.MaxMapSize));
            markers.Add(new MapMarker(kind, area, ReadAxis(marker.Required("across"))));
            marker.End();
        }

        file.End();

        var map = new MapDef(id, width, height, symmetry, tiles, starts, deposits, markers);
        if (root.ErrorCount == errorsBefore)
        {
            MapChecker.Check(map, rules, capitol, (location, message) => root.ErrorAt(location, message));
        }

        return map;
    }

    private static int ReadCoordinate(JsonValue value) => value.Integer(0, DataRules.MaxMapSize - 1);

    private static List<TerrainKind> ReadTerrain(JsonValue value, int width, int height, bool sizeIsValid)
    {
        var tiles = new List<TerrainKind>(width * height);
        var rows = value.Array();
        if (sizeIsValid && rows.Count != height)
        {
            value.Error($"must have one row per tile of height ({height}), found {rows.Count}");
        }

        foreach (var rowValue in rows)
        {
            var row = rowValue.String();
            if (row.Length == 0)
            {
                continue;
            }

            if (sizeIsValid && row.Length != width)
            {
                rowValue.Error($"must have one character per tile of width ({width}), found {row.Length}");
                continue;
            }

            for (var x = 0; x < row.Length; x++)
            {
                switch (row[x])
                {
                    case OpenTile:
                        tiles.Add(TerrainKind.Open);
                        break;
                    case ForestTile:
                        tiles.Add(TerrainKind.Forest);
                        break;
                    case CliffTile:
                        tiles.Add(TerrainKind.Cliff);
                        break;
                    default:
                        rowValue.Error($"unknown terrain character '{row[x]}' at x = {x}; "
                            + $"use '{OpenTile}' for open ground, '{ForestTile}' for forest and '{CliffTile}' for cliff");
                        x = row.Length;
                        break;
                }
            }
        }

        return tiles;
    }

    private static MapSymmetry ReadSymmetry(JsonValue value)
    {
        var text = value.String();
        switch (text)
        {
            case "":
            case "mirror_x":
                return MapSymmetry.MirrorX;
            case "mirror_y":
                return MapSymmetry.MirrorY;
            default:
                value.Error($"unknown symmetry '{text}'; use mirror_x or mirror_y");
                return MapSymmetry.MirrorX;
        }
    }

    private static DepositKind ReadDepositKind(JsonValue value)
    {
        var text = value.String();
        switch (text)
        {
            case "":
            case "main":
                return DepositKind.Main;
            case "natural":
                return DepositKind.Natural;
            case "contested":
                return DepositKind.Contested;
            default:
                value.Error($"unknown deposit kind '{text}'; use main, natural or contested");
                return DepositKind.Main;
        }
    }

    internal static MarkerKind ReadMarkerKind(JsonValue value)
    {
        var text = value.String();
        switch (text)
        {
            case "":
            case "chokepoint":
                return MarkerKind.Chokepoint;
            case "open_field":
                return MarkerKind.OpenField;
            default:
                value.Error($"unknown marker kind '{text}'; use chokepoint or open_field");
                return MarkerKind.Chokepoint;
        }
    }

    private static MapAxis ReadAxis(JsonValue value)
    {
        var text = value.String();
        switch (text)
        {
            case "":
            case "x":
                return MapAxis.X;
            case "y":
                return MapAxis.Y;
            default:
                value.Error($"unknown axis '{text}'; use x or y");
                return MapAxis.X;
        }
    }
}
