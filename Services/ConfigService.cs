using System;
using System.IO;
using System.Text.Json;
using LiteDBEditor.Models;

namespace LiteDBEditor.Services;

public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Nichts_Studio", "LiteDBEditor");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static AppConfig? _cachedConfig;

    public static AppConfig Config
    {
        get
        {
            if (_cachedConfig == null)
            {
                Load();
            }
            return _cachedConfig!;
        }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _cachedConfig = JsonSerializer.Deserialize<AppConfig>(json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load config: {ex.Message}");
        }

        _cachedConfig ??= new AppConfig();
    }

    public static void Save()
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }

            var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save config: {ex.Message}");
        }
    }
}
