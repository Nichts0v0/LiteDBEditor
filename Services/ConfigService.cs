using System;
using System.IO;
using System.Text.Json;
using LiteDBEditor.Models;

namespace LiteDBEditor.Services;

/// <summary>
/// 配置服务类，负责应用程序配置（如语言设置、路径记录等）的持久化加载与保存。
/// </summary>
public static class ConfigService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Nichts_Studio", "LiteDBEditor");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static AppConfig? _cachedConfig;

    /// <summary>
    /// 获取当前的应用程序配置。如果尚未加载，则会自动触发加载。
    /// </summary>
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

    /// <summary>
    /// 从本地磁盘加载配置文件。如果文件不存在或加载失败，则初始化默认配置。
    /// </summary>
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

        // 如果加载失败或文件不存在，确保不返回 null
        _cachedConfig ??= new AppConfig();
    }

    /// <summary>
    /// 将当前的内存配置持久化保存到本地磁盘。
    /// </summary>
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
