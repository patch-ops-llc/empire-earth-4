using Sim.Data;

namespace Sim.Tests.Data;

/// <summary>
/// The sample data with four structure files added. The files are not part of
/// <see cref="SampleData.Files"/>, whose content hash is pinned.
/// </summary>
internal static class SampleStructures
{
    public const string CapitolPath = SampleData.CapitolPath;
    public const string BarracksPath = "structures/barracks.jsonc";
    public const string TowerPath = "structures/tower.jsonc";
    public const string WallPath = "structures/wall.jsonc";

    public const string Capitol = SampleData.Capitol;

    public const string Barracks = """
        {
          "id": "barracks",
          "track": "manufacturing",
          "footprint": 3,
          "hp": 1000,
          "cost": { "materials": 150, "energy": 0 },
          "build_seconds": 35,
          "sight": 6,
          "trains": ["guard"]
        }
        """;

    public const string Tower = """
        {
          "id": "tower",
          "track": "protection",
          "footprint": 2,
          "hp": 600,
          "cost": { "materials": 120, "energy": 30 },
          "build_seconds": 30,
          "sight": 10,
          "attack": {
            "dps": 10, "interval_seconds": 1, "range": 7, "projectile_speed": 20,
            "cannot_hit": ["air"], "can_hit_all_from_protection": 3
          }
        }
        """;

    public const string Wall = """
        {
          "id": "wall",
          "track": "protection",
          "footprint": 1,
          "hp": 400,
          "cost": { "materials": 20, "energy": 0 },
          "build_seconds": 5,
          "sight": 2,
          "wall": { "gate": { "cost": { "materials": 40, "energy": 0 }, "build_seconds": 10 } }
        }
        """;

    /// <summary>The sample data with a Tower and a Wall in its roster and the four structure files.</summary>
    public static InMemoryDataFiles Files() => SampleData
        .With(SampleData.RulesPath, "\"structures\": [\"capitol\", \"barracks\"]", "\"structures\": [\"capitol\", \"barracks\", \"tower\", \"wall\"]")
        .Set(BarracksPath, Barracks)
        .Set(TowerPath, Tower)
        .Set(WallPath, Wall);

    public static InMemoryDataFiles With(string path, string oldText, string newText) => Files().Edit(path, oldText, newText);
}

/// <summary>What the loader reads from <c>structures/&lt;id&gt;.jsonc</c>, and what it rejects there.</summary>
public class StructureTests
{
    private const int Protection = 1;
    private const int Manufacturing = 3;
    private const int Logistics = 4;

    /// <summary>Every player starts with a Capitol, so that one file a data set must have.</summary>
    [Fact]
    public void ADataSetNeedsNoStructureFileButTheCapitols()
    {
        var data = DataAssert.Valid(SampleData.Files());

        Assert.Equal(["barracks", "capitol"], data.Rules.Structures);
        Assert.Equal(["capitol"], data.Structures.Select(structure => structure.Id));
        Assert.Same(data.Structures[0], data.StartStructure);
        Assert.Same(data.Structures[0], data.StructureOf("capitol"));
        Assert.Null(data.StructureOf(0));
        Assert.Throws<ArgumentException>(() => data.StructureOf("barracks"));

        var files = SampleData.Files();
        files.Remove(SampleData.CapitolPath);
        DataAssert.RejectedOnce(files, SampleData.CapitolPath, "", "this file is required and is missing");
    }

    [Fact]
    public void TheRosterMayNameStructuresThatHaveNoFileYet()
    {
        var files = SampleStructures.Files();
        files.Remove(SampleStructures.TowerPath);
        files.Remove(SampleStructures.BarracksPath);

        var data = DataAssert.Valid(files);

        Assert.Equal(["capitol", "wall"], data.Structures.Select(structure => structure.Id));
        Assert.Null(data.StructureOf(data.Rules.Structures.ToList().IndexOf("tower")));
    }

