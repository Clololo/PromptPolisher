using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Providers;

namespace PromptPolisher.Core.Services;

public static class ProviderFactory
{
    public static IPolisherProvider Create(
        ProviderDescriptor descriptor,
        HttpClient httpClient,
        ICredentialStore credentials,
        IRedactingLogger logger,
        IWebSessionHost webSessionHost)
    {
        return descriptor.Kind switch
        {
            ProviderKind.Api => new OpenAiCompatibleProvider(descriptor, httpClient, credentials, logger),
            ProviderKind.Web => new WebChatProvider(descriptor, webSessionHost, logger),
            ProviderKind.Manual => new ManualProvider(descriptor),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor.Kind), descriptor.Kind, "Unknown provider kind.")
        };
    }
}
