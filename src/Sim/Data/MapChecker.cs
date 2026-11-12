namespace Sim.Data;

/// <summary>
/// Checks one map against the design's map rules (design 5.3 and 5.4). All distances are compared as
/// squares of doubled coordinates, so tile centers and footprint centers stay whole numbers.
/// </summary>
/// <remarks>
/// The footprint and the territory radius of a start are those of the structure every player
/// starts with, read from its own file.
/// </remarks>
internal static class MapChecker
{
    public static void Check(MapDef map, MapRules rules, MapStartRules capitol, Action<string, string> report)
    {
        var startAreas = new List<TileRect>();
        foreach (var start in map.Starts)
        {
            startAreas.Add(new TileRect(start.X, start.Y, capitol.Footprint, capitol.Footprint));
        }

        var depositAreas = new List<TileRect>();
        foreach (var deposit in map.Deposits)
        {
            depositAreas.Add(new TileRect(deposit.X, deposit.Y, rules.DepositSize, rules.DepositSize));
        }

        var footprintsFit = true;
        for (var i = 0; i < startAreas.Count; i++)
        {
            footprintsFit &= CheckOpenArea(map, startAreas[i], $"$.starts[{i}]", "the starting Capitol", report);
        }

        for (var i = 0; i < depositAreas.Count; i++)
        {
            var location = $"$.deposits[{i}]";
            footprintsFit &= CheckOpenArea(map, depositAreas[i], location, "the deposit", report);
            for (var s = 0; s < startAreas.Count; s++)
            {
                if (depositAreas[i].Overlaps(startAreas[s]))
                {
                    report(location, $"overlaps the starting Capitol of start {s}");
                }
            }

            for (var j = 0; j < i; j++)
            {
                if (depositAreas[i].Overlaps(depositAreas[j]))
                {
                    report(location, $"overlaps the deposit at $.deposits[{j}]");
                }
            }
        }

        CheckMarkers(map, rules, report);
        CheckSymmetry(map, startAreas, depositAreas, report);

        if (footprintsFit)
        {
            CheckStartDistance(startAreas, capitol, report);
            CheckDeposits(map, startAreas, depositAreas, rules, capitol, report);
        }
    }

    private static bool CheckOpenArea(MapDef map, TileRect area, string location, string what, Action<string, string> report)
    {
        if (!Fits(map, area))
        {
            report(location, $"{what} covers {area.SizeX} by {area.SizeY} tiles from ({area.X}, {area.Y}) and does not fit on the map");
            return false;
        }

        for (var y = area.Y; y < area.Y + area.SizeY; y++)
        {
            for (var x = area.X; x < area.X + area.SizeX; x++)
            {
                if (map.TerrainAt(x, y) != TerrainKind.Open)
                {
                    report(location, $"{what} must stand on open ground, but tile ({x}, {y}) is {Name(map.TerrainAt(x, y))}");
                    return true;
                }
            }
        }

        return true;
    }

    private static void CheckMarkers(MapDef map, MapRules rules, Action<string, string> report)
    {
        var chokepoints = 0;
        var openFields = 0;
        for (var i = 0; i < map.Markers.Count; i++)
        {
            var marker = map.Markers[i];
            var location = $"$.markers[{i}]";
            if (marker.Kind == MarkerKind.Chokepoint)
            {
                chokepoints++;
            }
            else
            {
                openFields++;
            }

            if (!CheckOpenArea(map, marker.Area, location, "the marker", report))
            {
                continue;
            }

            if (marker.Kind == MarkerKind.OpenField)
            {
                if (marker.Width < rules.OpenFieldMinWidth)
                {
                    report(location, $"an open field must be at least {rules.OpenFieldMinWidth} tiles wide, found {marker.Width}");
                }

                continue;
            }

            if (marker.Width < rules.ChokepointMinWidth || marker.Width > rules.ChokepointMaxWidth)
            {
                report(location, $"a chokepoint must be {rules.ChokepointMinWidth} to {rules.ChokepointMaxWidth} tiles wide, found {marker.Width}");
            }

            if (FindOpenTileBesideChokepoint(map, marker) is { } open)
            {
                report(location, $"the passage is wider than the marker: tile ({open.X}, {open.Y}) beside it is open ground");
            }
        }

        if (chokepoints == 0)
        {
            report("$.markers", "needs at least one chokepoint marker");
        }

        if (openFields == 0)
        {
            report("$.markers", "needs at least one open_field marker");
        }
    }

