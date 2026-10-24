using PromptPolisher.Core.Models;
using PromptPolisher.Core.Storage;
using Xunit;

namespace PromptPolisher.Tests;

public sealed class HistoryRepositoryTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        "PromptPolisherTests",
        Guid.NewGuid().ToString("N") + ".db");

    private SqliteHistoryRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new SqliteHistoryRepository(_databasePath);
        await _repository.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch
        {
            // 测试清理失败不影响结果。
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task AddAndSearch_RoundTrips()
    {
        var id = await _repository.AddAsync(new HistoryEntry
        {
            ProviderId = "deepseek-api",
            Model = "deepseek-chat",
            ParametersJson = "{}",
            OriginalPrompt = "写一个登录页",
            PolishedPrompt = "请编写一个登录页面",
            Status = HistoryStatus.Success,
            DurationMs = 123
        });

        var results = await _repository.SearchAsync("登录页");

        Assert.True(id > 0);
        Assert.Single(results);
        Assert.Equal("deepseek-api", results[0].ProviderId);
        Assert.Equal(HistoryStatus.Success, results[0].Status);
        Assert.Equal(123, results[0].DurationMs);
    }

    [Fact]
    public async Task Delete_RemovesOnlyTarget()
    {
        var first = await _repository.AddAsync(CreateEntry("first"));
        await _repository.AddAsync(CreateEntry("second"));

        await _repository.DeleteAsync(first);

        var results = await _repository.SearchAsync(null);
        Assert.Single(results);
        Assert.Equal("second", results[0].OriginalPrompt);
    }

    [Fact]
    public async Task Clear_RemovesAll()
    {
        await _repository.AddAsync(CreateEntry("first"));
        await _repository.AddAsync(CreateEntry("second"));

        await _repository.ClearAsync();

        var results = await _repository.SearchAsync(null);
        Assert.Empty(results);
    }

    private static HistoryEntry CreateEntry(string prompt) => new()
    {
        ProviderId = "test",
        Model = "test-model",
        ParametersJson = "{}",
        OriginalPrompt = prompt,
        PolishedPrompt = prompt + "-polished",
        Status = HistoryStatus.Success,
        DurationMs = 1
    };
}
