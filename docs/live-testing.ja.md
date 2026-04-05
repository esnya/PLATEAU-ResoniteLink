# Live Testing

## 対象

この手順は、起動中の ResoniteLink listener に対する実機レベルの確認だけに使う。

## 制約

- ResoniteLink のポートはセッションごとに変わる。ソース管理に固定値を書かない。
- この環境では WSL から Windows 側 listener へ直接到達できない。
- live import は Windows 上で実行し、宛先は `localhost` を使う。

## WSL の勘所

- WSL プロセスから見た `ws://localhost:<port>/` は Windows host ではなく WSL 自身を向く。
- そのため、編集や検証を WSL で行っていても、live test の実送信だけは Windows 側の `dotnet` process で起動する必要がある。
- WSL から起動する場合は、Windows filesystem 上の repository へ移動したうえで `cmd.exe` または PowerShell 経由で送る。

WSL からの例:

```bash
cmd.exe /c "cd /d C:\path\to\repo && dotnet.exe run --project src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj -- build --dataset <dataset> --mesh-code <mesh-code> --source local --input <windows-dataset-root> --resonitelink-port <port>"
```

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

長時間の送信では、インラインの `cmd.exe /c dotnet ...` よりも、小さな Windows PowerShell ラッパーを使う方が安定する。この環境では、`Start-Process -Wait -PassThru` と stdout/stderr の redirect を使う形の方が、WSL interop 由来の詰まりを切り分けやすい。

最小パターン:

```powershell
$process = Start-Process `
  -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
  -ArgumentList @(
    'C:\path\to\Plateau.ResoniteLink.Cli.dll',
    'build',
    '--dataset', '<dataset>',
    '--mesh-code', '<mesh-code>',
    '--source', 'local',
    '--input', 'C:\path\to\dataset-root',
    '--resonitelink-port', '<port>'
  ) `
  -Wait `
  -PassThru `
  -RedirectStandardOutput 'send.stdout.log' `
  -RedirectStandardError 'send.stderr.log'

$process.ExitCode
```

運用上の注意:

- 実送信は Windows 上で行い、dataset path も `C:\...` 形式で渡す。
- 大きい mesh code では、JSON artifact の生成が終わっても live 送信はその後しばらく継続する。
- 長時間の送信では、process 終了まで stdout に有意な内容が出ないことがある。redirect した log と最終 exit code を主な判定材料にする。

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
