using PromptPolisher.Core.Models;

namespace PromptPolisher.Core.Abstractions;

public interface IPolisherProvider
{
    ProviderDescriptor Descriptor { get; }
    bool RequiresApiKey { get; }

    Task<PolisherResult> PolishAsync(
        PolisherRequest request,
        string renderedSystemPrompt,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}

public interface IProviderResolver
{
    IReadOnlyList<ProviderDescriptor> Descriptors { get; }
    IPolisherProvider? Resolve(string providerId);
}

public interface ICredentialStore
{
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);
    Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default);
    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
}

public interface IHistoryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoryEntry>> SearchAsync(string? query, int limit = 100, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IRedactingLogger
{
    void Info(string message);
    void Warn(string message, Exception? exception = null);
    void Error(string message, Exception? exception = null);
    string LogFilePath { get; }
}

public interface IWebSessionHost
{
    Task<WebSessionStatus> GetStatusAsync(string providerId, CancellationToken cancellationToken);
    Task<PolisherResult> SendAsync(
        string providerId,
        string renderedPrompt,
        int timeoutSeconds,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
    Task<bool> ShowLoginAsync(string providerId, CancellationToken cancellationToken);
}
