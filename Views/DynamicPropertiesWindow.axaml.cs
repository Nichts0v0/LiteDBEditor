using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LiteDB;
using LiteDBEditor.Models;
using LiteDBEditor.ViewModels;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace LiteDBEditor.Views;

public partial class DynamicPropertiesWindow : Window
{
    #region 生命周期与初始化

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

    private void OnRequestScrollToError(DynamicPropertyItemViewModel item)
    {
        // 延迟一小段时间确保 UI 已反映任何可能的状态变更（虽然此处通常是即时的）
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var itemsControl = this.FindControl<ItemsControl>("PropertiesItemsControl");
            if (itemsControl == null) return;

            // 在视觉树中查找 DataContext 等于该 item 的对应控件
            foreach (var logicalChild in itemsControl.GetVisualChildren())
            {
                // ItemsControl 的直接视觉子项通常是 DataTemplate 渲染出来的容器
                if (logicalChild is Avalonia.Controls.Control control && control.DataContext == item)
                {
                    control.BringIntoView();
                    FocusFirstErrorInput(control);
                    return;
                }
                
                // 递归查找深度嵌套的情况（如果需要）
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

    private void FocusFirstErrorInput(Control control)
    {
        // 查找所有 TextBox 并 Focus 第一个可见且可能报错的
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

    #region 保存与取消

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DynamicPropertiesViewModel vm)
        {
            // 执行保存回调（含系统化校验与回退），如果校验失败则不关闭窗口
            if (!await vm.ExecuteSaveAsync())
            {
                return;
            }
        }
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    #endregion

    #region 数值输入框字符过滤（Loaded 事件挂载）

    /// <summary>
    /// 数值 TextBox 初始化完成后，通过 Tunnel 事件拦截非法字符输入。
    /// - Int32 / Int64：只允许数字和首位负号
    /// - Double：允许小数点（最多 1 个）和首位负号
    /// TypeName 来自行的 DynamicPropertyItemViewModel。
    /// </summary>
    private void OnNumericTextBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not DynamicPropertyItemViewModel vm) return;

        var typeName = vm.TypeName; // 运行时取类型，闭包固定

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
