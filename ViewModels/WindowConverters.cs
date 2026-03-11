using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LiteDBEditor.ViewModels;

public static class WindowConverters
{
    public static readonly IValueConverter ErrorBackgroundConverter =
        new FuncValueConverter<string?, IBrush>(v =>
            string.IsNullOrEmpty(v) ? Brushes.Transparent : Brush.Parse("#FFCCCC"));

    public static readonly IValueConverter ErrorForegroundConverter =
        new FuncValueConverter<string?, IBrush>(v =>
            string.IsNullOrEmpty(v) ? Brushes.Gray : Brushes.DarkRed);
}
