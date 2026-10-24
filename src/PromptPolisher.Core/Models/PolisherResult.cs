namespace PromptPolisher.Core.Models;

/// <summary>
/// 统一返回结果。UI 只依赖这个对象，不依赖任何厂商 SDK。
/// </summary>
public sealed record PolisherResult
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public bool RequiresLogin { get; init; }
    public bool RequiresManualAction { get; init; }

    public string? ProviderId { get; init; }
    public int? HttpStatus { get; init; }
    public TimeSpan Duration { get; init; }
    public string? RawError { get; init; }

    public static PolisherResult Ok(string content, string providerId, TimeSpan duration) => new()
    {
        Success = true,
        Content = content,
        ProviderId = providerId,
        Duration = duration
    };

    public static PolisherResult Manual(string renderedPrompt, string providerId) => new()
    {
        Success = false,
        RequiresManualAction = true,
        Content = renderedPrompt,
        ProviderId = providerId,
        ErrorCode = ErrorCodes.ManualActionRequired,
        ErrorMessage = ErrorMessages.Get(ErrorCodes.ManualActionRequired)
    };

    public static PolisherResult Fail(
        string errorCode,
        string providerId,
        string? rawError = null,
        bool requiresLogin = false,
        int? httpStatus = null,
        TimeSpan duration = default) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = ErrorMessages.Get(errorCode),
        ProviderId = providerId,
        RawError = rawError,
        RequiresLogin = requiresLogin,
        HttpStatus = httpStatus,
        Duration = duration
    };
}
