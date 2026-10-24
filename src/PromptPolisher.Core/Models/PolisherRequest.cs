namespace PromptPolisher.Core.Models;

/// <summary>
/// 一次润色请求。所有敏感信息（API Key、Cookie）都不在这里，走独立的凭据存储。
/// </summary>
public sealed class PolisherRequest
{
    public string UserPrompt { get; set; } = string.Empty;
    public string ProviderId { get; set; } = "deepseek-api";

    public PolishStrength Strength { get; set; } = PolishStrength.Standard;
    public PolishStyle Style { get; set; } = PolishStyle.General;
    public OutputLanguage Language { get; set; } = OutputLanguage.KeepOriginal;
    public LengthControl Length { get; set; } = LengthControl.Keep;
    public OutputFormat Format { get; set; } = OutputFormat.ResultOnly;

    public string PreserveTerms { get; set; } = string.Empty;
    public bool PreserveMeaning { get; set; } = true;
    public string ExtraInstructions { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;
    public int RetryCount { get; set; } = 2;
    public bool Stream { get; set; } = true;

    /// <summary>可选的模型覆盖（例如豆包 Endpoint ID）。为空则使用配置中的模型。</summary>
    public string? ModelOverride { get; set; }

    /// <summary>
    /// 网页版自动化必须显式确认：用户确认平台允许自动化并遵守其条款。
    /// 默认 false，应用不会替用户做合规判断。
    /// </summary>
    public bool WebAutomationConsent { get; set; }

    public bool IsValid(out string error)
    {
        if (string.IsNullOrWhiteSpace(UserPrompt))
        {
            error = "原始 Prompt 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProviderId))
        {
            error = "未选择模型提供方。";
            return false;
        }

        if (TimeoutSeconds is < 5 or > 600)
        {
            error = "超时时间应在 5–600 秒之间。";
            return false;
        }

        if (RetryCount is < 0 or > 5)
        {
            error = "重试次数应在 0–5 之间。";
            return false;
        }

        if (UserPrompt.Length > 200_000)
        {
            error = "原始 Prompt 过长（超过 20 万字符），请拆分后再试。";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
