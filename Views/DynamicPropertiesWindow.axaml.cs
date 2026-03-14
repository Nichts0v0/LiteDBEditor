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

    #endregion

    #region 保存与取消

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DynamicPropertiesViewModel vm)
        {
            vm.ClearErrors();
            // 执行保存回调（含查重校验），如果校验失败则不关闭窗口
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
