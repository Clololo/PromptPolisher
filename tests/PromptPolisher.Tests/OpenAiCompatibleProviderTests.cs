using System.Net;
using System.Text;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Providers;
using PromptPolisher.Core.Security;
using Xunit;

namespace PromptPolisher.Tests;

public sealed class OpenAiCompatibleProviderTests
{
    private static ProviderDescriptor Descriptor => new()
    {
        Id = "test",
        DisplayName = "Test",
        Kind = ProviderKind.Api,
        BaseUrl = "https://example.test",
        Model = "test-model"
    };

    private static PolisherRequest Request(bool stream = false) => new()
    {
        UserPrompt = "hello",
        ProviderId = "test",
        Stream = stream,
        TimeoutSeconds = 5,
        RetryCount = 0
    };

    private static OpenAiCompatibleProvider CreateProvider(
        HttpMessageHandler handler,
        FakeCredentialStore? credentials = null)
    {
        if (credentials is null)
        {
            credentials = new FakeCredentialStore();
            credentials.Secrets[CredentialKeys.ApiKey("test")] = "test-key";
        }

        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new OpenAiCompatibleProvider(Descriptor, http, credentials, new FakeLogger());
    }

    [Fact]
    public async Task NonStreaming_ParsesMessageContent()
    {
        var handler = new FakeHandler(_ => JsonResponse("""
            {"choices":[{"message":{"content":"polished prompt"}}]}
            """));

        var provider = CreateProvider(handler);
        var result = await provider.PolishAsync(Request(), "system prompt", null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("polished prompt", result.Content);
        Assert.Equal("test", result.ProviderId);
    }

    [Fact]
    public async Task Streaming_ParsesDeltasAndReportsProgress()
    {
        const string sse = """
            data: {"choices":[{"delta":{"content":"he"}}]}

            data: {"choices":[{"delta":{"content":"llo"}}]}

            data: [DONE]

            """;

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        var progress = new CollectingProgress();
        var provider = CreateProvider(handler);
        var result = await provider.PolishAsync(Request(stream: true), "system prompt", progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("hello", result.Content);
        Assert.Equal("hello", progress.Builder.ToString());
    }

    [Fact]
    public async Task Unauthorized_MapsToAuthRequired()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid key\"}", Encoding.UTF8, "application/json")
        });

        var provider = CreateProvider(handler);
        var result = await provider.PolishAsync(Request(), "system prompt", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.AuthRequired, result.ErrorCode);
        Assert.Equal(401, result.HttpStatus);
    }

    [Fact]
    public async Task RateLimited_MapsToRateLimited()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":\"slow down\"}", Encoding.UTF8, "application/json")
        });

        var provider = CreateProvider(handler);
        var result = await provider.PolishAsync(Request(), "system prompt", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.RateLimited, result.ErrorCode);
    }

    [Fact]
    public async Task MissingApiKey_FailsWithoutHttpCall()
    {
        var called = false;
        var handler = new FakeHandler(_ =>
        {
            called = true;
            return JsonResponse("{}");
        });

        var credentials = new FakeCredentialStore();
        var provider = CreateProvider(handler, credentials);
        var result = await provider.PolishAsync(Request(), "system prompt", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.AuthRequired, result.ErrorCode);
        Assert.False(called);
    }

    [Fact]
    public async Task EmptyContent_MapsToEmptyResult()
    {
        var handler = new FakeHandler(_ => JsonResponse("""
            {"choices":[{"message":{"content":""}}]}
            """));

        var provider = CreateProvider(handler);
        var result = await provider.PolishAsync(Request(), "system prompt", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.EmptyResult, result.ErrorCode);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(_factory(request));
    }

    private sealed class CollectingProgress : IProgress<string>
    {
        public StringBuilder Builder { get; } = new();

        public void Report(string value) => Builder.Append(value);
    }
}
