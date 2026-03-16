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
        _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nichts_Studio", "LiteDBEditor", "Schemas");
        if (!Directory.Exists(_baseDir))
        {
            Directory.CreateDirectory(_baseDir);
        }
        _bindingFile = Path.Combine(_baseDir, "Bindings.json");
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

    public void BindSchema(string dbPath, string collectionName, string sourceCsFilePath)
    {
        if (string.IsNullOrEmpty(sourceCsFilePath) || !File.Exists(sourceCsFilePath))
        {
            Console.WriteLine($"[Error] BindSchema failed: Source file not found at {sourceCsFilePath}");
            return;
        }

        var bindings = LoadBindings();
        var key = GetBindingKey(dbPath, collectionName);

        var fileName = Path.GetFileName(sourceCsFilePath);
        var destPath = Path.Combine(_baseDir, fileName);

        // 如果源文件已经在这个目录下，就不再复制
        if (Path.GetFullPath(sourceCsFilePath).StartsWith(_baseDir, StringComparison.OrdinalIgnoreCase))
        {
            destPath = sourceCsFilePath;
        }
        else
        {
            try
            {
                File.Copy(sourceCsFilePath, destPath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] BindSchema Copy failed: {ex.Message}");
            }
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

    public string? GetBoundSchemaCode(string dbPath, string collectionName)
    {
        var path = GetBoundSchemaFilePath(dbPath, collectionName);
        if (!string.IsNullOrEmpty(path))
        {
            return File.ReadAllText(path);
        }
        return null;
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
        if (!Directory.Exists(_baseDir)) return new List<string>();

        // 搜索目录下所有的 .cs 缓存文件
        return Directory.GetFiles(_baseDir, "*.cs").ToList();
    }
}
