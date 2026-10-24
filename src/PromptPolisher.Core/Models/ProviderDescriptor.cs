using System.Text.Json.Serialization;

namespace PromptPolisher.Core.Models;

public sealed class ProviderDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ProviderKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public bool RequiresLogin { get; set; }

    /// <summary>OpenAI 兼容 API 的根地址，例如 https://api.deepseek.com。</summary>
    public string? BaseUrl { get; set; }

    /// <summary>模型名或豆包 Endpoint ID。</summary>
    public string? Model { get; set; }

    public string? LoginUrl { get; set; }
    public string? ChatUrl { get; set; }
    public string? Notes { get; set; }

    public WebSelectors? Selectors { get; set; }

    [JsonIgnore]
    public bool IsWeb => Kind == ProviderKind.Web;

    [JsonIgnore]
    public bool IsApi => Kind == ProviderKind.Api;

    [JsonIgnore]
    public bool IsManual => Kind == ProviderKind.Manual;

    [JsonIgnore]
    public string KindText => Kind switch
    {
        ProviderKind.Api => "官方 API",
        ProviderKind.Web => "网页版（实验性）",
        ProviderKind.Manual => "手动模式",
        _ => Kind.ToString()
    };

    public ProviderDescriptor Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Kind = Kind,
        Enabled = Enabled,
        RequiresLogin = RequiresLogin,
        BaseUrl = BaseUrl,
        Model = Model,
        LoginUrl = LoginUrl,
        ChatUrl = ChatUrl,
        Notes = Notes,
        Selectors = Selectors?.Clone()
    };
}

public sealed class WebSelectors
{
    public List<string> Input { get; set; } = new();
    public List<string> SendButton { get; set; } = new();
    public List<string> Response { get; set; } = new();
    public List<string> Generating { get; set; } = new();
    public List<string> CaptchaText { get; set; } = new();
    public List<string> CaptchaUrl { get; set; } = new();
    public List<string> LoginUrl { get; set; } = new();
    public List<string> LoginText { get; set; } = new();
    public List<string> LoggedIn { get; set; } = new();

    public WebSelectors Clone() => new()
    {
        Input = new List<string>(Input),
        SendButton = new List<string>(SendButton),
        Response = new List<string>(Response),
        Generating = new List<string>(Generating),
        CaptchaText = new List<string>(CaptchaText),
        CaptchaUrl = new List<string>(CaptchaUrl),
        LoginUrl = new List<string>(LoginUrl),
        LoginText = new List<string>(LoginText),
        LoggedIn = new List<string>(LoggedIn)
    };
}
