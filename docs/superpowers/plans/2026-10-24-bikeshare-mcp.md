# シェアサイクル MCP サーバー 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ODPT の GBFS フィード（ドコモ・バイクシェア全国版・HELLO CYCLING）からポートを絞り込んで検索し、ポート ID からリアルタイム状態を返す MCP サーバー（stdio / HTTP）を作る。

**Architecture:** 実行環境に依存しないコア（クラスライブラリ: モデル・GBFS 解析・正規化・検索・応答組み立て）と、サーバー（実行ファイル: 起動引数・HTTP 取得とキャッシュ・MCP ツール定義・stdio/HTTP 切り替え）の 2 プロジェクト。フィードの取得はコアの `IFeedSource` 越しに行い、将来の Cloudflare Workers 版は取得と MCP の受け口だけを差し替える。

**Tech Stack:** C# / .NET 10（mise で `dotnet = "10.0.402"`）、ModelContextProtocol 2.2.0 / ModelContextProtocol.AspNetCore 2.2.0、Microsoft.Extensions.Hosting / Http 10.0.13、xUnit v3。

**Spec:** `docs/superpowers/specs/2026-10-24-bikeshare-mcp-design.md`

## Global Constraints

- .NET SDK は mise で `10.0.402` に固定。`TargetFramework` は `net10.0`、`Nullable` / `ImplicitUsings` 有効、`InvariantGlobalization` は `false`。
- コア（`src/CycleSharingMcp.Core`）は ASP.NET Core・`Microsoft.Extensions.Hosting`・MCP SDK を参照しない。`Task.Run`・スレッド・`Task.Delay` を使わない。応答は匿名型ではなく record で返す（将来 JSON ソース生成に載せるため）。
- 日時の表示は日本時間 `yyyy-MM-dd HH:mm:ss`。タイムゾーン DB に頼らず固定の `+09:00` で変換する。
- 事業者を表す文字列は `docomo` / `hellocycling`。ポート ID は `{事業者}:{station_id}`（例: `docomo:5053`）。
- 件数の上限は 40（近傍・名前・住所・状態のすべて）。近傍の既定 10、名前の既定 5。
- キャッシュ: `station_status` は 1 分、`system_information` / `station_information` / `vehicle_types` は 1 時間。取得失敗はキャッシュせず、古いキャッシュで代用しない。
- 公開版 `https://api-public.odpt.org/api/v4/gbfs/{system}/{feed}.json`、トークン版 `https://api.odpt.org/api/v4/gbfs/{system}/{feed}.json?acl:consumerKey={token}`。system はドコモ `docomo-cycle`、HELLO CYCLING `hellocycling`。
- トークン: 起動引数 `--odpt-token` が環境変数 `ODPT_ACCESS_TOKEN` より優先。トークンをログ・例外メッセージ・ツール応答に出さない。
- HTTP: `--http` で有効、`--host` 既定 `127.0.0.1`、`--port` 既定 `8080`、エンドポイント `/mcp`、認証なし。stdio 時のログはすべて stderr。
- ツールの JSON 出力は参考実装と同じ設定（snake_case、`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`、null は出さない）。
- ツールの説明文・エラーメッセージ・README は日本語。
- アセンブリ名（実行ファイル名）は `cycle-sharing-mcp`。C# のプロジェクト名・名前空間は `CycleSharingMcp`。

## Review Focus

1. **トークンの漏えい**: `IHttpClientFactory` の既定ログは要求 URI（クエリ付き）を Information で出しうる。トークン付きで起動しても stderr にトークンが出ないこと → Task 7 の検証手順で確認。
2. **ODPT の障害・誤ったトークン**: 403 や接続失敗のとき、トークンを含まない日本語のエラーになり、次の呼び出しで再取得されること → Task 6 のテスト。
3. **緯度と経度の取り違え**: `latitude=139.7, longitude=35.6` のような入力は範囲外としてエラーにし、取り違えを示唆すること → Task 4 のテスト。
4. **ポート ID の表記ゆれ**: 前後の空白・`Docomo:5053` のような大文字は受け付け、`5053` だけ・未知の事業者は要素単位のエラーになること → Task 1 と Task 5 のテスト。
5. **フィードの欠けた項目**: `vehicle_capacity` が数字でない・`address` や `rental_uris` がないポートでも解析が落ちず、該当項目が null になること → Task 3 のテスト。

## ファイル構成

```
mise.toml                      ツール固定とタスク（build / test / run / publish）
.gitignore                     参考実装と同じ
CycleSharingMcp.slnx           ソリューション
README.md / TODO.md            利用者向け説明 / Workers 対応メモ
.github/scripts/smoke_test.py  stdio で initialize と tools/list を確かめる
src/CycleSharingMcp.Core/
  Operator.cs                  Operator 列挙と事業者ごとの定数・文字列変換
  PortId.cs                    ポート ID の解釈と表示
  BikeshareException.cs        利用者向けエラー
  TextNormalizer.cs            検索用の正規化
  FuzzyMatcher.cs              部分文字列編集距離としきい値
  GeoMath.cs                   ハバーサイン距離
  Gbfs/Models.cs               Port / StationStatus / SystemInformation / VehicleType
  Gbfs/GbfsParser.cs           GBFS JSON → モデル
  IFeedSource.cs               フィード取得の抽象
  OdptEndpoints.cs             フィード URL の組み立て
  Views.cs                     ツール応答の record
  BikeshareService.cs          検索・状態・事業者情報
src/CycleSharingMcp/
  CycleSharingMcp.csproj       AssemblyName cycle-sharing-mcp
  ServerOptions.cs             起動引数と環境変数の解釈
  FeedCache.cs                 TTL 付き・同時取得をまとめるキャッシュ
  OdptFeedSource.cs            HttpClient + FeedCache による IFeedSource 実装
  BikeshareTools.cs            MCP ツール定義
  Program.cs                   stdio / HTTP の起動
tests/CycleSharingMcp.Tests/
  Fixtures/*.json              GBFS の縮小版
  FakeFeedSource.cs            テスト用 IFeedSource
  *Tests.cs
```

---

### Task 1: リポジトリの骨組みと事業者・ポート ID

