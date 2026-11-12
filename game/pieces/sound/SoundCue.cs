namespace Game;

/// <summary>
/// The sounds of a match: one for each thing the player is told by ear. The design's first
/// playable asks for minimal sound (design 6), and this list is all of it.
/// </summary>
/// <remarks>
/// A cue is either of the world or a notice. One of the world happens at a place: it is louder
/// near where the camera looks, and it follows the fog as the effect drawn for it does. A notice
/// is for the local player alone and sounds the same wherever the camera is.
/// </remarks>
public enum SoundCue
{
    /// <summary>An attack landed, hand to hand. Of the world.</summary>
    MeleeHit,

    /// <summary>An attack landed: a shot reached its target. Of the world.</summary>
    ShotHit,

    /// <summary>A unit died. Of the world.</summary>
    UnitDied,

    /// <summary>A structure of the local player's is finished. A notice.</summary>
    StructureFinished,

    /// <summary>A structure was destroyed. Of the world.</summary>
    StructureLost,

    /// <summary>A research of the local player's is finished. A notice.</summary>
    ResearchFinished,

    /// <summary>A player reached a new epoch, which is announced to all (design 1.4). A notice.</summary>
    EpochReached,

    /// <summary>The local player gave an order, and it left for the match. A notice.</summary>
    OrderGiven,

    /// <summary>An order of the local player's was not given, or the match refused it. A notice.</summary>
    OrderRefused,
}

/// <summary>What is fixed about each <see cref="SoundCue"/>: its name, how loud it is and how soon it may sound again.</summary>
public static class SoundCues
{
    /// <summary>Every cue, in the order of the enumeration.</summary>
    public static IReadOnlyList<SoundCue> All { get; } = Enum.GetValues<SoundCue>();

    /// <summary>True for a cue that happens at a place on the map.</summary>
    public static bool IsOfTheWorld(SoundCue cue) =>
        cue is SoundCue.MeleeHit or SoundCue.ShotHit or SoundCue.UnitDied or SoundCue.StructureLost;

    /// <summary>The cue as a player is told of it, to finish the sentence "The sound when ...".</summary>
    public static string Name(SoundCue cue) => cue switch
    {
        SoundCue.MeleeHit => "an attack lands, hand to hand",
        SoundCue.ShotHit => "a shot lands",
        SoundCue.UnitDied => "a unit dies",
        SoundCue.StructureFinished => "a structure of yours is finished",
        SoundCue.StructureLost => "a structure is destroyed",
        SoundCue.ResearchFinished => "a research of yours is finished",
        SoundCue.EpochReached => "a player reaches a new epoch",
        SoundCue.OrderGiven => "you give an order",
        SoundCue.OrderRefused => "an order is refused",
        _ => cue.ToString(),
    };

    /// <summary>
    /// How loud the cue is before the place and the player's setting are counted, from 0 to 1.
    /// The frequent ones are the quiet ones: an order's answer is heard on every click, and a
    /// hit many times a second.
    /// </summary>
    public static float Gain(SoundCue cue) => cue switch
    {
        SoundCue.MeleeHit => 0.5f,
        SoundCue.ShotHit => 0.5f,
        SoundCue.UnitDied => 0.6f,
        SoundCue.StructureFinished => 0.7f,
        SoundCue.StructureLost => 0.8f,
        SoundCue.ResearchFinished => 0.7f,
        SoundCue.EpochReached => 0.85f,
        SoundCue.OrderGiven => 0.45f,
        SoundCue.OrderRefused => 0.45f,
        _ => 0.5f,
    };

    /// <summary>
    /// The least time, in seconds, between two starts of the cue. Whatever asks for it sooner is
    /// not played, so that a large fight is a steady sound and not a pile of the same one.
    /// </summary>
    public static double Gap(SoundCue cue) => cue switch
    {
        SoundCue.MeleeHit => 0.1,
        SoundCue.ShotHit => 0.1,
        SoundCue.UnitDied => 0.12,
        SoundCue.StructureFinished => 0.3,
        SoundCue.StructureLost => 0.25,
        SoundCue.ResearchFinished => 0.3,
        SoundCue.EpochReached => 1.0,
        SoundCue.OrderGiven => 0.08,
        SoundCue.OrderRefused => 0.2,
        _ => 0.1,
    };

    /// <summary>
    /// How far the cue's pitch may wander from one play to the next, as a part of its pitch: the
    /// cues heard many times in a row are never twice quite the same. A notice keeps its pitch.
    /// </summary>
    public static float PitchSpread(SoundCue cue) => cue switch
    {
        SoundCue.MeleeHit or SoundCue.ShotHit => 0.07f,
        SoundCue.UnitDied => 0.05f,
        SoundCue.StructureLost => 0.03f,
        _ => 0f,
    };
}
