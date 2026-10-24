using Microsoft.Extensions.Time.Testing;

namespace CycleSharingMcp.Tests;

public class FeedCacheTests
{
    private readonly FakeTimeProvider time = new();

    [Fact]
    public async Task Cache_ReusesWithinTtl_ReloadsAfter()
    {
        var cache = new FeedCache<int>(TimeSpan.FromMinutes(1), time);
        var n = 0;
        Task<int> Load(CancellationToken _) => Task.FromResult(++n);

        Assert.Equal(1, await cache.GetAsync(Load, default));
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(1, await cache.GetAsync(Load, default));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(2, await cache.GetAsync(Load, default));
    }

    [Fact]
    public async Task Cache_ConcurrentCallsShareOneLoad()
    {
        var cache = new FeedCache<int>(TimeSpan.FromMinutes(1), time);
        var gate = new TaskCompletionSource<int>();
        var loads = 0;
        Task<int> Load(CancellationToken _)
        {
            loads++;
            return gate.Task;
        }

        var first = cache.GetAsync(Load, default);
        var second = cache.GetAsync(Load, default);
        gate.SetResult(7);

        var results = await Task.WhenAll(first, second);
        Assert.Equal([7, 7], results);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task Cache_FailureIsNotCached()
    {
        var cache = new FeedCache<int>(TimeSpan.FromMinutes(1), time);
        var n = 0;
        Task<int> Load(CancellationToken _) => ++n == 1 ? Task.FromException<int>(new InvalidOperationException("boom")) : Task.FromResult(n);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(Load, default));
        Assert.Equal(2, await cache.GetAsync(Load, default));
    }

    [Fact]
    public async Task Cache_CancelledWaiter_DoesNotCancelSharedLoad()
    {
        var cache = new FeedCache<int>(TimeSpan.FromMinutes(1), time);
        var gate = new TaskCompletionSource<int>();
        using var cts = new CancellationTokenSource();

        var cancelled = cache.GetAsync(_ => gate.Task, cts.Token);
        var other = cache.GetAsync(_ => gate.Task, default);
        cts.Cancel();
        gate.SetResult(3);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(3, await other);
    }
}
