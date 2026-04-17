# Workflow

この reference は `SKILL.md` が発火した後に使ってください。

この file は Coding Agent 向けの補助メモです。repository の current operator-facing workflow、command example、validation sequence には [docs/live-testing.md](../../../docs/live-testing.md) を使ってください。

## Defaults

- 別の dataset が必要でない限り、`plateau-20202-matsumoto-shi-2020` と mesh `54372778` / `54372788` を使う。
- `frn` 検証が必要なときだけ Yokohama mesh `53391530` に切り替える。
- これらは selector であり cache path の保証ではない。cleanup や send の前に actual local source path を確認する。

## Agent Guardrails

- ad hoc command ではなく `.agents/skills/resonite-live-send-debug/scripts/` 配下の bundled script を優先する。
- WSL から listener に `localhost` で到達できないなら Windows 側 wrapper を使う。
- comparison rerun の前には listener discovery をやり直し、`sessionName`、`sessionID`、`linkPort` を run note に残す。
- listener port、process ID、log path、session identity を推測しない。discovery の出力と wrapper の返り値を使う。
- cleanup は destructive。dataset root、matching live-send CLI process、local runtime artifact を消しうる。
- successful validation 後の final `DatasetRoot` は、user が明示しない限り残す。
- `stdout` を解釈する前に `stderr` を見る。`stderr` が空でも stalled 判定前に timestamp 付き log 読みを最低 2 回取る。

## Required Artifacts

この skill 配下に次の file がある前提です。

- `tools/ResoniteAdmin/ResoniteAdmin.csproj`

wrapper は admin utility や CLI binary を必要に応じて build します。dump / cleanup helper では fresh な Windows build output が前提です。

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
