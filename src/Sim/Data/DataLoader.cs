using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Sim.Data;

/// <summary>What <see cref="DataLoader.Load"/> found: the data when every file is valid, the errors otherwise.</summary>
public sealed class DataLoadResult
{
    internal DataLoadResult(GameData? data, IReadOnlyList<DataError> errors, int fileCount)
    {
        Data = data;
        Errors = errors;
        FileCount = fileCount;
    }

    public GameData? Data { get; }

    /// <summary>Every problem found, grouped by file. Empty exactly when <see cref="Data"/> is set.</summary>
    public IReadOnlyList<DataError> Errors { get; }

    public int FileCount { get; }

    [MemberNotNullWhen(true, nameof(Data))]
    public bool IsValid => Data is not null;
}

/// <summary>
/// Loads and checks the data files. The game, the tools and the tests all load through here, so data
/// that the checker accepts is data the game can run.
/// </summary>
public static class DataLoader
{
    private const string Layout = "tracks.jsonc, rules.jsonc, research.jsonc, resources.jsonc, accents.jsonc, lines/<id>.jsonc, structures/<id>.jsonc, maps/<id>.jsonc and ai/<id>.jsonc";

    private static readonly string[] RequiredFiles =
        [DataRules.TracksFile, DataRules.RulesFile, DataRules.ResearchFile, DataRules.ResourcesFile, DataRules.AccentsFile];

