using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LiteDBEditor.Views;

/// <summary>
/// 通用确认对话框，用于执行危险操作前向用户寻求确认。
/// </summary>
public partial class ConfirmWindow : Window
{
    private bool _result = false;

    public ConfirmWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 静态显示确认窗口并等待结果。
    /// </summary>
    /// <param name="owner">父窗口。</param>
    /// <param name="title">对话框标题。</param>
    /// <param name="message">提示信息正文。</param>
    /// <returns>用户是否点击了确认。</returns>
    public static async Task<bool> Show(Window owner, string title, string message)
    {
        var window = new ConfirmWindow();
        window.FindControl<TextBlock>("TitleText")!.Text = title;
        window.FindControl<TextBlock>("MessageText")!.Text = message;

        await window.ShowDialog(owner);
        return window._result;
    }

    /// <summary>
    /// 响应确认按钮点击。
    /// </summary>
    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    /// <summary>
    /// 响应取消按钮点击。
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}
