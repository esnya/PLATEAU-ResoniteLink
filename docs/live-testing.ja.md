# Live Testing

## 対象

この手順は、起動中の ResoniteLink listener に対する実機レベルの確認だけに使う。

警告: この手順中の cleanup は、現在の Resonite session にある live-world の成果を破壊しうる。破棄してよい実験用 session で使うか、dataset root を削除してよいことを明示的に確認してから実行する。

## 先に Dry-Run

どの live send より前にも、同じ import 条件で一度 `--dry-run` を実行する。

- `--dry-run` は Resonite へ接続せずに、dataset resolution、archive / local source の検査、construction source の準備、city object の streaming までを通す。
- 実セッションを触れないまま前段を確認したい場合、このリポジトリでは `--dry-run` を必須の preflight とする。
- `--dry-run` は `--resonitelink-port`、`--resonitelink-url`、`--resonitelink-connections`、`--send-metrics` を意図的に受け付けない。検証対象は live socket path ではなく、その前の import pipeline である。

例:

```powershell
dotnet run --project src/Plateau.ResoniteLink.Cli -- `
  build `
  --dataset <dataset> `
  --mesh-code <mesh-code> `
  --source local `
  --local-source-path <dataset-root> `
  --dry-run
```

real-session の send 許可を取りに行く前の gate は、green の dry run と repository の CI check 一式とする。

## Live Contract Rules

- ResoniteLink の component payload は、自由な property bag ではなく strict な transport contract として扱う。
- target の Resonite component type に定義されていない member を送ってはいけない。source fingerprint のような local cache metadata は、live component member とは別に持つ。
- Resonite 側が nullable enum field を使う component では、単なる enum field で代用せず、対応する ResoniteLink field type を使う。
- 実機 run に時間を使う前に、少なくとも次を落とせる local test seam を追加または更新する。
  - `AddComponent` 後に `GetComponent` がまだ `null` を返す read-after-write lag
  - 想定外の component member
  - live 書き込み member の field type mismatch

これらの確認は、事後に Resonite runtime log を読むより安価で再現性が高い。Resonite 側の log は、local の contract と test seam を詰め切った後の最後の手段として使う。

## 制約

- ResoniteLink のポートはセッションごとに変わる。ソース管理に固定値を書かない。
- この環境では WSL から Windows 側 listener へ直接到達できない。
- live import は Windows 上で実行し、宛先は `localhost` を使う。
- このリポジトリの CLI 自体は listener port を自動発見しない。したがって、先に有効な listener port を確認し、その値を CLI に明示指定する必要がある。

## Port の見つけ方

ResoniteLink の port を、単なる `Renderite.Host.exe` の listener から推測してはいけない。UnitySDK が使っているのと同じ情報源を使う。

- ResoniteLink は UDP `12512` で active session を announce する。
- announce payload には `sessionName`、`sessionID`、`linkPort` が入る。
- UnitySDK の `AutoDiscovery` UI は、実際には `ResoniteLink.LinkSessionListener` が UDP `12512` を listen して `linkPort` を読んでいるだけである。

Unity なしの運用手順:

1. Resonite を起動し、対象 world を開くか join する。
2. Resonite の dash で `Session` を開き、`Enable Resonite Link` を押す。
3. Session settings に表示される `ResoniteLink running on port: <port number>` を読めるなら、それを使う。
4. UI だけでは足りない場合や複数 session がある場合は、UDP `12512` の announce を listen し、受信 JSON の `linkPort` を読む。
5. 発見した `sessionName` / `sessionID` を、対象 world と照合する。
6. 見つかった port をこのリポジトリの CLI に `--resonitelink-port` で渡すか、完全な endpoint を `--resonitelink-url` で渡す。

announce ベースで確認する最小の Windows PowerShell パターン:

```powershell
$udp = [System.Net.Sockets.UdpClient]::new(12512)
$udp.Client.ReceiveTimeout = 20000

try {
  $remote = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
  $bytes = $udp.Receive([ref]$remote)
  $json = [System.Text.Encoding]::UTF8.GetString($bytes)
  $json | ConvertFrom-Json
}
finally {
  $udp.Close()
}
```

