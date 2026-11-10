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
    private const string Layout = "tracks.jsonc, rules.jsonc, lines/<id>.jsonc and maps/<id>.jsonc";

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
            var mapPaths = new List<string>();
            foreach (var path in paths)
            {
                if (!Classify(path, linePaths, mapPaths, errors))
                {
                    continue;
                }

                if (Parse(files.ReadFile(path), path, errors) is { } document)
                {
                    documents.Add(document);
                    roots[path] = document.RootElement;
                }
            }

            ReportMissing(paths, linePaths, mapPaths, errors);
            var data = Read(roots, linePaths, mapPaths, errors);

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

    private static bool Classify(string path, List<string> linePaths, List<string> mapPaths, ErrorList errors)
    {
        if (path is DataRules.TracksFile or DataRules.RulesFile)
        {
            return true;
        }

        foreach (var (folder, found) in new[] { (DataRules.LinesFolder, linePaths), (DataRules.MapsFolder, mapPaths) })
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

    private static void ReportMissing(SortedSet<string> paths, List<string> linePaths, List<string> mapPaths, ErrorList errors)
    {
        foreach (var required in new[] { DataRules.TracksFile, DataRules.RulesFile })
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
        List<string> mapPaths,
        ErrorList errors)
    {
        JsonValue? Root(string path) => roots.TryGetValue(path, out var root) ? new JsonValue(root, path, "$", errors) : null;

        IReadOnlyList<string> epochNames = [];
        IReadOnlyList<TrackDef> tracks = [];
        var tracksAreValid = false;
        if (Root(DataRules.TracksFile) is { } tracksRoot)
        {
            var errorsBefore = errors.Count;
            (epochNames, tracks) = TracksReader.Read(tracksRoot);
            tracksAreValid = errors.Count == errorsBefore;
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

        var lines = new List<LineDef>();
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
        }

        var maps = new List<MapDef>();
        if (rules is not null)
        {
            foreach (var path in mapPaths)
            {
                if (Root(path) is { } mapRoot)
                {
                    maps.Add(MapReader.Read(mapRoot, IdOf(path, DataRules.MapsFolder), rules.Map));
                }
            }

            maps.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        }

        return tracksAreValid && rules is not null ? new GameData(epochNames, tracks, rules, lines, maps, ContentHash: "") : null;
    }
}
