using System.Text.Json.Nodes;
using CycleSharingMcp.Core.Mcp;

namespace CycleSharingMcp.Tests;

public class McpHttpHandlerTests
{
    private readonly BikeshareService service = new(FakeFeedSource.FromFixtures());
    private readonly McpHttpHandler handler;

    public McpHttpHandlerTests() => handler = new McpHttpHandler(service);

    [Fact]
    public async Task Initialize_EchoesSupportedVersion()
    {
        var (status, json) = await Call("initialize", """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}""");

        Assert.Equal(200, status);
        Assert.Equal("2025-06-18", (string?)json["result"]!["protocolVersion"]);
        Assert.Equal("cycle-sharing-mcp", (string?)json["result"]!["serverInfo"]!["name"]);
        Assert.NotNull(json["result"]!["capabilities"]!["tools"]);
        Assert.Equal(1, (int)json["id"]!);
    }

    [Fact]
    public async Task Initialize_UnknownVersion_ReturnsLatest()
    {
        var (_, json) = await Call("initialize", """{"protocolVersion":"1999-01-01"}""");
        Assert.Equal(McpHttpHandler.SupportedProtocolVersions[0], (string?)json["result"]!["protocolVersion"]);
    }

    [Fact]
    public async Task Notification_Returns202WithoutBody()
        => Assert.Equal(new McpHttpResult(202, null),
                        await handler.HandleAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", default));

    [Fact]
    public async Task Ping_ReturnsEmptyObject()
    {
        var (_, json) = await Call("ping", null, id: "\"abc\"");
        Assert.Equal("abc", (string?)json["id"]);
        Assert.Empty(json["result"]!.AsObject());
    }

    [Fact]
    public async Task ToolsList_ReturnsToolDefinitions()
    {
        var (_, json) = await Call("tools/list", null);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ToolDefinitions.Json), json["result"]!["tools"]));
    }

    [Fact]
    public async Task ToolsCall_Name_ReturnsSameJsonAsService()
    {
        var (_, json) = await CallTool("search_ports_by_name", """{"query":"新木場"}""");

        Assert.Null(json["result"]!["isError"]);
        Assert.Equal(BikeshareJson.Serialize(await service.SearchByNameAsync("新木場", null, 5, default)), Text(json));
    }

    [Fact]
    public async Task ToolsCall_StatusWithSingleStringId()
    {
        var (_, json) = await CallTool("get_port_status", """{"port_ids":"docomo:5053"}""");
        Assert.Single(JsonNode.Parse(Text(json))!["ports"]!.AsArray());
    }

    [Fact]
    public async Task ToolsCall_LimitAsString_IsAccepted()
    {
        var (_, json) = await CallTool("search_ports_nearby", """{"latitude":35.6457,"longitude":139.8268,"limit":"2"}""");
        Assert.Equal(2, JsonNode.Parse(Text(json))!["ports"]!.AsArray().Count);
    }

    [Fact]
    public async Task ToolsCall_MissingRequired_IsToolError()
    {
        var (_, json) = await CallTool("search_ports_nearby", """{"latitude":35.6}""");
        Assert.True((bool)json["result"]!["isError"]!);
        Assert.Contains("longitude", Text(json));
    }

    [Theory]
    [InlineData("""{"latitude":"abc","longitude":139.7}""", "latitude")]
    [InlineData("""{"latitude":35.6,"longitude":139.7,"limit":1.5}""", "limit")]
    [InlineData("""{"latitude":35.6,"longitude":139.7,"operator":3}""", "operator")]
    public async Task ToolsCall_WrongType_IsToolError(string arguments, string name)
    {
        var (_, json) = await CallTool("search_ports_nearby", arguments);
        Assert.True((bool)json["result"]!["isError"]!);
        Assert.Contains(name, Text(json));
    }

    [Fact]
    public async Task ToolsCall_ServiceError_IsToolError()
    {
        var (_, json) = await CallTool("search_ports_nearby", """{"latitude":139.7,"longitude":35.6}""");
        Assert.True((bool)json["result"]!["isError"]!);
        Assert.Contains("緯度", Text(json));
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_IsInvalidParams()
    {
        var (_, json) = await CallTool("delete_everything", "{}");
        Assert.Equal(-32602, (int)json["error"]!["code"]!);
    }

    [Theory]
    [InlineData("not json", 400, -32700)]
    [InlineData("[]", 400, -32600)]
    [InlineData("""{"id":1,"method":"ping"}""", 400, -32600)]
    [InlineData("""{"jsonrpc":"2.0","id":1}""", 400, -32600)]
    public async Task BadRequests(string body, int status, int code)
    {
        var result = await handler.HandleAsync(body, default);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, (int)JsonNode.Parse(result.Json!)!["error"]!["code"]!);
    }

    [Fact]
    public async Task DuplicateKeys_AreParseError()
    {
        var result = await handler.HandleAsync("""{"jsonrpc":"2.0","id":1,"id":2,"method":"ping"}""", default);

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(-32700, (int)JsonNode.Parse(result.Json!)!["error"]!["code"]!);
    }

    [Fact]
    public async Task ToolsCall_UnexpectedException_IsToolErrorWithoutDetails()
    {
        // 取得元が想定外の例外を投げる（BikeshareException ではない）
        var broken = FakeFeedSource.FromFixtures();
        broken.Ports.Remove(Operator.Docomo);
        var brokenHandler = new McpHttpHandler(new BikeshareService(broken));

        var result = await brokenHandler.HandleAsync(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search_ports_by_name","arguments":{"query":"新木場","operator":"docomo"}}}""",
            default);

        Assert.Equal(200, result.StatusCode);
        var json = JsonNode.Parse(result.Json!)!;
        Assert.True((bool)json["result"]!["isError"]!);
        Assert.Contains("内部エラー", Text(json));
        Assert.DoesNotContain("KeyNotFound", Text(json)); // 例外の型やスタックは出さない
    }

    [Fact]
    public async Task UnknownMethod_IsMethodNotFound()
    {
        var (_, json) = await Call("resources/list", null, id: "7");
        Assert.Equal(-32601, (int)json["error"]!["code"]!);
        Assert.Equal(7, (int)json["id"]!);
    }

    [Theory]
    [InlineData("search_ports_nearby", McpHttpHandler.NearbyDefaultLimit)]
    [InlineData("search_ports_by_name", McpHttpHandler.NameDefaultLimit)]
    public void Defaults_MatchToolDefinitions(string tool, int expected)
    {
        var definition = JsonNode.Parse(ToolDefinitions.Json)!.AsArray().Single(t => (string?)t!["name"] == tool)!;
        Assert.Equal(expected, (int)definition["inputSchema"]!["properties"]!["limit"]!["default"]!);
    }

    private async Task<(int Status, JsonNode Json)> Call(string method, string? @params, string id = "1")
    {
        var body = $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}"{{(@params is null ? "" : $",\"params\":{@params}")}}}""";
        var result = await handler.HandleAsync(body, default);
        return (result.StatusCode, JsonNode.Parse(result.Json!)!);
    }

    private Task<(int Status, JsonNode Json)> CallTool(string name, string arguments)
        => Call("tools/call", $$"""{"name":"{{name}}","arguments":{{arguments}}}""");

    private static string Text(JsonNode json) => (string)json["result"]!["content"]![0]!["text"]!;
}
