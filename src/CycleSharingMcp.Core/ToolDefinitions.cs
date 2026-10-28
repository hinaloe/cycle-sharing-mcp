namespace CycleSharingMcp.Core;

/// <summary>
/// サーバー版の tools/list の結果（ツール名・説明文・inputSchema・注釈）を書き出したもの。Workers 版はこれをそのまま返す。
/// サーバー版と食い違わないことはテスト（ToolDefinitionsTests）で確かめる。
/// </summary>
public static class ToolDefinitions
{
    public static string Json { get; } = Load();

    private static string Load()
    {
        using var stream = typeof(ToolDefinitions).Assembly.GetManifestResourceStream("ToolDefinitions.json")
            ?? throw new InvalidOperationException("ToolDefinitions.json が埋め込まれていません。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