    /// <summary>
    /// A chokepoint marker must span the whole passage, so the tiles just past both ends of its width
    /// must be impassable or off the map.
    /// </summary>
    private static (int X, int Y)? FindOpenTileBesideChokepoint(MapDef map, MapMarker marker)
    {
        var area = marker.Area;
        if (marker.Across == MapAxis.Y)
        {
            for (var x = area.X; x < area.X + area.SizeX; x++)
            {
                foreach (var y in (int[])[area.Y - 1, area.Y + area.SizeY])
                {
                    if (map.Contains(x, y) && map.TerrainAt(x, y) == TerrainKind.Open)
                    {
                        return (x, y);
                    }
                }
            }
        }
        else
        {
            for (var y = area.Y; y < area.Y + area.SizeY; y++)
            {
                foreach (var x in (int[])[area.X - 1, area.X + area.SizeX])
                {
                    if (map.Contains(x, y) && map.TerrainAt(x, y) == TerrainKind.Open)
                    {
                        return (x, y);
                    }
                }
            }
        }

        return null;
    }

    private static void CheckSymmetry(
        MapDef map,
        List<TileRect> startAreas,
        List<TileRect> depositAreas,
        Action<string, string> report)
    {
        var symmetry = map.Symmetry == MapSymmetry.MirrorX ? "mirror_x" : "mirror_y";
        var terrainMatches = true;
        for (var y = 0; y < map.Height && terrainMatches; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var mirror = Mirror(map, new TileRect(x, y, 1, 1));
                if (map.TerrainAt(x, y) != map.TerrainAt(mirror.X, mirror.Y))
                {
                    report("$.terrain", $"is not symmetric under {symmetry}: tile ({x}, {y}) is {Name(map.TerrainAt(x, y))} "
                        + $"but its mirror tile ({mirror.X}, {mirror.Y}) is {Name(map.TerrainAt(mirror.X, mirror.Y))}");
                    terrainMatches = false;
                    break;
                }
            }
        }

        var mirroredStart = Mirror(map, startAreas[0]);
        if (mirroredStart != startAreas[1])
        {
            report("$.starts[1]", $"must mirror start 0 under {symmetry}: expected x = {mirroredStart.X}, y = {mirroredStart.Y}");
        }

        for (var i = 0; i < map.Deposits.Count; i++)
        {
            var mirror = Mirror(map, depositAreas[i]);
            var found = false;
            for (var j = 0; j < map.Deposits.Count; j++)
            {
                found |= depositAreas[j] == mirror && map.Deposits[j].Kind == map.Deposits[i].Kind;
            }

            if (!found)
            {
                report($"$.deposits[{i}]", $"has no mirror image under {symmetry}: expected a {Name(map.Deposits[i].Kind)} deposit at x = {mirror.X}, y = {mirror.Y}");
            }
        }

