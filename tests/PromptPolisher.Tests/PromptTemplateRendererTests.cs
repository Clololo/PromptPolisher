using PromptPolisher.Core.Models;
using PromptPolisher.Core.Services;
using Xunit;

namespace PromptPolisher.Tests;

public sealed class PromptTemplateRendererTests
{
    private readonly PromptTemplateRenderer _renderer = new();

    private static PolisherRequest CreateRequest() => new()
    {
        UserPrompt = "帮我写一个登录页面",
        Strength = PolishStrength.Deep,
        Style = PolishStyle.Technical,
        Language = OutputLanguage.Chinese,
        Length = LengthControl.Expand,
        PreserveTerms = "OAuth, JWT",
        Format = OutputFormat.ResultWithNotes,
        PreserveMeaning = true,
        ExtraInstructions = "不要使用第三方 UI 库"
    };

    [Fact]
    public void Render_ReplacesAllKnownPlaceholders()
    {
        var result = _renderer.Render(PromptTemplateRenderer.DefaultTemplate, CreateRequest());

        Assert.DoesNotContain("{{", result);
        Assert.Contains("帮我写一个登录页面", result);
        Assert.Contains("OAuth, JWT", result);
        Assert.Contains("不要使用第三方 UI 库", result);
        Assert.Contains("【修改说明】", result);
    }

    [Fact]
    public void Render_ChangingStrength_ChangesOutput()
    {
        var light = CreateRequest();
        light.Strength = PolishStrength.Light;
        var deep = CreateRequest();
        deep.Strength = PolishStrength.Deep;

        var lightText = _renderer.Render(PromptTemplateRenderer.DefaultTemplate, light);
        var deepText = _renderer.Render(PromptTemplateRenderer.DefaultTemplate, deep);

        Assert.NotEqual(lightText, deepText);
        Assert.Contains("轻度", lightText);
        Assert.Contains("深度", deepText);
    }

    [Fact]
    public void Render_EmptyPreserveTerms_UsesNone()
    {
        var request = CreateRequest();
        request.PreserveTerms = "";

        var result = _renderer.Render(PromptTemplateRenderer.DefaultTemplate, request);

        Assert.Contains("必须保留的术语：无", result);
    }

    [Fact]
    public void TechnicalStyle_DoesNotTurnSoftwareTaskIntoDocumentation()
    {
        var request = CreateRequest();
        request.Style = PolishStyle.Technical;
        request.UserPrompt = "帮我写一个登录页面";

        var result = _renderer.Render(PromptTemplateRenderer.DefaultTemplate, request);

        Assert.Contains("【任务类型锁定】", result);
        Assert.Contains("不得把“写软件/写代码”改成“写文档”", result);
        Assert.Contains("帮我写一个登录页面", result);
    }

    [Fact]
    public void Render_AddsTaskTypeGuard_WhenLegacyTemplateLacksIt()
    {
        var result = _renderer.Render("hello {{user_prompt}}", CreateRequest());

        Assert.StartsWith("【任务类型锁定】", result);
        Assert.Contains("hello 帮我写一个登录页面", result);
    }

    [Fact]
    public void Render_DoesNotDuplicateTaskTypeGuard()
    {
        var result = _renderer.Render(PromptTemplateRenderer.DefaultTemplate, CreateRequest());

        var count = result.Split("【任务类型锁定】").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Validate_DefaultTemplate_Passes()
    {
        var result = _renderer.Validate(PromptTemplateRenderer.DefaultTemplate);

        Assert.True(result.IsValid);
        Assert.True(result.HasUserPrompt);
        Assert.Empty(result.UnknownPlaceholders);
    }

    [Fact]
    public void Validate_UnknownPlaceholder_Fails()
    {
        var result = _renderer.Validate("hello {{user_prompt}} {{unknown_thing}}");

        Assert.False(result.IsValid);
        Assert.Contains("unknown_thing", result.UnknownPlaceholders);
    }

    [Fact]
    public void Validate_MissingUserPrompt_Fails()
    {
        var result = _renderer.Validate("hello {{strength}}");

        Assert.False(result.IsValid);
        Assert.False(result.HasUserPrompt);
        Assert.Contains("user_prompt", result.Message);
    }

    [Fact]
    public void Validate_EmptyTemplate_Fails()
    {
        var result = _renderer.Validate("   ");

        Assert.False(result.IsValid);
    }
}
