using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using System.IO;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Controls;

namespace LiteDBEditor.Services;

/// <summary>
/// 语言管理服务，负责 UI 的国际化多语言切换及资源加载。
/// </summary>
public class LanguageService
{
    private static ResourceInclude? _currentResourceInclude;

    /// <summary>
    /// 系统支持的语言映射列表。
    /// </summary>
    public static Dictionary<string, string> AvailableLanguages { get; } = new()
    {
        { "zh-CN", "简体中文" },
        { "en-US", "English" }
    };

    /// <summary>
    /// 当前正在使用的语言标识（如 zh-CN）。
    /// </summary>
    public static string CurrentLanguage { get; private set; } = "zh-CN";

    /// <summary>
    /// 当语言发生改变时触发的事件。
    /// </summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// 初始化语言设置，尝试从配置文件恢复上次设置，否则跟随系统区域。
    /// </summary>
    public static void Initialize()
    {
        try
        {
            // 优先加载用户手动保存的配置
            var savedLang = ConfigService.Config.Language;
            if (!string.IsNullOrEmpty(savedLang) && AvailableLanguages.ContainsKey(savedLang))
            {
                SetLanguage(savedLang, false);
                return;
            }

            // 尝试匹配系统本地环境
            var systemLocale = CultureInfo.CurrentUICulture.Name;
            if (AvailableLanguages.ContainsKey(systemLocale))
            {
                SetLanguage(systemLocale);
            }
            else
            {
                SetLanguage("zh-CN");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Language initialization failed: {ex}");
        }
    }

    /// <summary>
    /// 动态切换应用程序当前的语言资源。
    /// </summary>
    /// <param name="locale">目标语言标识</param>
    /// <param name="save">是否将此更改保存到配置文件</param>
    public static void SetLanguage(string locale, bool save = true)
    {
        if (!AvailableLanguages.ContainsKey(locale)) return;

        try
        {
            var app = Application.Current;
            if (app == null) return;

            var mergedDicts = app.Resources.MergedDictionaries;

            // 移除当前已经加载的语言资源字典
            if (_currentResourceInclude != null)
            {
                mergedDicts.Remove(_currentResourceInclude);
            }

            // 动态构建并加载新的 axaml 资源文件
            var sourceUri = new Uri($"avares://LiteDBEditor/Resources/Languages/{locale}.axaml");
            _currentResourceInclude = new ResourceInclude(sourceUri) { Source = sourceUri };

            mergedDicts.Add(_currentResourceInclude);
            CurrentLanguage = locale;

            if (save)
            {
                ConfigService.Config.Language = locale;
                ConfigService.Save();
            }

            Console.WriteLine($"Language successfully set to: {locale}");
            LanguageChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting language: {ex}");
        }
    }

    /// <summary>
    /// 根据 Resource Key 从当前激活的语言字典中获取字符串。
    /// </summary>
    /// <param name="key">资源键</param>
    /// <returns>本地化后的字符串，若未找到则返回 key 原文</returns>
    public static string GetString(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out var val) == true && val is string s)
        {
            return s;
        }
        return key;
    }
}
