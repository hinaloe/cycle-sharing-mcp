# Cloudflare Workers 版 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 今と同じ 5 ツールを、Cloudflare Workers 上の `POST /mcp`（セッションなしの JSON-RPC）で提供する Worker を作り、手元の `wrangler dev` と CI で確かめられるようにする（デプロイはしない）。

**Architecture:** フィードの取得・キャッシュ、JSON の形、ツール定義、MCP の JSON-RPC 処理をコア（`src/CycleSharingMcp.Core`）に集め、サーバー版と Workers 版の両方から使う。Workers 版（`src/CycleSharingMcp.Workers`）は、`Cloudflare.Workers.Hosting` の上でルーティングと NFKC の差し替え（JavaScript の `normalize`）だけを持つ薄い層にする。

**Tech Stack:** C# / .NET 10、`Cloudflare.Workers.Hosting` 0.3.0、`Microsoft.DotNet.ILCompiler.LLVM` 10.0.0-rc.1.26357.1（dotnet-experimental）、WASI SDK 29.0、wrangler 4（Node.js 24）、xUnit v3。

**Spec:** `docs/superpowers/specs/2026-10-25-cloudflare-workers-design.md`（既存の設計 `docs/superpowers/specs/2026-10-24-bikeshare-mcp-design.md` も前提）

## Global Constraints

- コアは ASP.NET Core・`Microsoft.Extensions.Hosting`・MCP SDK・`Cloudflare.Workers.Hosting` を参照しない。`Task.Run`・スレッド・`Task.Delay` を使わない。
- Workers 版は `RuntimeIdentifier` `wasi-wasm`、`InvariantGlobalization` `true`。dotnet-experimental フィードの `nuget.config` は `src/CycleSharingMcp.Workers/` にだけ置く。Workers 版のプロジェクトはソリューション（`CycleSharingMcp.slnx`）に入れない（WASI SDK なしの `dotnet build` / `dotnet test` を壊さないため）。
- `wrangler.jsonc` に `account_id`・`routes`・バインディングの ID を書かない。ODPT のアクセストークンは Workers 版では使わない（公開版だけ）。
- サーバー版（stdio / HTTP）のツールの結果・説明文・引数は変えない。既存のテストはすべて通ったままにする。
- ツールの結果の JSON は snake_case、null の項目は出さない、日本語はエスケープしない。
- WASI SDK・ダウンロードしたものは `.cache/`（git の管理外）に置き、コミットしない。
- この作業のブランチは PR #7（`chore/renovate`）の上に積んだ `feat/cloudflare-workers`。#6・#7 のマージ後は `gh stack sync` で追従する。
- ファイル内の置き換えは Edit ツールで行う。npm パッケージの一時実行は `pnpx`。

## Review Focus

1. **引数の型の揺れ**: クライアントによって `limit: "5"` のように数値が文字列で来る、`port_ids` が配列でなく 1 つの文字列で来る、必須の引数がない。サーバー版と同じく、落ちずに日本語のツールエラー（`isError: true`）になること → Task 5 のテスト。
2. **既定値の食い違い**: Workers 版の `limit` の既定値（近傍 10、名前 5）が、ツール定義（`inputSchema` の `default`）と食い違うこと → Task 5 のテスト。
3. **不正な HTTP 本文**: JSON でない、配列（バッチ）、`jsonrpc` がない、`id` が文字列・数値・null。落ちずに JSON-RPC のエラーになること → Task 5 のテスト。
4. **JavaScript の `normalize` が使えない・例外になる**: Workers 版で正規化が黙って効かなくなること。CI の確認で、正規化の表のすべてがサーバー版と同じ結果になること → Task 7 の確認。
5. **初回リクエストの同時到着**: isolate の起動直後に複数のリクエストが同時に来ても、取得・解析は 1 回で済むこと（`FeedCache` のテストで担保済み。Task 1 で移したあとも同じテストが通ること）。

## ファイル構成

