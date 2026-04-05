# Plateau.ResoniteLink

Plateau.ResoniteLink は、[PLATEAU](https://www.mlit.go.jp/plateau/) のデータセットを [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink) 経由で Resonite に取り込むための .NET 10 プロジェクトです。決定的な Resonite 構築データを生成しつつ、city object を逐次的に流すことで、インポート結果が Resonite 上に順次現れるようにします。

インポート挙動と用語は、[PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/) を参考にしています。

リリースタグは `vX.Y.Z` 形式を使います。ビルド成果物の `Version`、`AssemblyVersion`、`FileVersion`、`InformationalVersion` はそのタグから決定し、アセンブリの数値バージョンには `v` prefix を含めません。

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
