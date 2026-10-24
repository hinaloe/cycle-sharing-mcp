using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CycleSharingMcp;
using CycleSharingMcp.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

ServerOptions options;
try
{
    options = ServerOptions.Parse(args, Environment.GetEnvironmentVariable);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(ServerOptions.Usage);
    return 2;
}

var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    // 日本語を \uXXXX にエスケープするとトークンを無駄に消費するのでそのまま出す
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
};

// ホストのビルダーには args を渡さない（--http のような値のない引数を構成プロバイダーが誤解釈するため）
if (options.Http)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls($"http://{FormatHost(options.Host)}:{options.Port}");
    ConfigureLogging(builder.Logging, stdio: false);
    ConfigureServices(builder.Services, options);
    builder.Services
        .AddMcpServer()
        .WithHttpTransport(o => o.Stateless = true)
        .WithTools<BikeshareTools>(serializerOptions);

    var app = builder.Build();
    LogMode(app.Services, options);
    app.MapMcp("/mcp");
    await app.RunAsync();
}
else
{
    var builder = Host.CreateApplicationBuilder();
    ConfigureLogging(builder.Logging, stdio: true);
    ConfigureServices(builder.Services, options);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<BikeshareTools>(serializerOptions);

    var host = builder.Build();
    LogMode(host.Services, options);
    await host.RunAsync();
}
return 0;

static void ConfigureLogging(ILoggingBuilder logging, bool stdio)
{
    // stdio transport では stdout が JSON-RPC 専用なので、ログはすべて stderr に出す
    if (stdio) logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    logging.SetMinimumLevel(LogLevel.Information);
    // HttpClient のログは要求 URI（トークン版ではクエリにアクセストークンが載る）を出すので止める
    logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
}

static void ConfigureServices(IServiceCollection services, ServerOptions options)
{
    services
        .AddHttpClient(OdptFeedSource.HttpClientName, http =>
        {
            http.Timeout = TimeSpan.FromSeconds(60);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "cycle-sharing-mcp");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            // OdptFeedSource はシングルトンで HttpClient を持ち続けるので、DNS の変更は接続の作り直しで拾う
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
    services.AddSingleton(new OdptOptions(options.AccessToken));
    services.AddSingleton(TimeProvider.System);
    services.AddSingleton<IFeedSource>(sp => new OdptFeedSource(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(OdptFeedSource.HttpClientName),
        sp.GetRequiredService<OdptOptions>(),
        sp.GetRequiredService<TimeProvider>()));
    services.AddSingleton<BikeshareService>();
}

static void LogMode(IServiceProvider services, ServerOptions options)
{
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CycleSharingMcp");
    logger.LogInformation(options.AccessToken is null ? "ODPT 公開版を使用" : "ODPT トークン版を使用");
}

// IPv6 アドレスは URL では [] で囲む
static string FormatHost(string host) => host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
