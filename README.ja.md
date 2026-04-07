# Plateau.ResoniteLink

Plateau.ResoniteLink は、[PLATEAU](https://www.mlit.go.jp/plateau/) のデータセットを [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink) 経由で Resonite に取り込むための .NET 10 プロジェクトです。CityGML 由来の city object を Resonite へ逐次的に流し、インポート結果が処理中から順次現れるようにします。

インポート挙動と用語は、[PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/) を参考にしています。

リリースタグは `vX.Y.Z` 形式を使います。ビルド成果物の `Version`、`AssemblyVersion`、`FileVersion`、`InformationalVersion` はそのタグから決定し、アセンブリの数値バージョンには `v` prefix を含めません。

## Scope

- ローカル folder か、公式 CKAN ベースの remote ZIP/7z flow から PLATEAU CityGML データセットを読み取り、起動中の ResoniteLink listener へ送る。
- `ParameterizedTexture` appearance を保持しつつ、mesh / material 順序を決定的に保ち、source texture がない場合は同梱 default material に fallback する。
- dataset / mesh-code branch を段階的に構築し、大きい import でも全処理完了前から Resonite 側に結果を出し始める。

## Known Limitations

- 現在公開している表面は CLI の live-send pipeline が中心で、standalone の offline exporter や Resonite 内 authoring workflow はまだない。
- remote import は現時点で公式 PLATEAU catalog / ZIP/7z discovery flow を前提とし、展開後は同じ local importer を使う。
- live adapter は現在の ResoniteLink runtime に合わせ、mesh には `ImportMesh(ImportMeshRawData)`、texture には `ImportTexture(ImportTexture2DFile)` を使っている。

## Runtime And Prerequisites

- 対象 runtime: .NET SDK 10。
- 前提: `--resonitelink-port` または `--resonitelink-url` で到達できる ResoniteLink listener が起動していること。
- live test 手順: [docs/live-testing.ja.md](docs/live-testing.ja.md) を参照。
- default material と DEM terrain imagery の出所・追跡情報: [docs/default-materials.ja.md](docs/default-materials.ja.md)、`THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt`、`THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt` を参照。

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
  --packages dem,bldg,brid,frn,tran,rwy,trk,tun,ubld,unf,veg \
  --source local \
  --local-source-path /path/to/plateau \
  --resonitelink-port <port> \
  --resonitelink-connections 4 \
  --send-metrics
```

`--resonitelink-port` または `--resonitelink-url` は必須です。`--work-root` の既定値は `runtime/<os>/resonite/` で、live 用の生成 asset と remote download cache の保存先としてだけ使います。`--packages` には公式 PLATEAU の `udx/<package>/` 名をカンマ区切りで指定でき、省略時の CLI 既定値は `dem,bldg,brid,frn,tran,rwy,trk,tun,ubld,unf,veg` です。`--resonitelink-connections` の既定値は `4` です。`--send-metrics` を付けると、`System.Diagnostics.Metrics` による opt-in の計測を有効化し、低カーディナリティの counter / histogram と CLI summary を出します。オプション名は可能な範囲で PLATEAU SDK for Unity に寄せており、`--local-source-path` は `DatasetSourceConfigLocal.LocalSourcePath`、`--server-url` は `DatasetSourceConfigRemote.ServerUrl` に対応します。

既定の CKAN catalog flow を使って、公式 PLATEAU CityGML ZIP/7z archive をオンライン取得しつつ Resonite に取り込む例:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset tokyo23ku \
  --mesh-code 533944 \
  --source remote \
  --resonitelink-port <port>
```

`--source remote` は既定で公式の `search.ckan.jp` catalog を使い、対応する CityGML ZIP/7z resource を探索して `runtime/<os>/resonite/cache/remote/` にダウンロードし、展開した上で同じ local importer に渡します。`--server-url` で catalog base URI を上書きすることも、ZIP/7z archive URL を直接指定することもできます。

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

ライブ経路は `ws://localhost:<port>/` に接続し、既定では複数の ResoniteLink session を張って、公式の ResoniteLink import message で mesh / texture asset を送信し、dataset / mesh-code slot を作成した上で、PLATEAU の帰属表記を持つ dataset-level の `License` コンポーネントを付与し、インポートした scene に必要な Resonite コンポーネントを構築します。共有 slot / component ID の初期化は 1 回だけに畳み込み、対象 session に既に置かれている city object は mesh / material placement 前に skip しつつ、city object の送信は設定した接続数へ分散するため、大きい mesh code でも full batch をメモリ保持せず live 出力を重ねられます。

同じ ResoniteLink session と dataset に対して `build` を再実行すると、既存 dataset 配下に branch を追記します。各 city object は実際に source data を持つ meshcode branch 配下に置かれるため、`53394525` の要求で読み込んだ親 mesh 由来の object は `533945` 配下に、要求固有の object は `53394525` 配下に残ります。mesh-code root は slot の offset で整列するため近接 import を並べて表示でき、既存 meshcode branch にある object は再送しません。

現行の live adapter は、mesh には `ImportMesh(ImportMeshRawData)`、texture には `ImportTexture(ImportTexture2DFile)` を使います。現在の ResoniteLink runtime では raw-data 経路の方が利用可能な mesh asset URL を返すためです。

フォーマット、Analyzer、テストを検証します。

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release -m:1 -p:UseSharedCompilation=false
```

## License Notes

- ルートのソースコードは [MIT](LICENSE) で提供する。
- import 対象の PLATEAU dataset 自体を、この repository が再 license するわけではない。利用条件は元の [PLATEAU Site Policy](https://www.mlit.go.jp/plateau/site-policy/) に従い、現行ポリシーでは、公開コンテンツは概ね PDL 1.0 互換の条件で利用できる一方、出典記載と、加工・編集した場合の明示が必要とされている。
- PLATEAU SDK for Unity は別 upstream の MIT project であり、ライセンス控えを `THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt` に置いている。
- `src/Plateau.ResoniteLink.Cli/Assets/DefaultMaterials/` 配下の同梱 default material texture は AmbientCG 由来で、`THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt` に CC0 1.0 と出所を記録している。
- 既定の DEM terrain imagery overlay は同梱 asset ではない。Geospatial Information Authority of Japan (GSI, 国土地理院) の seamless photo tile endpoint `https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg` から生成しており、出所と利用上の注意は `THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt` に記録している。
- NuGet を含む主要依存は、それぞれ upstream のライセンスに従う。binary や同梱 asset を再配布する前に、実際に出荷する version の package metadata と upstream ライセンス条件を確認する。

PLATEAU guidance:

- [PLATEAU Start Guide](https://www.mlit.go.jp/plateau/start-guide/) では、3D都市モデルの著作権は各地方公共団体に帰属し、dataset ごとに PDL 1.0、CC BY 4.0、ODC BY、ODbL などの open license で提供されると案内されている。
- 派生コンテンツや再配布を行う場合は、元 dataset の attribution を保持し、dataset 個別条件や測量法由来の制約がないかも確認する。
