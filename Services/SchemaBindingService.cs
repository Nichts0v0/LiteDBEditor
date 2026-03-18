using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiteDBEditor.Services;

/// <summary>
/// 绑定信息实体类，记录集合关联的 Schema 文件路径。
/// </summary>
public class SchemaBindingInfo
{
    /// <summary>
    /// 对应的 Schema (.schema.json) 文件路径。
    /// </summary>
    public string CSFilePath { get; set; } = string.Empty;
}

/// <summary>
/// Schema 绑定服务，管理数据库集合与其对应的 C# 定义或 Schema 元数据之间的映射关系。
/// </summary>
public class SchemaBindingService
{
    private readonly string _baseDir;
    private readonly string _bindingFile;

    public SchemaBindingService()
    {
        _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nichts_Studio", "LiteDBEditor");
        _bindingFile = Path.Combine(_baseDir, "Bindings.json");
        
        if (!Directory.Exists(_baseDir))
        {
            Directory.CreateDirectory(_baseDir);
        }
    }

    /// <summary>
    /// 从持久化文件中加载所有绑定关系。
    /// </summary>
    private Dictionary<string, SchemaBindingInfo> LoadBindings()
    {
        if (!File.Exists(_bindingFile)) return new Dictionary<string, SchemaBindingInfo>();
        try
        {
            var json = File.ReadAllText(_bindingFile);
            return JsonSerializer.Deserialize<Dictionary<string, SchemaBindingInfo>>(json) ?? new Dictionary<string, SchemaBindingInfo>();
        }
        catch
        {
            return new Dictionary<string, SchemaBindingInfo>();
        }
    }

    /// <summary>
    /// 将绑定关系持久化到磁盘。
    /// </summary>
    private void SaveBindings(Dictionary<string, SchemaBindingInfo> bindings)
    {
        var json = JsonSerializer.Serialize(bindings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_bindingFile, json);
    }

    /// <summary>
    /// 根据数据库路径和集合名生成唯一的映射键。
    /// </summary>
    private string GetBindingKey(string dbPath, string collectionName)
    {
        return $"{Path.GetFullPath(dbPath).ToLowerInvariant()}::{collectionName}";
    }

    /// <summary>
    /// 获取当前活动数据库中指定集合的 Schema 文件路径。
    /// </summary>
    public string? GetSchemaPath(string collectionName)
    {
        var dbPath = DataCenter.Database.CurrentDbPath;
        if (string.IsNullOrEmpty(dbPath)) return null;
        return GetBoundSchemaFilePath(dbPath, collectionName);
    }

    /// <summary>
    /// 创建或更新集合与 Schema 源文件之间的绑定。
    /// </summary>
    /// <param name="dbPath">数据库路径</param>
    /// <param name="collectionName">集合名</param>
    /// <param name="sourcePath">源文件路径（可以是 .cs 或 .schema.json）</param>
    public void BindSchema(string dbPath, string collectionName, string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return;

        var bindings = LoadBindings();
        var key = GetBindingKey(dbPath, collectionName);

        // 统一存放在应用程序数据目录下的 Schemas 子目录中
        var schemaDir = Path.Combine(_baseDir, "Schemas");
        if (!Directory.Exists(schemaDir)) Directory.CreateDirectory(schemaDir);

        string destPath;
        if (sourcePath.EndsWith(".schema.json"))
        {
            // 如果已经是 schema.json，将其拷贝到管理目录以便统一维护
            var fileName = Path.GetFileName(sourcePath);
            destPath = Path.Combine(schemaDir, fileName);
            if (Path.GetFullPath(sourcePath) != Path.GetFullPath(destPath))
            {
                File.Copy(sourcePath, destPath, true);
            }
        }
        else
        {
            // 如果是 .cs 源代码，则指向其生成的对应的 .schema.json 元数据
            var fileName = Path.GetFileNameWithoutExtension(sourcePath) + ".schema.json";
            destPath = Path.Combine(schemaDir, fileName);
        }

        bindings[key] = new SchemaBindingInfo { CSFilePath = destPath };
        SaveBindings(bindings);
    }

    /// <summary>
    /// 当数据库集合重命名时，同步更新绑定映射键。
    /// </summary>
    public void RenameBinding(string dbPath, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(dbPath) || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return;

        var bindings = LoadBindings();
        var oldKey = GetBindingKey(dbPath, oldName);
        var newKey = GetBindingKey(dbPath, newName);

        if (bindings.TryGetValue(oldKey, out var info))
        {
            bindings.Remove(oldKey);
            bindings[newKey] = info;
            SaveBindings(bindings);
        }
    }

    /// <summary>
    /// 根据数据库和集合名查询绑定的 Schema 文件路径。
    /// </summary>
    public string? GetBoundSchemaFilePath(string dbPath, string collectionName)
    {
        if (string.IsNullOrEmpty(dbPath) || string.IsNullOrEmpty(collectionName)) return null;

        var bindings = LoadBindings();
        var key = GetBindingKey(dbPath, collectionName);

        if (bindings.TryGetValue(key, out var info) && File.Exists(info.CSFilePath))
        {
            return info.CSFilePath;
        }
        return null;
    }

    /// <summary>
    /// 获取管理目录下所有可用的 Schema 定义文件列表。
    /// </summary>
    public List<string> GetAvailableSchemas()
    {
        var schemaDir = Path.Combine(_baseDir, "Schemas");
        if (!Directory.Exists(schemaDir)) return new List<string>();
        return Directory.GetFiles(schemaDir, "*.schema.json").ToList();
    }
}
