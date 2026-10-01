using System.Globalization;
using System.Text.Json;

namespace KasumiCertHelper.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsFilePath => Path.Combine(AppServices.AppDataDirectory, "settings.json");

    public string GpgExecutablePath { get; set; } = string.Empty;

    public string? GpgHomeDirectory { get; set; }

    public string? LastDatabasePath { get; set; }

    public string? LastDirectory { get; set; }

    public List<string> RecentDatabases { get; set; } = new();

    /// <summary>
    /// Free form storage for per page UI state (pane widths, table column widths, ...) so the
    /// layout the user arranges is the layout they get next time.
    /// </summary>
    public Dictionary<string, string> Layout { get; set; } = new();

    public string? GetLayout(string key)
        => Layout.TryGetValue(key, out string? value) ? value : null;

    public void SetLayout(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Layout.Remove(key);
        }
        else
        {
            Layout[key] = value;
        }
    }

    public double GetLayoutDouble(string key, double fallback)
        => double.TryParse(GetLayout(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    public void SetLayoutDouble(string key, double value)
        => SetLayout(key, value.ToString("0.##", CultureInfo.InvariantCulture));

    public bool GetLayoutBool(string key, bool fallback)
        => bool.TryParse(GetLayout(key), out bool value) ? value : fallback;

    public void SetLayoutBool(string key, bool value) => SetLayout(key, value ? "true" : "false");

    public static SettingsService Load()
    {
        try
        {
            string path = SettingsFilePath;
            if (File.Exists(path))
            {
                SettingsService? loaded = JsonSerializer.Deserialize<SettingsService>(File.ReadAllText(path), JsonOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
        }
        return new SettingsService();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppServices.AppDataDirectory);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
        }
    }
}