**Files:**
- Create: `mise.toml`, `.gitignore`, `CycleSharingMcp.slnx`, `src/CycleSharingMcp.Core/CycleSharingMcp.Core.csproj`, `src/CycleSharingMcp.Core/Operator.cs`, `src/CycleSharingMcp.Core/PortId.cs`, `src/CycleSharingMcp.Core/BikeshareException.cs`, `tests/CycleSharingMcp.Tests/CycleSharingMcp.Tests.csproj`
- Test: `tests/CycleSharingMcp.Tests/PortIdTests.cs`

**Interfaces:**
- Produces:
  - `public enum Operator { Docomo, HelloCycling }`
  - `public static class OperatorExtensions`: `string Key(this Operator)`（`"docomo"` / `"hellocycling"`）、`string SystemId(this Operator)`（`"docomo-cycle"` / `"hellocycling"`）、`string DisplayName(this Operator)`（`"ドコモ・バイクシェア"` / `"HELLO CYCLING"`）、`static Operator? ParseOptional(string? value)`（null・空白なら null、大文字小文字無視、未知の値は `BikeshareException`）、`static IReadOnlyList<Operator> All`
  - `public readonly record struct PortId(Operator Operator, string StationId)`: `static PortId Parse(string value)`（失敗は `BikeshareException`）、`static bool TryParse(string? value, out PortId id)`、`ToString()` は `"docomo:5053"`
  - `public sealed class BikeshareException(string message) : Exception(message)`

- [ ] **Step 1: 骨組みを作る**

`mise.toml` は参考実装（`~/ghq/github.com/hiyuki2578/ekispert-mcp/mise.toml`）と同じ形で `[tools] dotnet = "10.0.402"` と、タスク `build`（`dotnet build -c Release`）、`test`（`dotnet test`）、`run`（`dotnet run --project src/CycleSharingMcp -c Release --no-launch-profile`）、`publish`（`dotnet publish src/CycleSharingMcp -c Release --use-current-runtime --self-contained -p:PublishSingleFile=true -o dist`）。`.gitignore` は参考実装をコピー。テストプロジェクトは `xunit.v3` 4.0.2、`xunit.runner.visualstudio` 4.0.1、`Microsoft.NET.Test.Sdk` 18.11.0、`Microsoft.Extensions.TimeProvider.Testing` 10.11.0 を参照し、`Fixtures/**` を出力にコピーする。サーバープロジェクトは Task 6 で足す。

- [ ] **Step 2: 失敗するテストを書く**

```csharp
[Theory]
[InlineData("docomo:5053", Operator.Docomo, "5053")]
[InlineData("hellocycling:17", Operator.HelloCycling, "17")]
[InlineData("  Docomo:5053 ", Operator.Docomo, "5053")]
public void Parse_AcceptsValidIds(string input, Operator op, string station)
    => Assert.Equal(new PortId(op, station), PortId.Parse(input));

[Theory]
[InlineData("5053")] [InlineData("hello:17")] [InlineData("docomo:")] [InlineData("")]
public void Parse_RejectsInvalidIds(string input)
{
    var ex = Assert.Throws<BikeshareException>(() => PortId.Parse(input));
    Assert.Contains("docomo:5053", ex.Message); // 正しい形式の例を示す
}

[Fact] public void ToString_RoundTrips() => Assert.Equal("hellocycling:17", PortId.Parse("hellocycling:17").ToString());

[Fact] public void ParseOptional_NullMeansAll() => Assert.Null(OperatorExtensions.ParseOptional(null));
[Fact] public void ParseOptional_Unknown_Throws() => Assert.Throws<BikeshareException>(() => OperatorExtensions.ParseOptional("luup"));
```

- [ ] **Step 3: テストが失敗することを確かめる**

Run: `mise run test`
Expected: コンパイルエラー（`PortId` 未定義）

- [ ] **Step 4: `Operator.cs` / `PortId.cs` / `BikeshareException.cs` を実装する**

- [ ] **Step 5: テストが通ることを確かめる**

Run: `mise run test`
Expected: PASS

- [ ] **Step 6: コミット**

```bash
git add -A && git commit -m "feat: プロジェクトの骨組みと事業者・ポート ID"
```

### Task 2: 正規化とあいまい一致

**Files:**
- Create: `src/CycleSharingMcp.Core/TextNormalizer.cs`, `src/CycleSharingMcp.Core/FuzzyMatcher.cs`
- Test: `tests/CycleSharingMcp.Tests/TextNormalizerTests.cs`, `tests/CycleSharingMcp.Tests/FuzzyMatcherTests.cs`

**Interfaces:**
- Produces:
  - `public static class TextNormalizer { public static string Normalize(string value); }` — NFKC → 空白（`char.IsWhiteSpace`）を除去 → カタカナ（U+30A1〜U+30F6）をひらがなへ（-0x60）→ `ToLowerInvariant`
  - `public static class FuzzyMatcher { public static int SubstringEditDistance(string pattern, string text); public static int MaxDistance(int patternLength); }` — どちらも正規化済みの文字列を受け取る前提

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void Normalize_FoldsWidthKanaCaseAndSpace()
    => Assert.Equal("しぶやport", TextNormalizer.Normalize("ｼﾌﾞﾔ　Ｐｏｒｔ"));
[Fact] public void Normalize_FullWidthDigits()
    => Assert.Equal("台東4丁目31-1", TextNormalizer.Normalize("台東４丁目３１－１"));

[Theory]
[InlineData("新木場", "h1-57.新木場駅", 0)]
[InlineData("新木馬", "h1-57.新木場駅", 1)]
[InlineData("h1-57", "h1-57.新木場駅", 0)]
[InlineData("abc", "", 3)]
[InlineData("", "abc", 0)]
[InlineData("新木場駅前ポート東口", "新木場駅", 6)] // 検索語が名前より長くても落ちない
public void SubstringEditDistance_Cases(string p, string t, int expected)
    => Assert.Equal(expected, FuzzyMatcher.SubstringEditDistance(p, t));

