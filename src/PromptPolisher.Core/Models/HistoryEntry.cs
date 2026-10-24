namespace PromptPolisher.Core.Models;

public sealed class HistoryEntry
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
    public string OriginalPrompt { get; set; } = string.Empty;
    public string PolishedPrompt { get; set; } = string.Empty;
    public HistoryStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public long DurationMs { get; set; }
}
