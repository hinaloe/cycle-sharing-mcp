# TODO

## Cloudflare Workers 版

Workers 版（`src/CycleSharingMcp.Workers`）は、手元の `wrangler dev` と CI での確認まで。残っていること:

- 本番の Workers で動かして確かめる（有料プラン）。
  - メモリ: HELLO CYCLING の `station_information`（約 8 MB、約 15,600 ポート）の解析中に約 51 MB を使う（手元の計測）。取得した本文が JavaScript 側にも残ることや、TTL が切れて取り直すときに古いデータと新しいデータが同時に残ることを考えると、上限 128 MB に対して余裕は大きくない。
  - CPU 時間: 初回の取得・解析で 1 秒前後（手元の計測）。
  - Claude.ai・ChatGPT・Notion などのコネクタから実際に使えるか。
- 上限に掛かった場合の対策（そのときに決める）:
  - コアの `Port` を軽くする（検索用の正規化済み文字列を持たないなど）。
  - 事業者ごとに必要になってから読み込む。
  - Scheduled Handler（Cron）で定期的に軽い形へ変換し、KV などに置く。
- WebAssembly が 13 MB（gzip 3.6 MB）ある。起動を速くしたくなったら、何が大きいかを調べて削る。
- `Microsoft.DotNet.ILCompiler.LLVM` は dotnet-experimental フィードの実験版で、macOS 向けが配信されていない。正式版が出たら乗り換える。
