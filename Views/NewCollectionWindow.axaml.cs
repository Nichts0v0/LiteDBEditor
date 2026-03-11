using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiteDBEditor.Services;
using Avalonia.Markup.Xaml;

namespace LiteDBEditor.Views;

public class NewCollectionResult
{
    public string CollectionName { get; set; } = string.Empty;
    public string? BoundCsFilePath { get; set; }
}

public partial class NewCollectionWindow : Window
{
    private string? _selectedCsFilePath;
    private readonly SchemaBindingService _bindingService;

    public NewCollectionWindow()
    {
        InitializeComponent();
        _bindingService = new SchemaBindingService();
        LoadHistorySchemas();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private ComboBox? GetHistoryComboBox() => this.FindControl<ComboBox>("HistoryComboBox");
    private TextBox? GetNameTextBox() => this.FindControl<TextBox>("NameTextBox");
    private TextBlock? GetTemplatePathText() => this.FindControl<TextBlock>("TemplatePathText");

    private void LoadHistorySchemas()
    {
        var cb = GetHistoryComboBox();
        if (cb == null) return;

        var schemas = _bindingService.GetAvailableSchemas();
        var displayItems = schemas.Select(s => new { Path = s, Name = Path.GetFileName(s) }).ToList();
        cb.ItemsSource = displayItems;
        cb.DisplayMemberBinding = new Avalonia.Data.Binding("Name");
        cb.SelectedValueBinding = new Avalonia.Data.Binding("Path");
    }

    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var cb = GetHistoryComboBox();
        if (cb != null && cb.SelectedValue is string path)
        {
            ApplySchemaSelection(path, true);
        }
    }

    private void ApplySchemaSelection(string path, bool isFromHistory)
    {
        _selectedCsFilePath = path;
        var fileName = Path.GetFileNameWithoutExtension(path);

        var tbName = GetNameTextBox();
        if (tbName != null && string.IsNullOrEmpty(tbName.Text))
        {
            tbName.Text = fileName;
        }

        var txtPath = GetTemplatePathText();
        if (txtPath != null)
        {
            txtPath.Text = $"已选定: {Path.GetFileName(path)}";
            txtPath.Foreground = Avalonia.Media.Brushes.Green;
        }
    }

    private async void OnSelectCodeTemplateClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择表模板 C# 文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("C# 代码文件") { Patterns = new[] { "*.cs" } }
            }
        });

        if (files.Count >= 1)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                // 清掉下拉框本身的视觉高亮以免混淆
                var cb = GetHistoryComboBox();
                if (cb != null) cb.SelectedIndex = -1;

                ApplySchemaSelection(path, false);
            }
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var tbName = GetNameTextBox();
        var collName = tbName?.Text?.Trim();
        if (!string.IsNullOrEmpty(collName))
        {
            Close(new NewCollectionResult
            {
                CollectionName = collName,
                BoundCsFilePath = _selectedCsFilePath
            });
        }
        else
        {
            ShowError("表名不能为空。");
        }
    }

    private void OnNameTextChanged(object? sender, TextChangedEventArgs e)
    {
        var errBorder = this.FindControl<Border>("ErrorBorder");
        var nameBox = this.FindControl<TextBox>("NameTextBox");
        if (errBorder != null) errBorder.IsVisible = false;
        if (nameBox != null) nameBox.Classes.Remove("error");
    }

    private void ShowError(string message)
    {
        var errText = this.FindControl<TextBlock>("ErrorText");
        var errBorder = this.FindControl<Border>("ErrorBorder");
        var nameBox = this.FindControl<TextBox>("NameTextBox");
        if (errText != null && errBorder != null)
        {
            errText.Text = message;
            errBorder.IsVisible = true;
        }
        if (nameBox != null) nameBox.Classes.Add("error");
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
