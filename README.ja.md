# Plateau.ResoniteLink

<img width="2560" height="1440" alt="2026-04-08 03 02 41" src="https://github.com/user-attachments/assets/7dac58c7-8855-4362-855d-f12e884dc05e" />

Plateau.ResoniteLink は、[PLATEAU](https://www.mlit.go.jp/plateau/) の CityGML データセットを [ResoniteLink](https://github.com/Yellow-Dog-Man/ResoniteLink) 経由で Resonite に逐次送信する .NET 10 CLI です。インポート挙動と用語は [PLATEAU SDK for Unity](https://project-plateau.github.io/PLATEAU-SDK-for-Unity/) に揃えています。changelog の正本は GitHub Releases で、各 `vX.Y.Z` release は framework-dependent な CLI asset `Plateau.ResoniteLink-cli-vX.Y.Z.zip` を公開します。

この README は、現在の `beta` branch における、人間向け current scope の正本です。`requirements` のような別文書は復活させず、shipped / experimental / pending / intentionally regressed はここ と tests に揃えます。

## Scope

Shipped:
- ローカルの PLATEAU dataset または explicit な remote CityGML ZIP/7z archive を、起動中の ResoniteLink listener へ送る。
- `ParameterizedTexture` appearance を保持しつつ、mesh / material 順序を決定的に保ち、source texture がない場合は bundled default material に fallback する。
- source bootstrap の完了後は、dataset / mesh-code branch を段階的に構築し、full live send 完了前から Resonite 側に取り込み結果を出し始める。
- LOD1 mesh bake と LOD2 atlas bake は CityGML scope、package、LOD、bake policy をキーにまとめ、emit される bake payload が cityObject の到着順に依存しないように保つ。

Experimental:
- `--resonitelink-connections > 1` は引き続き experimental な live-send mode であり、信頼できる既定経路としては扱わない。

Pending:
- target-agnostic IR の抽出と、`Targets.Resonite` / `Transport.ResoniteLink` の深い責務分離は、この release では完了済み保証に含めない内部 follow-up です。

Intentionally regressed:
- standalone の requirements 文書は release-truth surface としては維持しません。product scope は `README.md` と tests に置き、live-send の operator workflow は `docs/live-testing*.md` に置きます。

## Runtime And Prerequisites

- 対象 runtime は .NET SDK 10。release asset の実行にも .NET 10 が必要。
- `--resonitelink-port` または `--resonitelink-url` で到達できる ResoniteLink listener が必要。
- live adapter の asset import は mesh に `ImportMesh(ImportMeshRawData)` を使います。texture は bundled common material のような元から画像ファイルとして持つ asset ではまず file import を試しますが、listener が送信側ローカルの file path を解決できない場合は自動で `ImportTexture` の raw payload にフォールバックします。dataset 由来または生成された texture は raw payload を使います。
- ResoniteLink の entity ID は session-scoped な opaque value として扱います。create が成功した場合、その session 内で正規 ID として扱うのは resolve 済み `Response` の ID です。requested ID は cityObject 単位の DataModel batch 内で使う参照ヒントに限定し、別 session へ永続化・再利用してはいけません。既存 entity の reuse 探索は、新規 create の確認とは別の仕組みとして扱います。

## Quick Start

pull request を作成または更新する前に、repository の正本となる検証コマンド列を実行します。

```bash
dotnet restore Plateau.ResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

contributor workflow、環境 bootstrap、検証フローの ownership は [CONTRIBUTING.ja.md](CONTRIBUTING.ja.md) を参照してください。

## Usage

ローカル import 例:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset plateau-20202-matsumoto-shi-2020 \
  --mesh-code 54372778 \
  --source local \
  --local-source-path /path/to/plateau \
  --resonitelink-port <port>
```

remote archive import 例:

```bash
dotnet run --project src/Plateau.ResoniteLink.Cli -- \
  build \
  --dataset plateau-20202-matsumoto-shi-2020 \
  --mesh-code 54372788 \
  --source remote \
  --server-url https://example.invalid/plateau-20202-matsumoto-shi-2020_citygml.zip \
  --resonitelink-port <port>
```

`--resonitelink-port` または `--resonitelink-url` は必須です。`--source remote` では direct な `.zip` / `.7z` CityGML archive URL が必要で、組み込みの dataset search は行いません。

CLI は既定でマイルストーン級の進捗だけを表示し、file ごとの詳細や live-send trace は隠します。debug レベルの import / ResoniteLink trace が必要なときは `--verbose` を付けてください。

`--work-root` を省略した場合、CLI は dataset ごとの archive と live temporary file を `local/<dataset>/` 配下に置きます。

## 参考資料

- contributor 向け workflow: [CONTRIBUTING.ja.md](CONTRIBUTING.ja.md)
- operator 向け live workflow: [docs/live-testing.ja.md](docs/live-testing.ja.md)
- Coding Agent 向け live workflow: [.agents/skills/resonite-live-send-debug/SKILL.md](.agents/skills/resonite-live-send-debug/SKILL.md)

## License And Provenance

- repository の source code は [MIT](LICENSE) です。
- import 対象の PLATEAU dataset はこの repository が再 license しません。import、再配布、派生物公開の前に、dataset ごとの README、metadata、配布 page、権利表記を確認してください。
- [PLATEAU Site Policy](https://www.mlit.go.jp/plateau/site-policy/) は portal-level の既定条件であり、dataset 固有条件を上書きしません。
- [PLATEAU Start Guide](https://www.mlit.go.jp/plateau/start-guide/) では、dataset ごとに PDL 1.0、CC BY 4.0、ODC BY、ODbL など異なる条件がありうると案内されています。
- PLATEAU SDK for Unity は別 upstream の MIT licensed project で、license 控えは `THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt` にあります。
- `src/Plateau.ResoniteLink/Assets/DefaultMaterials/` 配下の bundled default material texture は AmbientCG 由来で、追跡メモは `THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt` にあります。
- 既定の DEM terrain imagery overlay は bundled asset ではありません。GSI seamless photo tile endpoint `https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg` から生成し、repository 内の追跡メモは `THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt` にあります。
- NuGet package やその他の runtime dependency には upstream license が適用されます。binary や vendored asset を再配布する前に、実際に出荷する version を確認してください。
