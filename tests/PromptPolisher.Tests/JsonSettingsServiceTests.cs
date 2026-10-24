using System.Text.Json;
using System.Text.Json.Serialization;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Services;
using Xunit;

namespace PromptPolisher.Tests;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "PromptPolisherTests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private string TemplatePath => Path.Combine(_directory, "system-prompt.txt");
    private string DefaultTemplatePath => Path.Combine(_directory, "system-prompt.default.txt");

    public JsonSettingsServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Load_MissingFile_UsesDefaultsAndDefaultTemplate()
    {
        var service = new JsonSettingsService(new FakeLogger());

        var settings = service.Load(SettingsPath, TemplatePath, DefaultTemplatePath, out var template);

        Assert.NotEmpty(settings.Providers);
        Assert.Equal("deepseek-web", settings.DefaultProviderId);
        Assert.Equal(PromptTemplateRenderer.DefaultTemplate, template);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var service = new JsonSettingsService(new FakeLogger());
        var settings = AppSettings.CreateDefault();
        settings.DefaultProviderId = "manual";
        settings.SaveHistory = false;
        settings.Providers[0].Model = "custom-model";

        service.Save(SettingsPath, settings, TemplatePath, "custom template {{user_prompt}}");

        var loaded = service.Load(SettingsPath, TemplatePath, DefaultTemplatePath, out var template);

        Assert.Equal("manual", loaded.DefaultProviderId);
        Assert.False(loaded.SaveHistory);
        Assert.Equal("custom-model", loaded.Providers[0].Model);
        Assert.Equal("custom template {{user_prompt}}", template);
    }

    [Fact]
    public void Load_PrefersUserTemplateOverBuiltInDefault()
    {
        File.WriteAllText(TemplatePath, "user template {{user_prompt}}");
        File.WriteAllText(DefaultTemplatePath, "built-in template {{user_prompt}}");

        var service = new JsonSettingsService(new FakeLogger());
        service.Load(SettingsPath, TemplatePath, DefaultTemplatePath, out var template);

        Assert.Equal("user template {{user_prompt}}", template);
    }

    [Fact]
    public void Load_FallsBackToBuiltInDefault_WhenUserTemplateMissing()
    {
        File.WriteAllText(DefaultTemplatePath, "built-in template {{user_prompt}}");

        var service = new JsonSettingsService(new FakeLogger());
        service.Load(SettingsPath, TemplatePath, DefaultTemplatePath, out var template);

        Assert.Equal("built-in template {{user_prompt}}", template);
    }

    [Fact]
    public void EditedTemplate_SurvivesRestart_WithoutBeingOverwrittenByBuiltInDefault()
    {
        File.WriteAllText(DefaultTemplatePath, "built-in template {{user_prompt}}");
        var service = new JsonSettingsService(new FakeLogger());

        service.Save(SettingsPath, AppSettings.CreateDefault(), TemplatePath, "edited {{user_prompt}}");
        service.Load(SettingsPath, TemplatePath, DefaultTemplatePath, out var template);

        Assert.Equal("edited {{user_prompt}}", template);
    }

    [Fact]
    public void Save_DoesNotWriteApiKeys()
    {
        var service = new JsonSettingsService(new FakeLogger());
        var settings = AppSettings.CreateDefault();

        service.Save(SettingsPath, settings, TemplatePath, PromptTemplateRenderer.DefaultTemplate);

        var json = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_MigratesLegacyCaptchaKeyword()
    {
        var settings = AppSettings.CreateDefault();
        var web = settings.Providers.First(p => p.Id == "deepseek-web");
        web.Selectors!.CaptchaText = new List<string> { "验证码" };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, options));

        var service = new JsonSettingsService(new FakeLogger());
        var loaded = service.Load(SettingsPath, TemplatePath, DefaultTemplatePath, out _);
        var loadedWeb = loaded.Providers.First(p => p.Id == "deepseek-web");

        Assert.DoesNotContain("验证码", loadedWeb.Selectors!.CaptchaText);
        Assert.Contains("安全验证", loadedWeb.Selectors!.CaptchaText);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // 测试清理失败不影响结果。
        }
    }
}
