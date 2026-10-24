namespace PromptPolisher.Core.Models;

public static class ErrorCodes
{
    public const string Ok = "OK";
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string ConfigError = "CONFIG_ERROR";
    public const string AuthRequired = "AUTH_REQUIRED";
    public const string RateLimited = "RATE_LIMITED";
    public const string Timeout = "TIMEOUT";
    public const string NetworkError = "NETWORK_ERROR";
    public const string ProviderError = "PROVIDER_ERROR";
    public const string EmptyResult = "EMPTY_RESULT";
    public const string CaptchaRequired = "CAPTCHA_REQUIRED";
    public const string ComplianceConsentRequired = "COMPLIANCE_CONSENT_REQUIRED";
    public const string WebSelectorNotFound = "WEB_SELECTOR_NOT_FOUND";
    public const string WebAutomationFailed = "WEB_AUTOMATION_FAILED";
    public const string ManualActionRequired = "MANUAL_ACTION_REQUIRED";
    public const string Cancelled = "CANCELLED";
    public const string InternalError = "INTERNAL_ERROR";
}

public static class ErrorMessages
{
    private static readonly Dictionary<string, string> Messages = new(StringComparer.OrdinalIgnoreCase)
    {
        [ErrorCodes.Ok] = "成功。",
        [ErrorCodes.InvalidRequest] = "请求参数不合法，请检查输入后重试。",
        [ErrorCodes.ConfigError] = "配置不完整。请在设置中检查 Base URL、模型名和 API Key。",
        [ErrorCodes.AuthRequired] = "登录已失效或未配置 API Key。请重新登录，或在设置中填写密钥。",
        [ErrorCodes.RateLimited] = "请求过于频繁（限流）。请稍后重试，或降低调用频率。",
        [ErrorCodes.Timeout] = "请求超时。请检查网络/代理，或调大超时时间。",
        [ErrorCodes.NetworkError] = "网络连接失败。请检查网络、代理或防火墙设置。",
        [ErrorCodes.ProviderError] = "模型服务返回错误。请稍后重试；若持续失败，请查看日志中的脱敏错误信息。",
        [ErrorCodes.EmptyResult] = "模型返回了空结果。请重试，或更换模型/提供方。",
        [ErrorCodes.CaptchaRequired] = "检测到验证码或安全验证。应用不会绕过验证，请手动完成验证，或改用官方 API/手动模式。",
        [ErrorCodes.ComplianceConsentRequired] = "网页版自动化需要你确认：该平台允许自动化，且你会遵守其服务条款。未确认时不会发送请求。",
        [ErrorCodes.WebSelectorNotFound] = "网页结构可能已更新，未找到输入框/发送按钮。请更新提供方选择器配置，或改用官方 API/手动模式。",
        [ErrorCodes.WebAutomationFailed] = "网页自动化失败。请重试，或改用官方 API/手动模式。",
        [ErrorCodes.ManualActionRequired] = "请复制生成的 Prompt 到网页版，完成后把结果粘贴回来。",
        [ErrorCodes.Cancelled] = "操作已取消。",
        [ErrorCodes.InternalError] = "应用内部错误。请查看日志。"
    };

    public static string Get(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "未知错误。";
        }

        return Messages.TryGetValue(code, out var message) ? message : $"未知错误：{code}";
    }
}
