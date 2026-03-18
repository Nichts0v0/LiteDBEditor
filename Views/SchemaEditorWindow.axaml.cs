using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LiteDBEditor.ViewModels;
using LiteDBEditor.Models;

namespace LiteDBEditor.Views;

/// <summary>
/// Schema 编辑器窗口，允许用户可视化地定义数据结构并自动生成 C# 模型类。
/// </summary>
public partial class SchemaEditorWindow : Window
{
    public SchemaEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击保存按钮，生成 C# 源代码并返回生成的类名及文件路径。
    /// </summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SchemaEditorViewModel vm)
        {
            var path = vm.SaveAndGenerate();
            if (path != null)
            {
                Close(new SchemaEditorResult
                {
                    ClassName = vm.MainClass.ClassName,
                    FilePath = path
                });
            }
        }
    }

    /// <summary>
    /// 取消操作。
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    /// <summary>
    /// 使用历史列表中的现有 Schema 填充编辑器。
    /// </summary>
    private void OnUseExistingClick(object? sender, RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("ExistingSchemasList");
        if (listBox?.SelectedItem is SchemaItem selected && DataContext is SchemaEditorViewModel vm)
        {
            vm.LoadFromPath(selected.FullPath);
        }
    }

    /// <summary>
    /// 浏览本地文件系统以加载现有的 Schema 定义。
    /// </summary>
    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Schema 架构 (JSON 或 C#)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Schema 架构文件") { Patterns = new[] { "*.schema.json", "*.cs" } }
            }
        });

        if (files.Count >= 1 && DataContext is SchemaEditorViewModel vm)
        {
            var path = files[0].Path.LocalPath;
            vm.LoadFromPath(path);
        }
    }
}
