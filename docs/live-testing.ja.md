# Live Testing

## 目的

この手順は、ローカルの PLATEAU dataset を ResoniteLink 経由で実際の Resonite session へ送れるかを、実機レベルで確認したいときに使う英語版正本の翻訳補助です。

この文書は repo の英語版 live-send 手順書に対する operator / human 向け参照です。`.agents/skills/resonite-live-send-debug/scripts/` 配下の bundled script を、この workflow の command surface として扱います。live-send 手順の正本は英語版 [live-testing.md](live-testing.md) であり、日本語版は翻訳補助です。
Coding Agent 固有の実行判断は `.agents/skills/resonite-live-send-debug/SKILL.md` に置き、この手順書は operator に読みやすい形を保ちます。

## 事前条件

- 現在の bundled helper script は Windows 寄りです。対象 ResoniteLink listener が WSL 側から `localhost` で到達できない場合は、helper script を Windows 側から実行します。
- listener が WSL 内で起動され、WSL 側から `localhost` で到達確認できるなら、WSL 起点の live run も有効です。この例外は listener の到達性についてのものであり、listener 自体が Windows process であることを要求するものではありません。
- 素の ResoniteLink は、WebSocket upgrade 時の `Host` ヘッダが `localhost:<port>` と一致しないと基本的に受理しません。sender が WSL、listener が Windows のときは、この既定制約のため reverse proxy 等がない限り sender process を Windows 側に置く必要があります。
- reverse proxy や同等の bridge が host 判定を吸収し、listener から見て妥当な host を維持できるなら、IP 経由の経路も有効です。Windows 固定や WSL 固定で決め打ちせず、実際の到達経路と listener の観測結果で sender 環境を決めてください。
- 破壊的な live run に入る前に、repository の verify flow を実行します。

```bash
dotnet restore Plateau.ResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

- beta の反復中は、`dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity minimal -m:1 --disable-build-servers -p:UseSharedCompilation=false --filter "Category!=Slow"` を使って non-slow 範囲を素早く確認して構いません。ただし、live 結果を信用する前、push 前、PR 更新前には上の完全な検証コマンド列を実行します。

- cleanup や send に入る前に、対象 dataset root がローカルに存在することを確認してください。
- CLI や admin utility の build 産物は事前に無くても構いません。bundled helper script が必要に応じて build します。
- `-Connections` は検証対象の active-lane cap として扱います。まず `-Connections 1` で baseline run を取り、その後に目的の多接続値と比較して invariant を確認してください。
- 下記の cleanup は破壊的です。現在の Resonite session から一致する dataset root を削除し、同じ repository から起動した live-send CLI process も停止します。
- この workflow では、operator 向けの command surface を `.agents/skills/resonite-live-send-debug/scripts/` 配下の bundled script に固定します。
- 破棄可能な listener が必要で、local の Resonite headless install がある場合は、UI で session を手作業で用意するより bundled headless wrapper を優先します。

## Recommended Fixture Parameters

task が別 target を必要としない限り、まずは Matsumoto dataset `plateau-20202-matsumoto-shi-2020` と隣接する detailed-building mesh `54372778` / `54372788` から始めます。これらは document-backed な再現と比較に使う推奨パラメータであり、手順上の必須条件ではありません。

## Headless の直接起動

repository 側で disposable な listener を立ち上げたい場合は、現行 helper script では Windows から headless session を直接起動するのを基本とします。listener を WSL 内で起動し `localhost` 経由で到達できる場合は、その経路も有効です。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\start-headless-session.ps1 -RepoPath C:\path\to\repo -HeadlessPath C:\path\to\Resonite -ResoniteLinkPort <port> -SessionName PlateauHeadlessLive -LogPrefix headless-live"
```

この wrapper は一時的な headless `Config.json` を生成し、`Resonite.exe` または `Resonite.dll` を `-HeadlessConfig` 付きで起動し、`World Running` log line を待ったうえで、UDP discovery が要求した `linkPort` を観測できることまで確認します。

既定では、後続の stop command が同じ disposable session を PID 再入力なしで止められるように、起動結果を `runtime/windows/headless/active-session.json` にも記録します。

wrapper の返り値から次を記録してください。

- `ProcessId`
- `SessionName`
- `SessionId`
- `LinkPort`
- `Endpoint`
- `ConfigPath`
- `StdoutLog`
- `StderrLog`
- `StatePath`

experiment 終了時は、追跡中の disposable headless process を止めます。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\stop-headless-session.ps1 -RepoPath C:\path\to\repo"
```

別の tracked state file や明示的な PID を止めたい場合は、`-StatePath` または `-ProcessId` を渡してください。

## Root Dump の採取

disposable な headless session であれば、Root の full dump は baseline と post-send の world-state artifact として保持してもノイズが比較的少ないはずです。同じ tracked session に対して両方の snapshot を採ることを優先してください。

tracked されている disposable session から Root full dump を採るには、次を使います。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\dump-root-session.ps1 -RepoPath C:\path\to\repo -Label baseline"
```

