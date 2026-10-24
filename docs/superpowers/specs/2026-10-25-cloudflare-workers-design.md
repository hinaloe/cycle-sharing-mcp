# Cloudflare Workers 版 設計

## 目的

今の MCP サーバー（stdio / HTTP）と同じ 5 つのツールを、Cloudflare Workers 上の公開 URL（`https://<名前>.<アカウント>.workers.dev/mcp`）でも提供する。
Claude.ai・ChatGPT・Notion など、各種サービスの「カスタムコネクタ」として登録し、チャットからシェアサイクルのポートと空き状況を調べられるようにする。

既存の設計は `docs/superpowers/specs/2026-10-24-bikeshare-mcp-design.md`。検索・状態・事業者情報の処理（コア `src/CycleSharingMcp.Core`）はそのまま使い、Workers 版で足すのは「フィードの取得とキャッシュ」と「MCP の受け口」だけにする。

## ゴール

- Workers 上で、今と同じ名前・引数・結果の 5 ツール（`search_ports_nearby` / `search_ports_by_address` / `search_ports_by_name` / `get_port_status` / `get_operator_info`）を提供する。
- Streamable HTTP（`POST /mcp`）で、各種サービスのコネクタから「認証なし」で登録して使える。
- ツールの説明文と引数の形（JSON Schema）が、サーバー版と Workers 版で食い違わない。
- 手元（`wrangler dev`）と CI でビルド・動作確認ができる。

## ノンゴール

- デプロイ。今回は行わない（README にデプロイ手順を書くまで）。
- 認証（OAuth など）、流量制限、独自ドメイン。いずれもデプロイする環境ごとの設定で、リポジトリには入れない。
- ODPT のアクセストークン。誰でも呼べる公開 URL なので、トークンを使わず公開版（`api-public.odpt.org`）だけを使う。
- 無料プランでの動作（解析 1 回で CPU 時間 0.8 秒前後かかるため、10 ms の上限に収まらない）。
- 定期実行（Cron）での事前変換、KV・R2・D1 の利用。
- セッションを持つ MCP（サーバーからの通知、SSE での応答、`resources` / `prompts`）。

## アンチゴール（動いても失敗とみなすもの）

- アカウント ID・ルート・独自ドメイン・KV の ID など、特定の環境に依存する設定がコミットされること。公開リポジトリとして、誰でも自分のアカウントで動かせる形にする。
- サーバー版と Workers 版で、ツールの説明文や引数の形が食い違うこと（片方だけ直して片方が古いまま、など）。
- Workers 対応のために、既存のサーバー版（stdio / HTTP）の動作や結果が変わること。
- Workers 版だけ、全角・半角などの正規化が効かず、検索が当たらなくなること（試作で実際に起きた。後述）。

## 前提

