# Live Testing

## 目的

この手順は、ローカルの PLATEAU dataset を ResoniteLink 経由で実際の Resonite session へ送れるかを、実機レベルで確認したいときの正本です。

この文書を repository の canonical な live-send workflow かつ唯一の手順書とします。`skills/resonite-live-send-debug/` 配下の skill は、default、guardrail、報告要件だけを補足し、競合する手順を定義しません。

## 事前条件

- live testing は現在、Windows 上の Resonite session と PowerShell helper script を前提にした運用です。
- 破壊的な live run に入る前に、repository の verify flow を実行します。

```bash
bash scripts/verify-ci.sh
```

- cleanup や send に入る前に、対象 dataset root がローカルに存在することを確認してください。
- CLI や admin utility の build 産物は事前に無くても構いません。bundled helper script が必要に応じて build します。
- 下記の cleanup は破壊的です。現在の Resonite session から一致する dataset root を削除し、同じ repository から起動した live-send CLI process も停止します。
- この workflow では、operator 向けの command surface を `skills/resonite-live-send-debug/scripts/` 配下の bundled script に固定します。root `scripts/` 配下の PowerShell helper は下位の repository utility であり、live run の手順正本ではありません。

## Listener Discovery

ResoniteLink の UDP announcement を取るには、bundled discovery script を使います。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\discover-session.ps1 -TimeoutSeconds 20 -MaxAnnouncements 5"
```

各比較 run の前に、次を記録してください。

- `sessionName`
- `sessionID`
- `linkPort`

ルール:

- `sessionID` が取れるなら UDP discovery を優先します。
- discovery と in-game UI が別 session を指すなら、その run は invalid とします。
- 比較の各 rerun 前に discovery をやり直し、同じ session identity を再確認します。

## Cleanup と Send

各比較 run の前に、bundled cleanup wrapper で dataset root を削除し、matching root が 0 件に収束したことを確認します。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset>"
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset> -ListOnly"
```

polling window 内に list mode が 0 件を返した場合だけ次へ進みます。

その後、bundled wrapper を使って Windows 側から send を起動します。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath C:\path\to\dataset-root -Dataset <dataset> -MeshCode <mesh> -DemTerrainMode <heightmap|mesh> -Connections 8 -LogPrefix <name> -NoWait"
```

wrapper は次を返します。

- `ProcessId`
- `StdoutLog`
- `StderrLog`
- `CliDllPath`
- `CliDllLastWriteTime`

process id や log path は推測せず、返り値をそのまま使ってください。

helper script は既定で per-run の stdout / stderr log を保持します。比較のため、固有名のまま残してください。

## 比較と検証

mode 差分を見たい場合でも、次は run 間で固定します。

- dataset
- mesh code
- local source path
- listener port
- connection count

推奨シーケンス:

1. `heightmap`
2. cleanup
3. `mesh`
4. cleanup
5. `heightmap`

標準の comparison driver が必要なら、次を使います。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -ExpectedSessionId <session-id>"
```

まず `stderr` を見ます。`stderr` が空でない場合はそれを主な failure signal として扱います。`stderr` が空でも、stall と判断する前に、timestamp 付きの log sample を少なくとも 2 回取ってください。

標準的な log 読み出し:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

## Acceptance Signals

`docs/requirements.md` の受け入れ条件には、少なくとも次で対応づけます。

- deterministic な live payload:
  同じ dataset、mesh code、mode、source path、listener port、connection count で複数回流し、log の並び、dataset root の構造、再利用 asset hierarchy に説明不能な差がないことを確認する。
- 可視な imported content:
  live world に期待した dataset root が現れ、対象 subtree に mesh、material、renderer、collider が揃うことを確認する。
- CI 相当の検証:
  `bash scripts/verify-ci.sh` が成功している状態で live 結果を評価する。

途中で run を止めた場合は、起動した PID だけを止め、終了を確認してから cleanup を再実行し、log は保持します。自然終了した run でも、cleanup の前に exit code を記録してください。
