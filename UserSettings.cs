using System.Text.Json;

namespace Docs2MD;

/// <summary>
/// Small persisted settings file: %AppData%\Docs2MD\settings.json.
/// Remembers the folders the user last worked with.
/// </summary>
public sealed class UserSettings
{
    public string? LastInputFolder { get; set; }
    public string? LastOutputFolder { get; set; }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Docs2MD", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings();
        }
        catch { /* corrupt or unreadable settings — start fresh */ }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings persistence is best-effort, never crash the app */ }
    }
}