この wrapper は既定で `runtime/windows/headless/active-session.json` から endpoint を解決します。`-OutputPath` を渡さない限り、再帰的な Root snapshot を `runtime/windows/resonite/root-dumps/` に書き出します。
`ResoniteAdmin` は都度 rebuild され、binary がある場合は `ResoniteAdmin.exe` を優先し、なければ `dotnet` + `.dll` にフォールバックします。

wrapper の返り値から次を記録してください。

- `Endpoint`
- `OutputPath`
- `Depth`
- `IncludeComponentData`
- `AdminDllPath`
- `AdminDllLastWriteTime`

disposable headless 検証の推奨シーケンス:

1. `start-headless-session.ps1`
2. `dump-root-session.ps1 -Label baseline`
3. `cleanup-session.ps1`
4. `run-live-send.ps1`
5. `dump-root-session.ps1 -Label after-send`
6. `stop-headless-session.ps1`

## Listener Discovery

ResoniteLink の UDP announcement を取るには、bundled discovery script を使います。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\discover-session.ps1 -TimeoutSeconds 20 -MaxAnnouncements 5"
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
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset>"
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\cleanup-session.ps1 -RepoPath C:\path\to\repo -Endpoint ws://localhost:<port>/ -Dataset <dataset> -ListOnly"
```

polling window 内に list mode が 0 件を返した場合だけ次へ進みます。

successful validation の終了時に cleanup を既定で自動実行してはいけません。終了時 cleanup は opt-in とし、user が明示した場合、または disposable headless の teardown のように workflow 自体が明示的に破壊的な場合だけ実施します。最終 run が成功した後は、削除理由が明示されていない限り dataset root を残してください。残した `DatasetRoot` は final の目視確認に使う既定 artifact です。

その後、bundled wrapper で send を起動します。現行 bundled wrapper を使う場合、listener が WSL 側から `localhost` で到達できないなら Windows 側で実行します。WSL 側から `localhost` 到達確認できる listener については、WSL 起点の send も許容します。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\run-live-send.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -LocalSourcePath C:\path\to\dataset-root -Dataset <dataset> -MeshCode <mesh> -DemTerrainMode <heightmap|mesh> -Connections 1 -LogPrefix <name> -NoWait"
```

この wrapper は、明示的に `-SkipBuild` を渡さない限り、send 前に Windows 向け CLI build output を rebuild します。返り値の `CliDllLastWriteTime` が不自然に古い場合や、出力 path が Windows build でない場合は、その run を invalid としてください。

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

connection count の guardrail では、他の入力を変える前に、同じ mode を `-Connections 1` と比較対象の高い値でそれぞれ実行し、log と world state を比較します。

log 比較に加えて world-state の証拠が必要な場合は、最初の run 前と各観測点の後に Root dump を採取します。

標準の comparison driver が必要なら、次を使います。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -ExpectedSessionId <session-id>"
```

comparison driver 自身に disposable な headless listener の起動と停止までさせる場合は、`-HeadlessPath` を追加します。

```bash
cmd.exe /c "powershell -ExecutionPolicy Bypass -File C:\path\to\repo\.agents\skills\resonite-live-send-debug\scripts\compare-modes.ps1 -RepoPath C:\path\to\repo -HeadlessPath C:\path\to\Resonite -ResoniteLinkPort <port> -Dataset <dataset> -MeshCode <mesh> -LocalSourcePath C:\path\to\dataset-root -ObserveSeconds 30 -HeadlessSessionName PlateauHeadlessLive"
```

まず `stderr` を見ます。`stderr` が空でない場合はそれを主な failure signal として扱います。`stderr` が空でも、stall と判断する前に、timestamp 付きの log sample を少なくとも 2 回取ってください。

標準的な log 読み出し:

```powershell
Get-Content <stdout-log> -Tail 40
Get-Content <stderr-log> -Tail 40
```

## Acceptance Signals

live validation run の受け入れ可否は、少なくとも次の観点で判断します。

- deterministic な live payload:
  同じ dataset、mesh code、mode、source path、listener port、connection count で複数回流し、log の並び、dataset root の構造、再利用 asset hierarchy に説明不能な差がないことを確認する。
- bake-scope の guardrail:
  LOD1 mesh bake と LOD2 atlas bake が無関係な CityGML file をまたいで merge していないこと、また input city object の到着順を変えても、決定的な batch identity suffix を除く baked material / mesh payload の内容が変わらないことを確認する。
- 可視な imported content:
  live world に期待した dataset root が現れ、対象 subtree に mesh、material、renderer、collider が揃うことを確認する。
- CI 相当の検証:
  反復中は non-slow の `dotnet test ... --filter "Category!=Slow"` を緑に保ち、live 結果を評価する前には完全な検証コマンド列を再実行する。

途中で run を止めた場合は、起動した PID だけを止め、終了を確認してから cleanup を再実行し、log は保持します。自然終了した run でも、optional な cleanup の前に exit code を記録してください。最終 cleanup を暗黙動作として扱ってはいけません。
