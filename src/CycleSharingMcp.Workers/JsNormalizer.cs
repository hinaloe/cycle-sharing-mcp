using Cloudflare.Workers.Hosting.Interop;

namespace CycleSharingMcp.Workers;

/// <summary>
/// WebAssembly では .NET の NFKC（string.Normalize(FormKC)）が使えないので、ホストの JavaScript の String.prototype.normalize を使う。
/// リクエストの処理中（JsRuntime.Current がある間）にだけ呼べる。
/// </summary>
internal static class JsNormalizer
{
    public static string Nfkc(string value)
    {
        var runtime = JsRuntime.Current;
        using var stringConstructor = runtime.GetGlobal("String");
        using var text = stringConstructor.InvokeAsFunction(null, JsArg.From(value));
        using var normalized = text.Call("normalize", JsArg.From("NFKC"));
        return normalized.AsString();
    }
}
