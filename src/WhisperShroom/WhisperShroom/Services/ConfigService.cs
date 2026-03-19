using System.Text.Json;
using WhisperShroom.Models;

namespace WhisperShroom.Services;

public sealed class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WhisperShroom",
        "config.json");

    public AppConfig Config { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(ConfigPath))
            return;

        try
        {
            var json = File.ReadAllText(ConfigPath);
            Config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch (Exception)
        {
            Config = new AppConfig();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
