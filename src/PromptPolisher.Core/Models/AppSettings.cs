namespace PromptPolisher.Core.Models;

public sealed class AppSettings
{
    public string DefaultProviderId { get; set; } = "deepseek-web";
    public bool SaveHistory { get; set; } = true;
    public bool StorePromptContent { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 60;
    public int RetryCount { get; set; } = 2;
    public bool StreamByDefault { get; set; } = true;
    public bool WebAutomationConsent { get; set; }

    /// <summary>非空时覆盖 system-prompt.default.txt。</summary>
    public string SystemPromptTemplate { get; set; } = string.Empty;

    public List<ProviderDescriptor> Providers { get; set; } = new();

    public AppSettings Clone() => new()
    {
        DefaultProviderId = DefaultProviderId,
        SaveHistory = SaveHistory,
        StorePromptContent = StorePromptContent,
        RequestTimeoutSeconds = RequestTimeoutSeconds,
        RetryCount = RetryCount,
        StreamByDefault = StreamByDefault,
        WebAutomationConsent = WebAutomationConsent,
        SystemPromptTemplate = SystemPromptTemplate,
        Providers = Providers.Select(p => p.Clone()).ToList()
    };

    public static AppSettings CreateDefault() => new()
    {
        Providers = new List<ProviderDescriptor>
        {
            new()
            {
                Id = "deepseek-api",
                DisplayName = "DeepSeek（官方 API，可选备用）",
                Kind = ProviderKind.Api,
                Enabled = false,
                BaseUrl = "https://api.deepseek.com",
                Model = "deepseek-chat",
                Notes = "可选备用。需要 API Key；免费网页版不可用/被限制时再启用。"
            },
            new()
            {
                Id = "doubao-api",
                DisplayName = "豆包 / 火山方舟（官方 API，可选备用）",
                Kind = ProviderKind.Api,
                Enabled = false,
                BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
                Model = "请填写你的 Endpoint ID",
                Notes = "可选备用。需要火山方舟 Endpoint ID 与 API Key。"
            },
            new()
            {
                Id = "deepseek-web",
                DisplayName = "DeepSeek 网页版（实验性）",
                Kind = ProviderKind.Web,
                Enabled = true,
                RequiresLogin = true,
                LoginUrl = "https://chat.deepseek.com/",
                ChatUrl = "https://chat.deepseek.com/",
                Notes = "网页自动化可能违反平台条款；仅在平台允许时使用。遇验证码会停止。",
                Selectors = new WebSelectors
                {
                    Input = new List<string> { "textarea", "div[contenteditable='true']" },
                    SendButton = new List<string>
                    {
                        "button[type='submit']",
                        "button[aria-label*='发送']",
                        "div[role='button'][aria-label*='发送']"
                    },
                    Response = new List<string> { ".ds-markdown", "[class*='markdown']" },
                    Generating = new List<string> { "button[aria-label*='停止']", "[class*='stop']" },
                    CaptchaText = new List<string> { "安全验证", "人机验证", "拖动滑块", "请完成验证", "captcha" },
                    CaptchaUrl = new List<string> { "captcha", "verify", "risk" },
                    LoginUrl = new List<string> { "login", "signin", "sign-in" },
                    LoginText = new List<string> { "扫码登录", "手机号登录", "登录/注册", "验证码登录" },
                    LoggedIn = new List<string> { "textarea", "div[contenteditable='true']" }
                }
            },
            new()
            {
                Id = "doubao-web",
                DisplayName = "豆包网页版（实验性）",
                Kind = ProviderKind.Web,
                Enabled = true,
                RequiresLogin = true,
                LoginUrl = "https://www.doubao.com/chat/",
                ChatUrl = "https://www.doubao.com/chat/",
                Notes = "网页自动化可能违反平台条款；仅在平台允许时使用。遇验证码会停止。",
                Selectors = new WebSelectors
                {
                    Input = new List<string> { "textarea", "div[contenteditable='true']" },
                    SendButton = new List<string>
                    {
                        "button[type='submit']",
                        "button[aria-label*='发送']",
                        "[data-testid*='send']"
                    },
                    Response = new List<string> { "[class*='markdown']", "[data-testid*='message']" },
                    Generating = new List<string> { "button[aria-label*='停止']", "[class*='stop']" },
                    CaptchaText = new List<string> { "安全验证", "人机验证", "拖动滑块", "请完成验证", "captcha" },
                    CaptchaUrl = new List<string> { "captcha", "verify", "risk" },
                    LoginUrl = new List<string> { "login", "signin", "sign-in" },
                    LoginText = new List<string> { "扫码登录", "手机号登录", "登录/注册", "验证码登录" },
                    LoggedIn = new List<string> { "textarea", "div[contenteditable='true']" }
                }
            },
            new()
            {
                Id = "manual",
                DisplayName = "手动模式（最合规）",
                Kind = ProviderKind.Manual,
                Enabled = true,
                Notes = "应用生成最终 Prompt，你复制到任意网页版，完成后把结果粘贴回来。"
            },
            new()
            {
                Id = "mock-api",
                DisplayName = "本地 Mock（验收用）",
                Kind = ProviderKind.Api,
                Enabled = false,
                BaseUrl = "http://127.0.0.1:8765/v1",
                Model = "mock",
                Notes = "用于本地验收：运行 tools/mock_openai_server.py，启用此提供方并在设置中填任意 Key。"
            }
        }
    };
}