    [Fact]
    public void StructuresAreHeldInTheOrderOfTheRosterAndFoundByTheirIndexInIt()
    {
        var data = DataAssert.Valid(SampleStructures.Files());

        Assert.Equal(["barracks", "capitol", "tower", "wall"], data.Rules.Structures);
        Assert.Equal(["barracks", "capitol", "tower", "wall"], data.Structures.Select(structure => structure.Id));
        for (var index = 0; index < data.Rules.Structures.Count; index++)
        {
            Assert.Equal(index, data.Structures[index].Index);
            Assert.Same(data.Structures[index], data.StructureOf(index));
        }
    }

    [Fact]
    public void AStructureIsReadWithEveryValueOfItsFile()
    {
        var data = DataAssert.Valid(SampleStructures.Files());

        var capitol = data.Structures.Single(structure => structure.Id == "capitol");
        Assert.Equal(
            (Logistics, 1, 2, DataNumber.FromInteger(2000), 400, 0, 1800, 10),
            (capitol.Track, capitol.UnlockTier, capitol.Footprint, capitol.HitPoints, capitol.Materials, capitol.Energy, capitol.BuildTicks, capitol.Sight));
        Assert.Equal((6, 1, 3), (capitol.TerritoryRadius, capitol.ResearchSlots, capitol.MaxPerPlayer));
        Assert.True(capitol.TrainsWorkers);
        Assert.Empty(capitol.Trains);
        Assert.False(capitol.IsEnergyWorkSite);
        Assert.Null(capitol.Attack);
        Assert.Null(capitol.Wall);

        var barracks = data.Structures.Single(structure => structure.Id == "barracks");
        Assert.Equal(Manufacturing, barracks.Track);
        Assert.False(barracks.TrainsWorkers);
        Assert.Equal(["guard"], barracks.Trains.Select(line => data.Rules.Lines[line]));
        Assert.Equal((null, 0, null), (barracks.TerritoryRadius, barracks.ResearchSlots, barracks.MaxPerPlayer));
    }

    [Fact]
    public void ATowerIsReadWithItsAttack()
    {
        var data = DataAssert.Valid(SampleStructures.Files());

        var tower = data.Structures.Single(structure => structure.Id == "tower");
        var attack = tower.Attack!;
        Assert.Equal(Protection, tower.Track);
        Assert.Equal(DataNumber.FromInteger(10), attack.DamagePerSecond);
        Assert.Equal(
            new AttackDef(AttackKind.Ranged, IntervalTicks: 20, Range: DataNumber.FromInteger(7), ProjectileSpeed: DataNumber.FromInteger(20)),
            attack.Attack);
        Assert.Equal(["air"], attack.CannotHit.Select(line => data.Rules.Lines[line]));
        Assert.Equal(3, attack.CanHitAllFromProtection);
    }

    [Fact]
    public void AWallIsReadWithItsGate()
    {
        var data = DataAssert.Valid(SampleStructures.Files());

        var wall = data.Structures.Single(structure => structure.Id == "wall");
        Assert.Equal((1, 20, 0, 100), (wall.Footprint, wall.Materials, wall.Energy, wall.BuildTicks));
        Assert.Equal(new WallDef(new GateDef(Materials: 40, Energy: 0, BuildTicks: 200)), wall.Wall);
    }

    [Fact]
    public void AStructureMayShowNoTrackUnlockLaterAndBeAWorkSite()
    {
        var files = SampleStructures.With(SampleStructures.BarracksPath, "\"track\": \"manufacturing\",", "");
        files.Edit(SampleStructures.TowerPath, "\"track\": \"protection\",", "\"track\": \"protection\", \"unlock_tier\": 3, \"work_site\": \"energy\",");

        var data = DataAssert.Valid(files);

        Assert.Null(data.Structures.Single(structure => structure.Id == "barracks").Track);
        var tower = data.Structures.Single(structure => structure.Id == "tower");
        Assert.Equal((Protection, 3), (tower.Track, tower.UnlockTier));
        Assert.True(tower.IsEnergyWorkSite);
    }

