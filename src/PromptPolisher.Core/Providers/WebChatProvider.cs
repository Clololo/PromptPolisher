using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.Core.Providers;

/// <summary>
/// 网页版适配器：真正的浏览器会话由 App 层的 IWebSessionHost 实现（WebView2）。
/// 这里只做合规检查、登录状态检查和结果映射。
/// </summary>
public sealed class WebChatProvider : IPolisherProvider
{
    private readonly IWebSessionHost _sessionHost;
    private readonly IRedactingLogger _logger;

    public WebChatProvider(
        ProviderDescriptor descriptor,
        IWebSessionHost sessionHost,
        IRedactingLogger logger)
    {
        Descriptor = descriptor;
        _sessionHost = sessionHost;
        _logger = logger;
    }

    public ProviderDescriptor Descriptor { get; }
    public bool RequiresApiKey => false;

    public async Task<PolisherResult> PolishAsync(
        PolisherRequest request,
        string renderedSystemPrompt,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!request.WebAutomationConsent)
        {
            return PolisherResult.Fail(
                ErrorCodes.ComplianceConsentRequired,
                Descriptor.Id);
        }

        var status = await _sessionHost.GetStatusAsync(Descriptor.Id, cancellationToken);
        if (status == WebSessionStatus.NotLoggedIn)
        {
            _logger.Info($"Provider {Descriptor.Id}: web session is not logged in.");
            return PolisherResult.Fail(
                ErrorCodes.AuthRequired,
                Descriptor.Id,
                requiresLogin: true);
        }

        return await _sessionHost.SendAsync(
            Descriptor.Id,
            renderedSystemPrompt,
            request.TimeoutSeconds,
            progress,
            cancellationToken);
    }
}
