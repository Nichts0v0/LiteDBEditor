using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LiteDBEditor.Views;

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

    public static async Task<bool> Show(Window owner, string title, string message)
    {
        var window = new ConfirmWindow();
        window.FindControl<TextBlock>("TitleText")!.Text = title;
        window.FindControl<TextBlock>("MessageText")!.Text = message;

        await window.ShowDialog(owner);
        return window._result;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}
