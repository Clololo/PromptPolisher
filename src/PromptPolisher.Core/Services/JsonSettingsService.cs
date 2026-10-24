using System.Text.Json;
using System.Text.Json.Serialization;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.Core.Services;

/// <summary>
/// 配置文件只保存非敏感设置。API Key 永远不进这里。
/// </summary>
public sealed class JsonSettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IRedactingLogger _logger;

    public JsonSettingsService(IRedactingLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 加载设置与系统 Prompt 模板。模板优先级：
    /// 旧配置内嵌模板 &gt; 用户模板文件 &gt; 程序内置默认模板 &gt; 代码常量。
    /// 用户模板单独存放在 LocalAppData，程序升级不会覆盖；只有“恢复默认”才回退到内置模板。
    /// </summary>
    public AppSettings Load(
        string settingsPath,
        string templatePath,
        string defaultTemplatePath,
        out string template)
    {
        AppSettings settings;

        try
        {
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? AppSettings.CreateDefault();
            }
            else
            {
                settings = AppSettings.CreateDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("Failed to load settings; using defaults.", ex);
            settings = AppSettings.CreateDefault();
        }

        if (settings.Providers.Count == 0)
        {
            settings.Providers = AppSettings.CreateDefault().Providers;
        }
        else
        {
            // 兼容升级：新版本新增的提供方自动补进来，不覆盖用户已有配置。
            var defaults = AppSettings.CreateDefault().Providers;
            foreach (var defaultProvider in defaults)
            {
                if (!settings.Providers.Any(p =>
                        string.Equals(p.Id, defaultProvider.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    settings.Providers.Add(defaultProvider);
                }
            }
        }

        ApplyMigrations(settings);

        if (!string.IsNullOrWhiteSpace(settings.SystemPromptTemplate))
        {
            // 兼容旧版本把模板内嵌在 settings.json 的配置。
            template = settings.SystemPromptTemplate;
        }
        else if (File.Exists(templatePath))
        {
            // 用户在设置页保存过的模板，重启/升级后优先使用。
            template = File.ReadAllText(templatePath);
        }
        else if (File.Exists(defaultTemplatePath))
        {
            // 首次启动：复制程序目录自带的默认模板到用户模板文件。
            template = File.ReadAllText(defaultTemplatePath);
        }
        else
        {
            template = PromptTemplateRenderer.DefaultTemplate;
        }

        return settings;
    }

    private static void ApplyMigrations(AppSettings settings)
    {
        var defaults = AppSettings.CreateDefault();

        // v1 把泛化的“验证码”当成 captcha 关键词，导致登录页被误判。
        // 这里把旧配置迁移为更精确的关键词。
        foreach (var provider in settings.Providers.Where(p => p.Kind == ProviderKind.Web))
        {
            provider.Selectors ??= new WebSelectors();
            var hasOldKeyword = provider.Selectors.CaptchaText.Any(x =>
                string.Equals(x, "验证码", StringComparison.OrdinalIgnoreCase));

            if (provider.Selectors.CaptchaText.Count == 0 || hasOldKeyword)
            {
                var defaultProvider = defaults.Providers.FirstOrDefault(p =>
                    string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
                if (defaultProvider?.Selectors is not null)
                {
                    provider.Selectors.CaptchaText =
                        new List<string>(defaultProvider.Selectors.CaptchaText);
                }
            }
        }
    }

    public void Save(string settingsPath, AppSettings settings, string templatePath, string template)
    {
        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 模板单独存放，便于用户直接编辑与版本控制；配置里不重复保存正文。
            settings.SystemPromptTemplate = string.Empty;
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, Options));

            var templateDirectory = Path.GetDirectoryName(templatePath);
            if (!string.IsNullOrEmpty(templateDirectory))
            {
                Directory.CreateDirectory(templateDirectory);
            }

            File.WriteAllText(templatePath, template);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save settings.", ex);
            throw;
        }
    }
}
