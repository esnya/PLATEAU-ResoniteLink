# Workflow

この reference は `SKILL.md` が発火した後に使ってください。

この file は repository の canonical な operator-facing live-send workflow です。current procedure の正本は `.agents/skills/resonite-live-send-debug/` 配下に閉じ、tracked な `docs/` には依存しません。

既定の document fixture:

- 別の dataset が必要でない限り、`plateau-20202-matsumoto-shi-2020` と隣接する detailed-building mesh `54372778` / `54372788` を使う。
- `frn` 検証が必要なときだけ Yokohama mesh `53391530` に切り替える。
- これら fixture choice は dataset / mesh selector であり、cache path の保証ではない。cleanup や file inspection の前に、actual local source path がローカルに存在することを確認する。

## Preconditions

- target の ResoniteLink listener が WSL から `localhost` で到達できない場合は、bundled helper script を Windows から使う。
- listener が WSL から `localhost` で到達確認できるなら、WSL 起点の live run も有効。
- bare ResoniteLink は `Host` header が `localhost:<port>` に一致する場合だけ WebSocket upgrade を受ける。sender が WSL、listener が Windows の場合は、許容される host header を保てる bridge がない限り Windows 側実行を優先する。
- destructive な live run の前に repository の検証フローを実行する:

```bash
dotnet restore Plateau.ResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

- 反復中の quick check としては `dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity minimal -m:1 --disable-build-servers -p:UseSharedCompilation=false --filter "Category!=Slow"` を使ってよいが、live result や pull request 更新を信用する前の gate は上の full sequence。
- cleanup や send の前に target dataset root がローカルに存在することを確認する。
- `-Connections` は active-lane cap として扱い、より高い値を比較する前に `-Connections 1` の baseline run を最低 1 回取る。
- cleanup は destructive であり、現在の Resonite session から matching dataset root を削除し、この repository から起動した matching な live-send CLI process を停止する。

## Required Skill Artifacts

この skill 配下に次の file がある前提です。

- `tools/ResoniteAdmin/ResoniteAdmin.csproj`

ad hoc な repository command より bundled skill script を優先してください。これらの wrapper は admin utility や CLI binary を必要に応じて build するため、別の手動 build command は canonical procedure には含めません。
root dump や cleanup の helper を使うときは、`ResoniteAdmin` が都度 rebuild され、実際の dump / cleanup の前に build output が出ること、そして fresh な Windows build output が必須であることを想定してください。Windows の app host がある場合は `ResoniteAdmin.exe` を起動し、なければ freshly built な `.dll` を `dotnet` で起動します。

## Direct Headless Launch

破棄可能な listener が必要なら、bundled wrapper で headless session を直接起動する:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\start-headless-session.ps1 -RepoPath C:\path\to\repo -HeadlessPath C:\path\to\Resonite -ResoniteLinkPort <port> -SessionName PlateauHeadlessLive -LogPrefix headless-live"
```

wrapper の出力から次を記録する:

- `ProcessId`
- `SessionName`
- `SessionId`
- `LinkPort`
- `Endpoint`
- `ConfigPath`
- `StdoutLog`
- `StderrLog`
- `StatePath`

実験が終わったら tracked された disposable headless process を停止する:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\stop-headless-session.ps1 -RepoPath C:\path\to\repo"
```

disposable headless 検証では次の sequence を優先する:

1. `start-headless-session.ps1`
2. `dump-root-session.ps1 -Label baseline`
3. `cleanup-session.ps1`
4. `run-live-send.ps1`
5. `dump-root-session.ps1 -Label after-send`
6. `stop-headless-session.ps1`

## Root Dump Capture

tracked された disposable session から full Root dump を採取する:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\dump-root-session.ps1 -RepoPath C:\path\to\repo -Label baseline"
```

wrapper の出力から次を記録する:

- `Endpoint`
- `OutputPath`
- `Depth`
- `IncludeComponentData`
- `AdminDllPath`
- `AdminDllLastWriteTime`

## Listener Discovery

bundled discovery script で UDP `12512` announcement を取得する:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\discover-session.ps1 -TimeoutSeconds 20 -MaxAnnouncements 5"
```

各 comparison run の前に次を記録する:

- `sessionName`
- `sessionID`
- `linkPort`

Rules:

- `sessionID` が得られるなら UDP discovery を優先する。
- discovery と in-game UI が異なる session を指すなら、その run は invalid。
- comparison rerun の前に discovery をやり直し、同一 session identity を再確認する。

## Cleanup And Send

各 comparison run の前に dataset root を削除し、matching root が 0 であることを確認する:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset>"
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset> -ListOnly"
```