[Theory]
[InlineData(1, 0)] [InlineData(2, 0)] [InlineData(3, 1)] [InlineData(7, 1)] [InlineData(8, 2)] [InlineData(12, 3)]
public void MaxDistance_Cases(int len, int expected) => Assert.Equal(expected, FuzzyMatcher.MaxDistance(len));
```

（Normalize の 2 つめは `－`（U+FF0D）が NFKC で `-` になることを前提にしている。実際の出力が違えば期待値ではなく実装の前処理を見直すこと。）

- [ ] **Step 2: テストが失敗することを確かめる**

Run: `mise run test`
Expected: コンパイルエラー

- [ ] **Step 3: 実装する**

`MaxDistance(len) = len <= 2 ? 0 : Math.Max(1, len / 4)`。
`SubstringEditDistance` は Sellers のアルゴリズム（1 行分の DP 配列を使い回す）:

```csharp
// prev[i] = pattern[..i] と「text のどこかで終わる部分文字列」の最小編集距離
// 初期化: prev[i] = i（text の前に何もない）。各 text 文字ごとに cur[0] = 0（部分文字列はどこからでも始められる）
// cur[i] = min(prev[i-1] + (pattern[i-1]==t ? 0 : 1), prev[i] + 1, cur[i-1] + 1)
// 答え: 全列を通した prev[pattern.Length] の最小値（text が空なら pattern.Length）
```

- [ ] **Step 4: テストが通ることを確かめる**

Run: `mise run test`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add -A && git commit -m "feat: 検索用の正規化と部分文字列編集距離"
```

### Task 3: GBFS のモデルと解析

**Files:**
- Create: `src/CycleSharingMcp.Core/Gbfs/Models.cs`, `src/CycleSharingMcp.Core/Gbfs/GbfsParser.cs`, `tests/CycleSharingMcp.Tests/Fixtures/docomo_station_information.json`, `.../docomo_station_status.json`, `.../docomo_system_information.json`, `.../hellocycling_station_information.json`, `.../hellocycling_station_status.json`, `.../hellocycling_system_information.json`, `.../hellocycling_vehicle_types.json`
- Test: `tests/CycleSharingMcp.Tests/GbfsParserTests.cs`

**Interfaces:**
- Consumes: `Operator`, `PortId`, `TextNormalizer.Normalize`
- Produces（すべて `CycleSharingMcp.Core.Gbfs`）:
  - `public sealed record Port(PortId Id, string Name, double Lat, double Lon, int? Capacity, string? RegionId, string? Address, bool? IsChargingStation, string? ParkingType, string? RentalUrl) { public string SearchName { get; init; } public string? SearchAddress { get; init; } }` — `SearchName` / `SearchAddress` は解析時に `TextNormalizer.Normalize` 済みの値を入れる
  - `public sealed record StationStatus(string StationId, int? NumBikesAvailable, int? NumDocksAvailable, bool? IsInstalled, bool? IsRenting, bool? IsReturning, long? LastReported, IReadOnlyList<VehicleTypeCount> VehicleTypesAvailable, IReadOnlyList<VehicleDockCount> VehicleDocksAvailable)`
  - `public sealed record VehicleTypeCount(string VehicleTypeId, int Count)`、`public sealed record VehicleDockCount(IReadOnlyList<string> VehicleTypeIds, int Count)`
  - `public sealed record SystemInformation(string? Name, string? ShortName, string? Operator, string? Url, string? TermsUrl, string? PrivacyUrl, string? LicenseUrl, string? IosStoreUri, string? AndroidStoreUri)`
  - `public sealed record VehicleType(string VehicleTypeId, string? Name, string? FormFactor, string? PropulsionType, int? MaxRangeMeters)`
  - `public static class GbfsParser`: `IReadOnlyList<Port> ParseStationInformation(ReadOnlySpan<byte> utf8Json, Operator op)`、`IReadOnlyDictionary<string, StationStatus> ParseStationStatus(ReadOnlySpan<byte> utf8Json)`、`SystemInformation ParseSystemInformation(ReadOnlySpan<byte> utf8Json)`、`IReadOnlyList<VehicleType> ParseVehicleTypes(ReadOnlySpan<byte> utf8Json)`

対応: ドコモは `capacity`（数値）→ `Capacity`、`region_id` → `RegionId`。HELLO CYCLING は `vehicle_capacity`（文字列）を整数にして `Capacity`（数字でなければ null）、`address`、`is_charging_station`、`parking_type`、`rental_uris.web` → `RentalUrl`。`system_information.rental_apps.ios.store_uri` / `.android.store_uri`。欠けた項目は null（配列は空）。解析手段（`JsonDocument` か DTO + `JsonSerializer`）は任せる。

- [ ] **Step 1: フィクスチャを書く**

実フィードと同じ形（`{"ttl":60,"data":{...},"version":"2.3","last_updated":...}`）で、次の内容だけを入れる。

ドコモ `station_information`:
| station_id | name | lat | lon | capacity | region_id |
|---|---|---|---|---|---|
| 5053 | M05.塩見縄手 | 35.478819 | 133.04884 | 6 | 2 |
| 1131 | T2-54.土支田農業公園 | 35.760939 | 139.609264 | 7 | 9 |
| 100 | H1-57.新木場駅 | 35.6457 | 139.8268 | 20 | 1 |

ドコモ `station_status`: 5053 → bikes 4, docks 2, installed/renting/returning true, last_reported 1792839276。

HELLO CYCLING `station_information`:
| station_id | name | address | lat | lon | vehicle_capacity | その他 |
|---|---|---|---|---|---|---|
| 17 | 新御徒町ステーション | 東京都台東区台東４丁目３１－１ | 35.707252 | 139.777587 | "8" | is_charging_station false, parking_type street_parking, rental_uris.web `https://www.hellocycling.jp/app/port/detail/17?referrer=odpt` |
| 200 | 新木場駅前 | 東京都江東区新木場１丁目５－１ | 35.6460 | 139.8270 | "10" | 同上の形 |
| 300 | シブヤ公園ポート | （address なし） | 35.6640 | 139.7010 | "不明" | rental_uris なし |

HELLO CYCLING `station_status`: 17 → bikes 2, docks 6, installed/renting/returning true, last_reported 1792839002, `vehicle_types_available: [{"count":2,"vehicle_type_id":"2"}]`, `vehicle_docks_available: [{"count":6,"vehicle_type_ids":["2"]}]`。

`vehicle_types`: `{"vehicle_type_id":"2","name":"hellocycling bicycle","form_factor":"bicycle","propulsion_type":"electric_assist","max_range_meters":70000}`。
`system_information` はそれぞれ実フィード（`https://api-public.odpt.org/api/v4/gbfs/{system}/system_information.json`）をそのまま保存したもの。

