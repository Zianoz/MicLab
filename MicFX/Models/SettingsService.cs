using System.IO;
using System.Text.Json;

namespace MicFX.Models;

/// <summary>Loads and saves AppSettings and EqPresets to %AppData%/MicFX/.</summary>
public static class SettingsService
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicFX");

    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");
    private static readonly string PresetsDir = Path.Combine(AppDataDir, "presets");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch { /* return defaults on any error */ }
        return new AppSettings();
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch { }
    }

    public static IReadOnlyList<string> ListPresetNames()
    {
        try
        {
            Directory.CreateDirectory(PresetsDir);
            return Directory.GetFiles(PresetsDir, "*.json")
                            .Select(Path.GetFileNameWithoutExtension)
                            .Where(n => n != null)
                            .Select(n => n!)
                            .OrderBy(n => n)
                            .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public static EqPreset? LoadPreset(string name)
    {
        try
        {
            var path = Path.Combine(PresetsDir, $"{name}.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<EqPreset>(json, JsonOpts);
        }
        catch { return null; }
    }

    public static void SavePreset(EqPreset preset)
    {
        try
        {
            Directory.CreateDirectory(PresetsDir);
            var path = Path.Combine(PresetsDir, $"{preset.Name}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(preset, JsonOpts));
        }
        catch { }
    }
}
