namespace Sim.Data;

internal static class TracksReader
{
    private static readonly string[] TracksTheRulesName =
    [
        DataRules.FirepowerTrack,
        DataRules.ProtectionTrack,
        DataRules.MobilityTrack,
        DataRules.ManufacturingTrack,
        DataRules.LogisticsTrack,
        DataRules.ExtractionTrack,
    ];

    public static (IReadOnlyList<string> EpochNames, IReadOnlyList<TrackDef> Tracks, TrackRoles Roles) Read(JsonValue root)
    {
        var errorsBefore = root.ErrorCount;
        var file = root.Object();
        file.Optional(DataRules.SchemaKey)?.String();

        var epochNames = new List<string>();
        foreach (var epoch in file.Required("epochs").Array(1, DataRules.MaxTiers))
        {
            epochNames.Add(epoch.String());
        }

        var tracks = new List<TrackDef>();
        var tracksValue = file.Required("tracks");
        foreach (var trackValue in tracksValue.Array(1, DataRules.MaxTracks))
        {
            var track = trackValue.Object();
            var idValue = track.Required("id");
            var id = idValue.Id();
            if (id.Length > 0 && tracks.Exists(other => other.Id == id))
            {
                idValue.Error($"track '{id}' is defined twice");
            }

            var name = track.Required("name").String();
            var shortName = track.Required("short").String();
            var group = ReadGroup(track.Required("group"));

            var tierNames = new List<string>();
            var tiersValue = track.Required("tiers");
            foreach (var tier in tiersValue.Array())
            {
                tierNames.Add(tier.String());
            }

            if (epochNames.Count > 0 && tierNames.Count != epochNames.Count)
            {
                tiersValue.Error($"must name {epochNames.Count} tiers, one per epoch, found {tierNames.Count}");
            }

            track.End();
            tracks.Add(new TrackDef(id, name, shortName, group, tierNames));
        }

        file.End();

        var roles = new int[TracksTheRulesName.Length];
        var missing = new List<string>();
        for (var role = 0; role < roles.Length; role++)
        {
            roles[role] = tracks.FindIndex(track => track.Id == TracksTheRulesName[role]);
            if (roles[role] < 0)
            {
                missing.Add(TracksTheRulesName[role]);
            }
        }

        if (missing.Count > 0 && root.ErrorCount == errorsBefore)
        {
            tracksValue.Error(
                $"the rules name the tracks {string.Join(", ", TracksTheRulesName)}, and each needs an entry with that ID; missing: {string.Join(", ", missing)}");
        }

        return (epochNames, tracks, new TrackRoles(roles[0], roles[1], roles[2], roles[3], roles[4], roles[5]));
    }

    private static TrackGroup ReadGroup(JsonValue value)
    {
        var text = value.String();
        switch (text)
        {
            case "":
            case "better":
                return TrackGroup.Better;
            case "more":
                return TrackGroup.More;
            case "richer":
                return TrackGroup.Richer;
            default:
                value.Error($"unknown group '{text}'; use better, more or richer");
                return TrackGroup.Better;
        }
    }
}
