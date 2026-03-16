using System;
using System.IO;
using System.Text.Json;
using LiteDBEditor.Models;

namespace LiteDBEditor.Services;

/// <summary>
/// 负责 .schema.json 元数据的持久化与加载
/// </summary>
public class SchemaMetadataService
{
    private static readonly JsonSerializerOptions _options = new() 
    { 
        WriteIndented = true,
        PropertyNameCaseInsensitive = true 
    };

    /// <summary>
    /// 将类定义保存为 JSON 元数据
    /// </summary>
    public void SaveSchema(ClassDefinition classDef, string filePath)
    {
        var jsonPath = GetJsonPath(filePath);
        var json = JsonSerializer.Serialize(classDef, _options);
        File.WriteAllText(jsonPath, json);
    }

    /// <summary>
    /// 加载 JSON 元数据
    /// </summary>
    public ClassDefinition? LoadMetadata(string filePath)
    {
        var jsonPath = GetJsonPath(filePath);
        if (!File.Exists(jsonPath)) return null;

        try
        {
            var json = File.ReadAllText(jsonPath);
            return JsonSerializer.Deserialize<ClassDefinition>(json, _options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] LoadMetadata failed: {ex.Message}");
            return null;
        }
    }

    private string GetJsonPath(string filePath)
    {
        if (filePath.EndsWith(".schema.json")) return filePath;
        var dir = Path.GetDirectoryName(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(dir ?? "", name + ".schema.json");
    }
}
