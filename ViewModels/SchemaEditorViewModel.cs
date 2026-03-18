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

/// <summary>
/// SchemaItem 实体类，代表一个可供选择或已绑定的 Schema 文件项。
/// </summary>
public class SchemaItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsBound { get; set; }
}

/// <summary>
/// Schema 编辑器的 ViewModel，提供可视化界面来定义、修改类结构，
/// 并支持自动同步结构变更到数据库（重命名、删除字段）。
/// </summary>
public partial class SchemaEditorViewModel : ViewModelBase
{
    private readonly CSharpGeneratorService _generator = new();
    private readonly string _schemaDir;

    /// <summary>
    /// 获取或设置主类定义。
    /// </summary>
    [ObservableProperty]
    private ClassDefinition _mainClass = new();

    /// <summary>
    /// 获取或设置当前正在编辑的类定义（可能是主类，也可能是其内部嵌套的辅助类）。
    /// </summary>
    [ObservableProperty]
    private ClassDefinition _currentEditingClass; 

    /// <summary>
    /// 当前数据库集合已绑定的 Schema 文件路径。
    /// </summary>
    [ObservableProperty]
    private string? _currentBoundPath; 

    private ClassDefinition? _selectedInnerClass;

    /// <summary>
    /// 选中的内部辅助类定义。
    /// </summary>
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

    /// <summary>
    /// 命令：切换编辑目标回到主类。
    /// </summary>
    [RelayCommand]
    private void SelectMainClass()
    {
        CurrentEditingClass = MainClass;
        SelectedInnerClass = null;
    }

    /// <summary>
    /// 窗口显示的错误消息。
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 当前已定义的类名列表，用于字段类型（Custom）的快速智能感知和选择。
    /// </summary>
    public ObservableCollection<string> DefinedClassNames { get; } = new();

    /// <summary>
    /// 重新扫描所有定义的类名并刷新下拉列表。
    /// </summary>
    public void RefreshClassNames()
    {
        var names = new List<string> { MainClass.ClassName };
        names.AddRange(MainClass.InnerClasses.Select(c => c.ClassName));
        var distinctNames = names.Distinct().OrderBy(n => n).ToList();

        // 差量同步集合，避免全量替换导致 UI 闪烁或选择丢失
        var toRemove = DefinedClassNames.Where(n => !distinctNames.Contains(n)).ToList();
        foreach (var name in toRemove)
        {
            DefinedClassNames.Remove(name);
        }

        foreach (var name in distinctNames)
        {
            if (!DefinedClassNames.Contains(name))
            {
                DefinedClassNames.Add(name);
            }
        }
    }

    /// <summary>
    /// 存储在管理目录下的现有模板文件列表。
    /// </summary>
    public ObservableCollection<SchemaItem> ExistingSchemas { get; } = new();

    /// <summary>
    /// 获取基础类型列表供 UI 选择。
    /// </summary>
    public FieldType[] AvailableTypes => Enum.GetValues<FieldType>();

    /// <summary>
    /// 获取容器（List/Dictionary）子元素可用的基础类型列表。
    /// </summary>
    public FieldType[] AvailableSubTypes => new[] { FieldType.Int, FieldType.Float, FieldType.Bool, FieldType.String, FieldType.Custom };

    /// <summary>
    /// 获取字典键（Key）可用的类型列表。
    /// </summary>
    public FieldType[] AvailableKeyTypes => new[] { FieldType.Int, FieldType.Float, FieldType.String };

