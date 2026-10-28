using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CycleSharingMcp.Core;

/// <summary>
/// ツール結果の JSON 変換。WASM（Workers 版）ではリフレクションによる変換がトリミングで使えないので、ソース生成を使う。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PortListResponse))]
[JsonSerializable(typeof(PortStatusResponse))]
[JsonSerializable(typeof(OperatorInfoResponse))]
public sealed partial class BikeshareJsonContext : JsonSerializerContext;

public static class BikeshareJson
{
    /// <summary>snake_case、null の項目は出さない、日本語はエスケープしない（トークンを無駄に消費しないため）。</summary>
    public static JsonSerializerOptions Options { get; } = Create(BikeshareJsonContext.Default);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)));

    /// <summary>サーバー版の MCP SDK 用。SDK が自分の型をリフレクションで扱えるよう、既定の解決も足す。</summary>
    public static JsonSerializerOptions CreateServerOptions() =>
        Create(JsonTypeInfoResolver.Combine(BikeshareJsonContext.Default, new DefaultJsonTypeInfoResolver()));

    private static JsonSerializerOptions Create(IJsonTypeInfoResolver resolver) => new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = resolver,
    };
}
