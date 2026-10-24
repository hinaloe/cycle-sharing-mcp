# cycle-sharing-mcp

[公共交通オープンデータセンター（ODPT）](https://www.odpt.org/) が配信する GBFS で、ドコモ・バイクシェアと HELLO CYCLING のシェアサイクルのポート（駐輪場）と空き状況を調べる MCP サーバー（stdio / HTTP）。

> [!IMPORTANT]
> **非公式**のツールです。株式会社ドコモ・バイクシェア、OpenStreet株式会社、公共交通オープンデータ協議会とは関係がありません。
> データの利用条件は各データセットのライセンスに従います（後述の「クレジット表示とライセンス」）。

## ツール

| ツール | 内容 |
|---|---|
| `search_ports_nearby` | 座標から近い順にポートを返す（両事業者、最大 40 件）。各ポートに直線距離 `distance_m` が付く |
| `search_ports_by_address` | HELLO CYCLING のポートを住所の部分一致で探す（最大 40 件、超えたらエラー）。ドコモ・バイクシェアのデータには住所がないので対象外 |
| `search_ports_by_name` | 両事業者のポートを名前であいまい検索する（最大 40 件）。各ポートに編集距離 `edit_distance` が付く |
| `get_port_status` | ポート ID から、貸出可能台数・返却可能数・貸出/返却の可否・最終報告時刻を返す（最大 40 件）。HELLO CYCLING は車種別の台数も返す |
| `get_operator_info` | 事業者の基本情報、ポート数、HELLO CYCLING の車種、クレジット表示とライセンス |

主な引数:

| 引数 | ツール | 既定 | 内容 |
|---|---|---|---|
| `latitude` / `longitude` | `search_ports_nearby` | （必須） | WGS84（世界測地系）の 10 進度。Google Maps や駅すぱあと（`gcs=wgs84`）の座標はそのまま使える。旧日本測地系の値は東京付近で約 400 m ずれる |
| `operator` | 住所検索以外 | 両方 | `docomo`（ドコモ・バイクシェア）か `hellocycling`（HELLO CYCLING） |
| `limit` | `search_ports_nearby` / `search_ports_by_name` | 10 / 5 | 返す件数（1〜40） |
| `port_ids` | `get_port_status` | （必須） | ポート ID の配列（1〜40 件） |

- ポート ID は `docomo:5053`・`hellocycling:17` のように事業者名を前に付けた形。検索結果の `port_id` をそのまま `get_port_status` に渡す。
- 検索結果には空き状況を含めない。空き状況はポート ID からだけ引ける。
- 住所・名前の検索は、全角・半角、ひらがな・カタカナ、大文字・小文字、空白の違いを無視する。名前検索は多少の書き違いも吸収するが、読み（「しんきば」で「新木場」）では当たらない。
- HELLO CYCLING の `capacity` は設置上の収容数で、使えない車両・枠を含むため、貸出可能台数と返却可能数の合計より大きいことがある。

## しくみ

- 使うデータセット: [ドコモ・バイクシェア バイクシェア関連情報](https://ckan.odpt.org/dataset/c_bikeshare_gbfs-d-nationwide-bikeshare)（全国版。東京エリア版のポートはほぼすべて全国版に含まれる）と [OpenStreet（ハローサイクリング） バイクシェア関連情報](https://ckan.odpt.org/dataset/c_bikeshare_gbfs-openstreet)。
- ODPT のアクセストークンを渡すと `api.odpt.org`（トークン版）、渡さないと `api-public.odpt.org`（公開版）から取得する。
- 取得したデータはプロセス内にキャッシュする。`station_status`（空き状況）は 1 分、ポート一覧・事業者情報・車種は 1 時間。同じフィードへの同時の取得は 1 回にまとめ、取得に失敗したときは古いキャッシュで代用せずエラーにする。
- アクセストークンはログ・エラーメッセージ・ツールの結果に出さない。

## ビルド

```sh
mise install          # .NET SDK 10
mise run test         # テスト
mise run publish      # ./dist/cycle-sharing-mcp（自己完結型の単一バイナリ）を作る
```

mise で入れた dotnet は `DOTNET_ROOT` を設定しないため、MCP クライアントには `bin/` 配下ではなく `dist/cycle-sharing-mcp` を登録する。

## 起動と登録

| 設定 | 方法 | 内容 |
|---|---|---|
| ODPT アクセストークン | 環境変数 `ODPT_ACCESS_TOKEN` か `--odpt-token <トークン>`（両方あれば起動引数を優先） | [開発者サイト](https://developer.odpt.org/) で発行する。省略時は公開版を使う |
| HTTP で待ち受ける | `--http` | 省略時は stdio。エンドポイントは `/mcp`（Streamable HTTP） |
| 待ち受けアドレス・ポート | `--host`（既定 `127.0.0.1`）、`--port`（既定 `8080`） | `--http` のときだけ使う |

### Claude Code（stdio）

```sh
# 公開版
claude mcp add cycle-sharing --scope user -- "$PWD/dist/cycle-sharing-mcp"

# トークン版
claude mcp add cycle-sharing --scope user -e ODPT_ACCESS_TOKEN=アクセストークン -- "$PWD/dist/cycle-sharing-mcp"
```

### Claude Code（HTTP）

```sh
dist/cycle-sharing-mcp --http --port 8080
claude mcp add --transport http cycle-sharing http://127.0.0.1:8080/mcp
```

> [!WARNING]
> HTTP には認証がありません。`--host 0.0.0.0` などで外から届くようにするときは、リバースプロキシなどの手前で認証してください。

## クレジット表示とライセンス

| 事業者 | クレジット表示（提供者名） | ライセンス |
|---|---|---|
| ドコモ・バイクシェア | 株式会社ドコモ・バイクシェア / 公共交通オープンデータ協議会 | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/deed.ja) |
| HELLO CYCLING | OpenStreet株式会社 / 公共交通オープンデータ協議会 | CC BY 4.0 / ODC BY 1.0 / ODbL 1.0（[ライセンス](https://d1yl7kw204zjxn.cloudfront.net/gbfs/v2/public/hellocycling_gbfs_licence.txt)） |

クレジット表示については [ODPT の FAQ](https://developer.odpt.org/ja/faq-info#cc-by-credit) を参照。

## スモークテスト

publish したバイナリを stdio で起動し、MCP の initialize と tools/list が通るかを確かめる（ODPT には問い合わせない）。

```sh
python3 .github/scripts/smoke_test.py dist/cycle-sharing-mcp
```

## ライセンス

このリポジトリのコードは [MIT License](LICENSE)。取得するデータのライセンスは「クレジット表示とライセンス」のとおり。
