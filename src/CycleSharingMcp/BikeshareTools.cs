using System.ComponentModel;
using CycleSharingMcp.Core;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CycleSharingMcp;

[McpServerToolType]
public sealed class BikeshareTools(BikeshareService service)
{
    private const string OperatorDescription = "事業者で絞る。docomo（ドコモ・バイクシェア）か hellocycling（HELLO CYCLING）。省略すると両方";

    private const string CapacityNote =
        "HELLO CYCLING の capacity は設置上の収容数で、使えない車両・枠を含むため、貸出可能台数と返却可能数の合計より大きいことがある。";

    [McpServerTool(Name = "search_ports_nearby", ReadOnly = true, Idempotent = true)]
    [Description($"""
        指定した座標から近い順に、ドコモ・バイクシェアと HELLO CYCLING のシェアサイクルのポート（駐輪場）を返す。各ポートに直線距離 distance_m（m）が付く。
        座標は WGS84（世界測地系）の 10 進度で指定する。Google Maps や駅すぱあと（gcs=wgs84）の座標はそのまま使える。
        旧日本測地系の値は東京付近で約 400 m ずれるので使わないこと。
        空き状況（貸出可能台数など）は含まないので、必要なら port_id を get_port_status に渡す。
        {CapacityNote}
        """)]
    public Task<PortListResponse> SearchPortsNearby(
        [Description("緯度（WGS84、10 進度。例: 35.681236）")] double latitude,
        [Description("経度（WGS84、10 進度。例: 139.767125）")] double longitude,
        [Description(OperatorDescription)] string? @operator = null,
        [Description("返す件数（1〜40）")] int limit = 10,
        CancellationToken ct = default)
        => Run(() => service.SearchNearbyAsync(latitude, longitude, @operator, limit, ct));

    [McpServerTool(Name = "search_ports_by_address", ReadOnly = true, Idempotent = true)]
    [Description($"""
        HELLO CYCLING のポートを住所の部分一致で探す（ドコモ・バイクシェアには住所データがないので対象外。ドコモは search_ports_by_name か search_ports_nearby を使う）。
        全角・半角、ひらがな・カタカナの違いは無視する。漢数字と算用数字（四丁目 / 4丁目）は区別する。
        最大 40 件。一致が 40 件を超えるとエラーになるので、番地などを足して絞り込む。
        空き状況は含まないので、必要なら port_id を get_port_status に渡す。
        {CapacityNote}
        """)]
    public Task<PortListResponse> SearchPortsByAddress(
        [Description("住所の一部（例: 台東区台東４丁目）")] string address,
        CancellationToken ct = default)
        => Run(() => service.SearchByAddressAsync(address, ct));

    [McpServerTool(Name = "search_ports_by_name", ReadOnly = true, Idempotent = true)]
    [Description($"""
        ドコモ・バイクシェアと HELLO CYCLING のポートを名前であいまい検索し、近い順に返す。各ポートに編集距離 edit_distance（0 は完全一致）が付く。
        全角・半角、ひらがな・カタカナの違いと、多少の書き違いは吸収する。読みでは当たらない（「しんきば」で「新木場」は不可）。
        ドコモ・バイクシェアのポート番号（例: H1-57）でも探せる。
        空き状況は含まないので、必要なら port_id を get_port_status に渡す。
        {CapacityNote}
        """)]
    public Task<PortListResponse> SearchPortsByName(
        [Description("ポート名の一部（例: 新木場駅）")] string query,
        [Description(OperatorDescription)] string? @operator = null,
        [Description("返す件数（1〜40）")] int limit = 5,
        CancellationToken ct = default)
        => Run(() => service.SearchByNameAsync(query, @operator, limit, ct));

    [McpServerTool(Name = "get_port_status", ReadOnly = true, Idempotent = true)]
    [Description("""
        ポートのリアルタイム状態（貸出可能台数 num_bikes_available、返却可能数 num_docks_available、貸出・返却ができるか、最終報告時刻）を返す。
        HELLO CYCLING は車種ごとの台数も返す。データは最大 1 分前のもの。
        ポート ID は検索ツールの結果の port_id（例: docomo:5053, hellocycling:17）。最大 40 件まとめて指定できる。
        ID が誤っている・見つからない要素には error が入り、他の要素は通常どおり返る。
        """)]
    public Task<PortStatusResponse> GetPortStatus(
        [Description("ポート ID の配列（1〜40 件）。事業者が混ざってよい")] string[] port_ids,
        CancellationToken ct = default)
        => Run(() => service.GetPortStatusAsync(port_ids, ct));

    [McpServerTool(Name = "get_operator_info", ReadOnly = true, Idempotent = true)]
    [Description("""
        事業者の基本情報（サービス名、Web サイト、利用規約、アプリのリンク、ポート数）、HELLO CYCLING の車種、
        データのクレジット表示とライセンスを返す。データを利用者に示すときは credit を添えること。
        """)]
    public Task<OperatorInfoResponse> GetOperatorInfo(
        [Description(OperatorDescription)] string? @operator = null,
        CancellationToken ct = default)
        => Run(() => service.GetOperatorInfoAsync(@operator, ct));

    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (BikeshareException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
