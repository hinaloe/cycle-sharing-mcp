namespace CycleSharingMcp.Core;

/// <summary>
/// 1 つのフィードの取得結果を TTL の間だけ使い回す。取得中の呼び出しは同じ取得を待つ（HELLO CYCLING の 8 MB を同時に何本も取りに行かない）。
/// 失敗した取得は保持せず、次の呼び出しで取り直す。
/// </summary>
/// <param name="waitForOthers">
/// 他の呼び出しが始めた取得の完了を待つ方法。省略時はそのまま待つ。
/// Cloudflare Workers では、別のリクエストの I/O だけを待つリクエストは「ハング」とみなされて打ち切られるので、
/// 自分のタイマーで少しずつ待つ方法を差し込む。
/// </param>
public sealed class FeedCache<T>(TimeSpan ttl, TimeProvider time, Func<Task, CancellationToken, Task>? waitForOthers = null)
{
    private readonly Lock gate = new();
    private Task<T>? current;
    private DateTimeOffset expiresAt;

    public async Task<T> GetAsync(Func<CancellationToken, Task<T>> load, CancellationToken ct)
    {
        Task<T> task;
        var started = false;
        lock (gate)
        {
            if (current is null || current.IsFaulted || current.IsCanceled
                || (current.IsCompletedSuccessfully && time.GetUtcNow() >= expiresAt))
            {
                current = LoadAsync(load);
                started = true;
            }
            task = current;
        }
        if (!started && !task.IsCompleted && waitForOthers is not null)
            await waitForOthers(task, ct);
        // ct は待つのをやめるためだけに使い、他の呼び出しと共有している取得そのものは止めない
        return await task.WaitAsync(ct);
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
