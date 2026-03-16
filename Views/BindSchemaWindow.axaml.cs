using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiteDBEditor.Services;
using Avalonia.Markup.Xaml;

namespace LiteDBEditor.Views;

public partial class BindSchemaWindow : Window
{
    private string? _selectedCsFilePath;
    private readonly SchemaBindingService _bindingService;

    public BindSchemaWindow()
    {
        InitializeComponent();
        _bindingService = new SchemaBindingService();
        LoadHistorySchemas();
    }

    private ComboBox? GetHistoryComboBox() => this.FindControl<ComboBox>("HistoryComboBox");
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
            ApplySchemaSelection(path);
        }
    }

    private void ApplySchemaSelection(string path)
    {
        _selectedCsFilePath = path;
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
            Title = "选择表模板文件 (JSON 或 C#)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Schema 模板文件") { Patterns = new[] { "*.schema.json", "*.cs" } }
            }
        });

        if (files.Count >= 1)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                var cb = GetHistoryComboBox();
                if (cb != null) cb.SelectedIndex = -1;
                ApplySchemaSelection(path);
            }
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedCsFilePath))
        {
            Close(_selectedCsFilePath);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
