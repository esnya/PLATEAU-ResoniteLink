# Live Testing

## 対象

この手順は、起動中の ResoniteLink listener に対する実機レベルの確認だけに使う。

## 制約

- ResoniteLink のポートはセッションごとに変わる。ソース管理に固定値を書かない。
- この環境では WSL から Windows 側 listener へ直接到達できない。
- live import は Windows 上で実行し、宛先は `localhost` を使う。

## Windows 実行

Windows PowerShell または Command Prompt から、実際の CLI import を実行する。

```powershell
dotnet run --project src/Plateau.ResoniteLink.Cli -- `
  build `
  --dataset <dataset> `
  --mesh-code <mesh-code> `
  --source <local-or-server> `
  --resonitelink-port <port>
```

`--source local` を使う場合は、追加で `--input <dataset-root>` を渡す。
`--source server` を使う場合は、`--server-url` で上書きしない限り、CLI は既定の `search.ckan.jp` catalog flow から公式 PLATEAU CityGML ZIP を取得する。

この経路では、公式の ResoniteLink asset import message を使う。

- `ImportMesh(ImportMeshRawData)`
- `ImportTexture(ImportTexture2DFile)`

コマンドはローカルに JSON artifact を残しつつ、import した asset URL を参照する Resonite の slot / component を live 構築する。

## Done 条件

次のすべてを満たしたときだけ、その実行を done とみなす。

- CLI が成功終了する
- Resonite 上で、指定 mesh code の geometry が見える
- material が見えている
- collider の挙動が想定どおりである

見た目が崩れている場合は、現在の import contract と live adapter を調整する。transport-level probe tooling をこのリポジトリへ再導入しない。
