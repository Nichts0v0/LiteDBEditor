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
        if (listBox?.SelectedItem is string selectedFile && DataContext is SchemaEditorViewModel vm)
        {
            // 修改：不再直接关闭返回，而是加载到当前编辑区
            var result = vm.GetResultFromExisting(selectedFile);
            if (result != null) vm.LoadFromPath(result.FilePath);
        }
    }

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 C# 脚本模板",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("C# Files") { Patterns = new[] { "*.cs" } } }
        });

        if (files.Count >= 1 && DataContext is SchemaEditorViewModel vm)
        {
            // 修改：不再直接关闭返回，而是加载到当前编辑区
            var path = files[0].Path.LocalPath;
            vm.LoadFromPath(path);
        }
    }
}
