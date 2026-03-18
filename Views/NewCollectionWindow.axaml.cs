using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LiteDBEditor.Services;

namespace LiteDBEditor.Views;

/// <summary>
/// 新建集合窗口的结果载体。
/// </summary>
public class NewCollectionResult
{
    public string CollectionName { get; set; } = string.Empty;
    public string? BoundCsFilePath { get; set; }
}

/// <summary>
/// 新建集合（表）窗口。
/// 用户可以输入表名，并可选地通过模板快速初始化表结构。
/// </summary>
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

    /// <summary>
    /// 加载历史 Schema 以便用户在创建新表时直接复用。
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
    /// 当从历史下拉框中选择一个模板时，自动填充默认表名（类名）。
    /// </summary>
    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var cb = GetHistoryComboBox();
        if (cb != null && cb.SelectedValue is string path)
        {
            ApplySchemaSelection(path, true);
        }
    }

    /// <summary>
    /// 应用选中的模板路径，并更新 UI 反馈。
    /// </summary>
    private void ApplySchemaSelection(string path, bool isFromHistory)
    {
        _selectedCsFilePath = path;
        var fileName = Path.GetFileNameWithoutExtension(path);

        var tbName = GetNameTextBox();
        if (tbName != null && (string.IsNullOrEmpty(tbName.Text) || isFromHistory))
        {
            tbName.Text = fileName;
        }

        var txtPath = GetTemplatePathText();
        if (txtPath != null)
        {
            txtPath.Text = $"已选中模板: {Path.GetFileName(path)}";
            txtPath.Foreground = Avalonia.Media.Brushes.Green;
        }
    }

    /// <summary>
    /// 浏览文件系统以选择一个全新的代码模板。
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

                ApplySchemaSelection(path, false);
            }
        }
    }

    /// <summary>
    /// 点击确定，校验表名并返回结果。
    /// </summary>
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

    /// <summary>
    /// 当表名文本改变时，清除错误状态。
    /// </summary>
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