# TODO

## Cloudflare Workers 対応

- 候補: [nenoNaninu/cloudflare-workers-dotnet](https://github.com/nenoNaninu/cloudflare-workers-dotnet)（`Cloudflare.Workers.Hosting`）。C# を NativeAOT-LLVM で WASM にして Workers で動かす。
- 追加するのは「フィードの取得とキャッシュ」（`IFeedSource` の実装）と「MCP の受け口」だけ。検索・状態・事業者情報は `src/CycleSharingMcp.Core` の `BikeshareService` をそのまま使う。
  - コアは ASP.NET Core・`Microsoft.Extensions.Hosting`・MCP SDK を参照せず、`Task.Run`・スレッド・`Task.Delay` も使わない。この制約は守り続ける。
  - 応答は record なので、WASM（トリミングあり）では JSON ソース生成（`JsonSerializerContext`）に載せる。
- 制約:
  - ASP.NET Core ではないので `ModelContextProtocol.AspNetCore` は使えず、MCP SDK 本体も動く見込みが薄い。MCP の JSON-RPC（`initialize` / `tools/list` / `tools/call`、Streamable HTTP）を手で書く必要がありそう。
  - シングルスレッドで、`Task.Run` / `Task.Delay` / `Thread.Sleep` は使えない。
  - 外向きの HTTP は `HttpClient` + `FetchHttpMessageHandler`。
  - 正規化（`string.Normalize`）が WASM で ICU なしに動くか確かめる。
- 依存:
  - 実験版の `Microsoft.DotNet.ILCompiler.LLVM`（dotnet-experimental のフィードを足した `nuget.config` が必要）。
  - WASI SDK（`WASI_SDK_PATH`）。
  - wrangler。wrangler など Workers 関連のツールも mise に追加する。
- データの重さ: HELLO CYCLING の `station_information`（約 8 MB、約 15,600 ポート）をリクエストのたびに WASM で解析すると、CPU 時間（無料プランは 10 ms）やメモリ（128 MB）の制限に掛かるおそれがある。
  Scheduled Handler で定期的に軽い形へ変換して KV に置く、などの仕組みが要る。