- [ ] **Step 2: 失敗するテストを書く**

```csharp
[Fact] public void Docomo_StationInformation()
{
    var ports = GbfsParser.ParseStationInformation(Fixture("docomo_station_information.json"), Operator.Docomo);
    Assert.Equal(3, ports.Count);
    var p = ports.Single(x => x.Id.StationId == "5053");
    Assert.Equal((new PortId(Operator.Docomo, "5053"), "M05.塩見縄手", 6, "2", (string?)null),
                 (p.Id, p.Name, p.Capacity, p.RegionId, p.Address));
    Assert.Equal("m05.塩見縄手", p.SearchName);
}

[Fact] public void Hello_StationInformation_ReadsExtrasAndToleratesMissing()
{
    var ports = GbfsParser.ParseStationInformation(Fixture("hellocycling_station_information.json"), Operator.HelloCycling);
    var p17 = ports.Single(x => x.Id.StationId == "17");
    Assert.Equal(8, p17.Capacity);
    Assert.Equal("東京都台東区台東４丁目３１－１", p17.Address);
    Assert.Equal("https://www.hellocycling.jp/app/port/detail/17?referrer=odpt", p17.RentalUrl);
    var p300 = ports.Single(x => x.Id.StationId == "300");
    Assert.Null(p300.Capacity); Assert.Null(p300.Address); Assert.Null(p300.RentalUrl);
}

[Fact] public void Hello_StationStatus_ReadsVehicleCounts()
{
    var s = GbfsParser.ParseStationStatus(Fixture("hellocycling_station_status.json"))["17"];
    Assert.Equal((2, 6, 1792839002L), (s.NumBikesAvailable, s.NumDocksAvailable, s.LastReported));
    Assert.Equal(new VehicleTypeCount("2", 2), Assert.Single(s.VehicleTypesAvailable));
    Assert.Equal(6, Assert.Single(s.VehicleDocksAvailable).Count);
}

[Fact] public void VehicleTypes_And_SystemInformation()
{
    Assert.Equal("electric_assist", Assert.Single(GbfsParser.ParseVehicleTypes(Fixture("hellocycling_vehicle_types.json"))).PropulsionType);
    Assert.Equal("HELLO CYCLING", GbfsParser.ParseSystemInformation(Fixture("hellocycling_system_information.json")).ShortName);
}
```

（`Fixture(name)` は `File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))` を返すテスト用ヘルパー。）

- [ ] **Step 3: テストが失敗することを確かめる**

Run: `mise run test`
Expected: コンパイルエラー

- [ ] **Step 4: `Models.cs` / `GbfsParser.cs` を実装する**

- [ ] **Step 5: テストが通ることを確かめる**

Run: `mise run test`
Expected: PASS

- [ ] **Step 6: コミット**

```bash
git add -A && git commit -m "feat: GBFS フィードの解析"
```

### Task 4: 検索（近傍・住所・名前）

**Files:**
- Create: `src/CycleSharingMcp.Core/GeoMath.cs`, `src/CycleSharingMcp.Core/IFeedSource.cs`, `src/CycleSharingMcp.Core/Views.cs`, `src/CycleSharingMcp.Core/BikeshareService.cs`, `tests/CycleSharingMcp.Tests/FakeFeedSource.cs`
- Test: `tests/CycleSharingMcp.Tests/GeoMathTests.cs`, `tests/CycleSharingMcp.Tests/SearchTests.cs`

**Interfaces:**
- Consumes: Task 1〜3 のすべて
- Produces:
  - `public static class GeoMath { public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2); }` — ハバーサイン、地球半径 6371008.8 m
  - `public interface IFeedSource { Task<IReadOnlyList<Port>> GetPortsAsync(Operator op, CancellationToken ct); Task<IReadOnlyDictionary<string, StationStatus>> GetStatusesAsync(Operator op, CancellationToken ct); Task<SystemInformation> GetSystemInformationAsync(Operator op, CancellationToken ct); Task<IReadOnlyList<VehicleType>> GetVehicleTypesAsync(Operator op, CancellationToken ct); }` — ドコモの `GetVehicleTypesAsync` は空リスト
  - `Views.cs`:
    - `public sealed record PortView(string PortId, string Operator, string Name, double Lat, double Lon, int? Capacity, string? RegionId, string? Address, bool? IsChargingStation, string? ParkingType, string? RentalUrl, int? DistanceM = null, int? EditDistance = null)` と `static PortView From(Port p, int? distanceM = null, int? editDistance = null)`（`Operator` は `Key()`）
    - `public sealed record PortListResponse(IReadOnlyList<PortView> Ports, string? Note)`
  - `public sealed class BikeshareService(IFeedSource feeds)`:
    - `Task<PortListResponse> SearchNearbyAsync(double latitude, double longitude, string? @operator, int limit, CancellationToken ct)`
    - `Task<PortListResponse> SearchByAddressAsync(string address, CancellationToken ct)`
    - `Task<PortListResponse> SearchByNameAsync(string query, string? @operator, int limit, CancellationToken ct)`
    - `public const int MaxResults = 40;`
  - `FakeFeedSource`（テスト用）: 事業者ごとの `List<Port>` / 状態 / システム情報 / 車種を持ち、`Dictionary<(Operator, string feed), int> Calls` で呼ばれた回数を数える（feed はメソッドに対応するフィード名 `station_information` / `station_status` / `system_information` / `vehicle_types`）。`Port` を作るテスト用ヘルパー `FakeFeedSource.MakePort(Operator op, string id, string name, double lat, double lon, string? address = null)` を持つ（`SearchName` 等も埋める）

仕様（設計書から）:
- 近傍: `latitude` は -90〜90、`longitude` は -180〜180、外れたらエラー。`limit` は 1〜40。距離の昇順、`DistanceM` は四捨五入した整数。
- 住所: HELLO CYCLING だけを対象に、`SearchAddress` が `Normalize(address)` を含むものを集める。空白だけの入力はエラー。40 件超はエラー（件数を含める）。0 件は空と note。並びは元の `Address` の序数順。
- 名前: `Normalize(query)` が空ならエラー。`SubstringEditDistance(q, SearchName) <= MaxDistance(q.Length)` のものを、距離 → 名前の長さ → ポート ID の序数順で並べ、`limit`（1〜40）件。0 件は空と note。
- 対象外の事業者のフィードは取得しない。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void Distance_TokyoToShinjuku()
    => Assert.InRange(GeoMath.DistanceMeters(35.681236, 139.767125, 35.690921, 139.700258), 6100, 6170);

