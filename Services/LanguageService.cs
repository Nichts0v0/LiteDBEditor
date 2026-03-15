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

public class LanguageService
{
    private static ResourceInclude? _currentResourceInclude;

    public static Dictionary<string, string> AvailableLanguages { get; } = new()
    {
        { "zh-CN", "简体中文" },
        { "en-US", "English" }
    };

    public static string CurrentLanguage { get; private set; } = "zh-CN";

    public static event Action? LanguageChanged;

    public static void Initialize()
    {
        try
        {
            // 优先从配置加载项目小技巧。
            var savedLang = ConfigService.Config.Language;
            if (!string.IsNullOrEmpty(savedLang) && AvailableLanguages.ContainsKey(savedLang))
            {
                SetLanguage(savedLang, false);
                return;
            }

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

    public static void SetLanguage(string locale, bool save = true)
    {
        if (!AvailableLanguages.ContainsKey(locale)) return;

        try
        {
            var app = Application.Current;
            if (app == null) return;

            var mergedDicts = app.Resources.MergedDictionaries;

            // 移除旧资源项目小技巧。
            if (_currentResourceInclude != null)
            {
                mergedDicts.Remove(_currentResourceInclude);
            }

            // 加载新的嵌入资源项目小技巧。
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

    public static string GetString(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out var val) == true && val is string s)
        {
            return s;
        }
        return key;
    }
}