```
src/CycleSharingMcp.Core/
  FeedCache.cs              サーバー版から移す
  OdptFeedSource.cs         サーバー版から移す（OdptOptions も）
  BikeshareJson.cs          ツール結果の JSON 設定とソース生成のコンテキスト
  TextNormalizer.cs         NFKC を差し替えられるようにする
  ToolDefinitions.cs        ToolDefinitions.json を読む
  ToolDefinitions.json      サーバー版の tools/list を書き出したもの（埋め込みリソース）
  Mcp/McpHttpHandler.cs     POST /mcp の JSON-RPC 処理
  Mcp/ToolArguments.cs      tools/call の引数の解釈
src/CycleSharingMcp/        Program.cs と BikeshareTools.cs をコアの型・JSON 設定に合わせる
src/CycleSharingMcp.Workers/
  CycleSharingMcp.Workers.csproj / nuget.config / wrangler.jsonc / Program.cs / JsNormalizer.cs
scripts/workers.sh          WASI SDK の取得、ビルド（macOS は Docker）、wrangler dev
tests/CycleSharingMcp.Tests/
  Fixtures/normalization_cases.json   正規化の表（xUnit と Workers の確認で共有）
  BikeshareJsonTests.cs / NormalizationCasesTests.cs / ToolDefinitionsTests.cs / McpHttpHandlerTests.cs
.github/workflows/build.yml          workers ジョブを足す
.github/scripts/workers_smoke_test.py
```

---

### Task 1: フィードの取得とキャッシュをコアへ移す

**Files:**
- Move: `src/CycleSharingMcp/FeedCache.cs` → `src/CycleSharingMcp.Core/FeedCache.cs`、`src/CycleSharingMcp/OdptFeedSource.cs` → `src/CycleSharingMcp.Core/OdptFeedSource.cs`（`git mv`）
- Modify: 移した 2 ファイルの名前空間を `CycleSharingMcp.Core` に、`src/CycleSharingMcp/Program.cs`・`ServerOptions.cs`（`OdptOptions.Redact` を使っている）・テストの `using`
- Test: 既存の `FeedCacheTests` / `OdptFeedSourceTests` / `SecretRedactionTests`

**Interfaces:**
- Produces: `CycleSharingMcp.Core.FeedCache<T>`、`CycleSharingMcp.Core.OdptFeedSource(HttpClient http, OdptOptions options, TimeProvider time) : IFeedSource`、`CycleSharingMcp.Core.OdptOptions(string? AccessToken)`（`internal static string Redact` は `public` にする。サーバー版の `ServerOptions` から使うため）。中身は変えない。

- [ ] **Step 1: 移す**（`git mv` と名前空間の書き換えだけ。動作は変えない）
- [ ] **Step 2: テストが通ることを確かめる**

Run: `mise run test`
Expected: PASS（件数は移す前と同じ）。コアの csproj に新しいパッケージ参照が増えていないこと（`System.Net.Http` は BCL）

- [ ] **Step 3: コミット** — `refactor: フィードの取得とキャッシュをコアへ移す`

### Task 2: ツール結果の JSON 設定をコアに置く

**Files:**
- Create: `src/CycleSharingMcp.Core/BikeshareJson.cs`
- Modify: `src/CycleSharingMcp/Program.cs`（`serializerOptions` を `BikeshareJson.CreateServerOptions()` に置き換える）
- Test: `tests/CycleSharingMcp.Tests/BikeshareJsonTests.cs`