// SearchTests: FakeFeedSource にドコモ「H1-57.新木場駅」(35.6457,139.8268)、ドコモ「M05.塩見縄手」(35.478819,133.04884)、
// HELLO「新木場駅前」(35.6460,139.8270, 東京都江東区新木場１丁目５－１)、HELLO「新御徒町ステーション」(35.707252,139.777587, 東京都台東区台東４丁目３１－１)、
// HELLO「シブヤ公園ポート」(35.6640,139.7010, address なし) を入れる

[Fact] public async Task Nearby_SortsByDistanceAcrossOperators()
{
    var r = await svc.SearchNearbyAsync(35.6457, 139.8268, null, 2, default);
    Assert.Equal(["docomo:100", "hellocycling:200"], r.Ports.Select(p => p.PortId));
    Assert.Equal(0, r.Ports[0].DistanceM);
}
[Fact] public async Task Nearby_OperatorFilter_DoesNotFetchOther()
{
    await svc.SearchNearbyAsync(35.6457, 139.8268, "hellocycling", 10, default);
    Assert.False(fake.Calls.ContainsKey((Operator.Docomo, "station_information")));
}
[Theory] [InlineData(0)] [InlineData(41)]
public async Task Nearby_LimitOutOfRange_Throws(int limit)
    => await Assert.ThrowsAsync<BikeshareException>(() => svc.SearchNearbyAsync(35.6, 139.7, null, limit, default));
[Fact] public async Task Nearby_SwappedLatLon_Throws()
{
    var ex = await Assert.ThrowsAsync<BikeshareException>(() => svc.SearchNearbyAsync(139.7, 35.6, null, 10, default));
    Assert.Contains("緯度", ex.Message); // 取り違えを示唆する文面
}

[Theory] [InlineData("台東４丁目")] [InlineData("台東4丁目")] [InlineData("たいとう")] // 最後は 0 件
public async Task Address_PartialMatch(string q) { /* "たいとう" は Ports 空・Note 非 null、他は hellocycling:17 のみ */ }
[Fact] public async Task Address_Over40_Throws()
{
    // 41 件の HELLO ポート（住所「東京都千代田区丸の内{i}」）を入れた別の FakeFeedSource で
    var ex = await Assert.ThrowsAsync<BikeshareException>(() => svc41.SearchByAddressAsync("丸の内", default));
    Assert.Contains("41", ex.Message);
}
[Fact] public async Task Address_Exactly40_Returns40() { /* 40 件なら 40 件返る */ }

[Fact] public async Task Name_SortsByDistanceThenLength()
{
    var r = await svc.SearchByNameAsync("新木場", null, 5, default);
    Assert.Equal(["hellocycling:200", "docomo:100"], r.Ports.Select(p => p.PortId));
    Assert.All(r.Ports, p => Assert.Equal(0, p.EditDistance));
}
[Fact] public async Task Name_KanaFold() => Assert.Equal("hellocycling:300", (await svc.SearchByNameAsync("しぶや", null, 5, default)).Ports.Single().PortId);
[Fact] public async Task Name_ByDocomoNumber() => Assert.Equal("docomo:100", (await svc.SearchByNameAsync("H1-57", null, 5, default)).Ports.First().PortId);
[Fact] public async Task Name_Typo() => Assert.Equal(1, (await svc.SearchByNameAsync("新木馬", "docomo", 5, default)).Ports.Single().EditDistance);
[Theory] [InlineData("   ")] [InlineData("　")]
public async Task Name_EmptyAfterNormalize_Throws(string q)
    => await Assert.ThrowsAsync<BikeshareException>(() => svc.SearchByNameAsync(q, null, 5, default));
```

（`/* */` の 2 つは、コメントに書いた入力と期待値で同じ形の Assert を書く。）

- [ ] **Step 2: テストが失敗することを確かめる**

Run: `mise run test`
Expected: コンパイルエラー

- [ ] **Step 3: 実装する**

`SearchNearbyAsync` の緯度範囲エラーの文面は「緯度（latitude）は -90〜90 で指定してください。緯度と経度を取り違えていませんか（日本付近は緯度 20〜46、経度 122〜154）」とする。住所 40 件超の文面は「住所「{address}」に一致するポートが {count} 件あります（上限 40 件）。番地など、もっと詳しい住所で絞り込んでください。」。

- [ ] **Step 4: テストが通ることを確かめる**

Run: `mise run test`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add -A && git commit -m "feat: 座標・住所・名前によるポート検索"
```

### Task 5: リアルタイム状態と事業者情報

**Files:**
- Modify: `src/CycleSharingMcp.Core/Views.cs`, `src/CycleSharingMcp.Core/BikeshareService.cs`
- Test: `tests/CycleSharingMcp.Tests/StatusTests.cs`, `tests/CycleSharingMcp.Tests/OperatorInfoTests.cs`

**Interfaces:**
- Consumes: Task 4 の `BikeshareService` / `FakeFeedSource`、Task 3 のモデル
- Produces:
  - `public sealed record PortStatusView(string PortId, string? Name = null, int? NumBikesAvailable = null, int? NumDocksAvailable = null, bool? IsInstalled = null, bool? IsRenting = null, bool? IsReturning = null, string? LastReported = null, IReadOnlyList<VehicleAvailabilityView>? VehicleTypesAvailable = null, IReadOnlyList<DockAvailabilityView>? VehicleDocksAvailable = null, string? Error = null)`
  - `public sealed record VehicleAvailabilityView(string VehicleTypeId, string? Name, string? PropulsionType, int Count)`
  - `public sealed record DockAvailabilityView(IReadOnlyList<string> VehicleTypes, int Count)` — `VehicleTypes` は車種名（名前がなければ ID）
  - `public sealed record PortStatusResponse(IReadOnlyList<PortStatusView> Ports)`
  - `public sealed record VehicleTypeView(string VehicleTypeId, string? Name, string? FormFactor, string? PropulsionType, int? MaxRangeMeters)`
  - `public sealed record OperatorInfoView(string Operator, string? Name, string? ShortName, string? OperatorName, string? Url, string? TermsUrl, string? PrivacyUrl, string? IosApp, string? AndroidApp, int PortCount, IReadOnlyList<VehicleTypeView>? VehicleTypes, string Credit, string License, string? LicenseUrl)`
  - `public sealed record OperatorInfoResponse(IReadOnlyList<OperatorInfoView> Operators)`
  - `BikeshareService.GetPortStatusAsync(IReadOnlyList<string> portIds, CancellationToken ct) -> Task<PortStatusResponse>`
  - `BikeshareService.GetOperatorInfoAsync(string? @operator, CancellationToken ct) -> Task<OperatorInfoResponse>`

