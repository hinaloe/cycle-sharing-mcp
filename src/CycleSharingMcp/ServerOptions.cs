using System.Globalization;
using CycleSharingMcp.Core;

namespace CycleSharingMcp;

/// <summary>起動引数と環境変数から決まる設定。</summary>
public sealed record ServerOptions(bool Http, string Host, int Port, string? AccessToken)
{
    public const string AccessTokenEnvironmentVariable = "ODPT_ACCESS_TOKEN";

    // record の ToString() は全項目を出すので、トークンだけ伏せる
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"Http = {Http}, Host = {Host}, Port = {Port}, AccessToken = {OdptOptions.Redact(AccessToken)}");
        return true;
    }

    public const string Usage = """
        使い方: cycle-sharing-mcp [--odpt-token <トークン>] [--http [--host <アドレス>] [--port <番号>]]
          --odpt-token  ODPT のアクセストークン（環境変数 ODPT_ACCESS_TOKEN でも可。省略時は公開版を使う）
          --http        stdio ではなく HTTP（Streamable HTTP、/mcp）で待ち受ける
          --host        HTTP の待ち受けアドレス（既定 127.0.0.1）
          --port        HTTP の待ち受けポート（既定 8080）
        """;

    /// <summary>不正な引数は <see cref="ArgumentException"/>。トークンは起動引数が環境変数より優先。</summary>
    public static ServerOptions Parse(IReadOnlyList<string> args, Func<string, string?> getEnv)
    {
        var http = false;
        var host = "127.0.0.1";
        var port = 8080;
        string? token = null;

        for (var i = 0; i < args.Count; i++)
        {
            // --name=value の形も受け付ける
            var (name, inline) = args[i].IndexOf('=') is var eq and > 0 ? (args[i][..eq], args[i][(eq + 1)..]) : (args[i], null);
            switch (name)
            {
                case "--http" when inline is null:
                    http = true;
                    break;
                case "--host":
                    host = Value(name, inline, args, ref i);
                    break;
                case "--port":
                    var raw = Value(name, inline, args, ref i);
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
                        throw new ArgumentException($"--port は 1〜65535 の数値で指定してください: {raw}");
                    break;
                case "--odpt-token":
                    token = Value(name, inline, args, ref i);
                    break;
                default:
                    // 値はトークンかもしれないので、メッセージには = の手前だけを出す
                    throw new ArgumentException($"不明な引数です: {name}");
            }
        }

        token ??= getEnv(AccessTokenEnvironmentVariable) is { Length: > 0 } env ? env : null;
        return new ServerOptions(http, host, port, token);
    }

    private static string Value(string name, string? inline, IReadOnlyList<string> args, ref int i)
    {
        if (inline is not null)
            return inline.Length > 0 ? inline : throw new ArgumentException($"{name} の値を指定してください。");
        if (++i >= args.Count || args[i].Length == 0 || args[i].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{name} の値を指定してください。");
        return args[i];
    }
}