    public SchemaEditorViewModel(string? initialFilePath = null, string? currentBoundPath = null)
    {
        _schemaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Nichts_Studio", "LiteDBEditor", "Schemas");

        if (!Directory.Exists(_schemaDir))
            Directory.CreateDirectory(_schemaDir);

        _currentEditingClass = _mainClass; 
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
            // 默认添加一个初始字段
            AddField();
        }
    }

    /// <summary>
    /// 递归为所有类定义及其嵌套类订阅属性变更事件。
    /// </summary>
    private void SubscribeClassChanges(ClassDefinition classDef)
    {
        classDef.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ClassDefinition.ClassName)) RefreshClassNames(); };
        foreach (var inner in classDef.InnerClasses)
        {
            SubscribeClassChanges(inner);
        }
    }

    /// <summary>
    /// 从文件加载 Schema 定义。支持 .cs 源代码或 .schema.json 元数据。
    /// </summary>
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

            SubscribeClassChanges(MainClass);

            LoadExistingSchemas();
            RefreshClassNames();

            OnPropertyChanged(string.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{LanguageService.GetString("L_LoadFailed")}: {ex.Message}";
        }
    }

    /// <summary>
    /// 重新加载管理目录下的所有 Schema 文件。
    /// </summary>
    private void LoadExistingSchemas()
    {
        ExistingSchemas.Clear();
        if (Directory.Exists(_schemaDir))
        {
            var boundPath = CurrentBoundPath != null ? Path.GetFullPath(CurrentBoundPath).ToLowerInvariant() : null;

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
            
            // 向上兼容旧版本的 .cs 备份文件
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

    /// <summary>
    /// 命令：在当前编辑的类中添加一个新字段。
    /// </summary>
    [RelayCommand]
    private void AddField()
    {
        CurrentEditingClass.Fields.Add(new FieldDefinition());
    }

    /// <summary>
    /// 命令：移除指定的字段。
    /// </summary>
    [RelayCommand]
    private void RemoveField(FieldDefinition field)
    {
        CurrentEditingClass.Fields.Remove(field);
    }

    /// <summary>
    /// 命令：添加一个新的嵌套辅助类。
    /// </summary>
    [RelayCommand]
    private void AddInnerClass()
    {
        var newClass = new ClassDefinition { ClassName = "NestedClass" };
        newClass.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ClassDefinition.ClassName)) RefreshClassNames(); };
        MainClass.InnerClasses.Add(newClass);
        RefreshClassNames();
    }

    /// <summary>
    /// 命令：移除指定的嵌套辅助类。
    /// </summary>
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
    /// 执行保存逻辑：
    /// 1. 验证所有类定义的合法性。
    /// 2. 对比旧元数据，发现并同步重命名/物理删除等变更到数据库。
    /// 3. 生成并持久化 .schema.json 元数据及 C# 脚本备份。
    /// </summary>
    /// <returns>保存后的 Schema 文件路径，若失败则返回 null</returns>
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
            
            // 步骤一：加载旧元数据用于对比
            var oldMeta = DataCenter.Metadata.LoadMetadata(jsonPath);
            
            // 步骤二：执行数据库层面的一键迁移
            if (oldMeta != null)
            {
                ApplyMetadataChanges(oldMeta, MainClass);
            }

            // 步骤三：保存真理源元数据
            DataCenter.Metadata.SaveSchema(MainClass, jsonPath);

            // 步骤四：备份 C# 代码供用户参考或二次开发
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
    /// 物理结构迁移逻辑：基于字段的唯一 ID 追踪其名称变更或被删除状态。
    /// 该逻辑会直接对当前数据库执行 RenameField 或 RemoveFieldPermanently 操作。
    /// </summary>
    private void ApplyMetadataChanges(ClassDefinition oldClass, ClassDefinition newClass)
    {
        // 核心：利用 GUID 追踪字段生命周期，而非依赖名称
        foreach (var newField in newClass.Fields)
        {
            var oldField = oldClass.Fields.FirstOrDefault(f => f.Id == newField.Id);
            if (oldField != null && oldField.FieldName != newField.FieldName)
            {
                // 执行数据库字段重命名迁移
                DataCenter.Database.RenameField(newClass.ClassName, oldField.FieldName, newField.FieldName);
                Console.WriteLine($"[SchemaSync] Renamed field: {oldField.FieldName} -> {newField.FieldName}");
            }
        }

        foreach (var oldField in oldClass.Fields)
        {
            // 如果旧字段 ID 不在新的定义列表中，则视为被永久删除
            if (!newClass.Fields.Any(f => f.Id == oldField.Id))
            {
                // 物理抹除该字段在数据库中所有文档的数据
                DataCenter.Database.RemoveFieldPermanently(newClass.ClassName, oldField.FieldName);
                Console.WriteLine($"[SchemaSync] Physically removed field: {oldField.FieldName}");
            }
        }
    }

    /// <summary>
    /// 重置所有校验状态位。
    /// </summary>
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

    /// <summary>
    /// 递归验证类定义的合法性（命规范、关键字冲突、重名等）。
    /// </summary>
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

            if (field.Type == FieldType.Custom && string.IsNullOrWhiteSpace(field.CustomTypeName))
            {
                field.IsValid = false;
                field.ErrorDetail = "自定义类型必须选择或输入有效的类名。";
                ErrorMessage = $"[{classPath}] 变量 '{field.FieldName}' 错误: {field.ErrorDetail}";
                return false;
            }

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

    /// <summary>
    /// 校验字符串是否为合法的 C# 标识符。
    /// </summary>
    private bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Regex.IsMatch(name, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }

    /// <summary>
    /// 根据现有模板的文件名推断并获取编辑结果对象。
    /// </summary>
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

        var className = Path.GetFileNameWithoutExtension(fullPath);
        if (className.Length > 0)
            className = char.ToUpper(className[0]) + className.Substring(1);

        return new SchemaEditorResult
        {
            ClassName = className,
            FilePath = fullPath
        };
    }
}
