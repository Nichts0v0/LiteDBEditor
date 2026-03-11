using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LiteDBEditor.ViewModels;

namespace LiteDBEditor.Converters;

/// <summary>
/// A converter that checks if a specific field in the BsonDocumentWrapper is modified.
/// If it is, it returns an orange brush; otherwise, it returns a default foreground brush.
/// </summary>
public class ModifiedFieldColorConverter : IMultiValueConverter
{
    public static readonly ModifiedFieldColorConverter Instance = new();

    public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // 期望价值观：
        // values[0] -> The BsonDocumentWrapper instance
        // values[1] -> True/False 强行激发（用于绑定的刷新信号，比如 IsModified 状态本身）
        // parameter -> The column name (string)
        if (values == null || values.Count < 1)
            return null; // Let it fallback to default

        if (values[0] is BsonDocumentWrapper wrapper && parameter is string propertyName)
        {
            if (wrapper.IsFieldModified(propertyName))
            {
                // 可以修改为你喜欢的颜色，现设为醒目的金橙色。
                return new SolidColorBrush(Color.Parse("#FFA500"));
            }
        }

        // 默认返回 null，会让 TextBlock 依靠其原设默认前景色（也就是追随主题的白或者黑颜色）。
        return null;
    }
}
