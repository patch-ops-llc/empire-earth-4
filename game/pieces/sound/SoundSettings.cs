using Godot;

namespace Game;

/// <summary>
/// The player's two settings for sound: how loud, and whether at all. They are the game's, not
/// a match's: they hold from one match to the next and are kept in a file in the player's own
/// folder, so they are there the next time the game starts.
/// </summary>
/// <remarks>
/// Every sound of a match goes through one bus of the engine's mixer, named
/// <see cref="BusName"/>, which is made here the first time it is needed. The settings turn
/// that bus down or off and leave the engine's main bus alone, for whatever else the game
/// plays later.
/// </remarks>
public static class SoundSettings
{
    /// <summary>The bus of the engine's mixer that every sound of a match is played on.</summary>
    public const string BusName = "Sound";

    /// <summary>The volume of a game whose player has never set it.</summary>
    public const float DefaultVolume = 0.8f;

    private const string FilePath = "user://settings.cfg";
    private const string Section = "sound";
    private const string VolumeKey = "volume";
    private const string MutedKey = "muted";

    private static bool s_loaded;

    /// <summary>How loud, from 0 to 1, as the slider shows it.</summary>
    public static float Volume { get; private set; } = DefaultVolume;

    /// <summary>True while all sound is off.</summary>
    public static bool Muted { get; private set; }

    /// <summary>
    /// Reads the settings the player last chose, the first time it is called, and sets the bus
    /// by them. A game with no settings file, or one that cannot be read, has the defaults.
    /// </summary>
    public static void Load()
    {
        if (!s_loaded)
        {
            s_loaded = true;
            var file = new ConfigFile();
            if (file.Load(FilePath) == Error.Ok)
            {
                Volume = Mathf.Clamp(file.GetValue(Section, VolumeKey, DefaultVolume).AsSingle(), 0f, 1f);
                Muted = file.GetValue(Section, MutedKey, false).AsBool();
            }
        }

        Apply();
    }

    /// <summary>Sets the volume, from 0 to 1, and keeps it.</summary>
    public static void SetVolume(float volume)
    {
        Volume = Mathf.Clamp(volume, 0f, 1f);
        Apply();
        Save();
    }

    /// <summary>Turns all sound off or on, and keeps that.</summary>
    public static void SetMuted(bool muted)
    {
        Muted = muted;
        Apply();
        Save();
    }

    private static void Apply()
    {
        var bus = AudioServer.GetBusIndex(BusName);
        if (bus < 0)
        {
            AudioServer.AddBus();
            bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, BusName);
            AudioServer.SetBusSend(bus, "Master");
        }

        var gain = SoundMix.VolumeGain(Volume);
        AudioServer.SetBusMute(bus, Muted || gain <= 0f);
        AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Max(gain, 0.0001f)));
    }

    // The file may hold other sections, of other settings: they are read and written back.
    private static void Save()
    {
        var file = new ConfigFile();
        file.Load(FilePath);
        file.SetValue(Section, VolumeKey, Volume);
        file.SetValue(Section, MutedKey, Muted);
        var saved = file.Save(FilePath);
        if (saved != Error.Ok)
        {
            GD.PushWarning($"The sound settings could not be kept in {ProjectSettings.GlobalizePath(FilePath)}: {saved}.");
        }
    }
}
