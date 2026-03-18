using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LiteDBEditor.Converters;

/// <summary>
/// 通用对象状态转换器静态集合，提供简单的逻辑判断和 UI 资源转换。
/// </summary>
public class ObjectConverters
{
    /// <summary>
    /// 判断对象是否不为 null 的转换器。
    /// </summary>
    public static readonly IValueConverter IsNotNull =
        new FuncValueConverter<object?, bool>(x => x != null);

    /// <summary>
    /// 判断对象是否为 null 的转换器。
    /// </summary>
    public static readonly IValueConverter IsNull =
        new FuncValueConverter<object?, bool>(x => x == null);

    /// <summary>
    /// 展开/折叠图标转换器。
    /// 根据布尔状态返回对应的 Path 几何图形数据（下箭头或右箭头）。
    /// </summary>
    public static readonly IValueConverter ExpandIconConverter =
        new FuncValueConverter<bool, object?>(expanded => 
            expanded ? Avalonia.Media.Geometry.Parse("M7 10l5 5 5-5z") : Avalonia.Media.Geometry.Parse("M10 17l5-5-5-5z"));
}
