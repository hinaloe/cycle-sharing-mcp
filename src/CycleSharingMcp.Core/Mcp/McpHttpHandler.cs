using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CycleSharingMcp.Core.Mcp;

/// <param name="StatusCode">HTTP のステータス。</param>
/// <param name="Json">応答の本文。null なら本文なし（通知への 202）。</param>
public sealed record McpHttpResult(int StatusCode, string? Json);

/// <summary>
/// Streamable HTTP の POST /mcp を、セッションを持たず SSE も使わない最小限の JSON-RPC 2.0 として処理する（Workers 版用）。
/// WASM ではリフレクションによる JSON 変換が使えないので、JsonNode と BikeshareJson（ソース生成）だけを使う。
/// </summary>
public sealed class McpHttpHandler(BikeshareService service)
{
    public const string ServerName = "cycle-sharing-mcp";

    // サーバー版（BikeshareTools）の既定値と同じ。ToolDefinitions の default と一致することをテストで確かめる
    public const int NearbyDefaultLimit = 10;
    public const int NameDefaultLimit = 5;

    /// <summary>新しい順。initialize で要求された版が含まれていればそれを、なければ先頭を返す。</summary>
    public static IReadOnlyList<string> SupportedProtocolVersions { get; } = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    private static readonly string ServerVersion =
        typeof(McpHttpHandler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<McpHttpResult> HandleAsync(string body, CancellationToken ct)
    {
        JsonObject request;
        try
        {
            if (JsonNode.Parse(body) is not JsonObject parsed)
                return Error(400, null, -32600, "JSON-RPC のリクエストはオブジェクトで送ってください（バッチには対応していません）。");
            // JsonObject は中身を遅れて読むので、キーの重複（ArgumentException）はここで表に出しておく
            _ = parsed.Count;
            request = parsed;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return Error(400, null, -32700, "JSON として読めません。");
        }

        var id = request["id"]?.DeepClone();
        if (!IsString(request["jsonrpc"], "2.0") || request["method"] is not JsonValue methodValue
            || methodValue.GetValueKind() != JsonValueKind.String)
            return Error(400, id, -32600, "jsonrpc（\"2.0\"）と method を指定してください。");

        // id のないものは通知なので、本文なしで受け付ける
        if (!request.ContainsKey("id")) return new McpHttpResult(202, null);

        try
        {
            var @params = request["params"] as JsonObject;
            return methodValue.GetValue<string>() switch
            {
                "initialize" => Result(id, Initialize(@params)),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, new JsonObject { ["tools"] = JsonNode.Parse(ToolDefinitions.Json) }),
                "tools/call" => await CallToolAsync(id, @params, ct),
                var method => Error(200, id, -32601, $"メソッド「{method}」には対応していません。"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 例外の型やスタック（WASM ではファイルのパスも含む）は外に出さない
            return Error(500, id, -32603, "内部エラーが発生しました。");
        }
    }

    private static JsonObject Initialize(JsonObject? @params)
    {
        var requested = @params?["protocolVersion"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
        return new JsonObject
        {
            ["protocolVersion"] = requested is not null && SupportedProtocolVersions.Contains(requested) ? requested : SupportedProtocolVersions[0],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = ServerVersion },
        };
    }

    private async Task<McpHttpResult> CallToolAsync(JsonNode? id, JsonObject? @params, CancellationToken ct)
    {
        if (@params?["name"] is not JsonValue nameValue || nameValue.GetValueKind() != JsonValueKind.String)
            return Error(200, id, -32602, "params.name にツール名を指定してください。");

        var args = new ToolArguments(@params["arguments"] as JsonObject);
        try
        {
            string? text = nameValue.GetValue<string>() switch
            {
                "search_ports_nearby" => BikeshareJson.Serialize(await service.SearchNearbyAsync(
                    args.RequiredDouble("latitude"), args.RequiredDouble("longitude"), args.OptionalString("operator"),
                    args.OptionalInt("limit", NearbyDefaultLimit), args.OptionalString("available_for"), ct)),
                "search_ports_by_address" => BikeshareJson.Serialize(await service.SearchByAddressAsync(args.RequiredString("address"), ct)),
                "search_ports_by_name" => BikeshareJson.Serialize(await service.SearchByNameAsync(
                    args.RequiredString("query"), args.OptionalString("operator"), args.OptionalInt("limit", NameDefaultLimit), ct)),
                "get_port_status" => BikeshareJson.Serialize(await service.GetPortStatusAsync(args.RequiredStringList("port_ids"), ct)),
                "get_operator_info" => BikeshareJson.Serialize(await service.GetOperatorInfoAsync(args.OptionalString("operator"), ct)),
                _ => null,
            };
            return text is null
                ? Error(200, id, -32602, $"ツール「{nameValue.GetValue<string>()}」はありません。")
                : Result(id, ToolResult(text, isError: false));
        }
        catch (BikeshareException ex)
        {
            return Result(id, ToolResult(ex.Message, isError: true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // サーバー版（MCP SDK）と同じく、想定外の例外もツールのエラーとして返す。詳細は出さない
            return Result(id, ToolResult("ツールの実行中に内部エラーが発生しました。", isError: true));
        }
    }

    private static JsonObject ToolResult(string text, bool isError)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        };
        if (isError) result["isError"] = true;
        return result;
    }

    private static McpHttpResult Result(JsonNode? id, JsonObject result) =>
        new(200, Write(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }));

    // 不正な HTTP 本文は 400、JSON-RPC として読めたうえでの誤りは 200 で error を返す
    private static McpHttpResult Error(int status, JsonNode? id, int code, string message) =>
        new(status, Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        }));

    private static bool IsString(JsonNode? node, string expected) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == expected;

    private static string Write(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            node.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
