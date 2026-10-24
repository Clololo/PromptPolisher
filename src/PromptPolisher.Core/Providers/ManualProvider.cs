using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.Core.Providers;

/// <summary>
/// 最合规的兜底：应用只负责生成最终 Prompt，由用户手动复制到任意网页版，
/// 再把结果粘贴回来。不发起任何网页自动化请求。
/// </summary>
public sealed class ManualProvider : IPolisherProvider
{
    public ManualProvider(ProviderDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public ProviderDescriptor Descriptor { get; }
    public bool RequiresApiKey => false;

    public Task<PolisherResult> PolishAsync(
        PolisherRequest request,
        string renderedSystemPrompt,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(PolisherResult.Manual(renderedSystemPrompt, Descriptor.Id));
    }
}
