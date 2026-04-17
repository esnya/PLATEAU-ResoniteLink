---
name: resonite-live-send-debug
description: 実際の ResoniteLink session に対して PLATEAU-ResoniteLink の live-send 再現と調査を行う skill。simulated test ではなく machine-level の検証が必要なときに使い、listener discovery、run 間の DatasetRoot cleanup、`heightmap` と `mesh` の比較、log 採取、生成された Resonite world state の確認まで扱う。
---

# Resonite Live Send Debug

この skill は実際の ResoniteLink run にだけ使ってください。まず local test を優先し、failure が live な session や生成された Resonite world state に依存するときだけこの skill に切り替えます。

警告: この workflow の cleanup は、現在の Resonite session の live world result を破壊し、この repository から起動した matching な live-send CLI process を停止することがあります。明示的に破棄可能な experiment session でだけ使うか、現在の `DatasetRoot` と関連結果を破壊してよいことを user が明確に承認した後に使ってください。

code-only review や static な log 読みには使わないでください。実際の live send、実際の Resonite world inspection、または machine-level execution がないと無効な比較が必要なときにだけ使います。

このファイルは live-send を実行する Coding Agent 用 playbook であり、この repository における authoritative な live-send workflow reference です。

## Dataset Defaults

この section の dataset / mesh は、手順上の必須条件ではなく推奨パラメータとして扱ってください。

user が別 target を指定しない限り、document-backed な再現と比較では Matsumoto dataset `plateau-20202-matsumoto-shi-2020` と、隣接する detailed-building mesh `54372778` / `54372788` を使ってください。この 2 mesh を default fixture とする理由は、この repository に `54372778` の成功した live-send 証跡がすでにあり、現在の workspace dataset sample に両方の mesh の detailed-building source file が含まれているためです。

task が `frn` または city-furniture content を必要とする場合だけ、Yokohama mesh `53391530` を使ってください。この repository にはその mesh の成功した Yokohama live-send log があり、現在の workspace dataset sample に必要な `frn` source が含まれています。上の Matsumoto default pair は building 中心の確認用です。

これら fixture について、固定の on-disk cache layout を仮定してはいけません。現在の dataset resolver の挙動から actual local source path を解決し、dataset root がローカルに存在することを確認したうえで、requested dataset と mesh が destructive step の前に妥当であることを、既存の live-send 証跡や repository fixture で確認してください。

## Canonical Procedure

bundled helper script を直に組み合わせて実行してください。このファイルは Coding Agent 向けの guardrail、default、workflow、run worksheet を保持し、[references/workflow.md](./references/workflow.md) には agent-oriented な補助情報を置きます。

送信実行の判断は次の原則で行います。

- target listener が WSL から `localhost` で到達できない場合は、Windows 側で helper script を実行します。
- sender と listener が同一ホストで、WSL 側から `localhost` 到達が確認できるなら WSL 起点の送信も有効です。
- reverse proxy などで host 判定が listener 目線で許容可能に変換されるなら、IP 経由も有効になる場合があります。OS 固定ではなく、実際の到達経路と session 識別結果で判断します。

[references/workflow.ja.md](./references/workflow.ja.md) は、Coding Agent 向けの補助メモと bundled script inventory を保持するために使ってください。

disposable な headless 検証では、次の operator sequence を優先してください。

1. `start-headless-session.ps1`
2. `dump-root-session.ps1 -Label baseline`
3. `cleanup-session.ps1`
4. `run-live-send.ps1`
5. `dump-root-session.ps1 -Label after-send`
6. `stop-headless-session.ps1`

`19001` で Matsumoto `54372778 -> 54372788` の fixed base/append 検証を行う場合は、次の順で helper を直実行してください。

1. `cleanup-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Dataset plateau-20202-matsumoto-shi-2020`
2. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-baseappend-baseline`
3. `run-live-send.ps1 -RepoPath <repo> -ResoniteLinkPort 19001 -LocalSourcePath <archive> -Dataset plateau-20202-matsumoto-shi-2020 -MeshCode 54372778 -DemTerrainMode heightmap -Connections 1 -LogPrefix matsumoto-base-heightmap-19001`
4. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-base-heightmap-after-send`
5. `run-live-send.ps1 -RepoPath <repo> -ResoniteLinkPort 19001 -LocalSourcePath <archive> -Dataset plateau-20202-matsumoto-shi-2020 -MeshCode 54372788 -DemTerrainMode heightmap -Connections 1 -LogPrefix matsumoto-append-heightmap-19001`
6. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-append-heightmap-after-send`

## Run Worksheet

比較 run の間では、次の事実を固定するか、変更したら明示的に更新してください。

- dataset
- mesh code
- local source path
- listener port
- session name
- session id
- connection count
- mode
- log prefix
- launched PID
- launched CLI binary path と last write time

## Rules

- 自分で live send を実行できるなら、user に代わりに実行させない。
- world-side cleanup が確認される前に run を比較しない。
- redirected stdout が途切れただけで hang と判定しない。source parsing など同一 process 内で進んでいる仕事を除外してから判断する。
- ResoniteLink port を source control や skill に hard-code しない。
- `-NoWait` run の結論は、process exit を観測するか provisional と明記するまで final として扱わない。
- successful validation の最終 `DatasetRoot` は、明示的な cleanup 指示がない限り目視確認用 artifact として残す。
- root-only cleanup で orphan descendant が存在しないと断定しない。汚染の可能性があるなら明記する。
- failed / interrupted run 後の構造上の結論は、orphan audit がない限り provisional とする。

## Bundled Scripts

- `scripts/discover-session.ps1`
UDP `12512` の live ResoniteLink announcement を取得する。
- `scripts/start-headless-session.ps1`
破棄可能な Windows headless session を直接起動し、announcement された ResoniteLink port まで確認する。
- `scripts/stop-headless-session.ps1`
experiment 用に起動した tracked headless PID、または明示指定した PID を停止する。
- `scripts/dump-root-session.ps1`
tracked 済み、または明示指定した session から再帰的な Root snapshot を採取する。
- `scripts/cleanup-session.ps1`
live world から dataset root を消し、残存 CLI process を止め、local runtime artifact を消す。
- `scripts/run-live-send.ps1`
1 回の live send を explicit log 付きで起動する。
- `scripts/windows-build-tools.ps1`
他の script から使う Windows 側 `dotnet` / ResoniteAdmin build 解決 helper。

上記 7 path はすべて `.agents/skills/resonite-live-send-debug/` からの相対 path です。

## Outputs

- 各 run の stdout / stderr log は repository runtime directory 配下に distinct な name で保持する。
- 観測面は CLI の stdout / stderr log と direct helper stdout とする。ad-hoc な wrapper return object を前提にしない。
- 各 run は次で要約する。
  - listener endpoint
  - cleanup verification result
  - process status と exit code
  - exact な mode と mesh code
  - 最後の timestamped `import` line
  - 最後の timestamped `live` line
  - `stderr` が空だったか
  - world snapshot: dataset root count、top-level child slot 名、疑わしい slot component count
  - baseline / post-send で採取した root dump path
  - log と world-state 観測時刻
  - 結論が valid か contaminated か
