using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LiteDBEditor.Converters;

/// <summary>
/// 相等性比较转换器，用于在 XAML 中判断绑定值是否等于特定的参数值。
/// 常用于 Tab 选中状态、单选按钮逻辑等。
/// </summary>
public class EqualsConverter : IValueConverter
{
    /// <summary>
    /// 静态单例实例。
    /// </summary>
    public static readonly EqualsConverter Instance = new();

    /// <summary>
    /// 执行相等性比较。
    /// </summary>
    /// <param name="value">绑定的原始值</param>
    /// <param name="targetType">目标 UI 属性类型</param>
    /// <param name="parameter">要比较的期望值</param>
    /// <param name="culture">区域信息</param>
    /// <returns>若相等则返回 true，否则返回 false</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
