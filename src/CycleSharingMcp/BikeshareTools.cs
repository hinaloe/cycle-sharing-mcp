using System.ComponentModel;
using CycleSharingMcp.Core;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CycleSharingMcp;

[McpServerToolType]
public sealed class BikeshareTools(BikeshareService service)
{
    private const string OperatorDescription = "事業者で絞る。docomo（ドコモ・バイクシェア）か hellocycling（HELLO CYCLING）。省略すると両方";

    private const string PortFieldsNote =
        "capacity（収容数）は目安で、空きの判断には使わず get_port_status の台数を見ること。"
        + "HELLO CYCLING は使えない車両・枠も含む設置上の数で、貸出可能台数と返却可能数の合計より大きいことがある。"
        + "ドコモ・バイクシェアは 0 のことや、その合計と合わないことがある。"
        + "1000 以上（HELLO CYCLING は 1000、ドコモ・バイクシェアは 32767）は、返却台数に上限がないポートを表す。"
        + "is_charging_station（HELLO CYCLING のみ）は電動サイクル（特定小型原動機付自転車）用の充電器があるポートを表し、"
        + "電動アシスト自転車のバッテリーを交換・充電できるという意味ではない。"
        + "region_id（ドコモ・バイクシェアのみ）は地域の番号で、地域名を引くデータはない。";

    [McpServerTool(Name = "search_ports_nearby", ReadOnly = true, Idempotent = true)]
    [Description($"""
        指定した座標から近い順に、ドコモ・バイクシェアと HELLO CYCLING のシェアサイクルのポート（駐輪場）を返す。各ポートに直線距離 distance_m（m）が付く。
        座標は WGS84（世界測地系）の 10 進度で指定する。Google Maps や駅すぱあと（gcs=wgs84）の座標はそのまま使える。
        旧日本測地系の値は東京付近で約 400 m ずれるので使わないこと。
        空き状況（貸出可能台数など）は含まないので、必要なら port_id を get_port_status に渡す。
        {PortFieldsNote}
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
        住所は「東京都台東区台東４丁目３１－１」「東京都台東区台東4-30-6」のように都道府県名から始まる。
        全角・半角、ひらがな・カタカナの違いは無視する。「四丁目」「4丁目」「4-」は同じものとして扱い、
        数字の後の「ー」「−」などハイフンに似た記号も「-」とみなす。丁目以外の漢数字（「三番」など）は算用数字と区別する。
        最大 40 件。一致が 40 件を超えるとエラーになり、そのまま address に使える絞り込み候補を件数付きで示すので、それで検索し直す。
        候補は住所の続きの文字で機械的に分けたもので、町名の区切りとは限らない（同じ表記に揃えた形で示す）。
        空き状況は含まないので、必要なら port_id を get_port_status に渡す。
        {PortFieldsNote}
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
        edit_distance が 0 でないものは、名前が似ているだけの別の場所のことがある（例:「銀座一丁目」で「銀座6丁目」が 1 になる）。
        空き状況は含まないので、必要なら port_id を get_port_status に渡す。
        {PortFieldsNote}
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
        返却台数に上限がないポートでは、num_docks_available が 950〜1000 や 32767 のような大きな値になる。
        空き枠の数として案内せず「返却可能（台数の制限なし）」と案内すること（公式アプリでも「可能」と表示される）。
        is_installed はポートが設置されて稼働しているか（false なら撤去中・設置前）。
        last_reported は状態が報告された日本時間。HELLO CYCLING はポートごとの時刻だが、ドコモ・バイクシェアはデータ全体の更新時刻で全ポート同じ。
        ドコモ・バイクシェアには車種のデータがなく、電動アシストかどうかは分からない。
        HELLO CYCLING は車種ごとの台数も返す。vehicle_types_available は車種ごとの貸出可能台数、
        vehicle_docks_available は返却可能な空き枠を、そこへ返せる車種（vehicle_types）ごとにまとめた数（count）。データは最大 1 分前のもの。
        is_renting / is_returning が false のポート（夜間に閉まる駐輪場の営業時間外など）では、貸出・返却はできない。
        そのときも num_bikes_available / num_docks_available はその場にある実際の台数・空き枠の数が入るので、0 でなくても借りられる・返せると案内しないこと。
        ポート ID は検索ツールの結果の port_id（例: docomo:5053, hellocycling:17）。最大 40 件まとめて指定できる。
        ID が誤っている・見つからない要素には error が入り、他の要素は通常どおり返る。
        """)]
    public Task<PortStatusResponse> GetPortStatus(
        [Description("ポート ID の配列（1〜40 件）。事業者が混ざってよい")] string[] port_ids,
        CancellationToken ct = default)
        => Run(() => service.GetPortStatusAsync(port_ids, ct));

    [McpServerTool(Name = "get_operator_info", ReadOnly = true, Idempotent = true)]
    [Description("""
        事業者の基本情報（サービス名、Web サイト、ポート数。利用規約・プライバシーポリシー・アプリのリンクは配信している事業者のみで、
        ドコモ・バイクシェアは Web サイトだけ）、HELLO CYCLING の車種、データのクレジット表示とライセンスを返す。
        データを利用者に示すときは credit を添えること。
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
