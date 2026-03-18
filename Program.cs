using Avalonia;
using System;

namespace LiteDBEditor;

/// <summary>
/// 应用程序入口类。
/// </summary>
sealed class Program
{
    /// <summary>
    /// 应用程序主入口点。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// 构建并配置 Avalonia 应用程序。
    /// 此方法也被 Avalonia 可视化设计器使用。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
