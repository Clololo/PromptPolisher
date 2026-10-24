using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Providers;
using Xunit;

namespace PromptPolisher.Tests;

public sealed class WebChatProviderTests
{
    private static ProviderDescriptor Descriptor => new()
    {
        Id = "web-test",
        DisplayName = "Web Test",
        Kind = ProviderKind.Web,
        RequiresLogin = true,
        ChatUrl = "https://example.test/chat"
    };

    private static PolisherRequest Request(bool consent) => new()
    {
        UserPrompt = "hello",
        ProviderId = "web-test",
        WebAutomationConsent = consent,
        TimeoutSeconds = 10
    };

    [Fact]
    public async Task WithoutConsent_ReturnsComplianceError()
    {
        var host = new FakeWebSessionHost();
        var provider = new WebChatProvider(Descriptor, host, new FakeLogger());

        var result = await provider.PolishAsync(
            Request(consent: false),
            "system prompt",
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.ComplianceConsentRequired, result.ErrorCode);
        Assert.Equal(0, host.SendCount);
    }

    [Fact]
    public async Task NotLoggedIn_ReturnsLoginRequired()
    {
        var host = new FakeWebSessionHost { Status = WebSessionStatus.NotLoggedIn };
        var provider = new WebChatProvider(Descriptor, host, new FakeLogger());

        var result = await provider.PolishAsync(
            Request(consent: true),
            "system prompt",
            null,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.AuthRequired, result.ErrorCode);
        Assert.True(result.RequiresLogin);
        Assert.Equal(0, host.SendCount);
    }

    [Fact]
    public async Task Ready_DelegatesToHost()
    {
        var host = new FakeWebSessionHost
        {
            Status = WebSessionStatus.Ready,
            Response = PolisherResult.Ok("web result", Descriptor.Id, TimeSpan.Zero)
        };
        var provider = new WebChatProvider(Descriptor, host, new FakeLogger());

        var result = await provider.PolishAsync(
            Request(consent: true),
            "system prompt",
            null,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("web result", result.Content);
        Assert.Equal(1, host.SendCount);
        Assert.Equal("system prompt", host.LastPrompt);
    }

    private sealed class FakeWebSessionHost : IWebSessionHost
    {
        public WebSessionStatus Status { get; set; } = WebSessionStatus.Ready;
        public PolisherResult Response { get; set; } =
            PolisherResult.Fail(ErrorCodes.WebAutomationFailed, "web-test");
        public int SendCount { get; private set; }
        public string? LastPrompt { get; private set; }

        public Task<WebSessionStatus> GetStatusAsync(string providerId, CancellationToken cancellationToken) =>
            Task.FromResult(Status);

        public Task<PolisherResult> SendAsync(
            string providerId,
            string renderedPrompt,
            int timeoutSeconds,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            SendCount++;
            LastPrompt = renderedPrompt;
            return Task.FromResult(Response);
        }

        public Task<bool> ShowLoginAsync(string providerId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
