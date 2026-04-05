# Plateau.ResoniteLink

Plateau.ResoniteLink は、PLATEAU のデータセットを Resonite に取り込むための .NET 10 プロジェクトです。最初の実装済み縦切りは CLI 起点で、データセットとメッシュコード指定から、展開済み `udx/<package>/` ツリー配下にあるメッシュコード対応ファイルやネストされたメッシュコードディレクトリを対象に、決定的な Resonite 構築メタデータと逐次的な city object stream を生成します。

インポート意味論と用語の参照実装は `PLATEAU-SDK-for-UNITY` とします。

## 使い方

依存関係を復元します。

```bash
dotnet restore Plateau.ResoniteLink.sln
```

ローカルのデータセットルートから構築計画を生成します。

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 53394525 \
  --source local \
  --input /path/to/plateau
```

CLI は `artifacts/<os>/resonite/<dataset>/<mesh-code>/` 配下に Resonite 構築計画 JSON を出力します。既定ではホスト OS ごとに出力先を分け、Linux/WSL では `artifacts/linux/resonite/`、Windows では `artifacts/windows/resonite/` を使います。現行の v1 importer は公式 PLATEAU の `udx/<package>/` prefix 群にまたがるローカル CityGML を読み、deterministic な submesh / material 順序を保ちつつ、詳細モデルの `ParameterizedTexture` appearance も live-ready な mesh / material payload に反映し、live build 全体をメモリ保持せず city object 単位で下流へ流します。

既定の CKAN catalog flow を使って、公式 PLATEAU CityGML ZIP をオンライン取得する例:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source server
```

`--source server` は既定で公式の `search.ckan.jp` catalog を使い、対応する CityGML ZIP resource を探索して `artifacts/<os>/resonite/server-cache/` にダウンロードし、展開した上で同じ deterministic local importer に渡します。`--server-url` で catalog base URI を上書きすることも、ZIP archive URL を直接指定することもできます。

Windows から ResoniteLink 経由で Resonite にライブ構築する例:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source server \
  --resonitelink-port <port>
```

ライブ経路は `ws://localhost:<port>/` に接続し、公式の ResoniteLink import message で mesh / texture asset を送信し、dataset / mesh-code slot を作成した上で `StaticMesh`、`StaticTexture2D`、`MeshRenderer`、`PBS_Metallic`、`MeshCollider` を構築します。city object は逐次送信するため、大きい mesh code でも live 出力前に全件バッチを保持しません。JSON アーティファクト出力も同時に残します。

現行の live adapter は、mesh には `ImportMesh(ImportMeshRawData)`、texture には `ImportTexture(ImportTexture2DFile)` を使います。現在の ResoniteLink runtime では raw-data 経路の方が利用可能な mesh asset URL を返すためです。

フォーマット、Analyzer、テストを検証します。

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release
```