        for (var i = 0; i < map.Markers.Count; i++)
        {
            var mirror = map.Markers[i] with { Area = Mirror(map, map.Markers[i].Area) };
            var found = false;
            foreach (var other in map.Markers)
            {
                found |= other == mirror;
            }

            if (!found)
            {
                report($"$.markers[{i}]", $"has no mirror image under {symmetry}: expected the same marker at x = {mirror.Area.X}, y = {mirror.Area.Y}");
            }
        }
    }

    private static void CheckStartDistance(List<TileRect> startAreas, MapStartRules capitol, Action<string, string> report)
    {
        // In doubled coordinates the needed distance, two radii, is four times the radius.
        long needed = 4L * capitol.TerritoryRadius;
        if (SquaredDoubledDistance(startAreas[0], startAreas[1]) < needed * needed)
        {
            report("$.starts", $"the starting Capitols are too close: their centers must be at least {2 * capitol.TerritoryRadius} tiles apart, "
                + $"so that two territories of radius {capitol.TerritoryRadius} do not overlap");
        }
    }

    private static void CheckDeposits(
        MapDef map,
        List<TileRect> startAreas,
        List<TileRect> depositAreas,
        MapRules rules,
        MapStartRules capitol,
        Action<string, string> report)
    {
        var mainDeposits = new int[startAreas.Count];
        var naturalDeposits = new int[startAreas.Count];
        var contestedDeposits = 0;

        for (var i = 0; i < map.Deposits.Count; i++)
        {
            var location = $"$.deposits[{i}]";
            var kind = map.Deposits[i].Kind;
            var tileCount = depositAreas[i].SizeX * depositAreas[i].SizeY;

            if (kind == DepositKind.Main)
            {
                var owner = -1;
                for (var s = 0; s < startAreas.Count; s++)
                {
                    if (TilesInsideTerritory(depositAreas[i], startAreas[s], capitol.TerritoryRadius) == tileCount)
                    {
                        owner = s;
                    }
                }

                if (owner < 0)
                {
                    report(location, $"a main deposit must lie wholly inside a starting territory (every tile center within {capitol.TerritoryRadius} tiles of a starting Capitol's center)");
                }
                else
                {
                    mainDeposits[owner]++;
                }

                continue;
            }

            var isOutside = true;
            for (var s = 0; s < startAreas.Count; s++)
            {
                var inside = TilesInsideTerritory(depositAreas[i], startAreas[s], capitol.TerritoryRadius);
                if (inside > 0)
                {
                    report(location, $"a {Name(kind)} deposit must lie outside every starting territory, but {inside} of its {tileCount} tiles are inside the territory of start {s}");
                    isOutside = false;
                }
            }

            if (!isOutside)
            {
                continue;
            }

            if (kind == DepositKind.Contested)
            {
                contestedDeposits++;
                continue;
            }

            var toFirst = SquaredDoubledDistance(depositAreas[i], startAreas[0]);
            var toSecond = SquaredDoubledDistance(depositAreas[i], startAreas[1]);
            if (toFirst == toSecond)
            {
                report(location, "a natural deposit must be nearer to one start than to the other");
            }
            else
            {
                naturalDeposits[toFirst < toSecond ? 0 : 1]++;
            }
        }

        for (var s = 0; s < startAreas.Count; s++)
        {
            if (mainDeposits[s] != rules.MainDepositsPerPlayer)
            {
                report("$.deposits", $"start {s} needs {rules.MainDepositsPerPlayer} main deposits inside its territory, found {mainDeposits[s]}");
            }

            if (naturalDeposits[s] != rules.NaturalDepositsPerPlayer)
            {
                report("$.deposits", $"start {s} needs {rules.NaturalDepositsPerPlayer} natural deposits nearest to it, found {naturalDeposits[s]}");
            }
        }

        if (contestedDeposits != rules.ContestedDeposits)
        {
            report("$.deposits", $"needs {rules.ContestedDeposits} contested deposits, found {contestedDeposits}");
        }
    }

    /// <summary>
    /// How many tiles of <paramref name="area"/> are inside the territory of the Capitol on
    /// <paramref name="capitol"/>. A tile is inside when its center is within the radius of the
    /// Capitol's center (design 2.5).
    /// </summary>
    private static int TilesInsideTerritory(TileRect area, TileRect capitol, int radius)
    {
        long doubledRadius = 2L * radius;
        long centerX = (2L * capitol.X) + capitol.SizeX;
        long centerY = (2L * capitol.Y) + capitol.SizeY;
        var inside = 0;
        for (var y = area.Y; y < area.Y + area.SizeY; y++)
        {
            for (var x = area.X; x < area.X + area.SizeX; x++)
            {
                var dx = (2L * x) + 1 - centerX;
                var dy = (2L * y) + 1 - centerY;
                if ((dx * dx) + (dy * dy) <= doubledRadius * doubledRadius)
                {
                    inside++;
                }
            }
        }

        return inside;
    }

    /// <summary>The squared distance between the centers of two rectangles, in half tiles.</summary>
    private static long SquaredDoubledDistance(TileRect a, TileRect b)
    {
        long dx = (2L * a.X) + a.SizeX - (2L * b.X) - b.SizeX;
        long dy = (2L * a.Y) + a.SizeY - (2L * b.Y) - b.SizeY;
        return (dx * dx) + (dy * dy);
    }

    private static TileRect Mirror(MapDef map, TileRect area) => map.Symmetry == MapSymmetry.MirrorX
        ? area with { X = map.Width - area.X - area.SizeX }
        : area with { Y = map.Height - area.Y - area.SizeY };

    private static bool Fits(MapDef map, TileRect area) =>
        area.X >= 0 && area.Y >= 0 && area.X + area.SizeX <= map.Width && area.Y + area.SizeY <= map.Height;

    private static string Name(TerrainKind terrain) => terrain switch
    {
        TerrainKind.Open => "open ground",
        TerrainKind.Forest => "forest",
        _ => "cliff",
    };

    private static string Name(DepositKind kind) => kind switch
    {
        DepositKind.Main => "main",
        DepositKind.Natural => "natural",
        _ => "contested",
    };
}

/// <summary>What a map's starts are checked against: the footprint and territory radius of the starting Capitol.</summary>
internal readonly record struct MapStartRules(int Footprint, int TerritoryRadius);
