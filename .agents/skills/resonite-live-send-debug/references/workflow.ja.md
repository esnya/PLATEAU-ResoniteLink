# Workflow

この reference は `SKILL.md` が発火した後に使ってください。

repository の operator-facing workflow は [docs/live-testing.ja.md](../../../docs/live-testing.ja.md) です。この file は cleanup、send、comparison の手順を繰り返さないようにしています。

既定の document fixture:

- 別の dataset が必要でない限り、`plateau-20202-matsumoto-shi-2020` と隣接する detailed-building mesh `54372778` / `54372788` を使う。
- `frn` 検証が必要なときだけ Yokohama mesh `53391530` に切り替える。
- これら fixture choice は dataset / mesh selector であり、cache path の保証ではない。cleanup や file inspection の前に、actual local source path がローカルに存在することを確認する。

## Required Skill Artifacts

この skill 配下に次の file がある前提です。

- `tools/ResoniteAdmin/ResoniteAdmin.csproj`

ad hoc な repository command より bundled skill script を優先してください。これらの wrapper は admin utility や CLI binary を必要に応じて build するため、別の手動 build command は canonical procedure には含めません。
root dump や cleanup の helper を使うときは、`ResoniteAdmin` が都度 rebuild され、実際の dump / cleanup の前に build output が出ること、そして fresh な Windows build output が必須であることを想定してください。Windows の app host がある場合は `ResoniteAdmin.exe` を起動し、なければ freshly built な `.dll` を `dotnet` で起動します。

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
