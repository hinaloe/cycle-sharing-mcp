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
- 取得中に同時に来たリクエストは、取得を始めたリクエストの完了を自分のタイマーで少しずつ確かめながら待つ（別のリクエストの I/O だけを待つと Workers がハングとみなして打ち切るため）。待つのは最大 60 秒。
  取得を始めたリクエストがクライアントの切断などで取り消されて取得が終わらないままになると、isolate が作り直されるまで、そのフィードを使う呼び出しが毎回 60 秒待ってエラーになるおそれがある（手元の wrangler dev では切断しても取得が最後まで走り、再現できていない）。本番で起きるなら、一定時間を過ぎた取得を捨てて取り直す仕組みを `FeedCache` に足す。
- WebAssembly が 13 MB（gzip 3.6 MB）ある。起動を速くしたくなったら、何が大きいかを調べて削る。
- `Microsoft.DotNet.ILCompiler.LLVM` は dotnet-experimental フィードの実験版で、macOS 向けが配信されていない。正式版が出たら乗り換える。
