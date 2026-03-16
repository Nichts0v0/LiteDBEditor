using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteDBEditor.Models;
using LiteDBEditor.Services;
using LiteDBEditor;

namespace LiteDBEditor.ViewModels;

public class SchemaItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsBound { get; set; }
}

public partial class SchemaEditorViewModel : ViewModelBase
{
    private readonly CSharpGeneratorService _generator = new();
    private readonly string _schemaDir;

    [ObservableProperty]
    private ClassDefinition _mainClass = new();

    [ObservableProperty]
    private ClassDefinition _currentEditingClass; // 当前正在编辑的类（可能是主类，也可能是辅助类）

    [ObservableProperty]
    private string? _currentBoundPath; // 当前表已绑定的 Schema 路径

    private ClassDefinition? _selectedInnerClass;
    public ClassDefinition? SelectedInnerClass
    {
        get => _selectedInnerClass;
        set
        {
            if (SetProperty(ref _selectedInnerClass, value) && value != null)
            {
                CurrentEditingClass = value;
            }
        }
    }

    [RelayCommand]
    private void SelectMainClass()
    {
        CurrentEditingClass = MainClass;
        SelectedInnerClass = null;
    }

    [ObservableProperty]
    private string? _errorMessage;

    // 获取所有已定义的类名（用于字段类型快速引用）
    public ObservableCollection<string> DefinedClassNames { get; } = new();

    public void RefreshClassNames()
    {
        var names = new List<string> { MainClass.ClassName };
        names.AddRange(MainClass.InnerClasses.Select(c => c.ClassName));
        var distinctNames = names.Distinct().OrderBy(n => n).ToList();

        // 智能同步：保持集合引用和未变动项的稳定性
        // 1. 移除已不存在的项
        var toRemove = DefinedClassNames.Where(n => !distinctNames.Contains(n)).ToList();
        foreach (var name in toRemove)
        {
            DefinedClassNames.Remove(name);
        }

        // 2. 添加新项，并尝试保持现有顺序或直接追加
        foreach (var name in distinctNames)
        {
            if (!DefinedClassNames.Contains(name))
            {
                DefinedClassNames.Add(name);
            }
        }
    }

    // 现有模板列表 (对象列表，包含路径和绑定状态)
    public ObservableCollection<SchemaItem> ExistingSchemas { get; } = new();

    // 可供选择的基础类型列表
    public FieldType[] AvailableTypes => Enum.GetValues<FieldType>();

    // 容器子类型允许的类型（去除了 List 和 Dictionary）
    public FieldType[] AvailableSubTypes => new[] { FieldType.Int, FieldType.Float, FieldType.Bool, FieldType.String, FieldType.Custom };

    // 字典 Key 允许的类型列表
    public FieldType[] AvailableKeyTypes => new[] { FieldType.Int, FieldType.Float, FieldType.String };

