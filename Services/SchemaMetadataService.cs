using System;
using System.IO;
using System.Text.Json;
using LiteDBEditor.Models;

namespace LiteDBEditor.Services;

/// <summary>
/// 负责 .schema.json 元数据文件的持久化与读取。
/// 该元数据文件记录了类定义（ClassDefinition），用于在编辑器中描述 BsonDocument 的结构。
/// </summary>
public class SchemaMetadataService
{
    private static readonly JsonSerializerOptions _options = new() 
    { 
        WriteIndented = true,
        PropertyNameCaseInsensitive = true 
    };

    /// <summary>
    /// 将类定义对象序列化并保存为 JSON 元数据文件。
    /// </summary>
    /// <param name="classDef">要保存的类定义对象</param>
    /// <param name="filePath">目标文件路径</param>
    public void SaveSchema(ClassDefinition classDef, string filePath)
    {
        var jsonPath = GetJsonPath(filePath);
        var json = JsonSerializer.Serialize(classDef, _options);
        File.WriteAllText(jsonPath, json);
    }

    /// <summary>
    /// 从指定的 JSON 元数据文件中加载类定义。
    /// </summary>
    /// <param name="filePath">元数据文件路径</param>
    /// <returns>反序列化后的类定义对象，若文件不存在或读取失败则返回 null</returns>
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

    /// <summary>
    /// 获取正确的 .schema.json 文件路径。如果传入的是其他后缀，则会进行转换。
    /// </summary>
    private string GetJsonPath(string filePath)
    {
        if (filePath.EndsWith(".schema.json")) return filePath;
        var dir = Path.GetDirectoryName(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(dir ?? "", name + ".schema.json");
    }
}
