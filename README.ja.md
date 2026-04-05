# Plateau.ResoniteLink

Plateau.ResoniteLink は、[PLATEAU](https://www.mlit.go.jp/plateau/) のデータセットを [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink) 経由で Resonite に取り込むための .NET 10 プロジェクトです。CityGML 由来の city object を Resonite へ逐次的に流し、インポート結果が処理中から順次現れるようにします。

インポート挙動と用語は、[PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/) を参考にしています。

リリースタグは `vX.Y.Z` 形式を使います。ビルド成果物の `Version`、`AssemblyVersion`、`FileVersion`、`InformationalVersion` はそのタグから決定し、アセンブリの数値バージョンには `v` prefix を含めません。

## 使い方

依存関係を復元します。

```bash
dotnet restore Plateau.ResoniteLink.sln
```

ローカルのデータセットルートを、起動中の ResoniteLink listener 経由で Resonite に取り込みます。

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 53394525 \
  --source local \
  --local-source-path /path/to/plateau \
  --resonitelink-port <port>
```

`--resonitelink-port` または `--resonitelink-url` は必須です。任意の `--work-root` の既定値は `runtime/<os>/resonite/` で、live 用の生成 asset と remote download cache の保存先としてだけ使います。オプション名は可能な範囲で PLATEAU SDK for Unity に寄せており、`--local-source-path` は `DatasetSourceConfigLocal.LocalSourcePath`、`--server-url` は `DatasetSourceConfigRemote.ServerUrl` に対応します。現行の importer は公式 PLATEAU の `udx/<package>/` prefix 群にまたがるローカル CityGML を読み、deterministic な submesh / material 順序を保ちつつ、詳細モデルの `ParameterizedTexture` appearance も live-ready な mesh / material payload に反映し、live build 全体をメモリ保持せず city object 単位で下流へ流します。

既定の CKAN catalog flow を使って、公式 PLATEAU CityGML ZIP をオンライン取得しつつ Resonite に取り込む例:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source remote \
  --resonitelink-port <port>
```

`--source remote` は既定で公式の `search.ckan.jp` catalog を使い、対応する CityGML ZIP resource を探索して `runtime/<os>/resonite/cache/remote/` にダウンロードし、展開した上で同じ local importer に渡します。`--server-url` で catalog base URI を上書きすることも、ZIP archive URL を直接指定することもできます。

DL 済みデータを再利用する場合は、`--source local --local-source-path ...` に切り替え、`runtime/<os>/resonite/cache/remote/` 配下の展開済み dataset root か、その上位 directory を指定します。importer 側で `udx/` を含む最も近い descendant を自動解決するため、展開先がさらに 1 階層深い場合でも `runtime/<os>/resonite/cache/remote/tokyo23ku/533944/` のような指定で再利用できます。

Windows から ResoniteLink 経由で Resonite にライブ構築する例:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source remote \
  --resonitelink-port <port>
```

ライブ経路は `ws://localhost:<port>/` に接続し、公式の ResoniteLink import message で mesh / texture asset を送信し、dataset / mesh-code slot を作成した上で、PLATEAU の帰属表記を持つ dataset-level の `License` コンポーネントを付与し、その後 `StaticMesh`、`StaticTexture2D`、`MeshRenderer`、`PBS_Metallic`、`MeshCollider` を構築します。city object は逐次送信するため、大きい mesh code でも live 表示前に全件バッチを保持しません。

現行の live adapter は、mesh には `ImportMesh(ImportMeshRawData)`、texture には `ImportTexture(ImportTexture2DFile)` を使います。現在の ResoniteLink runtime では raw-data 経路の方が利用可能な mesh asset URL を返すためです。

フォーマット、Analyzer、テストを検証します。

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release
```
