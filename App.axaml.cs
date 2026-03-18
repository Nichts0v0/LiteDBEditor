using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System;
using Avalonia.Markup.Xaml;
using LiteDBEditor.ViewModels;
using LiteDBEditor.Views;
using LiteDBEditor.Services;

namespace LiteDBEditor;

public partial class App : Application
{
    public static LanguageService Language { get; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 确保在主窗口创建并设置 DataContext 之前初始化语言资源
        LanguageService.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            // 如果有命令行参数（例如通过右键“打开方式”），尝试直接打开数据库
            if (desktop.Args != null && desktop.Args.Length > 0)
            {
                var filePath = desktop.Args[0];
                if (System.IO.File.Exists(filePath))
                {
                    viewModel.OpenDatabase(filePath);
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}