using System;

namespace LiteDBEditor.Models;

/// <summary>
/// 应用程序配置模型，用于持久化用户的全局设置。
/// </summary>
public class AppConfig
{
    /// <summary>
    /// 获取或设置当前的界面语言标识（如 "zh-CN" 或 "en-US"）。
    /// </summary>
    public string Language { get; set; } = "zh-CN";
}
