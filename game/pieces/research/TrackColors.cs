using Godot;
using Sim.Data;

namespace Game;

/// <summary>
/// The fixed color of each track (design 3.2): the research grid uses it, and accent badges and
/// banners are to use the same one, so that a color means one track everywhere.
/// </summary>
public static class TrackColors
{
    private static readonly Color Firepower = new(0.90f, 0.30f, 0.25f);
    private static readonly Color Protection = new(0.32f, 0.57f, 0.95f);
    private static readonly Color Mobility = new(0.30f, 0.78f, 0.46f);
    private static readonly Color Manufacturing = new(0.95f, 0.58f, 0.22f);
    private static readonly Color Logistics = new(0.68f, 0.46f, 0.92f);
    private static readonly Color Extraction = new(0.92f, 0.80f, 0.28f);

    // A track the rules do not name, should the data ever hold one.
    private static readonly Color Other = new(0.70f, 0.72f, 0.76f);

    /// <summary>The color of a track, by its index in the data's tracks.</summary>
    public static Color Of(GameData data, int track)
    {
        var roles = data.TrackRoles;
        if (track == roles.Firepower)
        {
            return Firepower;
        }

        if (track == roles.Protection)
        {
            return Protection;
        }

        if (track == roles.Mobility)
        {
            return Mobility;
        }

        if (track == roles.Manufacturing)
        {
            return Manufacturing;
        }

        if (track == roles.Logistics)
        {
            return Logistics;
        }

        return track == roles.Extraction ? Extraction : Other;
    }
}
