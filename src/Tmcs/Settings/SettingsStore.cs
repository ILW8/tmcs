using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tmcs.Settings;

/// <summary>Reads and writes the settings file. <see cref="Load"/> never throws: a missing, unreadable or corrupt file gives the defaults.</summary>
internal sealed class SettingsStore(string path)
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tmcs", "settings.json");

    /// <summary>The saved settings, repaired by <see cref="AppSettings.Normalize"/>; the defaults when the file is missing, locked or not valid settings JSON.</summary>
    public AppSettings Load()
    {
        AppSettings? settings;
        try
        {
            using var stream = File.OpenRead(path);
            settings = JsonSerializer.Deserialize<AppSettings>(stream, Options);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            settings = null;
        }

        settings ??= AppSettings.CreateDefault();
        settings.Normalize();
        return settings;
    }

    /// <summary>Writes <paramref name="s"/> to a temporary file beside the target, then moves it over the target, so a crash never leaves a half-written settings file.</summary>
    public void Save(AppSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