- Cloudflare Workers の有料プラン（CPU 時間の上限 30 秒）。メモリの上限は 1 つの isolate あたり 128 MB。
- URL は `*.workers.dev`。
- isolate が作り直されたときの最初のリクエストが 3〜4 秒かかることは許容する（LLM の動作時間に比べてわずかなため）。
- ライブラリは [nenoNaninu/cloudflare-workers-dotnet](https://github.com/nenoNaninu/cloudflare-workers-dotnet)（`Cloudflare.Workers.Hosting`）。C# を NativeAOT-LLVM（`Microsoft.DotNet.ILCompiler.LLVM`、dotnet-experimental フィードの実験版）で WASM にして動かす。

## 試作で分かったこと（2026-10-25、手元の `wrangler dev`）

`Cloudflare.Workers.Hosting` 0.3.0、`Microsoft.DotNet.ILCompiler.LLVM` 10.0.0-rc.1.26357.1、WASI SDK 29 で、コアを参照する試作を作って測った。本番の CPU・メモリの上限はかかっていない環境での値。

| 項目 | 値 |
|---|---|
| WASM の大きさ | 7.7 MB（gzip で約 2.4 MB） |
| 起動直後の最初のリクエスト | 約 1.4 秒（WASM の読み込み） |
| ドコモ `station_information` の取得 / 解析 | 959 ms / 755 ms、解析後に残るメモリ約 6 MB |
| HELLO CYCLING `station_information` の取得 / 解析 | 696 ms / 801 ms、解析後に残るメモリ約 47 MB（解析中のピーク約 51 MB） |
| メモリに載ったデータでの検索（全 21,818 件） | 近傍 10 ms、名前 18 ms、住所 11 ms |

- ODPT への取得は `HttpClient` + `FetchHttpMessageHandler` で動いた。
- **`string.Normalize(NormalizationForm.FormKC)` が WebAssembly では使えない。**
  - `InvariantGlobalization=true`（ライブラリの既定）では何もせず、元の文字列を返した（「ｼﾌﾞﾔ台東４丁目」がそのまま）。
  - ICU を組み込んでも（`InvariantGlobalization=false`、WASM が 12.0 MB に増える）、.NET が `PlatformNotSupportedException`（「FormKC and FormKD are not supported in browser environments or WebAssembly」）を投げる。
  - 実データで NFKC が変える文字は、全角英数記号 24,048、全角空白 2,377、半角カナ 124、そのほか（ローマ数字・丸数字・康熙部首など）約 220。
- 実験版コンパイラには macOS 向け（arm64・x64 とも）の .NET 10 版がない。Linux（x64・arm64）と Windows（x64）向けはある。macOS では Linux のコンテナでビルドする必要がある。
- `wrangler dev`（wrangler 4）は Node.js 24 で動かした。

## 決めたこと

### 構成

- 新しいプロジェクト `src/CycleSharingMcp.Workers` を足す。参照するのはコアだけ（サーバー版のプロジェクト・MCP SDK・ASP.NET Core は参照しない）。
  - `RuntimeIdentifier` は `wasi-wasm`、`InvariantGlobalization` は `true`（ICU は上記のとおり役に立たず、WASM が大きくなるだけ）。
  - dotnet-experimental フィードを使う `nuget.config` は、このプロジェクトのディレクトリにだけ置く（ほかのプロジェクトの取得元を変えない）。
  - `wrangler.jsonc` には `name`・`main`・`compatibility_date` などの環境に依存しない設定だけを書き、`account_id`・`routes`・バインディングの ID は書かない。
- フィードの取得とキャッシュ（今のサーバー版の `OdptFeedSource` と `FeedCache`）は、Workers 版でも同じものを使う。`HttpClient` を外から渡す作りなので、Workers 版は `FetchHttpMessageHandler` を使う `HttpClient` を渡す。キャッシュは isolate のメモリに持ち、TTL は今と同じ（`station_status` 1 分、ほか 1 時間）。アクセストークンは渡さない（公開版だけを使う）。
  - そのために、この 2 つ（と `OdptOptions`）をサーバー版からコアへ移す。コアの制約（ASP.NET Core・Hosting・MCP SDK を参照しない、`Task.Run` やスレッドを使わない）は今のまま守れる。

### 正規化

- コアの正規化（`TextNormalizer`）のうち NFKC の部分を、実行環境ごとに差し替えられるようにする。サーバー版は今どおり `string.Normalize(FormKC)`。
- Workers 版は、ホストの JavaScript の `String.prototype.normalize('NFKC')` を、ライブラリの JavaScript 呼び出しの仕組み（`JsRuntime` / `JsObject`）で呼ぶ方向で検討する。
  - 解析時に約 2 万件 × 2 項目（名前・住所）を正規化するので、1 件ずつ呼ぶと WASM と JavaScript の行き来が約 4 万回になる。まとめて 1 回で正規化する（区切り文字でつないで正規化し、分け直す）などの工夫が要るかを、実装の最初に測って決める。
  - JavaScript の呼び出しが使えない・遅すぎる場合の代わりは、全角英数記号（文字コードを一定の数だけずらす）と半角カナ（対応表と濁点・半濁点の合成）だけを自前で変換する方法。
- どの方法でも、全角英数記号・全角空白・半角カナ（濁点・半濁点付きを含む）が、サーバー版と同じ結果になることを確かめる。
  - 実データはコミットせず、文字の種類ごとの例（例: `台東４丁目３１－１`、`セブンイレブン　浦和`、`ｱﾈｯｸｽ`、`ｶﾞﾊﾟ`）と期待する結果の表をテスト用に持つ（設計時の判断）。
  - サーバー版の結果は xUnit で、Workers 版の結果は CI の `wrangler dev` での確認の中で、同じ表を使って確かめる（JavaScript の `normalize` は xUnit からは呼べないため）。

### MCP の受け口

- `POST /mcp` で JSON-RPC 2.0 を受け、`application/json` で 1 つの応答を返す（Streamable HTTP のうち、セッションを持たず SSE を使わない形）。
- 扱うメソッド: `initialize`、`notifications/initialized`（通知なので本文なしで 202）、`ping`、`tools/list`、`tools/call`。それ以外は JSON-RPC の「メソッドが見つからない」エラー。`GET /mcp` などは 405。
- ツールの処理は `BikeshareService` を呼ぶだけにする。`BikeshareException` は、サーバー版と同じく、ツールの結果のエラー（`isError: true`、日本語のメッセージ）として返す。
- 結果の JSON は、サーバー版と同じ形（snake_case、null の項目は出さない、日本語をエスケープしない）。WASM ではリフレクションによる JSON 変換が使えない（トリミングされる）ので、System.Text.Json のソース生成を使う。

### ツール定義を 1 か所にする

- サーバー版の `tools/list` の結果（ツール名・説明文・`inputSchema`・注釈）を JSON ファイルに書き出してリポジトリに入れ、Workers 版はそのファイルを読み込んで `tools/list` に返す。
- サーバー版のテストで、実際の `tools/list` とファイルの内容が一致することを確かめる。説明文を直したらファイルの更新を忘れるとテストが落ちる、という形で食い違いを防ぐ。

### ビルドと CI

- 手元では、Linux はそのまま、macOS は Linux（arm64）の .NET SDK コンテナでビルドする。どちらでも同じ手順になるよう、mise のタスク（例: `mise run workers:build`、`mise run workers:dev`）にまとめる。
  - 保証するのは macOS と Linux。Windows は CI で確かめられないので必須にしない（設計時の判断）。
- WASI SDK（v29）は、ビルド時に決まった場所へダウンロードして使う（リポジトリには入れない）。
- wrangler と Node.js は mise で管理する（`mise.toml` に足す）。
- CI では Linux（x64）でビルドし、`wrangler dev` で起動して `initialize` / `tools/list` / 実データを使わない `tools/call`（引数の誤りでエラーになるもの）が通ることを確かめる。デプロイはしない。

### 完了の条件と、本番で確かめていないこと

- 今回の完了は「CI で Workers 版のビルドと `wrangler dev` での確認が通り、README にデプロイと登録の手順がある」こと。本番の Workers へのデプロイと、コネクタからの実際の利用は含めない。
- 本番でしか確かめられないリスク（設計時の判断で、今回は記録だけにする）:
  - メモリ上限 128 MB に対し、HELLO CYCLING の解析中は約 51 MB を使う。取得した本文が JavaScript 側にも残ることや、TTL が切れて取り直すときに古いデータと新しいデータが同時に残ることを考えると、余裕は大きくない。
  - CPU 時間は解析 1 回で 0.8 秒前後（手元の値）。
- 本番で上限を超えた場合の候補（その時点で改めて決める）: コアの `Port` を軽くする、事業者ごとに必要になってから読み込む（今もそうなっているかを確かめる）、ノンゴールにした KV への事前変換を使い直す。README の Workers の節に、このリスクを書いておく。

### 文書

- README に「Cloudflare Workers で動かす」節を足す（ビルド、`wrangler dev` での確認、自分のアカウントへのデプロイ手順、各サービスのコネクタへの登録、認証がないので必要なら Cloudflare 側で流量制限などをかけること）。
- `TODO.md` の Workers の項は、この対応で済んだものを消す。

## 実装者に任せること

- プロジェクト・タスク・ファイルの名前。
- 対応する MCP のプロトコルバージョン（`initialize` で受け取ったものに合わせるか、決まった版を返すか）。
- CORS の要否（ブラウザから直接呼ぶクライアント向け）。
- `tools/list` を書き出すファイルの置き場所と、書き出しの手順（テストの中で生成するか、mise のタスクにするか）。
- JavaScript の `normalize` を使う場合の、まとめ方の細部。