仕様:
- `portIds` は 1〜40 件、外れたら全体をエラー。要素は入力順に返す。形式不正は `Error` に `PortId.Parse` のメッセージ、存在しない ID は `Error = "ポートが見つかりません。"`（station_information にも station_status にもない場合）。
- 含まれる事業者のフィードだけ取得する。`Name` は station_information から。`LastReported` は Unix 秒を `+09:00` で `yyyy-MM-dd HH:mm:ss`。車種 ID は `vehicle_types` で名前・動力に置き換える。ドコモの車種系項目は null。
- `Credit`: ドコモ「株式会社ドコモ・バイクシェア / 公共交通オープンデータ協議会」、HELLO「OpenStreet株式会社 / 公共交通オープンデータ協議会」。`License`: ドコモ「CC BY 4.0」、HELLO「CC BY 4.0 / ODC BY 1.0 / ODbL 1.0」。`LicenseUrl` は system_information から。`VehicleTypes` はドコモでは null。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public async Task Status_MixedIds_KeepOrderAndPerItemErrors()
{
    var r = await svc.GetPortStatusAsync(["docomo:5053", "hellocycling:17", "hellocycling:999999", "bad"], default);
    Assert.Equal(["docomo:5053", "hellocycling:17", "hellocycling:999999", "bad"], r.Ports.Select(p => p.PortId));
    Assert.Equal((4, 2, "2026-10-24 19:54:36"), (r.Ports[0].NumBikesAvailable!.Value, r.Ports[0].NumDocksAvailable!.Value, r.Ports[0].LastReported));
    Assert.Equal("2026-10-24 19:50:02", r.Ports[1].LastReported);
    Assert.Equal(new VehicleAvailabilityView("2", "hellocycling bicycle", "electric_assist", 2), Assert.Single(r.Ports[1].VehicleTypesAvailable!));
    Assert.Equal(["hellocycling bicycle"], Assert.Single(r.Ports[1].VehicleDocksAvailable!).VehicleTypes);
    Assert.Equal("ポートが見つかりません。", r.Ports[2].Error);
    Assert.Contains("docomo:5053", r.Ports[3].Error);
}
[Fact] public async Task Status_OnlyFetchesNeededOperators()
{
    await svc.GetPortStatusAsync(["docomo:5053"], default);
    Assert.False(fake.Calls.ContainsKey((Operator.HelloCycling, "station_status")));
}
[Theory] [InlineData(0)] [InlineData(41)]
public async Task Status_CountOutOfRange_Throws(int n)
    => await Assert.ThrowsAsync<BikeshareException>(() => svc.GetPortStatusAsync(Enumerable.Repeat("docomo:5053", n).ToList(), default));

[Fact] public async Task OperatorInfo_Hello()
{
    var v = (await svc.GetOperatorInfoAsync("hellocycling", default)).Operators.Single();
    Assert.Equal("OpenStreet株式会社 / 公共交通オープンデータ協議会", v.Credit);
    Assert.Equal("CC BY 4.0 / ODC BY 1.0 / ODbL 1.0", v.License);
    Assert.Equal(3, v.PortCount);
    Assert.Equal("electric_assist", Assert.Single(v.VehicleTypes!).PropulsionType);
}
[Fact] public async Task OperatorInfo_All_ReturnsBoth() => Assert.Equal(2, (await svc.GetOperatorInfoAsync(null, default)).Operators.Count);
```

（このタスクの `fake` には Task 3 のフィクスチャを `GbfsParser` で読んだ内容を入れる。）

- [ ] **Step 2: テストが失敗することを確かめる**

Run: `mise run test`
Expected: コンパイルエラー

- [ ] **Step 3: 実装する**

- [ ] **Step 4: テストが通ることを確かめる**

Run: `mise run test`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add -A && git commit -m "feat: ポートのリアルタイム状態と事業者情報"
```

### Task 6: ODPT からの取得とキャッシュ

**Files:**
- Create: `src/CycleSharingMcp.Core/OdptEndpoints.cs`, `src/CycleSharingMcp/CycleSharingMcp.csproj`, `src/CycleSharingMcp/FeedCache.cs`, `src/CycleSharingMcp/OdptFeedSource.cs`
- Modify: `CycleSharingMcp.slnx`, `tests/CycleSharingMcp.Tests/CycleSharingMcp.Tests.csproj`（サーバーを参照）
- Test: `tests/CycleSharingMcp.Tests/OdptEndpointsTests.cs`, `tests/CycleSharingMcp.Tests/FeedCacheTests.cs`, `tests/CycleSharingMcp.Tests/OdptFeedSourceTests.cs`

**Interfaces:**
- Consumes: `IFeedSource`, `GbfsParser`, `Operator.SystemId()`
- Produces:
  - `public static class OdptEndpoints { public static Uri FeedUri(Operator op, string feed, string? accessToken); }`
  - `public sealed class FeedCache<T>(TimeSpan ttl, TimeProvider time) { public Task<T> GetAsync(Func<CancellationToken, Task<T>> load, CancellationToken ct); }` — 有効期限内は前回の値。取得中の呼び出しは同じ Task を待つ。失敗した Task は保持しない
  - `public sealed class OdptFeedSource(HttpClient http, OdptOptions options, TimeProvider time) : IFeedSource`、`public sealed record OdptOptions(string? AccessToken)`
  - `public const string HttpClientName = "odpt";`（`OdptFeedSource` に置く）

