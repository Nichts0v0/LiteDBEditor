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

namespace LiteDBEditor.ViewModels;

public partial class SchemaEditorViewModel : ViewModelBase
{
    private readonly CSharpGeneratorService _generator = new();
    private readonly string _schemaDir;

    [ObservableProperty]
    private ClassDefinition _mainClass = new();

    [ObservableProperty]
    private ClassDefinition _currentEditingClass; // 当前正在编辑的类（可能是主类，也可能是辅助类）

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

    // 现有模板列表 (文件名)
    public ObservableCollection<string> ExistingSchemas { get; } = new();

    // 可供选择的基础类型列表
    public FieldType[] AvailableTypes => Enum.GetValues<FieldType>();

    // 容器子类型允许的类型（去除了 List 和 Dictionary）
    public FieldType[] AvailableSubTypes => new[] { FieldType.Int, FieldType.Float, FieldType.Bool, FieldType.String, FieldType.Custom };

    // 字典 Key 允许的类型列表
    public FieldType[] AvailableKeyTypes => new[] { FieldType.Int, FieldType.Float, FieldType.String };

    public SchemaEditorViewModel(string? initialFilePath = null)
    {
        _schemaDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Nichts_Studio", "LiteDBEditor", "Schemas");

        if (!Directory.Exists(_schemaDir))
            Directory.CreateDirectory(_schemaDir);

        _currentEditingClass = _mainClass; // 初始指向主类

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
            var code = File.ReadAllText(path);
            var parsedClass = _generator.ParseCode(code);

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
            var files = Directory.GetFiles(_schemaDir, "*.cs");
            foreach (var f in files)
            {
                ExistingSchemas.Add(Path.GetFileName(f));
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

    /// <summary>
    /// 执行保存并生成代码
    /// </summary>
    /// <returns>返回生成的完整文件路径，如果失败则返回 null</returns>
    public string? SaveAndGenerate()
    {
        ErrorMessage = null;
        ResetValidation(MainClass);

        if (!ValidateClass(MainClass, "主类"))
        {
            return null;
        }

        try
        {
            var code = _generator.GenerateCode(MainClass);
            var fileName = $"{MainClass.ClassName.ToLowerInvariant()}.cs";
            var filePath = Path.Combine(_schemaDir, fileName);
            File.WriteAllText(filePath, code);
            return filePath;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{LanguageService.GetString("L_SaveFailed")}: {ex.Message}";
            return null;
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
