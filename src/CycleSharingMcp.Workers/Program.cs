using System.Text.Json.Nodes;
using Cloudflare.Workers.Hosting;
using CycleSharingMcp.Core;
using CycleSharingMcp.Core.Mcp;
using CycleSharingMcp.Workers;

// WebAssembly では .NET の NFKC が使えないので、ホストの JavaScript の normalize を使う
TextNormalizer.Compatibility = JsNormalizer.Nfkc;

var builder = WorkerApplication.CreateBuilder();

builder.MapPost("/mcp", static async context =>
{
    var result = await Services.Mcp.HandleAsync(await context.Request.ReadAsStringAsync(), CancellationToken.None);
    return result.Json is null ? HttpResponse.Accepted() : HttpResponse.Json(result.Json, result.StatusCode);
});

// セッションを持たないので、GET（SSE のストリーム）と DELETE（セッションの終了）には対応しない
builder.MapGet("/mcp", static _ => Task.FromResult(HttpResponse.Text("MCP は POST /mcp で JSON-RPC を送ってください。", 405)));

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
    public static readonly McpHttpHandler Mcp = new(new BikeshareService(new OdptFeedSource(Http, new OdptOptions(null), TimeProvider.System)));
}
