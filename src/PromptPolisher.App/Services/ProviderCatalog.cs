using System.Net.Http;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Services;

namespace PromptPolisher.App.Services;

/// <summary>
/// 根据当前设置动态创建 Provider。设置变更后调用 Rebuild()。
/// </summary>
public sealed class ProviderCatalog : IProviderResolver
{
    private readonly AppState _state;
    private readonly HttpClient _httpClient;
    private readonly ICredentialStore _credentials;
    private readonly IRedactingLogger _logger;
    private readonly IWebSessionHost _webSessionHost;

    private List<IPolisherProvider> _providers = new();

    public ProviderCatalog(
        AppState state,
        HttpClient httpClient,
        ICredentialStore credentials,
        IRedactingLogger logger,
        IWebSessionHost webSessionHost)
    {
        _state = state;
        _httpClient = httpClient;
        _credentials = credentials;
        _logger = logger;
        _webSessionHost = webSessionHost;
        Rebuild();
    }

    public IReadOnlyList<ProviderDescriptor> Descriptors =>
        _state.Settings.Providers
            .Where(p => p.Enabled)
            .OrderBy(p => p.Kind == ProviderKind.Web ? 0 : p.Kind == ProviderKind.Manual ? 1 : 2)
            .ToList();

    public IPolisherProvider? Resolve(string providerId) =>
        _providers.FirstOrDefault(p => string.Equals(p.Descriptor.Id, providerId, StringComparison.OrdinalIgnoreCase));

    public void Rebuild()
    {
        _providers = _state.Settings.Providers
            .Where(p => p.Enabled)
            .Select(p => ProviderFactory.Create(p, _httpClient, _credentials, _logger, _webSessionHost))
            .ToList();
    }
}
