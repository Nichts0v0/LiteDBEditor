using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using LiteDBEditor.ViewModels;

namespace LiteDBEditor;

/// <summary>
/// 视图定位器，负责根据 ViewModel 的类型自动匹配并创建对应的 View 实例。
/// 遵循约定：命名空间中的 "ViewModels" 替换为 "Views"，类名后缀 "ViewModel" 替换为 "View"。
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    /// <summary>
    /// 根据传入的 ViewModel 数据对象构建对应的视图控件。
    /// </summary>
    /// <param name="param">ViewModel 实例。</param>
    /// <returns>匹配的视图控件，若未找到则返回错误提示文本。</returns>
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    /// <summary>
    /// 检查当前数据定位器是否支持处理指定的数据类型。
    /// </summary>
    /// <param name="data">待匹配的数据对象。</param>
    /// <returns>如果对象派生自 ViewModelBase，则返回 true。</returns>
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
