using System;
using System.Collections.Generic;
using System.Text.Json;
using LiteDB;

namespace LiteDBEditor.Models;

/// <summary>
/// 表示一个独立字段或者属性的通用元数据结构。
/// 它是动态生成 UI 编辑表单、执行数据校验以及执行类型转换的核心骨架。
/// </summary>
public class SchemaProperty
{
    /// <summary>
    /// 字段的编程名称（对应 BsonDocument 的 Key）。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 在 UI 界面上显示的友好名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 数据类型标识符。
    /// 可选值：String, Int32, Int64, Double, Boolean, Array, Document, Dictionary 等。
    /// </summary>
    public string TypeName { get; set; } = "String";

    /// <summary>
    /// 如果字段是枚举类型，此处记录所有可选的字符串值列表。
    /// </summary>
    public List<string>? EnumValues { get; set; }

    /// <summary>
    /// 当 TypeName 为 Document（嵌套对象）时，定义其内部包含的子属性集合。
    /// </summary>
    public List<SchemaProperty>? NestedProperties { get; set; }

    /// <summary>
    /// 当 TypeName 为 Array（列表）或 Dictionary 时，定义其内部元素的结构特征。
    /// </summary>
    public SchemaProperty? ElementSchema { get; set; }

    /// <summary>
    /// 记录原始 C# 类型名称（如 "User", "int"），
    /// 用于在 UI 上还原真实的泛型签名描述。
    /// </summary>
    public string? CSharpTypeName { get; set; }

    /// <summary>
    /// 指示该字段是否为必填项。
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// 获取友好的类型显示字符串。
    /// 例如：将 "Array" 渲染为 "List&lt;Int32&gt;"，将 "Document" 渲染为类名。
    /// </summary>
    /// <returns>格式化后的类型描述文本</returns>
    public string GetFriendlyTypeString()
    {
        if (TypeName == "Array")
        {
            var elemType = ElementSchema?.GetFriendlyTypeStringInternal() ?? "Object";
            return $"List<{elemType}>";
        }
        if (TypeName == "Dictionary")
        {
            var valType = ElementSchema?.GetFriendlyTypeStringInternal() ?? "Object";
            return $"Dictionary<string, {valType}>";
        }
        if (TypeName == "Document")
        {
            return GetFriendlyTypeStringInternal();
        }
        return TypeName;
    }

    /// <summary>
    /// 内部逻辑：获取元素级别的友好类型名称。
    /// </summary>
    private string GetFriendlyTypeStringInternal()
    {
        // 优先使用显式记录的原始 C# 类型名
        if (!string.IsNullOrEmpty(CSharpTypeName))
        {
            return CSharpTypeName;
        }

        // 如果是 Document，尝试使用其显示名作为类名占位符
        if (TypeName == "Document")
        {
            return !string.IsNullOrEmpty(DisplayName) ? DisplayName : "Object";
        }

        return TypeName;
    }
}

/// <summary>
/// Schema 顶层元数据容器，代表一个完整的对象（如数据库的一张表）的结构定义。
/// </summary>
public class SchemaData
{
    /// <summary>
    /// 获取或设置目标名称（通常是集合名或类名）。
    /// </summary>
    public string TargetName { get; set; } = "Unknown";

    /// <summary>
    /// 获取或设置该对象所包含的所有顶级属性定义。
    /// </summary>
    public List<SchemaProperty> Properties { get; set; } = new();
}
