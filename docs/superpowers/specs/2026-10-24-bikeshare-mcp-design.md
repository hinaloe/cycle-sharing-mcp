# シェアサイクル MCP サーバー 設計

## 目的

ドコモ・バイクシェアと HELLO CYCLING（OpenStreet）のポート情報・リアルタイム状態を、LLM から引ける MCP サーバーにする。
データ源は公共交通オープンデータセンター（ODPT）が配信している GBFS 2.3 フィード。

想定する使い方は「Google Maps・ナビタイム・駅すぱあと（ekispert-mcp など）で得た座標や地名を起点に、近くのポートを探し、そのポートの今の空き状況を見る」流れ。
ポート一覧は全件で 2 万件以上あり、そのまま返すと LLM のコンテキストを食い潰すので、**必ず絞り込んでから返す**ことがこのサーバーの存在理由。

## ゴール

- 座標から最寄りのポートを、件数を指定して返せる（上限 40 件）。
- HELLO CYCLING のポートを住所の部分一致で探せる（最大 40 件、超えたらエラー）。
- 両事業者のポートを名前のあいまい検索で数件に絞れる。
- ポートのリアルタイム状態（貸出可能台数・返却可能数など）を **ポート ID からのみ** 引ける。
- ODPT のアクセストークンが起動時に渡されればトークン付きの API を、なければ公開版を使う。
- stdio と HTTP（Streamable HTTP）の両方で動く。

## ノンゴール

- 予約・貸出などの操作。読み取り専用。
- 2 事業者以外のシェアサイクル。
- ドコモ・バイクシェア東京エリア版データセット（全国版でほぼ包含されるため使わない。後述）。
- 地名・駅名から座標への変換（ジオコーディング）。座標は呼び出し側が他のツールで用意する。
- Cloudflare Workers での公開。ただし後から足せる構成にしておく（「将来の Cloudflare Workers 対応」参照）。
- 読みと漢字の対応（「しぶや」で「渋谷」を当てる）、漢数字と算用数字の対応（「四丁目」と「４丁目」）。
- HTTP 公開時の認証。必要ならサーバーの手前（リバースプロキシ等）で行う。

## アンチゴール（動いても失敗とみなすもの）

- 検索系ツールが件数制限なしに大量のポートを返すこと。
- 旧日本測地系など別の座標系の値を黙って受け取り、数百 m ずれた結果をそれらしく返すこと（入力は WGS84 と明記する）。
- ODPT のアクセストークンがログ・エラーメッセージ・ツールの応答に出ること。トークンは URL のクエリに載るので、HTTP エラーの例外メッセージ等をそのまま返さないよう特に注意する。

## データ源

GBFS 2.3。公開版とトークン版でホストとクエリだけが違う。

| | 公開版（トークンなし） | トークン版 |
|---|---|---|
| ベース URL | `https://api-public.odpt.org/api/v4/gbfs/{system}/` | `https://api.odpt.org/api/v4/gbfs/{system}/` |
| 認証 | なし | クエリ `acl:consumerKey={token}`（なしだと 403） |

| 事業者 | `{system}` | 使うフィード | 備考 |
|---|---|---|---|
| ドコモ・バイクシェア | `docomo-cycle`（全国版） | `system_information`, `station_information`, `station_status` | 約 6,200 ポート。住所なし。名前は `H1-57.新木場駅` のように番号が前に付く。`region_id` はあるが地域名を引くフィードはない |
| HELLO CYCLING | `hellocycling` | `system_information`, `station_information`, `station_status`, `vehicle_types` | 約 15,600 ポート。`station_information` は約 8 MB。住所・`rental_uris`・`is_charging_station`・`parking_type` などあり。`station_status` に車種別台数あり |

全国版を選んだ理由: 東京エリア版（`docomo-cycle-tokyo`、1,928 件）のうち 1,923 件は全国版にも同じ ID・同じ名前で載っている（2026-10-24 時点で確認）。

座標はどちらも WGS84（EPSG:4326）の 10 進度。

### クレジット表示

ODPT のデータは CC BY 4.0（HELLO CYCLING は CC BY 4.0 / ODC BY 1.0 / ODbL 1.0）。
提供者名は「株式会社ドコモ・バイクシェア / 公共交通オープンデータ協議会」「OpenStreet株式会社 / 公共交通オープンデータ協議会」。
README に記載し、`get_operator_info` の応答にも含める。

## キャッシュ

- `station_status`: 最大 1 分。
- `system_information` / `station_information` / `vehicle_types`: 最大 1 時間。
- 同じフィードへの同時リクエストは 1 回の取得にまとめる（HELLO CYCLING の 8 MB を同時に何本も取りに行かない）。
- フィードの取得に失敗したらツールはエラーを返す（古いキャッシュで代用しない）。

## ツール

すべて `ReadOnly = true, Idempotent = true`。出力は参考実装と同じく snake_case の JSON、日本語はエスケープしない。

### ポート ID