    [Fact]
    public void WhatAStructureTrainsKeepsTheOrderOfTheFile()
    {
        var files = SampleStructures.Files();
        files.Set("lines/cavalry_armor.jsonc", SampleData.Guard.Replace("\"id\": \"guard\"", "\"id\": \"cavalry_armor\"", StringComparison.Ordinal)
            .Replace("hoplite", "rider", StringComparison.Ordinal)
            .Replace("legionary", "knight", StringComparison.Ordinal)
            .Replace("grenadier", "dragoon", StringComparison.Ordinal)
            .Replace("\"cavalry_armor\": \"1.5\", ", "", StringComparison.Ordinal));
        files.Edit(SampleStructures.BarracksPath, "\"trains\": [\"guard\"]", "\"trains\": [\"guard\", \"worker\", \"cavalry_armor\"]");

        var barracks = DataAssert.Valid(files).Structures.Single(structure => structure.Id == "barracks");

        Assert.True(barracks.TrainsWorkers);
        Assert.Equal([2, 1], barracks.Trains);
    }

    [Theory]
    [InlineData(SampleStructures.CapitolPath, "\"id\": \"capitol\"", "\"id\": \"barracks\"", "$.id", "must be 'capitol' to match the file name, found 'barracks'")]
    [InlineData(SampleStructures.CapitolPath, "\"id\": \"capitol\",", "", "$", "missing key \"id\"")]
    [InlineData(SampleStructures.CapitolPath, "\"track\": \"logistics\"", "\"track\": \"supply\"", "$.track", "unknown track 'supply'; tracks.jsonc has: firepower, protection")]
    [InlineData(SampleStructures.CapitolPath, "\"footprint\": 2", "\"footprint\": 0", "$.footprint", "must be between 1 and 8, found 0")]
    [InlineData(SampleStructures.CapitolPath, "\"footprint\": 2", "\"footprint\": \"2\"", "$.footprint", "expected a whole number, found text")]
    [InlineData(SampleStructures.CapitolPath, "\"hp\": 2000", "\"hp\": 0", "$.hp", "must be above 0 and at most 1000000, found 0")]
    [InlineData(SampleStructures.CapitolPath, "\"hp\": 2000", "\"hp\": 2000.5", "$.hp", "write 2000.5 as text")]
    [InlineData(SampleStructures.CapitolPath, "\"materials\": 400", "\"materials\": -1", "$.cost.materials", "must be between 0 and 1000000, found -1")]
    [InlineData(SampleStructures.CapitolPath, ", \"energy\": 0", "", "$.cost", "missing key \"energy\"")]
    [InlineData(SampleStructures.CapitolPath, "\"build_seconds\": 90", "\"build_seconds\": \"90.01\"", "$.build_seconds", "90.01 s is not a whole number of ticks")]
    [InlineData(SampleStructures.CapitolPath, "\"sight\": 10", "\"sight\": 65", "$.sight", "must be between 1 and 64, found 65")]
    [InlineData(SampleStructures.CapitolPath, "\"sight\": 10,", "", "$", "missing key \"sight\"")]
    [InlineData(SampleStructures.CapitolPath, "\"research_slots\": 1", "\"research_slots\": 0", "$.research_slots", "must be between 1 and 8, found 0")]
    [InlineData(SampleStructures.CapitolPath, "\"max_per_player\": 3", "\"max_per_player\": 0", "$.max_per_player", "must be between 1 and 1000, found 0")]
    [InlineData(SampleStructures.CapitolPath, "\"trains\": [\"worker\"]", "\"trains\": []", "$.trains", "at least 1")]
    [InlineData(SampleStructures.CapitolPath, "\"trains\": [\"worker\"]", "\"trains\": [\"worker\", \"worker\"]", "$.trains[1]", "'worker' is listed twice")]
    [InlineData(SampleStructures.CapitolPath, "\"trains\": [\"worker\"]", "\"trains\": [\"settler\"]", "$.trains[0]", "unknown line 'settler'; use worker for the worker; the roster in rules.jsonc has: air, cavalry_armor, guard")]
    [InlineData(SampleStructures.CapitolPath, "\"sight\": 10,", "\"sight\": 10, \"supply\": 5,", "$.supply", "unknown key \"supply\"")]
    [InlineData(SampleStructures.CapitolPath, "\"sight\": 10,", "\"sight\": 10, \"unlock_tier\": 5,", "$.unlock_tier", "tier 5 cannot be an unlock: tracks have tiers 1 to 4")]
    [InlineData(SampleStructures.CapitolPath, "\"sight\": 10,", "\"sight\": 10, \"unlock_tier\": 1,", "$.unlock_tier", "a structure that is there from the start has no \"unlock_tier\"")]
    [InlineData(SampleStructures.CapitolPath, "\"track\": \"logistics\",", "\"unlock_tier\": 2,", "$.unlock_tier", "needs \"track\"")]
    [InlineData(SampleStructures.CapitolPath, "\"sight\": 10,", "\"sight\": 10, \"work_site\": \"materials\",", "$.work_site", "unknown resource 'materials'; use energy")]
    [InlineData(SampleStructures.BarracksPath, "\"trains\": [\"guard\"]", "\"trains\": [\"guard\", \"guard\"]", "$.trains[1]", "line 'guard' is listed twice")]
    [InlineData(SampleStructures.BarracksPath, "\"footprint\": 3,", "\"footprint\": 3, \"wall\": { \"gate\": { \"cost\": { \"materials\": 40, \"energy\": 0 }, \"build_seconds\": 10 } },", "$.wall", "its \"footprint\" must be 1, found 3")]
    [InlineData(SampleStructures.TowerPath, "\"dps\": 10", "\"dps\": 0", "$.attack.dps", "must be above 0")]
    [InlineData(SampleStructures.TowerPath, "\"interval_seconds\": 1", "\"interval_seconds\": \"0.33\"", "$.attack.interval_seconds", "0.33 s is not a whole number of ticks")]
    [InlineData(SampleStructures.TowerPath, "\"range\": 7", "\"range\": 65", "$.attack.range", "must be above 0 and at most 64, found 65")]
    [InlineData(SampleStructures.TowerPath, "\"projectile_speed\": 20,", "", "$.attack", "missing key \"projectile_speed\"")]
    [InlineData(SampleStructures.TowerPath, "\"range\": 7,", "\"range\": 7, \"kind\": \"melee\",", "$.attack.kind", "unknown key \"kind\"")]
    [InlineData(SampleStructures.TowerPath, "\"cannot_hit\": [\"air\"]", "\"cannot_hit\": [\"air\", \"air\"]", "$.attack.cannot_hit[1]", "line 'air' is listed twice")]
    [InlineData(SampleStructures.TowerPath, "\"cannot_hit\": [\"air\"]", "\"cannot_hit\": [\"planes\"]", "$.attack.cannot_hit[0]", "unknown line 'planes'")]
    [InlineData(SampleStructures.TowerPath, "\"cannot_hit\": [\"air\"], ", "", "$.attack.can_hit_all_from_protection", "needs \"cannot_hit\"")]
    [InlineData(SampleStructures.TowerPath, "\"can_hit_all_from_protection\": 3", "\"can_hit_all_from_protection\": 5", "$.attack.can_hit_all_from_protection", "tier 5 changes nothing: tracks have tiers 1 to 4")]
    [InlineData(SampleStructures.TowerPath, "\"can_hit_all_from_protection\": 3", "\"can_hit_all_from_protection\": 1", "$.attack.can_hit_all_from_protection", "an attack that can hit every line from the start has no \"cannot_hit\"")]
    [InlineData(SampleStructures.WallPath, "\"build_seconds\": 10 }", "\"build_seconds\": 0 }", "$.wall.gate.build_seconds", "must be")]
    [InlineData(SampleStructures.WallPath, "\"gate\":", "\"door\":", "$.wall", "missing key \"gate\"")]
    [InlineData(SampleStructures.WallPath, "\"materials\": 40", "\"materials\": 1000001", "$.wall.gate.cost.materials", "must be between 0 and 1000000, found 1000001")]
    public void AStructureMustBeCompleteAndSane(string path, string oldText, string newText, string location, string message)
    {
        var files = SampleStructures.With(path, oldText, newText);

        DataAssert.Rejected(files, path, location, message);
    }

