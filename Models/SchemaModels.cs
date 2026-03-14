using System;
using System.Collections.Generic;
using System.Text.Json;
using LiteDB;

namespace LiteDBEditor.Models;

/// <summary>
/// 表示一个独立字段或者属性的通用元数据结构，
/// 它是动态生成输入表单(Dialog)的基石。
/// </summary>
public class SchemaProperty
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// String, Int32, Double, Boolean, Array, Document, Enum等
    /// </summary>
    public string TypeName { get; set; } = "String";

    /// <summary>
    /// 如果是Enum，提供下拉框的选项
    /// </summary>
    public List<string>? EnumValues { get; set; }

    /// <summary>
    /// 如果是一个嵌套类 (Document)，其内部继续包含哪些字段
    /// </summary>
    public List<SchemaProperty>? NestedProperties { get; set; }

    /// <summary>
    /// 如果是一个列表 (Array)，其内部元素的类别是什么
    /// </summary>
    public SchemaProperty? ElementSchema { get; set; }

    /// <summary>
    /// 记录原始 C# 类型名称 (如 Weapon, int)，
    /// 解决在泛型集合中 DisplayName 被重写为 "Item" 或 "ValueItem" 的问题。
    /// </summary>
    public string? CSharpTypeName { get; set; }

    /// <summary>
    /// 是否必须(用于简单的数据校验)
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// 获取友好的类型显示字符串 (如 List<Int32>, Dictionary<string, Weapon>, User)
    /// </summary>
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

    private string GetFriendlyTypeStringInternal()
    {
        // 优先使用记录的原始 C# 类型名 (如 Weapon, int)
        if (!string.IsNullOrEmpty(CSharpTypeName))
        {
            return CSharpTypeName;
        }

        // 如果是 Document (自定义类)，降级使用 DisplayName (类名)
        if (TypeName == "Document")
        {
            return !string.IsNullOrEmpty(DisplayName) ? DisplayName : "Object";
        }

        return TypeName;
    }
}

public class SchemaData
{
    public string TargetName { get; set; } = "Unknown";
    public List<SchemaProperty> Properties { get; set; } = new();
}
