using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CycleSharingMcp.Core.Mcp;

/// <summary>
/// tools/call の arguments の読み取り。クライアントによって数値が文字列で来ることがあるので、数値として読める文字列も受け付ける。
/// 誤りは <see cref="BikeshareException"/>（ツールのエラーとして日本語で返す）。
/// </summary>
internal sealed class ToolArguments(JsonObject? arguments)
{
    public double RequiredDouble(string name) => Value(name) switch
    {
        null => throw Missing(name),
        var v when v.GetValueKind() == JsonValueKind.Number => v.GetValue<double>(),
        var v when v.GetValueKind() == JsonValueKind.String
            && double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => throw WrongType(name, "数値"),
    };

    public int OptionalInt(string name, int defaultValue) => Value(name) switch
    {
        null => defaultValue,
        var v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var i) => i,
        var v when v.GetValueKind() == JsonValueKind.Number
            && v.GetValue<double>() is var d && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue => (int)d,
        var v when v.GetValueKind() == JsonValueKind.String
            && int.TryParse(v.GetValue<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => throw WrongType(name, "整数"),
    };

    public string RequiredString(string name) => OptionalString(name) ?? throw Missing(name);

    public string? OptionalString(string name) => Value(name) switch
    {
        null => null,
        var v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => throw WrongType(name, "文字列"),
    };

    /// <summary>文字列の配列。文字列が 1 つだけ来たら 1 件の配列として扱う。null の要素はそのまま渡す（要素ごとのエラーになる）。</summary>
    public IReadOnlyList<string> RequiredStringList(string name)
    {
        var node = arguments?[name];
        return node switch
        {
            null => throw Missing(name),
            JsonValue v when v.GetValueKind() == JsonValueKind.String => [v.GetValue<string>()],
            JsonArray a => a.Select(e => e switch
            {
                null => null!,
                JsonValue s when s.GetValueKind() == JsonValueKind.String => s.GetValue<string>(),
                _ => throw WrongType(name, "文字列の配列"),
            }).ToList(),
            _ => throw WrongType(name, "文字列の配列"),
        };
    }

    // null（JSON の null を含む）は省略と同じに扱う
    private JsonValue? Value(string name) => arguments?[name] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() == JsonValueKind.Null => null,
        JsonValue v => v,
        _ => throw WrongType(name, "値"),
    };

    private static BikeshareException Missing(string name) => new($"引数 {name} を指定してください。");

    private static BikeshareException WrongType(string name, string kind) => new($"引数 {name} は{kind}で指定してください。");
}