    [Fact]
    public void AStructureFileMustBelongToAStructureOfTheRoster()
    {
        var files = SampleData.Files().Set(SampleStructures.TowerPath, SampleStructures.Tower);

        DataAssert.RejectedOnce(files, SampleStructures.TowerPath, "$.id", "unknown structure 'tower'; the roster in rules.jsonc has: barracks, capitol");
    }

    [Fact]
    public void AStructureMustListEveryLineThatNamesItInBuiltAt()
    {
        var files = SampleStructures.With(SampleStructures.BarracksPath, ",\n  \"trains\": [\"guard\"]", "");

        DataAssert.RejectedOnce(files, SampleStructures.BarracksPath, "$", "line 'guard' names this structure in \"built_at\", so \"trains\" must list it");
    }

    [Fact]
    public void AStructureCannotTrainALineThatIsBuiltElsewhere()
    {
        var files = SampleStructures.With(SampleStructures.CapitolPath, "\"trains\": [\"worker\"]", "\"trains\": [\"worker\", \"guard\"]");

        DataAssert.RejectedOnce(files, SampleStructures.CapitolPath, "$.trains", "line 'guard' is built at 'barracks' by its \"built_at\", so it cannot be trained here");
    }

    [Fact]
    public void AStructureMayListALineThatHasNoFileYet()
    {
        var files = SampleStructures.With(SampleStructures.BarracksPath, "\"trains\": [\"guard\"]", "\"trains\": [\"guard\", \"air\"]");

        var barracks = DataAssert.Valid(files).Structures.Single(structure => structure.Id == "barracks");

        Assert.Equal(["guard", "air"], barracks.Trains.Select(line => DataAssert.Valid(files).Rules.Lines[line]));
    }

