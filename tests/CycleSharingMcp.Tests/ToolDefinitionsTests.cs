using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CycleSharingMcp.Tests;

/// <summary>
/// サーバー版のツール定義（MCP SDK がコードから作る）と、Workers 版が使う ToolDefinitions.json が食い違わないことを確かめる。
/// 説明文や引数を変えたら <c>UPDATE_TOOL_DEFINITIONS=1 mise run test</c> でファイルを書き直す。
/// </summary>
public class ToolDefinitionsTests
{
    [Fact]
    public void ToolDefinitions_MatchServerTools()
    {
        var tools = typeof(BikeshareTools).GetMethods()
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => McpServerTool.Create(m, _ => null!, new McpServerToolCreateOptions
            {
                SerializerOptions = BikeshareJson.CreateServerOptions(),
            }).ProtocolTool)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        var actual = JsonSerializer.SerializeToNode(tools, McpJsonUtilities.DefaultOptions)!;

        if (Environment.GetEnvironmentVariable("UPDATE_TOOL_DEFINITIONS") == "1")
        {
            var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            File.WriteAllText(DefinitionsPath(), JsonSerializer.Serialize(tools, options) + "\n");
            return;
        }

        Assert.True(JsonNode.DeepEquals(actual, JsonNode.Parse(ToolDefinitions.Json)),
            "ToolDefinitions.json がサーバー版のツール定義と食い違っています。UPDATE_TOOL_DEFINITIONS=1 mise run test で更新してください。");
    }

    [Fact]
    public void ToolDefinitions_HaveFiveTools()
        => Assert.Equal(
            ["get_operator_info", "get_port_status", "search_ports_by_address", "search_ports_by_name", "search_ports_nearby"],
            JsonNode.Parse(ToolDefinitions.Json)!.AsArray().Select(t => t!["name"]!.GetValue<string>()));

    private static string DefinitionsPath([CallerFilePath] string testFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", "src", "CycleSharingMcp.Core", "ToolDefinitions.json"));
}
