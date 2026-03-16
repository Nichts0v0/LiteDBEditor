using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiteDBEditor.Services;

public class SchemaBindingInfo
{
    public string CSFilePath { get; set; } = string.Empty;
}

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

    private void SaveBindings(Dictionary<string, SchemaBindingInfo> bindings)
    {
        var json = JsonSerializer.Serialize(bindings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_bindingFile, json);
    }

    private string GetBindingKey(string dbPath, string collectionName)
    {
        return $"{Path.GetFullPath(dbPath).ToLowerInvariant()}::{collectionName}";
    }

    public string? GetSchemaPath(string collectionName)
    {
        var dbPath = DataCenter.Database.CurrentDbPath;
        if (string.IsNullOrEmpty(dbPath)) return null;
        return GetBoundSchemaFilePath(dbPath, collectionName);
    }

    public void BindSchema(string dbPath, string collectionName, string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return;

        var bindings = LoadBindings();
        var key = GetBindingKey(dbPath, collectionName);

        // 统一存放在 Schemas 目录下
        var schemaDir = Path.Combine(_baseDir, "Schemas");
        if (!Directory.Exists(schemaDir)) Directory.CreateDirectory(schemaDir);

        string destPath;
        if (sourcePath.EndsWith(".schema.json"))
        {
            // 如果已经是 schema.json，直接引用其全路径（或者拷贝一份到 Schemas 目录）
            // 这里为了管理方便，如果是外部文件，我们拷贝到 Schemas 目录
            var fileName = Path.GetFileName(sourcePath);
            destPath = Path.Combine(schemaDir, fileName);
            if (Path.GetFullPath(sourcePath) != Path.GetFullPath(destPath))
            {
                File.Copy(sourcePath, destPath, true);
            }
        }
        else
        {
            // 如果是 .cs，则指向对应的 .schema.json
            var fileName = Path.GetFileNameWithoutExtension(sourcePath) + ".schema.json";
            destPath = Path.Combine(schemaDir, fileName);
        }

        bindings[key] = new SchemaBindingInfo { CSFilePath = destPath };
        SaveBindings(bindings);
    }

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

    public List<string> GetAvailableSchemas()
    {
        var schemaDir = Path.Combine(_baseDir, "Schemas");
        if (!Directory.Exists(schemaDir)) return new List<string>();
        return Directory.GetFiles(schemaDir, "*.schema.json").ToList();
    }
}
