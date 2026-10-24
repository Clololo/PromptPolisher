using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;
using PromptPolisher.Core.Security;

namespace PromptPolisher.Core.Providers;

/// <summary>
/// 任意 OpenAI 兼容的 /chat/completions 服务：DeepSeek、火山方舟（豆包）、以及后续扩展。
/// 支持流式和非流式；错误统一映射为 ErrorCodes。
/// </summary>
public class OpenAiCompatibleProvider : IPolisherProvider
{
    private readonly HttpClient _http;
    private readonly ICredentialStore _credentials;
    private readonly IRedactingLogger _logger;

    public OpenAiCompatibleProvider(
        ProviderDescriptor descriptor,
        HttpClient http,
        ICredentialStore credentials,
        IRedactingLogger logger)
    {
        Descriptor = descriptor;
        _http = http;
        _credentials = credentials;
        _logger = logger;
    }

    public ProviderDescriptor Descriptor { get; }
    public bool RequiresApiKey => true;

    public async Task<PolisherResult> PolishAsync(
        PolisherRequest request,
        string renderedSystemPrompt,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(Descriptor.BaseUrl) ||
            string.IsNullOrWhiteSpace(Descriptor.Model) ||
            Descriptor.Model.Contains("请填写", StringComparison.Ordinal))
        {
            return PolisherResult.Fail(ErrorCodes.ConfigError, Descriptor.Id);
        }

        var apiKey = await _credentials.GetSecretAsync(
            CredentialKeys.ApiKey(Descriptor.Id), cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.Warn($"Provider {Descriptor.Id}: API key is not configured.");
            return PolisherResult.Fail(
                ErrorCodes.AuthRequired,
                Descriptor.Id,
                rawError: "API key not configured.",
                duration: stopwatch.Elapsed);
        }

        var model = string.IsNullOrWhiteSpace(request.ModelOverride)
            ? Descriptor.Model!
            : request.ModelOverride!;

        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = renderedSystemPrompt },
                new { role = "user", content = "请严格按照系统要求润色上面的 Prompt。" }
            },
            stream = request.Stream,
            temperature = 0.4
        };

        var json = JsonSerializer.Serialize(payload);
        var url = BuildChatCompletionsUrl(Descriptor.BaseUrl!);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (request.Stream)
        {
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

        try
        {
            _logger.Info($"Provider {Descriptor.Id}: sending request (model={model}, stream={request.Stream}).");

            using var response = await _http.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var body = await ReadBodySnippetAsync(response, timeoutCts.Token);
                stopwatch.Stop();
                _logger.Warn(
                    $"Provider {Descriptor.Id}: HTTP {(int)response.StatusCode}. " +
                    $"Body: {Redaction.Preview(body, 200)}");
                return MapHttpError(response.StatusCode, body, stopwatch.Elapsed);
            }

            var content = request.Stream
                ? await ReadStreamingContentAsync(response, progress, timeoutCts.Token)
                : await ReadNonStreamingContentAsync(response, timeoutCts.Token);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.Warn($"Provider {Descriptor.Id}: empty content.");
                return PolisherResult.Fail(
                    ErrorCodes.EmptyResult,
                    Descriptor.Id,
                    duration: stopwatch.Elapsed);
            }

            _logger.Info(
                $"Provider {Descriptor.Id}: success in {stopwatch.Elapsed.TotalSeconds:F1}s, " +
                $"chars={content.Length}.");
            return PolisherResult.Ok(content.Trim(), Descriptor.Id, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return PolisherResult.Fail(
                ErrorCodes.Cancelled,
                Descriptor.Id,
                duration: stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.Warn($"Provider {Descriptor.Id}: timeout after {request.TimeoutSeconds}s.");
            return PolisherResult.Fail(
                ErrorCodes.Timeout,
                Descriptor.Id,
                duration: stopwatch.Elapsed);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.Warn($"Provider {Descriptor.Id}: network error.", ex);
            return PolisherResult.Fail(
                ErrorCodes.NetworkError,
                Descriptor.Id,
                rawError: Redaction.Redact(ex.Message),
                duration: stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error($"Provider {Descriptor.Id}: unexpected error.", ex);
            return PolisherResult.Fail(
                ErrorCodes.InternalError,
                Descriptor.Id,
                rawError: Redaction.Redact(ex.Message),
                duration: stopwatch.Elapsed);
        }
    }

    private static string BuildChatCompletionsUrl(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + "/chat/completions";
    }

    private static async Task<string> ReadStreamingContentAsync(
        HttpResponseMessage response,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var builder = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            if (data == "[DONE]")
            {
                break;
            }

            string? delta = null;
            try
            {
                using var document = JsonDocument.Parse(data);
                var root = document.RootElement;
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var deltaElement) &&
                        deltaElement.TryGetProperty("content", out var contentElement) &&
                        contentElement.ValueKind == JsonValueKind.String)
                    {
                        delta = contentElement.GetString();
                    }
                    else if (choice.TryGetProperty("message", out var messageElement) &&
                             messageElement.TryGetProperty("content", out var messageContent) &&
                             messageContent.ValueKind == JsonValueKind.String)
                    {
                        delta = messageContent.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // 忽略心跳/非 JSON 行。
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
            {
                builder.Append(delta);
                progress?.Report(delta);
            }
        }

        return builder.ToString();
    }

    private static async Task<string> ReadNonStreamingContentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString() ?? string.Empty;
                }

                if (choice.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // 交给调用方按空结果处理。
        }

        return string.Empty;
    }

    private static async Task<string> ReadBodySnippetAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length <= 2000 ? body : body[..2000];
        }
        catch
        {
            return string.Empty;
        }
    }

    private PolisherResult MapHttpError(HttpStatusCode statusCode, string body, TimeSpan elapsed)
    {
        var code = (int)statusCode;
        var errorCode = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ErrorCodes.AuthRequired,
            HttpStatusCode.TooManyRequests => ErrorCodes.RateLimited,
            >= HttpStatusCode.InternalServerError => ErrorCodes.ProviderError,
            _ => ErrorCodes.ProviderError
        };

        return PolisherResult.Fail(
            errorCode,
            Descriptor.Id,
            rawError: Redaction.Redact(body),
            httpStatus: code,
            duration: elapsed);
    }
}