    /// <summary>
    /// The sample map fits a Capitol of 2 by 2 tiles with a territory of 6. With another Capitol
    /// its starts and deposits are measured anew, and no longer fit.
    /// </summary>
    [Theory]
    [InlineData("\"footprint\": 2", "\"footprint\": 4", "$.starts[1]", "must mirror start 0 under mirror_x")]
    [InlineData("\"territory_radius\": 6", "\"territory_radius\": 20", "$.starts", "the starting Capitols are too close: their centers must be at least 40 tiles apart")]
    [InlineData("\"territory_radius\": 6", "\"territory_radius\": 3", "$.deposits[0]", "a main deposit must lie wholly inside a starting territory (every tile center within 3 tiles")]
    public void AMapIsCheckedWithTheFootprintAndTheTerritoryOfTheCapitolsFile(string oldText, string newText, string location, string message)
    {
        var files = SampleStructures.With(SampleStructures.CapitolPath, oldText, newText);

        DataAssert.Rejected(files, SampleData.MapPath, location, message);
    }

    [Fact]
    public void TheRulesNoLongerRepeatTheCapitolsFootprintAndTerritory()
    {
        var files = SampleData.With(SampleData.RulesPath, "\"deposit_size\": 2,", "\"deposit_size\": 2, \"start_territory\": { \"footprint\": 2, \"radius\": 6 },");

        DataAssert.RejectedOnce(files, SampleData.RulesPath, "$.map.start_territory", "unknown key \"start_territory\"");
    }

    [Fact]
    public void TheCapitolNeedsATerritoryRadius()
    {
        var files = SampleStructures.With(SampleStructures.CapitolPath, "\"territory_radius\": 6,", "");

        DataAssert.RejectedOnce(files, SampleStructures.CapitolPath, "$", "every player starts with this structure, so it needs \"territory_radius\"");
    }

    [Fact]
    public void AStructureFileChangesTheContentHash()
    {
        var without = DataAssert.Valid(SampleData.With(SampleData.RulesPath, "\"structures\": [\"capitol\", \"barracks\"]", "\"structures\": [\"capitol\", \"barracks\", \"tower\", \"wall\"]"));
        var with = DataAssert.Valid(SampleStructures.Files());
        var changed = DataAssert.Valid(SampleStructures.With(SampleStructures.TowerPath, "\"range\": 7", "\"range\": 8"));

        Assert.NotEqual(without.ContentHash, with.ContentHash);
        Assert.NotEqual(with.ContentHash, changed.ContentHash);
    }
}