**Interfaces:**
- Produces:
  - `[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)] [JsonSerializable(typeof(PortListResponse))] [JsonSerializable(typeof(PortStatusResponse))] [JsonSerializable(typeof(OperatorInfoResponse))] public sealed partial class BikeshareJsonContext : JsonSerializerContext`
  - `public static class BikeshareJson`: `JsonSerializerOptions Options`（Web 既定 + `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` + 上と同じ命名・null の扱い、`TypeInfoResolver = BikeshareJsonContext.Default`）、`string Serialize<T>(T value)`、`JsonSerializerOptions CreateServerOptions()`（`Options` と同じ設定で、`TypeInfoResolver = JsonTypeInfoResolver.Combine(BikeshareJsonContext.Default, new DefaultJsonTypeInfoResolver())`。MCP SDK が自分の型をリフレクションで扱えるようにするため）

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact]
public void Serialize_SnakeCase_OmitsNull_KeepsJapanese()
{
    var view = new PortView("docomo:1", "docomo", "新木場", 35.6, 139.8, null, null, null, null, null, null, DistanceM: 10);
    Assert.Equal("""{"ports":[{"port_id":"docomo:1","operator":"docomo","name":"新木場","lat":35.6,"lon":139.8,"distance_m":10}]}""",
                 BikeshareJson.Serialize(new PortListResponse([view], null)));
}
```

- [ ] **Step 2: 失敗を確かめる** — Run: `mise run test` / Expected: コンパイルエラー
- [ ] **Step 3: 実装し、サーバー版の `Program.cs` を置き換える**
- [ ] **Step 4: 確かめる** — Run: `mise run test && mise run publish && python3 .github/scripts/smoke_test.py dist/cycle-sharing-mcp` / Expected: PASS、スモークテストで 5 ツール
- [ ] **Step 5: コミット** — `refactor: ツール結果の JSON 設定をコアに置き、ソース生成を使う`

### Task 3: NFKC を差し替えられるようにし、正規化の表を作る

**Files:**
- Modify: `src/CycleSharingMcp.Core/TextNormalizer.cs`
- Create: `tests/CycleSharingMcp.Tests/Fixtures/normalization_cases.json`
- Test: `tests/CycleSharingMcp.Tests/NormalizationCasesTests.cs`

**Interfaces:**
- Produces:
  - `public static Func<string, string> Compatibility { get; set; }`（`TextNormalizer` に置く。既定は `static s => s.Normalize(NormalizationForm.FormKC)`。`Normalize` の NFKC はこれを通す。実行環境が起動時に 1 度だけ差し替える）
  - `normalization_cases.json`: `[{"kind": "name" | "address", "input": "...", "expected": "..."}]`。`kind` が `name` なら `Normalize`、`address` なら `NormalizeAddress` の結果と比べる

- [ ] **Step 1: 表を書く**（期待値は今のサーバー版の結果。実データはコミットせず、種類ごとの例だけ）

| kind | input | expected |
|---|---|---|
| name | `ｼﾌﾞﾔ　Ｐｏｒｔ` | `しぶやport` |
| name | `ｶﾞﾊﾟｰｸ` | `がぱーく` |
| name | `松坂屋上野店ｱﾈｯｸｽ1F` | `松坂屋上野店あねっくす1f` |
| name | `Ａ４－０１．丸ビル（南側）` | `a4-01.丸びる(南側)` |
| name | `セブンイレブン　浦和南部領店` | `せぶんいれぶん浦和南部領店` |
| address | `東京都台東区台東４丁目３１－１` | `東京都台東区台東4-31-1` |
| address | `大阪府大阪市城東区中央２丁目１４－Ｃ` | `大阪府大阪市城東区中央2-14-c` |

- [ ] **Step 2: 表の全行を `Normalize` / `NormalizeAddress` で確かめるテストを書く**（`[Theory]` + 表を読む `MemberData`）。期待値が今の実装と食い違えば、期待値ではなく表の書き間違いを疑う
- [ ] **Step 3: 確かめる** — Run: `mise run test` / Expected: PASS（この表は今の実装のままで通る。`Compatibility` を足したあとも通ること）
- [ ] **Step 4: `Compatibility` を足し、`Normalize` の NFKC をそれ経由にする**
- [ ] **Step 5: 確かめる** — Run: `mise run test` / Expected: PASS
- [ ] **Step 6: コミット** — `feat: NFKC を実行環境ごとに差し替えられるようにする`

### Task 4: ツール定義をファイルに書き出し、コアから読めるようにする

**Files:**
- Create: `src/CycleSharingMcp.Core/ToolDefinitions.json`（埋め込みリソース）、`src/CycleSharingMcp.Core/ToolDefinitions.cs`
- Test: `tests/CycleSharingMcp.Tests/ToolDefinitionsTests.cs`

**Interfaces:**
- Produces: `public static class ToolDefinitions { public static string Json { get; } }` — `ToolDefinitions.json` の中身（MCP の `Tool` オブジェクトの配列。`tools/list` の `tools` にそのまま入れる）

- [ ] **Step 1: 失敗するテストを書く**

`ToolDefinitions_MatchServerTools`: `BikeshareTools` の `[McpServerTool]` が付いたメソッドごとに `McpServerTool.Create(method, _ => null!, new McpServerToolCreateOptions { SerializerOptions = BikeshareJson.CreateServerOptions() })` で作り、`ProtocolTool` の配列を `McpJsonUtilities.DefaultOptions` で JSON にする。`JsonNode.DeepEquals` で `ToolDefinitions.Json` と比べる。食い違ったら、環境変数 `UPDATE_TOOL_DEFINITIONS=1` で実行するとファイルを書き直す（リポジトリの `src/CycleSharingMcp.Core/ToolDefinitions.json` に、インデント付きで書く）、とメッセージに出す。

`ToolDefinitions_HaveFiveTools`: `Json` を読んだ配列の `name` が 5 つのツール名と一致。

- [ ] **Step 2: 失敗を確かめる** — Run: `mise run test` / Expected: `ToolDefinitions` が未定義でコンパイルエラー
- [ ] **Step 3: `ToolDefinitions.cs` を書き、`UPDATE_TOOL_DEFINITIONS=1 mise run test` でファイルを生成する**
- [ ] **Step 4: 確かめる** — Run: `mise run test` / Expected: PASS。生成されたファイルを読み、説明文・`default`・`required`・注釈（`readOnlyHint` など）が入っていること
- [ ] **Step 5: コミット** — `feat: サーバー版のツール定義をファイルに書き出して共有する`

### Task 5: POST /mcp の JSON-RPC 処理

**Files:**
- Create: `src/CycleSharingMcp.Core/Mcp/McpHttpHandler.cs`、`src/CycleSharingMcp.Core/Mcp/ToolArguments.cs`
- Test: `tests/CycleSharingMcp.Tests/McpHttpHandlerTests.cs`

**Interfaces:**
- Consumes: `BikeshareService`、`BikeshareJson.Serialize`、`ToolDefinitions.Json`、`BikeshareException`
- Produces:
  - `public sealed record McpHttpResult(int StatusCode, string? Json)` — `Json` が null なら本文なし
  - `public sealed class McpHttpHandler(BikeshareService service)`: `Task<McpHttpResult> HandleAsync(string body, CancellationToken ct)`
  - `public const string ServerName = "cycle-sharing-mcp";`、`public static IReadOnlyList<string> SupportedProtocolVersions`（新しい順。`initialize` で要求された版が含まれていればそれを、なければ先頭を返す）

仕様:
- 本文が JSON でない → 400、`{"jsonrpc":"2.0","id":null,"error":{"code":-32700,...}}`。オブジェクトでない（配列＝バッチを含む）・`jsonrpc` が `"2.0"` でない・`method` がない → 400、`-32600`。
- `id` がない（通知）→ 202、本文なし（`notifications/initialized` など、どのメソッドでも）。
- `initialize` → `{"protocolVersion":…, "capabilities":{"tools":{}}, "serverInfo":{"name":"cycle-sharing-mcp","version":…}}`。`ping` → `{}`。`tools/list` → `{"tools": ToolDefinitions の配列}`。
- `tools/call` → 成功は `{"content":[{"type":"text","text": BikeshareJson.Serialize(結果)}]}`。`BikeshareException` と引数の誤りは `{"content":[{"type":"text","text": 日本語のメッセージ}],"isError":true}`。知らないツール名は JSON-RPC エラー `-32602`。
- 知らないメソッド → `-32601`。`id` は受け取ったもの（文字列・数値）をそのまま返す。
- 引数の既定値はサーバー版と同じ（`search_ports_nearby` の `limit` 10、`search_ports_by_name` の `limit` 5、`operator`・`available_for` は省略可）。数値は JSON の数値のほか、数値として読める文字列も受け付ける。`port_ids` は文字列の配列のほか、文字列 1 つも 1 件の配列として受け付ける。

- [ ] **Step 1: 失敗するテストを書く**（`FakeFeedSource.FromFixtures()` の `BikeshareService` を使う）

```csharp
[Fact] public async Task Initialize_EchoesSupportedVersion() { /* params.protocolVersion "2025-06-18" → result.protocolVersion "2025-06-18"、serverInfo.name "cycle-sharing-mcp" */ }
[Fact] public async Task Initialize_UnknownVersion_ReturnsLatest() { /* "1999-01-01" → SupportedProtocolVersions[0] */ }
[Fact] public async Task Notification_Returns202WithoutBody() { /* {"jsonrpc":"2.0","method":"notifications/initialized"} → (202, null) */ }
[Fact] public async Task ToolsList_ReturnsToolDefinitions() { /* result.tools が ToolDefinitions.Json と DeepEquals */ }
[Fact] public async Task ToolsCall_Name_ReturnsSameJsonAsService() { /* search_ports_by_name {"query":"新木場"} の text が BikeshareJson.Serialize(await service.SearchByNameAsync("新木場", null, 5, default)) と一致 */ }
[Fact] public async Task ToolsCall_StatusWithSingleStringId() { /* get_port_status {"port_ids":"docomo:5053"} → 1 件返る */ }
[Fact] public async Task ToolsCall_LimitAsString_IsAccepted() { /* search_ports_nearby {"latitude":35.6457,"longitude":139.8268,"limit":"2"} → ports 2 件 */ }
[Fact] public async Task ToolsCall_MissingRequired_IsToolError() { /* search_ports_nearby {"latitude":35.6} → isError true、text に "longitude" */ }
[Fact] public async Task ToolsCall_ServiceError_IsToolError() { /* latitude 139.7 → isError true、text に "緯度" */ }
[Fact] public async Task ToolsCall_UnknownTool_IsInvalidParams() { /* error.code -32602 */ }
[Theory] [InlineData("not json", 400, -32700)] [InlineData("[]", 400, -32600)] [InlineData("""{"id":1,"method":"ping"}""", 400, -32600)]
public async Task BadRequests(string body, int status, int code) { /* … */ }
[Fact] public async Task UnknownMethod_IsMethodNotFound() { /* resources/list → -32601、id そのまま */ }
[Fact] public async Task Defaults_MatchToolDefinitions() { /* ToolDefinitions の inputSchema.properties.limit.default が nearby 10・name 5 と一致（Review Focus 2） */ }
```

（`/* */` は、コメントに書いた入力と期待値で Assert を書く。）

- [ ] **Step 2: 失敗を確かめる** — Run: `mise run test` / Expected: コンパイルエラー
- [ ] **Step 3: 実装する**。JSON の読み書きは `JsonDocument` / `Utf8JsonWriter`（または `JsonNode`）で行い、リフレクションによる `JsonSerializer` は使わない（WASM でトリミングされるため）。ツールの結果だけ `BikeshareJson.Serialize` を使う
- [ ] **Step 4: 確かめる** — Run: `mise run test` / Expected: PASS
- [ ] **Step 5: コミット** — `feat: POST /mcp の JSON-RPC 処理をコアに追加する`

### Task 6: Workers のプロジェクトとビルドの手順

**Files:**
- Create: `src/CycleSharingMcp.Workers/CycleSharingMcp.Workers.csproj`、`nuget.config`、`wrangler.jsonc`、`Program.cs`、`JsNormalizer.cs`、`scripts/workers.sh`
- Modify: `mise.toml`（`node = "24"`、`"npm:wrangler"` を 4 系の固定版で、タスク `workers:build` / `workers:dev`）、`.gitignore`（`.cache/`、`src/CycleSharingMcp.Workers/.wrangler/`）

**Interfaces:**
- Consumes: `McpHttpHandler`、`OdptFeedSource`、`OdptOptions`、`BikeshareService`、`TextNormalizer.Compatibility`
- Produces:
  - Worker のルート: `POST /mcp`（`McpHttpHandler`）、`GET /mcp` → 405、`GET /` → 説明の 1 行、それ以外 → 404。`SELFTEST` 変数が `"1"` のときだけ `POST /_selftest/normalize`（本文 `[{"kind","input"}]`、応答 `["正規化した結果", …]`）
  - `internal static class JsNormalizer { public static string Nfkc(string value); }` — `JsRuntime.Current.GetGlobal("String").InvokeAsFunction(null, JsArg.From(value))` で作った JavaScript の文字列に `.Call("normalize", JsArg.From("NFKC"))`（`JsObject` は `using` で解放する）
  - `scripts/workers.sh build | dev`: WASI SDK 29.0 を `.cache/wasi-sdk/` に取得（GitHub Release の `wasi-sdk-29.0-{x86_64,arm64}-{linux,macos}.tar.gz`。sha256 を確かめる）。Linux はそのまま `dotnet publish`、macOS は `mcr.microsoft.com/dotnet/sdk:10.0` の linux/arm64 コンテナで、リポジトリと NuGet のキャッシュを同じパスでつないで `dotnet publish`（`WASI_SDK_PATH` は Linux 版を指す）。`dev` は `build` のあと `wrangler dev`

- [ ] **Step 1: csproj・nuget.config・wrangler.jsonc を書く**（`dist/spike-workers/Spike/` の試作の設定を元にする。`name` は `cycle-sharing-mcp`、`compatibility_date` は `2026-09-10`、`main` は publish 先の `worker/index.js`。`build` は書かない）
- [ ] **Step 2: `Program.cs` を書く**。起動時に `TextNormalizer.Compatibility = JsNormalizer.Nfkc`。`HttpClient(new FetchHttpMessageHandler()) { Timeout = Timeout.InfiniteTimeSpan }`、`OdptFeedSource(http, new OdptOptions(null), TimeProvider.System)`、`BikeshareService`、`McpHttpHandler` は isolate 内で 1 つずつ（static）
- [ ] **Step 3: `scripts/workers.sh` と mise のタスクを書く**
- [ ] **Step 4: ビルドと手元の確認** — Run: `mise run workers:dev`（別の端末）のあと、`curl -s -X POST http://127.0.0.1:8787/mcp -H 'Content-Type: application/json' -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search_ports_by_name","arguments":{"query":"ﾌｧﾐﾘｰﾏｰﾄ","limit":3}}}'` と、`{"query":"ＭＦＬＰ新木場"}` で同じく呼ぶ
Expected: 前者は「ファミリーマート…」、後者は「H1-183.MFLP新木場1」などが `edit_distance` 0 で返る（半角カナ・全角英字が JavaScript の NFKC で正規化されている）。続けて `search_ports_nearby` の初回と 2 回目の時間を記録する
- [ ] **Step 5: JavaScript の `normalize` の速さを測る**。HELLO CYCLING の初回の解析が、試作（801 ms）より 1 秒以上遅くなるなら、`JsNormalizer` を「全角英数記号（`c - 0xFEE0`）と半角カナ（対応表と濁点・半濁点の合成）だけの自前の変換」に替え、その旨を README と設計書に書く。どちらにしたかと測定値をコミットメッセージに残す
- [ ] **Step 6: 既存のテストが通ることを確かめる** — Run: `mise run test` / Expected: PASS（Workers 版はソリューションに入っていないこと）
- [ ] **Step 7: コミット** — `feat: Cloudflare Workers 版を追加する`

