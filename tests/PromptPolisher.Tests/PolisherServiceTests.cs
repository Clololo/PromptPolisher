using PromptPolisher.Core.Models;
using PromptPolisher.Core.Services;
using Xunit;

namespace PromptPolisher.Tests;

public sealed class PolisherServiceTests
{
    private static ProviderDescriptor Descriptor => new()
    {
        Id = "test-provider",
        DisplayName = "Test Provider",
        Kind = ProviderKind.Api,
        BaseUrl = "https://example.test",
        Model = "test-model"
    };

    private static PolisherRequest Request() => new()
    {
        UserPrompt = "写一个登录页",
        ProviderId = "test-provider",
        TimeoutSeconds = 5,
        RetryCount = 1
    };

    [Fact]
    public async Task RetriesOnRetryableFailure_ThenSucceeds()
    {
        var provider = new FakeProvider(
            Descriptor,
            PolisherResult.Fail(ErrorCodes.Timeout, Descriptor.Id),
            PolisherResult.Ok("polished", Descriptor.Id, TimeSpan.FromMilliseconds(10)));
        var history = new FakeHistoryRepository();
        var service = new PolisherService(
            new FakeProviderResolver(provider),
            new PromptTemplateRenderer(),
            history,
            new FakeLogger());

        var result = await service.PolishAsync(
            Request(),
            PromptTemplateRenderer.DefaultTemplate,
            new AppSettings { RetryCount = 1, SaveHistory = true, StorePromptContent = true },
            null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("polished", result.Content);
        Assert.Equal(2, provider.CallCount);
        Assert.Single(history.Entries);
        Assert.Equal(HistoryStatus.Success, history.Entries[0].Status);
    }

    [Fact]
    public async Task InvalidRequest_DoesNotCallProvider()
    {
        var provider = new FakeProvider(Descriptor);
        var service = new PolisherService(
            new FakeProviderResolver(provider),
            new PromptTemplateRenderer(),
            new FakeHistoryRepository(),
            new FakeLogger());

        var request = Request();
        request.UserPrompt = "";

        var result = await service.PolishAsync(
            request,
            PromptTemplateRenderer.DefaultTemplate,
            new AppSettings(),
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidRequest, result.ErrorCode);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task ManualProvider_DoesNotWriteHistory()
    {
        var provider = new FakeProvider(
            Descriptor,
            PolisherResult.Manual("rendered prompt", Descriptor.Id));
        var history = new FakeHistoryRepository();
        var service = new PolisherService(
            new FakeProviderResolver(provider),
            new PromptTemplateRenderer(),
            history,
            new FakeLogger());

        var result = await service.PolishAsync(
            Request(),
            PromptTemplateRenderer.DefaultTemplate,
            new AppSettings { SaveHistory = true },
            null,
            CancellationToken.None);

        Assert.True(result.RequiresManualAction);
        Assert.Equal("rendered prompt", result.Content);
        Assert.Empty(history.Entries);
    }

    [Fact]
    public async Task HistoryCanBeDisabled()
    {
        var provider = new FakeProvider(
            Descriptor,
            PolisherResult.Ok("polished", Descriptor.Id, TimeSpan.Zero));
        var history = new FakeHistoryRepository();
        var service = new PolisherService(
            new FakeProviderResolver(provider),
            new PromptTemplateRenderer(),
            history,
            new FakeLogger());

        await service.PolishAsync(
            Request(),
            PromptTemplateRenderer.DefaultTemplate,
            new AppSettings { SaveHistory = false },
            null,
            CancellationToken.None);

        Assert.Empty(history.Entries);
    }
}
