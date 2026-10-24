using PromptPolisher.Core.Abstractions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.Tests;

internal sealed class FakeCredentialStore : ICredentialStore
{
    public Dictionary<string, string> Secrets { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Secrets.TryGetValue(key, out var value) ? value : null);

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Secrets[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        Secrets.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class FakeLogger : IRedactingLogger
{
    public List<string> Messages { get; } = new();
    public string LogFilePath => "memory";

    public void Info(string message) => Messages.Add("INFO " + message);
    public void Warn(string message, Exception? exception = null) => Messages.Add("WARN " + message);
    public void Error(string message, Exception? exception = null) => Messages.Add("ERROR " + message);
}

internal sealed class FakeHistoryRepository : IHistoryRepository
{
    public List<HistoryEntry> Entries { get; } = new();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<long> AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        entry.Id = Entries.Count + 1;
        Entries.Add(entry);
        return Task.FromResult(entry.Id);
    }

    public Task<IReadOnlyList<HistoryEntry>> SearchAsync(
        string? query,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HistoryEntry> result = Entries
            .Where(e => string.IsNullOrWhiteSpace(query) ||
                        e.OriginalPrompt.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        e.PolishedPrompt.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        Entries.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        Entries.Clear();
        return Task.CompletedTask;
    }
}

internal sealed class FakeProvider : IPolisherProvider
{
    private readonly Queue<PolisherResult> _results;

    public FakeProvider(ProviderDescriptor descriptor, params PolisherResult[] results)
    {
        Descriptor = descriptor;
        _results = new Queue<PolisherResult>(results);
    }

    public ProviderDescriptor Descriptor { get; }
    public bool RequiresApiKey => false;
    public int CallCount { get; private set; }

    public Task<PolisherResult> PolishAsync(
        PolisherRequest request,
        string renderedSystemPrompt,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        CallCount++;
        var result = _results.Count > 0
            ? _results.Dequeue()
            : PolisherResult.Ok("fallback", Descriptor.Id, TimeSpan.Zero);

        if (result.Success && result.Content is not null)
        {
            progress?.Report(result.Content);
        }

        return Task.FromResult(result);
    }
}

internal sealed class FakeProviderResolver : IProviderResolver
{
    private readonly List<IPolisherProvider> _providers;

    public FakeProviderResolver(params IPolisherProvider[] providers)
    {
        _providers = providers.ToList();
    }

    public IReadOnlyList<ProviderDescriptor> Descriptors =>
        _providers.Select(p => p.Descriptor).ToList();

    public IPolisherProvider? Resolve(string providerId) =>
        _providers.FirstOrDefault(p => p.Descriptor.Id == providerId);
}