### Task 7: CI で Workers 版をビルドして確かめる

**Files:**
- Create: `.github/scripts/workers_smoke_test.py`
- Modify: `.github/workflows/build.yml`（`workers` ジョブ）

**Interfaces:**
- Consumes: `scripts/workers.sh build`、`tests/CycleSharingMcp.Tests/Fixtures/normalization_cases.json`、`src/CycleSharingMcp.Core/ToolDefinitions.json`

- [ ] **Step 1: スモークテストを書く**。`python workers_smoke_test.py <base-url>` で次を確かめ、どれかが違えば終了コード 1:
  - `initialize` の `serverInfo.name` が `cycle-sharing-mcp`
  - `tools/list` の `tools` が `ToolDefinitions.json` と一致
  - `tools/call` `search_ports_nearby` `{"latitude":139.7,"longitude":35.6}` が `isError: true` で text に「緯度」（ODPT に問い合わせない）
  - `POST /_selftest/normalize` に正規化の表の全行を送り、すべて `expected` と一致（Review Focus 4）
- [ ] **Step 2: `workers` ジョブを足す**（`ubuntu-latest`、`test` の後。setup-dotnet 10.0.402、mise の公式アクションで node と wrangler を入れる、`scripts/workers.sh build`、`wrangler dev --port 8787 --var SELFTEST:1` を裏で起動して待ってからスモークテスト）。新しいアクションは `pinact run` で SHA に固定する
- [ ] **Step 3: 確かめる** — 手元で `mise run workers:build` のあと `wrangler dev --var SELFTEST:1` を起動し、`python3 .github/scripts/workers_smoke_test.py http://127.0.0.1:8787` が終了コード 0。push 後、PR の `workers` ジョブが通ること
- [ ] **Step 4: コミット** — `ci: Workers 版のビルドとスモークテストを追加する`

### Task 8: README と TODO

**Files:**
- Modify: `README.md`、`TODO.md`

- [ ] **Step 1: README に「Cloudflare Workers で動かす」節を足す**: ビルド（`mise run workers:build`。macOS は Docker が要る理由）、`mise run workers:dev` での確認、自分のアカウントへのデプロイ（`wrangler deploy`。有料プランが要ること、`account_id` は自分の環境で指定すること）、Claude.ai・ChatGPT などのコネクタへの登録（URL は `https://<名前>.<アカウント>.workers.dev/mcp`、認証なし）、認証がないので必要なら Cloudflare 側で流量制限をかけること、本番で確かめていないリスク（メモリ上限 128 MB に対して解析中のピーク約 51 MB）、Workers 版は ODPT のトークンを使わないこと
- [ ] **Step 2: TODO.md の Workers の項から、済んだもの（コアの再利用、MCP の受け口、mise での wrangler 管理、依存の整理）を消し、残り（本番での確認、事前変換・KV、メモリ上限を超えた場合の対策）だけにする**
- [ ] **Step 3: README のコマンドを実際に通す**（`mise run workers:build` と、Workers の節の `curl` の例）
- [ ] **Step 4: コミット** — `docs: Cloudflare Workers 版の使い方を書く`
