namespace Tools.Tests;

/// <summary>
/// Runs <c>datacheck</c> the way the command line does: on the real <c>game/data</c>, which must
/// pass, and on copies of it with one thing broken, which must fail and name what is wrong.
/// </summary>
public class DataCheckToolTests
{
    [Fact]
    public void TheGameDataPasses()
    {
        var (exitCode, output, errors) = Run("datacheck", Repository.DataFolder);

        Assert.Equal("", errors);
        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Contains("is valid.", output, StringComparison.Ordinal);
        Assert.Contains("  research:     8 tiers, from 2 to 9\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("  accents:      5\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("  structures:   8 of the 11 in the roster\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Matches("content hash: sha256:[0-9a-f]{64}", output);
    }

    [Theory]
    [InlineData("lines/guard.jsonc", "\"hp\": 130", "\"hp\": 0", "lines/guard.jsonc: $.base.hp: must be above 0")]
    [InlineData("lines/guard.jsonc", "\"speed\": \"2.6\"", "\"speed\": 2.6", "lines/guard.jsonc: $.base.speed: write 2.6 as text")]
    [InlineData("lines/guard.jsonc", "\"primary\": \"protection\"", "\"primary\": \"armor\"", "lines/guard.jsonc: $.primary: unknown track 'armor'")]
    [InlineData("lines/guard.jsonc", "\"built_at\": \"barracks\"", "\"built_at\": \"stable\"", "lines/guard.jsonc: $.built_at: unknown structure 'stable'")]
    [InlineData("lines/guard.jsonc", "\"cavalry_armor\": \"1.5\"", "\"cavalry\": \"1.5\"", "lines/guard.jsonc: $.counters.cavalry: unknown line 'cavalry'")]
    [InlineData("lines/guard.jsonc", "\"epoch\": 4", "\"epoch\": 10", "lines/guard.jsonc: $.chassis[3].epoch: epoch 10 can never unlock: tracks have tiers 1 to 9")]
    [InlineData("lines/guard.jsonc", "\"interval_seconds\": \"1\"", "\"interval_seconds\": \"0.33\"", "lines/guard.jsonc: $.attack.interval_seconds: 0.33 s is not a whole number of ticks")]
    [InlineData("lines/guard.jsonc", "\"build_seconds\": 18", "\"build_seconds\": \"18.01\"", "lines/guard.jsonc: $.base.build_seconds: 18.01 s is not a whole number of ticks")]
    [InlineData("lines/guard.jsonc", "\"sight\": 8,", "\"sight\": 8, \"sihgt\": 8,", "lines/guard.jsonc: $.base.sihgt: unknown key \"sihgt\"")]
    [InlineData("lines/guard.jsonc", "\"supply\": 2,", "\"supply\": 2", "lines/guard.jsonc: line 18, column 5: not valid JSON")]
    [InlineData("lines/artillery.jsonc", "\"min_range\": 3", "\"min_range\": 9", "lines/artillery.jsonc: $.attack: \"min_range\" must be below \"range\", found 9 and 9")]
    [InlineData("lines/artillery.jsonc", "\"blast_radius\": \"1.5\"", "\"blast_radius\": 0", "lines/artillery.jsonc: $.attack.blast_radius: must be above 0 and at most 64, found 0")]
    [InlineData("lines/cavalry_armor.jsonc", "\"interval_seconds\": \"1.5\" }", "\"interval_seconds\": \"1.5\", \"blast_radius\": 1 }", "lines/cavalry_armor.jsonc: $.attack: a melee attack has no \"range\", \"projectile_speed\", \"min_range\" or \"blast_radius\"")]
    [InlineData("lines/line_infantry.jsonc", "\"id\": \"peltast\"", "\"id\": \"hoplite\"", "lines/line_infantry.jsonc: $.chassis[0].id: chassis 'hoplite' is already defined in lines/guard.jsonc")]
    [InlineData("lines/line_infantry.jsonc", "\"primary\": \"manufacturing\"", "\"primary\": \"industry\"", "lines/line_infantry.jsonc: $.primary: unknown track 'industry'")]
    [InlineData("rules.jsonc", "\"hp\": \"1.25\"", "\"hp\": \"1.123456789\"", "lines/artillery.jsonc: $.chassis[2]: a stat or price of chassis 'trebuchet' is too large to compute exactly at some tier mix")]
    [InlineData("rules.jsonc", "\"hp\": \"1.25\"", "\"hp\": \"1.123456789\"", "structures/capitol.jsonc: $.hp: the hit points are too large to compute exactly at some Protection tier")]
    [InlineData("structures/capitol.jsonc", "\"footprint\": 4", "\"footprint\": 5", "maps/ridge.jsonc: $.starts[1]: must mirror start 0 under mirror_x")]
    [InlineData("structures/capitol.jsonc", "\"territory_radius\": 16", "\"territory_radius\": 40", "maps/ridge.jsonc: $.starts: the starting Capitols are too close: their centers must be at least 80 tiles apart")]
    [InlineData("structures/barracks.jsonc", "[\"line_infantry\", \"guard\"]", "[\"line_infantry\"]", "structures/barracks.jsonc: $.trains: line 'guard' names this structure in \"built_at\", so \"trains\" must list it")]
    [InlineData("structures/works.jsonc", "\"cavalry_armor\", ", "\"cavalry_armor\", \"guard\", ", "structures/works.jsonc: $.trains: line 'guard' is built at 'barracks' by its \"built_at\", so it cannot be trained here")]
    [InlineData("structures/works.jsonc", "\"track\": \"mobility\"", "\"track\": \"speed\"", "structures/works.jsonc: $.track: unknown track 'speed'")]
    [InlineData("structures/generator.jsonc", "\"work_site\": \"energy\"", "\"work_site\": \"materials\"", "structures/generator.jsonc: $.work_site: unknown resource 'materials'; use energy")]
    [InlineData("structures/academy.jsonc", "\"id\": \"academy\"", "\"id\": \"school\"", "structures/academy.jsonc: $.id: must be 'academy' to match the file name, found 'school'")]
    [InlineData("structures/wall.jsonc", "\"footprint\": 1", "\"footprint\": 2", "structures/wall.jsonc: $.wall: a wall is laid tile by tile, so its \"footprint\" must be 1, found 2")]
    [InlineData("structures/tower.jsonc", "\"interval_seconds\": 1", "\"interval_seconds\": \"0.33\"", "structures/tower.jsonc: $.attack.interval_seconds: 0.33 s is not a whole number of ticks")]
    [InlineData("structures/tower.jsonc", "\"can_hit_all_from_protection\": 5", "\"can_hit_all_from_protection\": 10", "structures/tower.jsonc: $.attack.can_hit_all_from_protection: tier 10 changes nothing: tracks have tiers 1 to 9")]
    [InlineData("structures/tower.jsonc", "\"cannot_hit\": [\"air\"]", "\"cannot_hit\": [\"planes\"]", "structures/tower.jsonc: $.attack.cannot_hit[0]: unknown line 'planes'")]
    [InlineData("rules.jsonc", "\"epoch_cap\": 4", "\"epoch_cap\": 10", "rules.jsonc: $.match.epoch_cap: must be between 1 and 9, found 10")]
    [InlineData("rules.jsonc", "\"tracks_needed\": 3", "\"tracks_needed\": 7", "rules.jsonc: $.epoch.tracks_needed: must be between 1 and 6, found 7")]
    [InlineData("tracks.jsonc", "\"Antimatter\"", "\"Antimatter\", \"Beyond\"", "tracks.jsonc: $.tracks[5].tiers: must name 9 tiers, one per epoch, found 10")]
    [InlineData("tracks.jsonc", "\"id\": \"extraction\"", "\"id\": \"mining\"", "tracks.jsonc: $.tracks: the rules name the tracks firepower, protection, mobility, manufacturing, logistics, extraction, and each needs an entry with that ID; missing: extraction")]
    [InlineData("research.jsonc", "\"tier\": 5,", "\"tier\": 6,", "research.jsonc: $.tiers[3].tier: list every tier from 2 to 9 in order; expected tier 5 here, found 6")]
    [InlineData("research.jsonc", "\"seconds\": 150 }", "\"seconds\": 150 },\n    { \"tier\": 10, \"cost\": { \"materials\": 1, \"energy\": 1 }, \"seconds\": 1 }", "research.jsonc: $.tiers[8].tier: tracks have tiers 1 to 9, so the last entry is tier 9; found tier 10")]
    [InlineData("research.jsonc", "\"materials\": 225,", "\"materials\": \"225\",", "research.jsonc: $.tiers[2].cost.materials: expected a whole number, found text")]
    [InlineData("resources.jsonc", "\"per_worker_per_minute\": 45", "\"per_worker_per_minute\": 0", "resources.jsonc: $.materials.per_worker_per_minute: must be above 0 and at most 100000, found 0")]
    [InlineData("resources.jsonc", "\"depleted_rate\": \"0.5\"", "\"depleted_rate\": 0.5", "resources.jsonc: $.materials.depleted_rate: write 0.5 as text")]
    [InlineData("resources.jsonc", "\"workers_per_generator\": 4", "\"generator_slots\": 4", "resources.jsonc: $.energy: missing key \"workers_per_generator\"")]
    [InlineData("resources.jsonc", "\"build_seconds\": 12", "\"build_seconds\": \"12.01\"", "resources.jsonc: $.worker.build_seconds: 12.01 s is not a whole number of ticks")]
    [InlineData("accents.jsonc", "\"mobility\": {", "\"speed\": {", "accents.jsonc: $.accents.speed: unknown track 'speed'")]
    [InlineData("accents.jsonc", "\"plating\":", "\"armor\":", "accents.jsonc: $.accents.protection.level_2.armor: unknown key \"armor\"")]
    [InlineData("accents.jsonc", "\"share\": \"0.25\"", "\"share\": \"25\"", "accents.jsonc: $.accents.manufacturing.level_2.death_refund.share: must be above 0 and at most 1, found 25")]
    [InlineData("accents.jsonc", "\"plating\":", "\"batch\": { \"time_multiplier\": \"1.2\" }, \"plating\":", "accents.jsonc: $.accents.manufacturing.level_1.batch: \"batch\" is already given at $.accents.protection.level_2.batch")]
    [InlineData("maps/ridge.jsonc", "{ \"x\": 82, \"y\": 46 }", "{ \"x\": 82, \"y\": 47 }", "maps/ridge.jsonc: $.starts[1]: must mirror start 0 under mirror_x: expected x = 82, y = 46")]
    [InlineData("maps/ridge.jsonc", "    { \"kind\": \"contested\", \"x\": 38, \"y\": 62 },\n", "", "maps/ridge.jsonc: $.deposits: needs 4 contested deposits, found 3")]
    [InlineData("maps/ridge.jsonc", "{ \"kind\": \"main\", \"x\": 6, \"y\": 38 }", "{ \"kind\": \"main\", \"x\": 6, \"y\": 30 }", "maps/ridge.jsonc: $.deposits[0]: a main deposit must lie wholly inside a starting territory")]
    [InlineData("maps/ridge.jsonc", "{ \"kind\": \"natural\", \"x\": 24, \"y\": 70 }", "{ \"kind\": \"natural\", \"x\": 24, \"y\": 50 }", "maps/ridge.jsonc: $.deposits[4]: a natural deposit must lie outside every starting territory")]
    [InlineData("maps/ridge.jsonc", "\"size_y\": 6, \"across\": \"y\"", "\"size_y\": 5, \"across\": \"y\"", "maps/ridge.jsonc: $.markers[0]: the passage is wider than the marker")]
    [InlineData("maps/ridge.jsonc", "\"kind\": \"open_field\"", "\"kind\": \"chokepoint\"", "maps/ridge.jsonc: $.markers[1]: a chokepoint must be 4 to 8 tiles wide, found 28")]
    public void BrokenDataFails(string file, string oldText, string newText, string expectedError)
    {
        using var data = Repository.CopyOfData();
        data.Edit(file, oldText, newText);

        var (exitCode, output, errors) = Run("datacheck", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal("", output);
        Assert.Contains($"{data.Path}/{expectedError}", errors, StringComparison.Ordinal);
        Assert.Matches(@"datacheck: \d+ errors? in ", errors);
    }

    [Fact]
    public void OneTileOfTerrainThatDoesNotMirrorFails()
    {
        using var data = Repository.CopyOfData();
        var path = Path.Combine(data.Path, "maps", "ridge.jsonc");
        var lines = File.ReadAllLines(path);
        var firstRow = Array.FindIndex(lines, line => line.StartsWith("    \"..", StringComparison.Ordinal));
        lines[firstRow + 90] = lines[firstRow + 90].Remove(5 + 60, 1).Insert(5 + 60, "#");
        File.WriteAllLines(path, lines);

        var (exitCode, _, errors) = Run("datacheck", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Contains(
            "maps/ridge.jsonc: $.terrain: is not symmetric under mirror_x: tile (35, 90) is open ground but its mirror tile (60, 90) is cliff",
            errors,
            StringComparison.Ordinal);
        Assert.Contains("datacheck: 1 error in ", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AStrayFileFails()
    {
        using var data = Repository.CopyOfData();
        data.Write("weapons/v_rocket.jsonc", "{}");

        var (exitCode, _, errors) = Run("datacheck", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Contains("weapons/v_rocket.jsonc: unexpected file", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFileFails()
    {
        using var data = Repository.CopyOfData();
        File.Delete(Path.Combine(data.Path, "rules.jsonc"));

        var (exitCode, _, errors) = Run("datacheck", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Contains("rules.jsonc: this file is required and is missing", errors, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("research.jsonc")]
    [InlineData("resources.jsonc")]
    [InlineData("accents.jsonc")]
    public void AMissingResearchResourcesOrAccentsFileFails(string file)
    {
        using var data = Repository.CopyOfData();
        File.Delete(Path.Combine(data.Path, file));

        var (exitCode, _, errors) = Run("datacheck", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Contains($"{file}: this file is required and is missing", errors, StringComparison.Ordinal);
        Assert.Contains("datacheck: 1 error in ", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryErrorIsListed()
    {
        using var data = Repository.CopyOfData();
        data.Edit("lines/guard.jsonc", "\"hp\": 130", "\"hp\": 0");
        data.Edit("lines/guard.jsonc", "\"sight\": 8", "\"sight\": 0");
        data.Edit("maps/ridge.jsonc", "\"kind\": \"chokepoint\"", "\"kind\": \"gap\"");

        var (exitCode, _, errors) = Run("datacheck", data.Path);

        Assert.Equal(ExitCodes.Failed, exitCode);
        Assert.Equal(4, errors.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("datacheck: 3 errors in ", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatDoesNotExistIsAUsageError()
    {
        var (exitCode, output, errors) = Run("datacheck", Path.Combine(Repository.Root, "no-such-folder"));

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", output);
        Assert.Contains("there is no folder", errors, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreThanOneFolderIsAUsageError()
    {
        var (exitCode, _, errors) = Run("datacheck", Repository.DataFolder, Repository.DataFolder);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains("Usage: datacheck [data folder]", errors, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Errors) Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var exitCode = ToolRunner.Run(arguments, output, errors);
        return (exitCode, output.ToString(), errors.ToString());
    }
}