サーバー csproj: 参考実装の csproj に倣い `AssemblyName` `cycle-sharing-mcp`、`RootNamespace` `CycleSharingMcp`。参照は `Microsoft.Extensions.Hosting` 10.0.13、`Microsoft.Extensions.Http` 10.0.13、`ModelContextProtocol` 2.2.0、`ModelContextProtocol.AspNetCore` 2.2.0、コアのプロジェクト参照。このタスクでは `Program.cs` は `return;` だけの仮のもの（Task 7 で置き換える）。

`OdptFeedSource`: キャッシュは事業者 × フィードごとに 1 つ。TTL は `station_status` 1 分、他は 1 時間。HTTP が成功以外、または `HttpRequestException` / タイムアウトのときは `BikeshareException("ODPT からの{事業者名}のデータ取得に失敗しました（HTTP {code}）。")` を投げる（接続失敗なら「（接続できません）」）。403 でトークンありなら「アクセストークンを確認してください。」を付ける。例外メッセージに URL・トークンを含めない。ドコモの `GetVehicleTypesAsync` は取得せず空を返す。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void FeedUri_Public()
    => Assert.Equal("https://api-public.odpt.org/api/v4/gbfs/docomo-cycle/station_status.json",
                    OdptEndpoints.FeedUri(Operator.Docomo, "station_status", null).AbsoluteUri);
[Fact] public void FeedUri_Token_Escaped()
    => Assert.Equal("https://api.odpt.org/api/v4/gbfs/hellocycling/station_information.json?acl:consumerKey=a%20b%26c",
                    OdptEndpoints.FeedUri(Operator.HelloCycling, "station_information", "a b&c").AbsoluteUri);

[Fact] public async Task Cache_ReusesWithinTtl_ReloadsAfter()
{
    var time = new FakeTimeProvider(); var cache = new FeedCache<int>(TimeSpan.FromMinutes(1), time); var n = 0;
    Task<int> Load(CancellationToken _) => Task.FromResult(++n);
    Assert.Equal(1, await cache.GetAsync(Load, default));
    time.Advance(TimeSpan.FromSeconds(59)); Assert.Equal(1, await cache.GetAsync(Load, default));
    time.Advance(TimeSpan.FromSeconds(2));  Assert.Equal(2, await cache.GetAsync(Load, default));
}
[Fact] public async Task Cache_ConcurrentCallsShareOneLoad() { /* TaskCompletionSource で止めた load に 2 回 GetAsync → load は 1 回、両方同じ値 */ }
[Fact] public async Task Cache_FailureIsNotCached() { /* 1 回目の load が例外 → 2 回目の GetAsync で load が再度呼ばれて成功する */ }

[Fact] public async Task FeedSource_ParsesFixture()
{
    // フィクスチャを URL ごとに返す偽 HttpMessageHandler を使う
    var ports = await source.GetPortsAsync(Operator.HelloCycling, default);
    Assert.Equal(3, ports.Count);
}
[Fact] public async Task FeedSource_403_MessageHasNoToken()
{
    // 常に 403 を返すハンドラー、トークン "secret-token"
    var ex = await Assert.ThrowsAsync<BikeshareException>(() => source.GetStatusesAsync(Operator.Docomo, default));
    Assert.Contains("403", ex.Message); Assert.DoesNotContain("secret-token", ex.Message); Assert.DoesNotContain("odpt.org", ex.Message);
}
[Fact] public async Task FeedSource_FailureThenSuccess_Retries() { /* 1 回目 503、2 回目 200 → 2 回目の呼び出しは成功 */ }
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: `mise run test`
Expected: コンパイルエラー

- [ ] **Step 3: 実装する**

`FeedCache` は `lock` で「現在の Task と期限」を守る。期限切れか未取得なら `load` を始めて Task を保存し、その Task が失敗したら保存を消す。期限は取得の完了時刻から数える。`ct` は待機の打ち切りにだけ使い、共有中の取得そのものは止めない（取得側には `CancellationToken.None` を渡す）。

- [ ] **Step 4: テストが通ることを確かめる**

Run: `mise run test`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add -A && git commit -m "feat: ODPT からのフィード取得とキャッシュ"
```

### Task 7: 起動引数・MCP ツール・stdio / HTTP

**Files:**
- Create: `src/CycleSharingMcp/ServerOptions.cs`, `src/CycleSharingMcp/BikeshareTools.cs`, `.github/scripts/smoke_test.py`
- Modify: `src/CycleSharingMcp/Program.cs`
- Test: `tests/CycleSharingMcp.Tests/ServerOptionsTests.cs`

**Interfaces:**
- Consumes: `BikeshareService`、`OdptFeedSource` / `OdptOptions` / `HttpClientName`、`BikeshareException`
- Produces:
  - `public sealed record ServerOptions(bool Http, string Host, int Port, string? AccessToken) { public static ServerOptions Parse(IReadOnlyList<string> args, Func<string, string?> getEnv); }` — 不正な引数は `ArgumentException`
  - `[McpServerToolType] public sealed class BikeshareTools(BikeshareService service)`: `search_ports_nearby(double latitude, double longitude, string? @operator = null, int limit = 10)`、`search_ports_by_address(string address)`、`search_ports_by_name(string query, string? @operator = null, int limit = 5)`、`get_port_status(string[] port_ids)`、`get_operator_info(string? @operator = null)`。すべて `ReadOnly = true, Idempotent = true`。`BikeshareException` を `McpException`（同じメッセージ）に変換する

- [ ] **Step 1: 失敗するテストを書く**

```csharp
static string? NoEnv(string _) => null;
[Fact] public void Defaults() => Assert.Equal(new ServerOptions(false, "127.0.0.1", 8080, null), ServerOptions.Parse([], NoEnv));
[Fact] public void EnvToken() => Assert.Equal("e", ServerOptions.Parse([], k => k == "ODPT_ACCESS_TOKEN" ? "e" : null).AccessToken);
[Fact] public void EmptyEnvToken_IsNull() => Assert.Null(ServerOptions.Parse([], _ => "").AccessToken);
[Fact] public void ArgTokenWins() => Assert.Equal("a", ServerOptions.Parse(["--odpt-token", "a"], _ => "e").AccessToken);
[Fact] public void Http() => Assert.Equal(new ServerOptions(true, "0.0.0.0", 9000, null),
    ServerOptions.Parse(["--http", "--port", "9000", "--host", "0.0.0.0"], NoEnv));
