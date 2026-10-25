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

    // Workers では、他のリクエストが始めた取得をそのまま待つと「ハング」とみなされるので、待ち方を差し込めるようにする
    [Fact]
    public async Task WaitForOthers_IsUsedOnlyWhenWaitingForAnotherCallersLoad()
    {
        var waits = 0;
        Task WaitForOthers(Task load, CancellationToken _)
        {
            waits++;
            return load;
        }

        var cache = new FeedCache<int>(TimeSpan.FromMinutes(1), time, WaitForOthers);
        var gate = new TaskCompletionSource<int>();

        var starter = cache.GetAsync(_ => gate.Task, default);   // 自分で取得を始めた呼び出し: 差し込みは使わない
        var waiter = cache.GetAsync(_ => gate.Task, default);    // 取得中の呼び出しを待つ: 差し込みを使う
        Assert.Equal(1, waits);

        gate.SetResult(5);
        Assert.Equal(5, await starter);
        Assert.Equal(5, await waiter);

        Assert.Equal(5, await cache.GetAsync(_ => gate.Task, default)); // 取得済み: 差し込みは使わない
        Assert.Equal(1, waits);
    }

    [Fact]
    public async Task WaitForOthers_FailureIsPropagated()
    {
        var cache = new FeedCache<int>(TimeSpan.FromMinutes(1), time,
            (_, _) => Task.FromException(new BikeshareException("待ちきれませんでした")));
        var gate = new TaskCompletionSource<int>();

        _ = cache.GetAsync(_ => gate.Task, default);
        var ex = await Assert.ThrowsAsync<BikeshareException>(() => cache.GetAsync(_ => gate.Task, default));
        Assert.Equal("待ちきれませんでした", ex.Message);
    }
}