`{事業者}:{GBFS の station_id}` の 1 文字列。例: `docomo:5053`, `hellocycling:17`。
両事業者とも station_id は数字だけで衝突しうるので、事業者名を前に付ける。
事業者を表す値はツール共通で `docomo` / `hellocycling`。

### 検索結果のポート項目（共通）

- 共通: `port_id`, `operator`, `name`, `lat`, `lon`, `capacity`
- ドコモのみ: `region_id`
- HELLO CYCLING のみ: `address`, `is_charging_station`, `parking_type`, `rental_url`（`rental_uris.web`）

リアルタイム状態は含めない（`get_port_status` で引く）。

`capacity` について（実装者判断として設計時に決めたもの）: HELLO CYCLING の `station_information` には GBFS 標準の `capacity` がなく、標準外の `vehicle_capacity` が文字列（例: `"8"`）で入っている。
これを整数にして `capacity` に入れる。ただし HELLO CYCLING では約 28% のポートで `num_bikes_available + num_docks_available` が `vehicle_capacity` より小さい。
電池切れなどで使えない車両・枠が数えられていない差と思われる（HELLO CYCLING の `station_status` には `num_bikes_disabled` / `num_docks_disabled` がない）。
ドコモ・バイクシェアの `capacity` も、0 のポート（2026-10-25 時点で約 6,200 件中 360 件）や、その合計と合わないポート（同 434 件）がある。
ツール説明には「capacity は目安で、空きの判断には get_port_status の台数を見ること」と、事業者ごとの上記のずれを書く。

### `search_ports_nearby`

座標から近い順にポートを返す。

| 引数 | 既定 | 内容 |
|---|---|---|
| `latitude` / `longitude` | 必須 | WGS84 の 10 進度。説明文に「旧日本測地系の値は不可（東京付近で約 400 m ずれる）」と書く |
| `operator` | 両方 | `docomo` / `hellocycling` で絞る |
| `limit` | 10 | 1〜40。範囲外はエラー |

各ポートに直線距離 `distance_m`（整数 m）を付ける。

### `search_ports_by_address`

HELLO CYCLING 専用。住所の部分一致。

| 引数 | 既定 | 内容 |
|---|---|---|
| `address` | 必須 | 住所の一部（例: `台東区台東４丁目`） |

- 比較前に両方へ正規化をかける（下記「文字列の正規化」）。
- 一致が 40 件を超えたらエラー。メッセージに一致件数を入れ、もっと詳しい住所で絞るよう促す。
- 0 件なら空配列と、その旨の `note` を返す。
- 並び順は住所の文字列順。

### `search_ports_by_name`

両事業者のポートを名前であいまい検索し、近い順に返す。

| 引数 | 既定 | 内容 |
|---|---|---|
| `query` | 必須 | ポート名の一部 |
| `operator` | 両方 | `docomo` / `hellocycling` で絞る |
| `limit` | 5 | 1〜40 |

アルゴリズム:

- 正規化した `query` と正規化したポート名で、**部分文字列に対する編集距離**（Sellers のアルゴリズム。名前のどの部分文字列との間で測っても最小になる距離）を求める。
  名前全体とのレーベンシュタイン距離は使わない（`新木場` と `H1-57.新木場駅` で長さの差がそのまま距離になるため）。
  部分文字列で測るので、ドコモの名前の先頭の番号を剥がす処理はいらない。番号で検索しても当たる。
- 並び順: 距離の昇順、同点なら名前の短い順。
- 検索語の長さに対して距離が大きすぎる候補は外す。しきい値は実装で調整してよいが、2 文字の検索語で 1 文字違いが大量に当たらない程度にする。
- 各ポートに `edit_distance`（編集距離）を付ける（近傍検索の `distance_m` と紛れないよう名前を分ける）。

### `get_port_status`

ポート ID からリアルタイム状態を返す。リアルタイム状態を引く手段はこれだけ。

| 引数 | 既定 | 内容 |
|---|---|---|
| `port_ids` | 必須 | ポート ID の配列（1〜40 件）。事業者が混ざってよい |

各ポートについて:

- `port_id`, `name`
- `num_bikes_available`, `num_docks_available`
- `is_installed`, `is_renting`, `is_returning`
- `last_reported`（日本時間の ISO 8601、時差付き。例: `2026-10-24T19:54:36+09:00`）
- HELLO CYCLING のみ: 車種別の貸出可能台数・返却可能数（`vehicle_type_id` を `vehicle_types` の名前・`propulsion_type` に置き換えて返す）。
  返却可能数（`vehicle_docks_available`）は GBFS では複数の車種 ID をまとめた単位で返るので、その単位のまま返す（2026-10 時点で車種は電動アシスト自転車の 1 種類のみ）。

存在しない ID・形式の誤った ID は、その要素にだけエラー内容を入れて返し、他の要素は普通に返す。

### `get_operator_info`

| 引数 | 既定 | 内容 |
|---|---|---|
| `operator` | 両方 | `docomo` / `hellocycling` |

`system_information` の主な項目（名前、URL、利用規約 URL、アプリのリンク等）、ポート数、HELLO CYCLING は `vehicle_types`（名前・動力・最大航続距離など）、クレジット表示とライセンスを返す。

