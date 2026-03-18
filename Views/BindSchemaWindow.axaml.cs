using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LiteDBEditor.Services;

namespace LiteDBEditor.Views;

/// <summary>
/// 绑定 Schema 窗口，允许用户为特定的 LiteDB 集合关联一个外部 C# 或 JSON 架构文件。
/// </summary>
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

    /// <summary>
    /// 加载历史绑定的 Schema 列表以供快速选择。
    /// </summary>
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

    /// <summary>
    /// 当用户从历史列表中选择一个 Schema 时触发。
    /// </summary>
    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var cb = GetHistoryComboBox();
        if (cb != null && cb.SelectedValue is string path)
        {
            ApplySchemaSelection(path);
        }
    }

    /// <summary>
    /// 应用当前选中的 Schema 路径并更新 UI 显示。
    /// </summary>
    private void ApplySchemaSelection(string path)
    {
        _selectedCsFilePath = path;
        var txtPath = GetTemplatePathText();
        if (txtPath != null)
        {
            txtPath.Text = $"已选中模板: {Path.GetFileName(path)}";
            txtPath.Foreground = Avalonia.Media.Brushes.Green;
        }
    }

    /// <summary>
    /// 浏览文件系统以选择新的 C# 或 JSON 模板文件。
    /// </summary>
    private async void OnSelectCodeTemplateClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择代码模板 (JSON 或 C#)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Schema 配置文件") { Patterns = new[] { "*.schema.json", "*.cs" } }
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

    /// <summary>
    /// 点击确定按钮，关闭窗口并返回选中的路径。
    /// </summary>
    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedCsFilePath))
        {
            Close(_selectedCsFilePath);
        }
    }

    /// <summary>
    /// 点击取消按钮。
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}