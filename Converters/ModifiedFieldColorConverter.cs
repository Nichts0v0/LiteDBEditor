using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LiteDBEditor.ViewModels;

namespace LiteDBEditor.Converters;

/// <summary>
/// 字段修改高亮转换器。
/// 该转换器会检查 BsonDocumentWrapper 中指定字段的修改状态，
/// 若该字段有尚未保存的更改，则返回醒目的橙色笔刷，用于在 DataGrid 中实现“脏数据”提示。
/// </summary>
public class ModifiedFieldColorConverter : IMultiValueConverter
{
    /// <summary>
    /// 静态单例实例。
    /// </summary>
    public static readonly ModifiedFieldColorConverter Instance = new();

    /// <summary>
    /// 执行多值转换逻辑。
    /// </summary>
    /// <param name="values">
    /// values[0]: BsonDocumentWrapper 实例。
    /// values[1]: 用于强制触发刷新的信号量。
    /// </param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">当前列对应的属性字段名称（string）</param>
    /// <param name="culture">区域信息</param>
    /// <returns>高亮颜色笔刷或 null</returns>
    public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 1)
            return null;

        if (values[0] is BsonDocumentWrapper wrapper && parameter is string propertyName)
        {
            // 如果包装器标记该字段已修改，则返回金橙色
            if (wrapper.IsFieldModified(propertyName))
            {
                return new SolidColorBrush(Color.Parse("#FFA500"));
            }
        }

        // 返回 null 表示不覆盖原本的前景色，由 DataGrid 决定
        return null;
    }
}