polling window 内に list mode が matching dataset root 0 を報告した場合だけ続行する。

successful validation の最後に cleanup を自動実行しない。user が明示した場合か workflow 自体が明示的に destructive な場合を除き、final `DatasetRoot` は残す。

bundled wrapper で send を起動する:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath C:\path\to\dataset-root -Dataset <dataset> -MeshCode <mesh> -DemTerrainMode <heightmap|mesh> -Connections 1 -LogPrefix <name> -NoWait"
```

wrapper は次を返す:

- `ProcessId`
- `StdoutLog`
- `StderrLog`
- `CliDllPath`
- `CliDllLastWriteTime`

これらの値を使い、process id や log path を推測しない。

## Comparison And Validation

mode-sensitive な comparison run では次の入力を固定する:

- dataset
- mesh code
- local source path
- listener port
- connection count

推奨 sequence:

1. `heightmap`
2. cleanup
3. `mesh`
4. cleanup
5. `heightmap`

bundled comparison driver が必要なら次を使う:

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -ExpectedSessionId <session-id>"
```

まず `stderr` を確認し、非空なら primary failure signal として扱う。`stderr` が空でも、stalled と結論づける前に timestamp 付き log sample を最低 2 回取る。

launch 後の canonical な log 読み出し:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

## Acceptance Signals

live validation run を acceptable とみなすチェック:

- 同一 dataset、mesh code、mode、source path、listener port、connection count で再実行しても live payload が決定的である。
- LOD1 mesh bake と LOD2 atlas bake が無関係な CityGML file を跨いで merge しない。
- 期待した dataset root と import content が live world に見える。
- 反復中は non-slow test を green に保ち、live result を信用する前に full verification sequence を通す。

## Skill Guardrails

この skill にだけ残す実務 rule:

- UDP `12512` announcement を受けるのに十分待つ。
- `sessionName`、`sessionID`、`linkPort` を取得する。
- 解決した `linkPort` は run note と一緒に保持する。
- listener が不在で disposable な headless install が使えるなら、bundled headless wrapper で直接起動する。そうでなければ停止し、user に Resonite を再起動してもらう。
- disposable な headless session では、send 前の baseline Root dump と send 後の post-send Root dump を優先する。
- UDP discovery が `sessionID` を返すならそれを優先し、返さなければ UI による明示確認を必須にする。
- UDP と UI が異なる session を指すなら、その run は invalid とする。
- 比較 rerun の前に listener を再発見し、同じ session identity を再確認する。
- listener port、process ID、log path を推測しないこと。discovery の出力と wrapper の返り値を使ってください。
- `.agents/skills/resonite-live-send-debug/scripts/` 配下の bundled script を、この skill の live-test 実行面全体として扱います。
- 警告: cleanup は destructive です。live world から dataset root を削除し、同じ repository から起動した matching な live-send CLI process を停止し、local runtime artifact も消します。

send wrapper は次の property を持つ PowerShell object を返します。

- `ProcessId`
- `StdoutLog`
- `StderrLog`
- `CliDllPath`
- `CliDllLastWriteTime`

これらの値を使ってください。log path や process id を推測してはいけません。

launch 後の canonical な log 読み出し:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

WSL から:

```bash
tail -n 40 /mnt/c/path/to/stdout.log
tail -n 40 /mnt/c/path/to/stderr.log
```

## Script Inventory

- `scripts/discover-session.ps1`
  UDP `12512` の live ResoniteLink announcement を取得する。
- `scripts/start-headless-session.ps1`
  disposable な Windows headless session を直接起動し、announcement された ResoniteLink port を検証する。
- `scripts/stop-headless-session.ps1`
  experiment 用に起動した tracked headless PID、または明示指定した PID を停止する。
- `scripts/dump-root-session.ps1`
  tracked 済み、または明示指定した session から再帰的な Root snapshot を採取する。
- `scripts/cleanup-session.ps1`
  live world から dataset root を削除し、残存 CLI process を停止し、local runtime artifact を消す。
- `scripts/run-live-send.ps1`
  Windows 側で 1 回の live send を explicit log 付きで起動する。
- `scripts/compare-modes.ps1`
  cleanup を挟んだ標準 `heightmap -> mesh -> heightmap` 比較を実行する。
- `scripts/check-matsumoto-base-append-heightmap-19001.ps1`
  `19001` で Matsumoto `54372778 -> 54372788` の base/append 検証を `heightmap` mode で固定実行し、base 送信前 / base 送信後 / append 送信後の root dump を採る。
