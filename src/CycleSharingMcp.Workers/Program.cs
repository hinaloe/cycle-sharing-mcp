using System.Text.Json.Nodes;
using Cloudflare.Workers.Hosting;
using CycleSharingMcp.Core;
using CycleSharingMcp.Core.Mcp;
using CycleSharingMcp.Workers;

// WebAssembly では .NET の NFKC が使えないので、ホストの JavaScript の normalize を使う
TextNormalizer.Compatibility = JsNormalizer.Nfkc;

var builder = WorkerApplication.CreateBuilder();

builder.Map("/mcp", static async context =>
{
    // セッションを持たないので、POST 以外（GET の SSE ストリーム、DELETE のセッション終了など）には対応しない
    if (context.Request.Method != "POST")
    {
        var notAllowed = HttpResponse.Text("MCP は POST /mcp で JSON-RPC を送ってください。", 405);
        notAllowed.Headers.Replace("Allow", "POST");
        return notAllowed;
    }

    var result = await Services.Mcp.HandleAsync(await context.Request.ReadAsStringAsync(), CancellationToken.None);
    return result.Json is null ? HttpResponse.Accepted() : HttpResponse.Json(result.Json, result.StatusCode);
});

builder.MapGet("/", static _ => Task.FromResult(HttpResponse.Text(
    "cycle-sharing-mcp: ドコモ・バイクシェアと HELLO CYCLING のポートと空き状況を調べる MCP サーバー。エンドポイントは POST /mcp")));

// CI の確認用。SELFTEST 変数が "1" のときだけ、正規化の表をこの Worker の中で計算して返す
builder.MapPost("/_selftest/normalize", static async context =>
{
    if (context.Env.Var("SELFTEST") != "1") return HttpResponse.NotFound();
    var cases = JsonNode.Parse(await context.Request.ReadAsStringAsync())!.AsArray();
    var results = new JsonArray();
    foreach (var c in cases)
    {
        var input = (string)c!["input"]!;
        // 汎用の JsonArray.Add<T> は AOT で使えないので、JsonNode として渡して Add(JsonNode) を使う
        JsonNode normalized = JsonValue.Create((string?)c["kind"] == "address" ? TextNormalizer.NormalizeAddress(input) : TextNormalizer.Normalize(input));
        results.Add(normalized);
    }
    return HttpResponse.Json(results.ToJsonString());
});

builder.MapFallback(static _ => Task.FromResult(HttpResponse.NotFound()));

builder.Build().Run();

/// <summary>isolate の中で 1 つずつ持つ。取得したデータのキャッシュは isolate が作り直されるまで使い回す。</summary>
internal static class Services
{
    private static readonly HttpClient Http = new(new FetchHttpMessageHandler()) { Timeout = Timeout.InfiniteTimeSpan };

    // 誰でも呼べる公開 URL なので、ODPT のアクセストークンは使わず公開版だけを使う
    public static readonly McpHttpHandler Mcp = new(new BikeshareService(
        new OdptFeedSource(Http, new OdptOptions(null), TimeProvider.System, WaitForOthersAsync)));

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 他のリクエストが始めた取得の完了を、自分のタイマーで少しずつ確かめながら待つ。
    /// 別のリクエストの I/O だけを待つと、Workers は「ハング」とみなしてリクエストを打ち切るため。
    /// 取得を始めたリクエストが取り消されて取得が終わらない場合に備えて、待つのは最大 60 秒まで。
    /// </summary>
    private static async Task WaitForOthersAsync(Task load, CancellationToken ct)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (!load.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            if (TimeProvider.System.GetElapsedTime(started) > MaxWait)
                throw new BikeshareException("ODPT からのデータ取得を待ちきれませんでした。少し待ってからやり直してください。");
            await WorkerTimer.Delay(PollInterval);
        }
    }
}