[Theory]
[InlineData("--port", "abc")] [InlineData("--port", "0")] [InlineData("--foo")] [InlineData("--odpt-token")]
public void Invalid_Throws(params string[] args) => Assert.Throws<ArgumentException>(() => ServerOptions.Parse(args, NoEnv));
```

- [ ] **Step 2: テストが失敗することを確かめる**

Run: `mise run test`
Expected: コンパイルエラー

- [ ] **Step 3: `ServerOptions` と `BikeshareTools` を実装する**

ツールの説明文に必ず入れること:
- `search_ports_nearby`: 座標は WGS84（世界測地系）の 10 進度。Google Maps・駅すぱあと（gcs=wgs84）の値はそのまま使える。旧日本測地系の値は東京付近で約 400 m ずれるので不可。空き状況は含まないので `get_port_status` に `port_id` を渡す。
- `search_ports_by_address`: HELLO CYCLING 専用（ドコモ・バイクシェアには住所データがない）。最大 40 件、超えたらエラー。
- `search_ports_by_name`: 表記ゆれ（全角半角・ひらがなカタカナ）と多少の書き違いを吸収する。読みでは当たらない（「しんきば」で「新木場」は不可）。ドコモの番号（例: H1-57）でも探せる。
- `get_port_status`: ポート ID は検索結果の `port_id`。最大 40 件。
- 共通（ポートを返すツール）: HELLO CYCLING の capacity は設置上の収容数で、使えない車両・枠を含むため、貸出可能台数と返却可能数の合計より大きいことがある。
- `operator` 引数: `docomo` / `hellocycling`、省略で両方。

- [ ] **Step 4: `Program.cs` を書く**

`ServerOptions.Parse(args, Environment.GetEnvironmentVariable)` で解釈し、`ArgumentException` なら stderr にメッセージを出して終了コード 2。ホストのビルダーには `args` を渡さない（`--http` のような値なし引数が構成プロバイダーで誤解釈されるため）。共通のサービス登録（名前付き HttpClient: タイムアウト 60 秒・自動解凍、`OdptOptions`、`TimeProvider.System`、`IFeedSource` → `OdptFeedSource`、`BikeshareService` をシングルトン）を 1 つの関数にまとめ、stdio は `Host.CreateApplicationBuilder()` + `WithStdioServerTransport()`、HTTP は `WebApplication.CreateBuilder()` + `WithHttpTransport(o => o.Stateless = true)` + `app.MapMcp("/mcp")`、待ち受けは `http://{Host}:{Port}`。ログは参考実装と同じく stderr（stdio 時）、さらに `AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)` で要求 URI のログを止める。JSON 設定は `~/ghq/github.com/shao1555/yj-weather-mcp/src/YjWeatherMcp/Program.cs` と同じ。起動時に「ODPT 公開版を使用」/「ODPT トークン版を使用」を 1 行ログに出す。

- [ ] **Step 5: テストとスモークテストを通す**

`smoke_test.py` は `~/ghq/github.com/hiyuki2578/ekispert-mcp/.github/scripts/smoke_test.py` を元に（帰属のヘッダーコメントも引き継ぐ）、`EXPECTED_TOOLS = {"search_ports_nearby", "search_ports_by_address", "search_ports_by_name", "get_port_status", "get_operator_info"}`。

Run: `mise run test && mise run publish && python3 .github/scripts/smoke_test.py dist/cycle-sharing-mcp`
Expected: テスト PASS、スモークテストが 5 つのツールを表示して終了コード 0

- [ ] **Step 6: 実データとトークン漏えいを確かめる**

stdio で `tools/call` を送り、`search_ports_nearby`（35.681236, 139.767125, limit 5）が両事業者のポートを距離付きで返し、その `port_id` を `get_port_status` に渡すと台数が返ることを確かめる。
次に `ODPT_ACCESS_TOKEN=dummy-token-xyz` で同じ呼び出しをし、エラー（403）が日本語で返ること、`2>&1 | grep -c dummy-token-xyz` が `0` であることを確かめる。
最後に `dist/cycle-sharing-mcp --http` で起動し、`curl -s -X POST http://127.0.0.1:8080/mcp -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}'` が `serverInfo` を含む応答を返すことを確かめる。

- [ ] **Step 7: コミット**

```bash
git add -A && git commit -m "feat: MCP ツールと stdio / HTTP での起動"
```

### Task 8: README と TODO

**Files:**
- Create: `README.md`, `TODO.md`

- [ ] **Step 1: README を書く**

参考実装の README（`~/ghq/github.com/hiyuki2578/ekispert-mcp/README.md`）と同じ構成で: 概要（非公式であること）、ツール一覧の表と主な引数、ポート ID の形式、座標は WGS84、キャッシュ時間、ビルド（`mise install` / `mise run publish`）、登録（Claude Code に stdio で `claude mcp add cycle-sharing --scope user -e ODPT_ACCESS_TOKEN=... -- "$PWD/dist/cycle-sharing-mcp"`、トークンなしの例、HTTP で `--http` 起動と `claude mcp add --transport http cycle-sharing http://127.0.0.1:8080/mcp`）、HTTP には認証がないので外部公開時は手前で認証すること、クレジット表示とライセンス（設計書の「クレジット表示」節の内容）。

- [ ] **Step 2: TODO.md を書く**

設計書の「将来の Cloudflare Workers 対応（TODO）」節の内容を、作業メモとして箇条書きで写す（wrangler 等を mise に追加すること、WASI SDK、実験版コンパイラ、8 MB 問題と KV 案、MCP の受け口を手書きする必要、コアを再利用すること）。

- [ ] **Step 3: README のコマンドを実際に通す**

Run: README の登録コマンドのうち `claude mcp add`（stdio、トークンなし）を実行し、`claude mcp list` で `cycle-sharing` が接続済みになることを確かめてから `claude mcp remove cycle-sharing --scope user` で戻す。
Expected: `cycle-sharing: ... - ✓ Connected`

- [ ] **Step 4: コミット**

```bash
git add -A && git commit -m "docs: README と Workers 対応の TODO"
```
