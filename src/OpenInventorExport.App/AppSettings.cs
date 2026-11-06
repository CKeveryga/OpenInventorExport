using System.IO;
using System.Text.Json;

namespace OpenInventorExport.App;

/// <summary>
/// What the window remembers between runs (last config file and output folder), in
/// %AppData%\OpenInventorExport\app-settings.json. A missing or broken file just means "nothing remembered".
/// </summary>
public sealed class AppSettings
{
    public string? ConfigPath { get; set; }
    public string? OutputFolder { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenInventorExport", "app-settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* unreadable settings are not worth bothering the user about */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* same: the window works without remembering */ }
    }
}