### 文字列の正規化

住所検索と名前検索で共通に使う。

- Unicode 正規化 NFKC。
- 全角/半角、ひらがな/カタカナ、大文字/小文字を区別しない（ICU の `CompareOptions.IgnoreWidth | IgnoreKanaType | IgnoreCase` 相当。実装は比較前の文字列変換でも `CompareInfo` でもよい）。
- 空白は除いて比べる。
- 住所検索だけは、さらに「丁目」を「-」に揃える（「台東4丁目」と「台東4-30-6」が互いに当たるように）。
- そのため `InvariantGlobalization` は `false` のまま（参考実装と同じ）。

## 起動と設定

| 設定 | 方法 | 内容 |
|---|---|---|
| ODPT アクセストークン | 環境変数 `ODPT_ACCESS_TOKEN`、または起動引数 `--odpt-token` | あればトークン版、なければ公開版。両方あれば起動引数を優先 |
| トランスポート | 起動引数 `--http` | なければ stdio |
| HTTP の待ち受け | `--host`（既定 `127.0.0.1`）、`--port`（既定は実装で決める） | MCP のエンドポイントは `/mcp` |

- stdio のときログはすべて stderr（参考実装と同じ）。
- 起動時に、公開版とトークン版のどちらを使うかを 1 行ログに出す（トークンそのものは出さない）。
- HTTP のときに認証はしない。

## 技術選択

参考実装（`hiyuki2578/ekispert-mcp`, `shao1555/yj-weather-mcp`, `shao1555/jre-ekinet-mcp`）に合わせる。

- C# / .NET 10、`ModelContextProtocol` パッケージ、HTTP 用に `ModelContextProtocol.AspNetCore`。
- `Microsoft.Extensions.Hosting` / `Microsoft.Extensions.Http`（`IHttpClientFactory`）。
- ツールのバージョン管理は mise。.NET SDK を固定し、`mise run publish` で自己完結型の単一バイナリを `dist/` に出す。
- プロジェクトを 2 つに分ける（後から Cloudflare Workers 版を足すため）。
  - **コア（クラスライブラリ）**: GBFS のモデルと JSON の解析、正規化、住所検索・名前検索・近傍検索、ポート ID の解釈、ツールの入力検証と応答の組み立て。
    実行環境に依存させない: ASP.NET Core・`Microsoft.Extensions.Hosting`・MCP SDK を参照しない。`Task.Run` やスレッドを使わない。
    フィードの取得とキャッシュはインターフェース越しに受け取る（取得元・キャッシュ先が Workers では変わるため）。
  - **サーバー（実行ファイル）**: 起動引数の処理、stdio / HTTP の切り替え、`IHttpClientFactory` を使ったフィード取得とメモリ上のキャッシュ、MCP SDK のツール定義（コアを呼んで結果を返すだけの薄い層）。
- 例外は `McpException` で利用者向けの日本語メッセージにする（コアは独自の例外型で投げ、サーバー層で `McpException` に変換する）。
- README は日本語で、ツール一覧、ビルド、Claude Code への登録（stdio と HTTP）、クレジット表示を書く。

## 将来の Cloudflare Workers 対応（TODO）

今回は作らない。リポジトリに TODO としてメモを残す（README の末尾か `TODO.md`）。メモに書くこと:

- 候補は [nenoNaninu/cloudflare-workers-dotnet](https://github.com/nenoNaninu/cloudflare-workers-dotnet)（`Cloudflare.Workers.Hosting`）。C# を NativeAOT-LLVM で WASM にして Workers で動かす。
- 制約: ASP.NET Core ではないので `ModelContextProtocol.AspNetCore` は使えず、MCP SDK 本体も動く見込みが薄い。MCP の JSON-RPC（`initialize` / `tools/list` / `tools/call`、Streamable HTTP）を手で書く必要がありそう。シングルスレッド、`Task.Run` / `Task.Delay` 不可。外向き HTTP は `HttpClient` + `FetchHttpMessageHandler`。
- 依存: 実験版の `Microsoft.DotNet.ILCompiler.LLVM`（専用の nuget.config が必要）、WASI SDK、wrangler。wrangler など Workers 関連のツールも mise に追加する。
- データの重さ: HELLO CYCLING の `station_information`（約 8 MB）をリクエストのたびに WASM で解析すると CPU 時間・メモリの制限に掛かるおそれがある。Scheduled Handler で定期的に軽い形へ変換して KV に置く、などの仕組みが要る。
- 追加するのは「フィードの取得とキャッシュ」と「MCP の受け口」だけで、検索などはコアを再利用する（コアがそれを満たすよう上記の制約を守る）。

## 実装者に任せること

- プロジェクト名・アセンブリ名。
- `--port` の既定値。
- 名前検索の距離しきい値の具体値。
- 距離計算の方式（ハバーサインで十分）と、近傍検索の方式（全件走査で十分な件数）。
- テストの範囲と方式（少なくとも正規化と名前検索の並び順にはユニットテストを書くのが望ましい）。
- `.github` の CI やスモークテストを置くかどうか。
