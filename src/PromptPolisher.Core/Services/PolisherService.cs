using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.Core.Services;

/// <summary>
/// 润色编排：解析提供方 → 渲染系统 Prompt → 调用（含重试）→ 写历史。
/// UI 不直接依赖任何具体 Provider。
/// </summary>
public sealed class PolisherService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IProviderResolver _providers;
    private readonly PromptTemplateRenderer _renderer;
    private readonly IHistoryRepository _history;
    private readonly IRedactingLogger _logger;

    public PolisherService(
        IProviderResolver providers,
        PromptTemplateRenderer renderer,
        IHistoryRepository history,
        IRedactingLogger logger)
    {
        _providers = providers;
        _renderer = renderer;
        _history = history;
        _logger = logger;
    }

    public async Task<PolisherResult> PolishAsync(
        PolisherRequest request,
        string template,
        AppSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!request.IsValid(out var validationError))
        {
            return PolisherResult.Fail(
                ErrorCodes.InvalidRequest,
                request.ProviderId,
                rawError: validationError,
                duration: stopwatch.Elapsed);
        }

        var provider = _providers.Resolve(request.ProviderId);
        if (provider is null)
        {
            return PolisherResult.Fail(
                ErrorCodes.InvalidRequest,
                request.ProviderId,
                rawError: $"Provider '{request.ProviderId}' not found or disabled.",
                duration: stopwatch.Elapsed);
        }

        var validation = _renderer.Validate(template);
        if (!validation.IsValid)
        {
            return PolisherResult.Fail(
                ErrorCodes.ConfigError,
                request.ProviderId,
                rawError: validation.Message,
                duration: stopwatch.Elapsed);
        }

        var renderedPrompt = _renderer.Render(template, request);
        var attempts = Math.Max(1, Math.Min(5, settings.RetryCount + 1));
        PolisherResult? lastResult = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trackingProgress = progress is null ? null : new TrackingProgress(progress);
            lastResult = await provider.PolishAsync(
                request,
                renderedPrompt,
                trackingProgress,
                cancellationToken);

            if (lastResult.Success || lastResult.RequiresManualAction)
            {
                break;
            }

            var retryable = IsRetryable(lastResult.ErrorCode);
            var partialStreamReceived = trackingProgress?.ReceivedAny == true;

            if (!retryable || attempt >= attempts || partialStreamReceived)
            {
                break;
            }

            _logger.Warn(
                $"Provider {request.ProviderId}: attempt {attempt}/{attempts} failed with " +
                $"{lastResult.ErrorCode}; retrying.");

            var delay = TimeSpan.FromMilliseconds(Math.Min(2000, 500 * Math.Pow(2, attempt - 1)));
            await Task.Delay(delay, cancellationToken);
        }

        stopwatch.Stop();
        lastResult ??= PolisherResult.Fail(
            ErrorCodes.InternalError,
            request.ProviderId,
            duration: stopwatch.Elapsed);

        // 用总耗时覆盖单次调用耗时。
        lastResult = lastResult with { Duration = stopwatch.Elapsed };

        if (!lastResult.RequiresManualAction)
        {
            await SaveHistoryAsync(request, provider.Descriptor, lastResult, settings, cancellationToken);
        }

        return lastResult;
    }

    public async Task SaveManualResultAsync(
        PolisherRequest request,
        ProviderDescriptor provider,
        string polished,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var result = PolisherResult.Ok(polished, provider.Id, TimeSpan.Zero);
        await SaveHistoryAsync(request, provider, result, settings, cancellationToken);
    }

    private async Task SaveHistoryAsync(
        PolisherRequest request,
        ProviderDescriptor provider,
        PolisherResult result,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.SaveHistory)
        {
            return;
        }

        try
        {
            var status = result.Success
                ? HistoryStatus.Success
                : result.ErrorCode == ErrorCodes.Cancelled
                    ? HistoryStatus.Cancelled
                    : HistoryStatus.Failed;

            var entry = new HistoryEntry
            {
                CreatedAt = DateTimeOffset.Now,
                ProviderId = request.ProviderId,
                Model = string.IsNullOrWhiteSpace(request.ModelOverride)
                    ? provider.Model ?? string.Empty
                    : request.ModelOverride!,
                ParametersJson = JsonSerializer.Serialize(new
                {
                    request.Strength,
                    request.Style,
                    request.Language,
                    request.Length,
                    request.Format,
                    request.PreserveMeaning,
                    request.PreserveTerms,
                    request.ExtraInstructions,
                    request.Stream,
                    request.TimeoutSeconds,
                    request.RetryCount
                }, JsonOptions),
                OriginalPrompt = settings.StorePromptContent ? request.UserPrompt : "[未保存]",
                PolishedPrompt = settings.StorePromptContent ? result.Content ?? string.Empty : string.Empty,
                Status = status,
                ErrorCode = result.ErrorCode,
                DurationMs = (long)result.Duration.TotalMilliseconds
            };

            await _history.AddAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            // 历史写入失败不应影响主流程。
            _logger.Warn("Failed to write history.", ex);
        }
    }

    private static bool IsRetryable(string? errorCode) => errorCode is
        ErrorCodes.Timeout or
        ErrorCodes.NetworkError or
        ErrorCodes.RateLimited or
        ErrorCodes.ProviderError or
        ErrorCodes.WebAutomationFailed;

    private sealed class TrackingProgress : IProgress<string>
    {
        private readonly IProgress<string> _inner;
        public bool ReceivedAny { get; private set; }

        public TrackingProgress(IProgress<string> inner)
        {
            _inner = inner;
        }

        public void Report(string value)
        {
            ReceivedAny = true;
            _inner.Report(value);
        }
    }
}
