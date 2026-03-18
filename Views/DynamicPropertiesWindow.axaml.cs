using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LiteDBEditor.ViewModels;

namespace LiteDBEditor.Views;

/// <summary>
/// 动态属性编辑窗口，用于展示和修改单个 BSON 文档的所有字段。
/// 支持根据 Schema 自动生成编辑控件（文本框、复选框等）。
/// </summary>
public partial class DynamicPropertiesWindow : Window
{
    #region 自动滚动与错误定位逻辑

    public DynamicPropertiesWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DynamicPropertiesViewModel vm)
        {
            vm.RequestScrollToError -= OnRequestScrollToError;
            vm.RequestScrollToError += OnRequestScrollToError;
        }
    }

    /// <summary>
    /// 当校验失败时，自动将错误的输入框滚动到视口中央。
    /// </summary>
    private void OnRequestScrollToError(DynamicPropertyItemViewModel item)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var itemsControl = this.FindControl<ItemsControl>("PropertiesItemsControl");
            if (itemsControl == null) return;

            foreach (var logicalChild in itemsControl.GetVisualChildren())
            {
                if (logicalChild is Avalonia.Controls.Control control && control.DataContext == item)
                {
                    control.BringIntoView();
                    FocusFirstErrorInput(control);
                    return;
                }

                var found = FindControlByDataContext(logicalChild, item);
                if (found != null)
                {
                    found.BringIntoView();
                    FocusFirstErrorInput(found);
                    return;
                }
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 将焦点设置到指定控件内的第一个有效输入框上。
    /// </summary>
    private void FocusFirstErrorInput(Control control)
    {
        foreach (var visual in control.GetVisualDescendants())
        {
            if (visual is TextBox tb && tb.IsVisible && tb.IsEnabled && !tb.IsReadOnly)
            {
                tb.Focus();
                tb.SelectionStart = 0;
                tb.SelectionEnd = tb.Text?.Length ?? 0;
                break;
            }
        }
    }

    private Avalonia.Controls.Control? FindControlByDataContext(Avalonia.Visual parent, object dataContext)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Avalonia.Controls.Control control && control.DataContext == dataContext)
                return control;

            var nested = FindControlByDataContext(child, dataContext);
            if (nested != null) return nested;
        }
        return null;
    }

    #endregion

    #region 按钮操作

    /// <summary>
    /// 点击保存按钮，触发 ViewModel 的深度校验和写回。
    /// </summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DynamicPropertiesViewModel vm)
        {
            if (!await vm.ExecuteSaveAsync())
            {
                return;
            }
        }
        Close(true);
    }

    /// <summary>
    /// 取消编辑。
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    #endregion

    #region 输入限制处理

    /// <summary>
    /// 针对数字类型的输入框，在加载时注入拦截器防止非数字字符输入。
    /// </summary>
    private void OnNumericTextBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not DynamicPropertyItemViewModel vm) return;

        var typeName = vm.TypeName;

        tb.AddHandler(
            InputElement.TextInputEvent,
            (object? s, TextInputEventArgs ev) =>
            {
                if (ev.Text == null) return;
                var box = (TextBox)s!;
                foreach (char c in ev.Text)
                {
                    bool ok = typeName switch
                    {
                        "Int32" or "Int64" =>
                            char.IsDigit(c)
                            || (c == '-' && (box.Text?.Length == 0 || box.SelectionStart == 0)),
                        "Double" =>
                            char.IsDigit(c)
                            || (c == '-' && (box.Text?.Length == 0 || box.SelectionStart == 0))
                            || (c == '.' && box.Text?.Contains('.') != true),
                        _ => true
                    };
                    if (!ok) { ev.Handled = true; return; }
                }
            },
            RoutingStrategies.Tunnel);
    }

    #endregion
}