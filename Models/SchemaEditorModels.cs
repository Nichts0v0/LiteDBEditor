using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiteDBEditor.Models;

public enum FieldType
{
    Int,
    Float,
    Bool,
    String,
    List,
    Dictionary,
    Custom
}

public partial class FieldDefinition : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _fieldName = "NewField";

    [ObservableProperty]
    private FieldType _type = FieldType.String;

    // 对于 List: 存储元素类型; 对于 Dictionary: 存储 Value 类型
    [ObservableProperty]
    private FieldType _subType = FieldType.String;

    // 对于 Dictionary: 存储 Key 类型
    [ObservableProperty]
    private FieldType _keyType = FieldType.String;

    // 当类型为 Custom 或子类型为 Custom 时使用的类名
    [ObservableProperty]
    private string? _customTypeName;

    [ObservableProperty]
    private string? _subCustomTypeName;

    [ObservableProperty]
    private string? _keyCustomTypeName;

    [ObservableProperty]
    private bool _isValid = true;

    [ObservableProperty]
    private string? _errorDetail;
}

public partial class ClassDefinition : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _className = "NewClass";

    [ObservableProperty]
    private ObservableCollection<FieldDefinition> _fields = new();

    // 辅助类列表（用于嵌套定义）
    [ObservableProperty]
    private ObservableCollection<ClassDefinition> _innerClasses = new();

    [ObservableProperty]
    private bool _isValid = true;

    [ObservableProperty]
    private string? _errorDetail;
}

public class SchemaEditorResult
{
    public string ClassName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}
