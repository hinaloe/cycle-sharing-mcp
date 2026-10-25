namespace CycleSharingMcp.Core;

/// <summary>
/// 1 つのフィードの取得結果を TTL の間だけ使い回す。取得中の呼び出しは同じ取得を待つ（HELLO CYCLING の 8 MB を同時に何本も取りに行かない）。
/// 失敗した取得は保持せず、次の呼び出しで取り直す。
/// </summary>
public sealed class FeedCache<T>(TimeSpan ttl, TimeProvider time)
{
    private readonly Lock gate = new();
    private Task<T>? current;
    private DateTimeOffset expiresAt;

    public Task<T> GetAsync(Func<CancellationToken, Task<T>> load, CancellationToken ct)
    {
        Task<T> task;
        lock (gate)
        {
            if (current is null || current.IsFaulted || current.IsCanceled
                || (current.IsCompletedSuccessfully && time.GetUtcNow() >= expiresAt))
            {
                current = LoadAsync(load);
            }
            task = current;
        }
        // ct は待つのをやめるためだけに使い、他の呼び出しと共有している取得そのものは止めない
        return task.WaitAsync(ct);
    }

    private async Task<T> LoadAsync(Func<CancellationToken, Task<T>> load)
    {
        var value = await load(CancellationToken.None);
        lock (gate)
        {
            // 期限は取得が終わった時刻から数える
            expiresAt = time.GetUtcNow() + ttl;
        }
        return value;
    }
}
