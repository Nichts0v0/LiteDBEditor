using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LiteDBEditor.ViewModels;
using LiteDBEditor.Models;

namespace LiteDBEditor.Views;

public partial class SchemaEditorWindow : Window
{
    public SchemaEditorWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SchemaEditorViewModel vm)
        {
            var path = vm.SaveAndGenerate();
            if (path != null)
            {
                // 保存成功，关闭窗口并返回结果
                Close(new SchemaEditorResult
                {
                    ClassName = vm.MainClass.ClassName,
                    FilePath = path
                });
            }
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnUseExistingClick(object? sender, RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("ExistingSchemasList");
        if (listBox?.SelectedItem is SchemaItem selected && DataContext is SchemaEditorViewModel vm)
        {
            vm.LoadFromPath(selected.FullPath);
        }
    }

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Schema 模板 (JSON 或 C#)",
            AllowMultiple = false,
            FileTypeFilter = new[] 
            { 
                new FilePickerFileType("Schema Files") { Patterns = new[] { "*.schema.json", "*.cs" } } 
            }
        });

        if (files.Count >= 1 && DataContext is SchemaEditorViewModel vm)
        {
            var path = files[0].Path.LocalPath;
            vm.LoadFromPath(path);
        }
    }
}
