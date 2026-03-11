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
    /// 是否必须(用于简单的数据校验)
    /// </summary>
    public bool IsRequired { get; set; }
}

public class SchemaData
{
    public string TargetName { get; set; } = "Unknown";
    public List<SchemaProperty> Properties { get; set; } = new();
}
