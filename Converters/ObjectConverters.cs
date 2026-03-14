using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LiteDBEditor.Converters;

public class ObjectConverters
{
    public static readonly IValueConverter IsNotNull =
        new FuncValueConverter<object?, bool>(x => x != null);

    public static readonly IValueConverter IsNull =
        new FuncValueConverter<object?, bool>(x => x == null);

    public static readonly IValueConverter ExpandIconConverter =
        new FuncValueConverter<bool, object?>(expanded => 
            expanded ? Avalonia.Media.Geometry.Parse("M7 10l5 5 5-5z") : Avalonia.Media.Geometry.Parse("M10 17l5-5-5-5z"));
}