返ってくる object には `sessionName`、`sessionID`、`linkPort` が含まれるはずである。

discovery 時の実務上の注意:

- 単発の短い待機で判定しない。実運用では、少なくとも 20 秒の receive window を取ってから失敗扱いにする。
- それでも announce を受け取れない場合でも、すぐに port 無効と決めつけず、`Enable Resonite Link` を維持したまま同じ listener 確認を再試行する。
- 複数 announce が想定される場合は、最初の 1 packet を盲信せず、複数 packet を取り、対象 world に一致する `sessionName` / `sessionID` を選ぶ。

失敗時の扱い:

- live send が `SocketException (10061)` などの connection refused で失敗した場合、その port で listener が有効ではない前提で扱う。
- まず `Enable Resonite Link` を再確認し、その後 Session UI か、上記の長めの receive window を使った新しい UDP `12512` announce で port を再確認する。

## WSL の勘所

- WSL プロセスから見た `ws://localhost:<port>/` は Windows host ではなく WSL 自身を向く。
- そのため、編集や検証を WSL で行っていても、live test の実送信だけは Windows 側の `dotnet` process で起動する必要がある。
- WSL から起動する場合は、Windows filesystem 上の repository へ移動したうえで `cmd.exe` または PowerShell 経由で送る。

WSL からの例:

```bash
cmd.exe /c "cd /d C:\path\to\repo && dotnet.exe run --project src\Plateau.ResoniteLink.Cli\Plateau.ResoniteLink.Cli.csproj -- build --dataset <dataset> --mesh-code <mesh-code> --source local --local-source-path <windows-dataset-root> --resonitelink-port <port>"
```

## Windows 実行

Windows PowerShell または Command Prompt から、実際の CLI import を実行する。

```powershell
dotnet run --project src/Plateau.ResoniteLink.Cli -- `
  build `
  --dataset <dataset> `
  --mesh-code <mesh-code> `
  --source local `
  --local-source-path <dataset-root> `
  --resonitelink-port <port>
```

remote を使う場合は、source 固有の引数を次の形に切り替える。

```powershell
dotnet run --project src/Plateau.ResoniteLink.Cli -- `
  build `
  --dataset <dataset> `
  --mesh-code <mesh-code> `
  --source remote `
  --server-url <direct-citygml-zip-or-7z-url> `
  --resonitelink-port <port>
```

`--local-source-path <dataset-root>` は Unity SDK の `LocalSourcePath` 命名に合わせており、dataset directory、ZIP/7z archive、または `udx/` を含む nested dataset root を配下に持つ ancestor directory を指せる。
`--server-url` には official PLATEAU の direct な CityGML ZIP/7z archive URL を指定する必要がある。CLI は dataset search を行わない。download した archive は `runtime/<os>/resonite/cache/remote/<dataset>/<archive-hash>/` に cache され、archive URL が同じである限り mesh code が変わっても再利用される。あとから `--source local --local-source-path ...` で再利用することもできる。

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
    '--local-source-path', 'C:\path\to\dataset-root',
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
- 長時間の送信では、process 終了まで stdout に有意な内容が出ないことがある。redirect した log と最終 exit code を主な判定材料にする。

この経路では、公式の ResoniteLink asset import message を使う。

- `ImportMesh(ImportMeshRawData)`
- `ImportTexture(ImportTexture2DFile)`

コマンドは mesh / texture asset を ResoniteLink 経由で直接送信し、import した asset URL を参照する Resonite の slot / component を live 構築する。

## Done 条件

次のすべてを満たしたときだけ、その実行を done とみなす。

- CLI が成功終了する
- Resonite 上で、指定 mesh code の geometry が見える
- material が見えている
- collider の挙動が想定どおりである

見た目が崩れている場合は、現在の import contract と live adapter を調整する。transport-level probe tooling をこのリポジトリへ再導入しない。
