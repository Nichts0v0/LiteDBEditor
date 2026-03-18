using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LiteDBEditor.Models;

/// <summary>
/// 字段类型枚举，定义了编辑器支持的数据类型种类。
/// </summary>
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

/// <summary>
/// 字段定义模型，代表类中的一个成员变量。
/// 包含变量名、主类型、子类型（容器元素）以及校验状态。
/// </summary>
public partial class FieldDefinition : ObservableObject
{
    /// <summary>
    /// 唯一标识符，用于在结构迁移时追踪字段重命名。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 获取或设置字段的变量名称。
    /// </summary>
    [ObservableProperty]
    private string _fieldName = "NewField";

    /// <summary>
    /// 获取或设置字段的基础数据类型。
    /// </summary>
    [ObservableProperty]
    private FieldType _type = FieldType.String;

    /// <summary>
    /// 获取或设置容器的子类型。
    /// 对于 List: 代表元素类型；对于 Dictionary: 代表 Value 的类型。
    /// </summary>
    [ObservableProperty]
    private FieldType _subType = FieldType.String;

    /// <summary>
    /// 获取或设置字典的键类型（仅在 Type 为 Dictionary 时有效）。
    /// </summary>
    [ObservableProperty]
    private FieldType _keyType = FieldType.String;

    /// <summary>
    /// 当 Type 为 Custom 时，记录引用的自定义类名。
    /// </summary>
    [ObservableProperty]
    private string? _customTypeName;

    /// <summary>
    /// 当 SubType 为 Custom 时，记录容器内元素的自定义类名。
    /// </summary>
    [ObservableProperty]
    private string? _subCustomTypeName;

    /// <summary>
    /// 当 KeyType 为 Custom 时记录的类名（目前 Key 仅支持基础类型）。
    /// </summary>
    [ObservableProperty]
    private string? _keyCustomTypeName;

    /// <summary>
    /// 指示该字段定义当前是否通过了合法性校验。
    /// </summary>
    [ObservableProperty]
    private bool _isValid = true;

    /// <summary>
    /// 记录校验失败时的详细错误描述。
    /// </summary>
    [ObservableProperty]
    private string? _errorDetail;
}

/// <summary>
/// 类定义模型，代表一个 C# 类结构。
/// 包含类名、所属字段集合以及嵌套定义的内部类列表。
/// </summary>
public partial class ClassDefinition : ObservableObject
{
    /// <summary>
    /// 唯一标识符，用于追踪类的变更。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 获取或设置类名。
    /// </summary>
    [ObservableProperty]
    private string _className = "NewClass";

    /// <summary>
    /// 获取或设置该类包含的字段列表。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FieldDefinition> _fields = new();

    /// <summary>
    /// 获取或设置该类内部嵌套定义的辅助类列表。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ClassDefinition> _innerClasses = new();

    /// <summary>
    /// 指示该类定义当前是否合法。
    /// </summary>
    [ObservableProperty]
    private bool _isValid = true;

    /// <summary>
    /// 校验失败时的详细说明。
    /// </summary>
    [ObservableProperty]
    private string? _errorDetail;
}

/// <summary>
/// Schema 编辑器操作结果，封装了新生成或选中的类信息及文件路径。
/// </summary>
public class SchemaEditorResult
{
    public string ClassName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}
