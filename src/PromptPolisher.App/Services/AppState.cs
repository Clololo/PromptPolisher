using System.IO;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Services;

namespace PromptPolisher.App.Services;

public sealed class AppState
{
    public string DataDirectory { get; init; } = string.Empty;
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string TemplatePath => Path.Combine(DataDirectory, "system-prompt.txt");
    public string DatabasePath => Path.Combine(DataDirectory, "history.db");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string WebViewDataDirectory => Path.Combine(DataDirectory, "webview");

    /// <summary>随程序发布的默认模板。</summary>
    public string DefaultTemplatePath => Path.Combine(AppContext.BaseDirectory, "config", "system-prompt.default.txt");

    /// <summary>启动时读取的内置默认模板；“恢复默认”使用此内容。</summary>
    public string DefaultTemplate { get; set; } = PromptTemplateRenderer.DefaultTemplate;

    public AppSettings Settings { get; set; } = new();
    public string Template { get; set; } = string.Empty;
}