    public SchemaEditorViewModel(string? initialFilePath = null, string? currentBoundPath = null)
    {
        _schemaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Nichts_Studio", "LiteDBEditor", "Schemas");

        if (!Directory.Exists(_schemaDir))
            Directory.CreateDirectory(_schemaDir);

        _currentEditingClass = _mainClass; // 初始指向主类
        _currentBoundPath = currentBoundPath;

        if (!string.IsNullOrEmpty(initialFilePath) && File.Exists(initialFilePath))
        {
            LoadFromPath(initialFilePath);
        }
        else
        {
            _mainClass.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ClassDefinition.ClassName)) RefreshClassNames(); };
            LoadExistingSchemas();
            RefreshClassNames();
            // 初始化时默认添加一个字段
            AddField();
        }
    }

    private void SubscribeClassChanges(ClassDefinition classDef)
    {
        classDef.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ClassDefinition.ClassName)) RefreshClassNames(); };
        foreach (var inner in classDef.InnerClasses)
        {
            SubscribeClassChanges(inner);
        }
    }

    public void LoadFromPath(string path)
    {
        try
        {
            ClassDefinition? parsedClass = null;
            if (path.EndsWith(".schema.json"))
            {
                parsedClass = _metadataService.LoadMetadata(path);
            }
            else
            {
                var code = File.ReadAllText(path);
                parsedClass = _generator.ParseCode(code);
            }

            if (parsedClass == null) throw new Exception("Failed to load schema.");

            MainClass = parsedClass;
            CurrentEditingClass = MainClass;
            SelectedInnerClass = null;

            // 重新订阅所有类的变更通知
            SubscribeClassChanges(MainClass);

            LoadExistingSchemas();
            RefreshClassNames();

            // 强制通知 UI 所有属性均已变动
            OnPropertyChanged(string.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{LanguageService.GetString("L_LoadFailed")}: {ex.Message}";
        }
    }

    private void LoadExistingSchemas()
    {
        ExistingSchemas.Clear();
        if (Directory.Exists(_schemaDir))
        {
            var boundPath = CurrentBoundPath != null ? Path.GetFullPath(CurrentBoundPath).ToLowerInvariant() : null;

            // 优先查找 .schema.json
            var files = Directory.GetFiles(_schemaDir, "*.schema.json");
            foreach (var f in files)
            {
                var fullPath = Path.GetFullPath(f);
                ExistingSchemas.Add(new SchemaItem 
                { 
                    Name = Path.GetFileName(f), 
                    FullPath = fullPath,
                    IsBound = boundPath != null && fullPath.ToLowerInvariant() == boundPath
                });
            }
            
            // 兼容性查找 .cs
            var csFiles = Directory.GetFiles(_schemaDir, "*.cs");
            foreach (var f in csFiles)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (!ExistingSchemas.Any(s => s.Name.StartsWith(name + ".schema.json")))
                {
                    var fullPath = Path.GetFullPath(f);
                    ExistingSchemas.Add(new SchemaItem 
                    { 
                        Name = Path.GetFileName(f), 
                        FullPath = fullPath,
                        IsBound = boundPath != null && fullPath.ToLowerInvariant() == boundPath
                    });
                }
            }
        }
    }

    [RelayCommand]
    private void AddField()
    {
        CurrentEditingClass.Fields.Add(new FieldDefinition());
    }

    [RelayCommand]
    private void RemoveField(FieldDefinition field)
    {
        CurrentEditingClass.Fields.Remove(field);
    }

    [RelayCommand]
    private void AddInnerClass()
    {
        var newClass = new ClassDefinition { ClassName = "NestedClass" };
        newClass.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ClassDefinition.ClassName)) RefreshClassNames(); };
        MainClass.InnerClasses.Add(newClass);
        RefreshClassNames();
    }

    [RelayCommand]
    private void RemoveInnerClass(ClassDefinition inner)
    {
        MainClass.InnerClasses.Remove(inner);
        RefreshClassNames();
    }

    private readonly string[] _forbiddenKeywords = {
        "int", "float", "bool", "string", "long", "double", "short", "byte", "decimal",
        "char", "object", "void", "list", "dictionary", "class", "struct", "interface", "enum",
        "public", "private", "protected", "internal", "static", "readonly", "volatile", "async", "await"
    };

    private readonly SchemaMetadataService _metadataService = new();

    /// <summary>
    /// 执行保存：生成 .schema.json 元数据，驱动数据库迁移，并备份 C# 脚本。
    /// </summary>
    public string? SaveAndGenerate()
    {
        ErrorMessage = null;
        ResetValidation(MainClass);

        if (!ValidateClass(MainClass, LanguageService.GetString("L_MainClass")))
        {
            return null;
        }

        try
        {
            var fileName = $"{MainClass.ClassName.ToLowerInvariant()}.schema.json";
            var jsonPath = Path.Combine(_schemaDir, fileName);
            
            // 1. 获取旧的元数据用于对比差异
            var oldMeta = DataCenter.Metadata.LoadMetadata(jsonPath);
            
            // 2. 如果存在旧元数据，执行数据库迁移 (根据 ID 追踪 Rename/Delete)
            if (oldMeta != null)
            {
                ApplyMetadataChanges(oldMeta, MainClass);
            }

            // 3. 保存新的 JSON 元数据 (这是真理源)
            DataCenter.Metadata.SaveSchema(MainClass, jsonPath);

            // 4. 同时备份生成 C# 脚本 (可选，供用户参考)
            var code = _generator.GenerateCode(MainClass);
            var csPath = Path.Combine(_schemaDir, $"{MainClass.ClassName.ToLowerInvariant()}.cs");
            File.WriteAllText(csPath, code);

            LoadExistingSchemas();
            return jsonPath;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{LanguageService.GetString("L_SaveFailed")}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// 对比新旧元数据，直接在数据库层面同步结构变更
    /// </summary>
    private void ApplyMetadataChanges(ClassDefinition oldClass, ClassDefinition newClass)
    {
        // 核心逻辑：基于唯一 ID (GUID) 发现变更
        // 1. 发现重命名 (ID 存在但 Name 变了)
        foreach (var newField in newClass.Fields)
        {
            var oldField = oldClass.Fields.FirstOrDefault(f => f.Id == newField.Id);
            if (oldField != null && oldField.FieldName != newField.FieldName)
            {
                // 执行数据库重命名
                DataCenter.Database.RenameField(newClass.ClassName, oldField.FieldName, newField.FieldName);
                Console.WriteLine($"[SchemaSync] Renamed field: {oldField.FieldName} -> {newField.FieldName}");
            }
        }

        // 2. 发现被删除的字段 (ID 在新的里面没了)
        foreach (var oldField in oldClass.Fields)
        {
            if (!newClass.Fields.Any(f => f.Id == oldField.Id))
            {
                // 执行数据库物理删除
                DataCenter.Database.RemoveFieldPermanently(newClass.ClassName, oldField.FieldName);
                Console.WriteLine($"[SchemaSync] Physically removed field: {oldField.FieldName}");
            }
        }
    }

    private void ResetValidation(ClassDefinition classDef)
    {
        classDef.IsValid = true;
        classDef.ErrorDetail = null;
        foreach (var field in classDef.Fields)
        {
            field.IsValid = true;
            field.ErrorDetail = null;
        }
        foreach (var inner in classDef.InnerClasses)
        {
            ResetValidation(inner);
        }
    }

    private bool ValidateClass(ClassDefinition classDef, string classPath)
    {
        if (string.IsNullOrWhiteSpace(classDef.ClassName) || !IsValidIdentifier(classDef.ClassName) || _forbiddenKeywords.Contains(classDef.ClassName.ToLowerInvariant()))
        {
            classDef.IsValid = false;
            classDef.ErrorDetail = $"类名 '{classDef.ClassName}' 无效或为保留关键字。";
            ErrorMessage = $"[{classPath}] 类名错误: {classDef.ErrorDetail}";
            return false;
        }

        var usedFieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in classDef.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.FieldName) || !IsValidIdentifier(field.FieldName) || _forbiddenKeywords.Contains(field.FieldName.ToLowerInvariant()))
            {
                field.IsValid = false;
                field.ErrorDetail = $"变量名 '{field.FieldName}' 无效或为保留关键字。";
                ErrorMessage = $"[{classPath}] 变量 '{field.FieldName}' 错误: {field.ErrorDetail}";
                return false;
            }

            // 核心修复：检查重名
            if (usedFieldNames.Contains(field.FieldName))
            {
                field.IsValid = false;
                field.ErrorDetail = $"变量名 '{field.FieldName}' 已在该类中定义。";
                ErrorMessage = $"[{classPath}] 变量重名: {field.ErrorDetail}";
                return false;
            }
            usedFieldNames.Add(field.FieldName);

            if (field.Type == FieldType.Dictionary)
            {
                if (field.KeyType != FieldType.String && field.KeyType != FieldType.Int && field.KeyType != FieldType.Float)
                {
                    field.IsValid = false;
                    field.ErrorDetail = "Dictionary 的 Key 只能是 string、int 或 float。";
                    ErrorMessage = $"[{classPath}] 变量 '{field.FieldName}' 错误: {field.ErrorDetail}";
                    return false;
                }
            }

            // 校验自定义类型类名非空
            if (field.Type == FieldType.Custom && string.IsNullOrWhiteSpace(field.CustomTypeName))
            {
                field.IsValid = false;
                field.ErrorDetail = "自定义类型必须选择或输入有效的类名。";
                ErrorMessage = $"[{classPath}] 变量 '{field.FieldName}' 错误: {field.ErrorDetail}";
                return false;
            }

            // 校验容器子类型（List/Dict Value）自定义类名非空
            if ((field.Type == FieldType.List || field.Type == FieldType.Dictionary) &&
                field.SubType == FieldType.Custom && string.IsNullOrWhiteSpace(field.SubCustomTypeName))
            {
                field.IsValid = false;
                field.ErrorDetail = "容器内的自定义类型必须选择有效的类名。";
                ErrorMessage = $"[{classPath}] 变量 '{field.FieldName}' 错误: {field.ErrorDetail}";
                return false;
            }
        }

        foreach (var inner in classDef.InnerClasses)
        {
            if (!ValidateClass(inner, $"辅助类 {inner.ClassName}"))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }

    public SchemaEditorResult? GetResultFromExisting(string fileNameOrPath)
    {
        string fullPath;
        if (File.Exists(fileNameOrPath))
        {
            fullPath = fileNameOrPath;
        }
        else
        {
            fullPath = Path.Combine(_schemaDir, fileNameOrPath);
        }

        if (!File.Exists(fullPath)) return null;

        // 尝试从文件名推断类名
        var className = Path.GetFileNameWithoutExtension(fullPath);
        // 这里可以做一些简单的处理，比如首字母大写
        if (className.Length > 0)
            className = char.ToUpper(className[0]) + className.Substring(1);

        return new SchemaEditorResult
        {
            ClassName = className,
            FilePath = fullPath
        };
    }
}
