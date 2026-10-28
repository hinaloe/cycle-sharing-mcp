using System.Net;
using System.Text.Json;
using CycleSharingMcp.Core.Gbfs;

namespace CycleSharingMcp.Core;

public sealed record OdptOptions(string? AccessToken)
{
    // record の ToString() は全項目を出すので、設定をログに出してもトークンが出ないよう伏せる
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("AccessToken = ").Append(Redact(AccessToken));
        return true;
    }

    public static string Redact(string? secret) => secret is null ? "(なし)" : "***";
}

/// <summary>ODPT から GBFS フィードを取得し、メモリにキャッシュする。</summary>
public sealed class OdptFeedSource : IFeedSource
{
    public const string HttpClientName = "odpt";

    private static readonly TimeSpan StatusTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InformationTtl = TimeSpan.FromHours(1);

    private readonly HttpClient http;
    private readonly string? accessToken;
    private readonly Dictionary<Operator, FeedCache<IReadOnlyList<Port>>> ports = [];
    private readonly Dictionary<Operator, FeedCache<IReadOnlyDictionary<string, StationStatus>>> statuses = [];
    private readonly Dictionary<Operator, FeedCache<SystemInformation>> systemInformations = [];
    private readonly Dictionary<Operator, FeedCache<IReadOnlyList<VehicleType>>> vehicleTypes = [];

    /// <param name="waitForOthers">他の呼び出しが始めた取得の待ち方（<see cref="FeedCache{T}"/> を参照）。Workers 版が差し込む。</param>
    public OdptFeedSource(HttpClient http, OdptOptions options, TimeProvider time, Func<Task, CancellationToken, Task>? waitForOthers = null)
    {
        this.http = http;
        accessToken = options.AccessToken;
        foreach (var op in OperatorExtensions.All)
        {
            ports[op] = new(InformationTtl, time, waitForOthers);
            statuses[op] = new(StatusTtl, time, waitForOthers);
            systemInformations[op] = new(InformationTtl, time, waitForOthers);
            vehicleTypes[op] = new(InformationTtl, time, waitForOthers);
        }
    }

    public Task<IReadOnlyList<Port>> GetPortsAsync(Operator op, CancellationToken ct) =>
        ports[op].GetAsync(c => FetchAsync(op, "station_information", b => GbfsParser.ParseStationInformation(b, op), c), ct);

    public Task<IReadOnlyDictionary<string, StationStatus>> GetStatusesAsync(Operator op, CancellationToken ct) =>
        statuses[op].GetAsync(c => FetchAsync(op, "station_status", b => GbfsParser.ParseStationStatus(b), c), ct);

    public Task<SystemInformation> GetSystemInformationAsync(Operator op, CancellationToken ct) =>
        systemInformations[op].GetAsync(c => FetchAsync(op, "system_information", b => GbfsParser.ParseSystemInformation(b), c), ct);

    public Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(Operator op, CancellationToken ct) => op == Operator.Docomo
        // ドコモ・バイクシェアは vehicle_types を配信していない
        ? Task.FromResult<IReadOnlyList<VehicleType>>([])
        : vehicleTypes[op].GetAsync(c => FetchAsync(op, "vehicle_types", b => GbfsParser.ParseVehicleTypes(b), c), ct);

    // 例外メッセージに URL を入れない（トークン版はクエリにアクセストークンが載るため）
    private async Task<T> FetchAsync<T>(Operator op, string feed, Func<byte[], T> parse, CancellationToken ct)
    {
        var what = $"ODPT からの{op.DisplayName()}のデータ取得に失敗しました";
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(OdptEndpoints.FeedUri(op, feed, accessToken), ct);
        }
        catch (HttpRequestException)
        {
            throw new BikeshareException($"{what}（接続できません）。");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BikeshareException($"{what}（タイムアウト）。");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var hint = accessToken is not null && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "アクセストークンを確認してください。"
                    : "";
                throw new BikeshareException($"{what}（HTTP {(int)response.StatusCode}）。{hint}");
            }

            var body = await response.Content.ReadAsByteArrayAsync(ct);
            try
            {
                return parse(body);
            }
            catch (JsonException)
            {
                throw new BikeshareException($"{what}（{feed} を解釈できません）。");
            }
        }
    }
}