    private const string StartStructureFile = DataRules.StructuresFolder + DataRules.StartStructure + DataRules.FileExtension;

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        AllowDuplicateProperties = false,
    };

    public static DataLoadResult Load(IDataFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var errors = new ErrorList();
        var paths = new SortedSet<string>(files.ListFiles(), StringComparer.Ordinal);
        var documents = new List<JsonDocument>();
        try
        {
            var roots = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
            var linePaths = new List<string>();
            var structurePaths = new List<string>();
            var mapPaths = new List<string>();
            var aiPaths = new List<string>();
            foreach (var path in paths)
            {
                if (!Classify(path, linePaths, structurePaths, mapPaths, aiPaths, errors))
                {
                    continue;
                }

                if (Parse(files.ReadFile(path), path, errors) is { } document)
                {
                    documents.Add(document);
                    roots[path] = document.RootElement;
                }
            }

            ReportMissing(paths, linePaths, structurePaths, mapPaths, errors);
            var data = Read(roots, linePaths, structurePaths, mapPaths, aiPaths, errors);
            if (data is not null && errors.Count == 0)
            {
                Tech.FormulaCheck.Check(data, errors);
            }

            if (data is not null && errors.Count == 0)
            {
                Economy.IncomeRules.Check(data, errors);
            }

            var sorted = errors.ToList().OrderBy(error => error.File, StringComparer.Ordinal).ToList();
            return sorted.Count == 0 && data is not null
                ? new DataLoadResult(data with { ContentHash = ContentHash.Compute(roots) }, [], paths.Count)
                : new DataLoadResult(null, sorted, paths.Count);
        }
        finally
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }
        }
    }

    private static bool Classify(
        string path,
        List<string> linePaths,
        List<string> structurePaths,
        List<string> mapPaths,
        List<string> aiPaths,
        ErrorList errors)
    {
        if (Array.IndexOf(RequiredFiles, path) >= 0)
        {
            return true;
        }

        foreach (var (folder, found) in new[] { (DataRules.LinesFolder, linePaths), (DataRules.StructuresFolder, structurePaths), (DataRules.MapsFolder, mapPaths), (DataRules.AiFolder, aiPaths) })
        {
            if (path.StartsWith(folder, StringComparison.Ordinal)
                && path.EndsWith(DataRules.FileExtension, StringComparison.Ordinal)
                && path.IndexOf('/', folder.Length) < 0)
            {
                if (!DataRules.IsId(IdOf(path, folder)))
                {
                    errors.Add(path, "", "name the file after its ID: lowercase letters, digits and underscores, starting with a letter");
                    return false;
                }

                found.Add(path);
                return true;
            }
        }

        errors.Add(path, "", $"unexpected file; the loader reads {Layout}");
        return false;
    }

    private static string IdOf(string path, string folder) => path[folder.Length..^DataRules.FileExtension.Length];

    private static JsonDocument? Parse(ReadOnlyMemory<byte> bytes, string path, ErrorList errors)
    {
        ReadOnlySpan<byte> byteOrderMark = [0xEF, 0xBB, 0xBF];
        if (bytes.Span.StartsWith(byteOrderMark))
        {
            bytes = bytes[byteOrderMark.Length..];
        }

        try
        {
            return JsonDocument.Parse(bytes, JsonOptions);
        }
        catch (JsonException exception)
        {
            var message = exception.Message;
            var positionSuffix = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
            if (positionSuffix >= 0)
            {
                message = message[..positionSuffix];
            }

            errors.Add(
                path,
                $"line {(exception.LineNumber ?? 0) + 1}, column {(exception.BytePositionInLine ?? 0) + 1}",
                $"not valid JSON: {message}");
            return null;
        }
    }

    private static void ReportMissing(SortedSet<string> paths, List<string> linePaths, List<string> structurePaths, List<string> mapPaths, ErrorList errors)
    {
        foreach (var required in RequiredFiles)
        {
            if (!paths.Contains(required))
            {
                errors.Add(required, "", "this file is required and is missing");
            }
        }

        if (linePaths.Count == 0)
        {
            errors.Add(DataRules.LinesFolder, "", "needs at least one line file");
        }

        if (!structurePaths.Contains(StartStructureFile))
        {
            errors.Add(StartStructureFile, "", "this file is required and is missing: every player starts with this structure");
        }

        if (mapPaths.Count == 0)
        {
            errors.Add(DataRules.MapsFolder, "", "needs at least one map file");
        }
    }

    /// <summary>
    /// Reads every parsed file. A file is read only when the files it refers to were valid, so one
    /// mistake in <c>tracks.jsonc</c> is not repeated as an error in every line.
    /// </summary>
    private static GameData? Read(
        SortedDictionary<string, JsonElement> roots,
        List<string> linePaths,
        List<string> structurePaths,
        List<string> mapPaths,
        List<string> aiPaths,
        ErrorList errors)
    {
        JsonValue? Root(string path) => roots.TryGetValue(path, out var root) ? new JsonValue(root, path, "$", errors) : null;

        IReadOnlyList<string> epochNames = [];
        IReadOnlyList<TrackDef> tracks = [];
        TrackRoles? roles = null;
        var tracksAreValid = false;
        if (Root(DataRules.TracksFile) is { } tracksRoot)
        {
            var errorsBefore = errors.Count;
            (epochNames, tracks, roles) = TracksReader.Read(tracksRoot);
            tracksAreValid = errors.Count == errorsBefore;
        }

        IReadOnlyList<ResearchTier> research = [];
        IReadOnlyList<AccentDef> accents = [];
        if (tracksAreValid)
        {
            if (Root(DataRules.ResearchFile) is { } researchRoot)
            {
                research = ResearchReader.Read(researchRoot, epochNames.Count);
            }

            if (Root(DataRules.AccentsFile) is { } accentsRoot)
            {
                accents = AccentsReader.Read(accentsRoot, tracks);
            }
        }

        RuleSet? rules = null;
        if (Root(DataRules.RulesFile) is { } rulesRoot)
        {
            var errorsBefore = errors.Count;
            var read = RulesReader.Read(
                rulesRoot,
                tracksAreValid ? tracks.Count : DataRules.MaxTracks,
                tracksAreValid ? epochNames.Count : DataRules.MaxTiers);
            rules = errors.Count == errorsBefore ? read : null;
        }

        ResourceRules? resources = null;
        if (Root(DataRules.ResourcesFile) is { } resourcesRoot)
        {
            var errorsBefore = errors.Count;
            var read = ResourcesReader.Read(resourcesRoot);
            resources = errors.Count == errorsBefore ? read : null;
        }

        var lines = new List<LineDef>();
        var chassis = new List<ChassisDef>();
        if (tracksAreValid && rules is not null)
        {
            var chassisFiles = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in linePaths)
            {
                if (Root(path) is not { } lineRoot)
                {
                    continue;
                }

                var errorsBefore = errors.Count;
                var line = LineReader.Read(lineRoot, IdOf(path, DataRules.LinesFolder), tracks, epochNames.Count, rules);
                if (errors.Count != errorsBefore)
                {
                    continue;
                }

                lines.Add(line);
                for (var i = 0; i < line.Chassis.Count; i++)
                {
                    if (!chassisFiles.TryAdd(line.Chassis[i].Id, path))
                    {
                        errors.Add(path, $"$.chassis[{i}].id", $"chassis '{line.Chassis[i].Id}' is already defined in {chassisFiles[line.Chassis[i].Id]}");
                    }
                }
            }

            lines.Sort((left, right) => left.Index.CompareTo(right.Index));
            if (errors.Count == 0)
            {
                (lines, chassis) = LineReader.WithChassisIndexes(lines);
            }
        }

        // Of the structures only the one every player starts with must have a file.
        var structures = new List<StructureDef>();
        if (tracksAreValid && rules is not null)
        {
            foreach (var path in structurePaths)
            {
                if (Root(path) is not { } structureRoot)
                {
                    continue;
                }

                var errorsBefore = errors.Count;
                var structure = StructureReader.Read(structureRoot, IdOf(path, DataRules.StructuresFolder), tracks, epochNames.Count, rules);
                if (errors.Count == errorsBefore)
                {
                    structures.Add(structure);
                }
            }

            structures.Sort((left, right) => left.Index.CompareTo(right.Index));
            StructureChecker.Check(structures, lines, rules, errors);
        }

        // A map's starts are measured with the starting structure's footprint and territory, so
        // the maps are read only when that file was valid.
        var maps = new List<MapDef>();
        if (rules is not null
            && structures.Find(structure => structure.Id == DataRules.StartStructure) is { TerritoryRadius: { } startRadius } start)
        {
            var capitol = new MapStartRules(start.Footprint, startRadius);
            foreach (var path in mapPaths)
            {
                if (Root(path) is { } mapRoot)
                {
                    maps.Add(MapReader.Read(mapRoot, IdOf(path, DataRules.MapsFolder), rules.Map, capitol));
                }
            }

            maps.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        }

        // The personalities of the computer player. No file is required: a data folder may have none.
        var personalities = new List<AiPersonalityDef>();
        if (tracksAreValid && rules is not null)
        {
            foreach (var path in aiPaths)
            {
                if (Root(path) is { } aiRoot)
                {
                    personalities.Add(AiReader.Read(aiRoot, IdOf(path, DataRules.AiFolder), tracks, epochNames.Count, rules));
                }
            }

            personalities.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        }

        return tracksAreValid && roles is not null && rules is not null && resources is not null
            ? new GameData(epochNames, tracks, roles, rules, research, resources, accents, lines, chassis, structures, maps, personalities, ContentHash: "")
            : null;
    }
}
