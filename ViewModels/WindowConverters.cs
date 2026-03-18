using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LiteDBEditor.ViewModels;

/// <summary>
/// 窗口专用的静态值转换器集合，主要用于 UI 状态（如校验错误）的视觉反馈。
/// </summary>
public static class WindowConverters
{
    /// <summary>
    /// 错误背景转换器：当字符串不为空（表示有错误）时返回浅红色背景，否则返回透明。
    /// </summary>
    public static readonly IValueConverter ErrorBackgroundConverter =
        new FuncValueConverter<string?, IBrush>(v =>
            string.IsNullOrEmpty(v) ? Brushes.Transparent : Brush.Parse("#FFCCCC"));

    /// <summary>
    /// 错误前景转换器：当字符串不为空时返回深红色，否则返回灰色。
    /// </summary>
    public static readonly IValueConverter ErrorForegroundConverter =
        new FuncValueConverter<string?, IBrush>(v =>
            string.IsNullOrEmpty(v) ? Brushes.Gray : Brushes.DarkRed);
}
