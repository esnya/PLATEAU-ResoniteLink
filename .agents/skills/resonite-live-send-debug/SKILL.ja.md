---
name: resonite-live-send-debug
description: Run and debug PLATEAU-ResoniteLink live-send reproductions against a real ResoniteLink session. Use when the user wants actual machine-level validation instead of simulated tests, including listener discovery, run cleanup, log capture, and inspection of the resulting Resonite world state.
---

# Resonite Live Send Debug

この skill は real ResoniteLink run でのみ使います。まず local test を優先し、論点が live session、destructive cleanup cycle、または結果としての Resonite world state に依存するときだけこの skill に切り替えます。

この file は repository における live-send workflow の Coding Agent entrypoint であり、public operator surface の authoritative reference です。詳細な運用手順は [references/workflow.md](./references/workflow.md) に置き、この file では trigger、guardrail、output contract に集中します。

## When To Use

- actual ResoniteLink listener に対する real live-send reproduction。
- log、process state、または結果の live world 観測が必要な検証。
- verification loop の一部としての session cleanup、root dump、headless-session bring-up。

## When Not To Use

- code-only review、static log 読み、documentation 作業。
- live session なしで十分に証明できる local/unit/integration test。
- current dataset root の destructive cleanup を許容できない task。

## Guardrails

- cleanup は destructive とみなします。live dataset root を削除し、`runtime/windows/resonite` 配下の local runtime artifact も消し得ます。
- 自分で直接 live send を実行できるなら、user に実行を依頼しません。
- relevant dataset root の cleanup が検証されるまで、run 同士を比較しません。
- cleanup が明示的に要求されない限り、最後に成功した `DatasetRoot` は残します。
- cleanup と post-run state の両方が検証されるまで、中断 run や partial run は provisional とみなします。
- operator surface は direct `dotnet run --project ...` command に限定します。thin wrapper script を再導入しません。
- `--start-headless` も direct tool surface の一部ですが、actual headless launcher path 自体は Windows-only のままになり得ます。unsupported environment は tool に明示的に拒否させ、WSL から Windows へ橋渡しする helper は使いません。

## Guide Surface

- Canonical guide: [references/workflow.md](./references/workflow.md)
- Japanese mirror: [references/workflow.ja.md](./references/workflow.ja.md)

次の内容は guide を使います。

- recommended dataset と fixture value
- fixed run worksheet と comparison checklist
- component discovery と BoxCollider inspection procedure
- version-scoped readback limit と reference artifact
- CLI / session tool の direct command example

## Operator Surface

operator-facing direct command は次だけを使います。

- `dotnet run --project src/Plateau.ResoniteLink.Cli/Plateau.ResoniteLink.Cli.csproj -- build ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --discover-session ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --dump-root ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --remove-slot ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --cleanup-dataset-root ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --start-headless ...`
- `dotnet run --project .agents/skills/resonite-live-send-debug/tools/ResoniteSessionTool/ResoniteSessionTool.csproj -- --stop-headless ...`

sandboxed environment では、これらの direct command でも restore/build の escalation が必要になることがあります。`dotnet restore` や `dotnet run` が .NET first-use や permission setup で失敗したら、ad hoc workflow に置き換えず、同じ direct command を必要な sandbox escalation 付きで再実行します。

## Required Outputs

各 live run は次を要約します。

- listener endpoint
- cleanup verification result
- process status と exit code
- exact mode と mesh code
- 最後の timestamped `import` line
- 最後の timestamped `live` line
- `stderr` が空だったか
- world snapshot summary
- root dump path
- observation timestamp
- conclusion が valid か contaminated か
