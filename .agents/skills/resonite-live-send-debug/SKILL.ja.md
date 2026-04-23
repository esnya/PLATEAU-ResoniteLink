---
name: resonite-live-send-debug
description: Run and debug PLATEAU-ResoniteLink live-send reproductions against a real ResoniteLink session. Use when the user wants actual machine-level validation instead of simulated tests, including listener discovery, targeted slot removal, log capture, and inspection of the resulting Resonite world state.
---

# Resonite Live Send Debug

この skill は real ResoniteLink run でのみ使います。まず local test を優先し、論点が live session、destructive cleanup cycle、または結果としての Resonite world state に依存するときだけこの skill に切り替えます。

この file は repository における live-send workflow の Coding Agent entrypoint であり、public operator surface の authoritative reference です。詳細な運用手順は [references/workflow.md](./references/workflow.md) に置き、この file では trigger、guardrail、output contract に集中します。

## When To Use

- actual ResoniteLink listener に対する real live-send reproduction。
- log、process state、または結果の live world 観測が必要な検証。
- verification loop の一部としての targeted slot removal、slot dump、headless-session bring-up。

## When Not To Use

- code-only review、static log 読み、documentation 作業。
- live session なしで十分に証明できる local/unit/integration test。
- current dataset root の destructive cleanup を許容できない task。

## Guardrails

- slot removal は destructive とみなします。current world の live content を削除し得ます。
- 自分で直接 live send を実行できるなら、user に実行を依頼しません。
- targeted removal が検証され、さらに post-removal pre-send root dump で base world state が確認できるまで、run 同士を比較しません。
- dataset root、shared assets、common materials の naming semantics を tool に埋め込みません。`dump-slot` と `remove-slot` は thin primitive としてだけ使います。
- cleanup が明示的に要求されない限り、最後に成功した `DatasetRoot` は残します。
- cleanup と post-run state の両方が検証されるまで、中断 run や partial run は provisional とみなします。
- operator surface は direct `dotnet` command に限定します。thin wrapper script や project-based session tool を再導入しません。
- `start-headless` も direct tool surface の一部です。`--headless-path` がある場合は、その path とその近傍の少数の標準候補だけから launcher を解決します。`--headless-path` が無い場合だけ、標準 Windows Steam install root を試し、その launcher が実際に起動できるかで environment support を判断します。
- 指定した headless path が誤っているときに、無関係な machine-local copy へ勝手に置き換えません。provided path に launcher が無ければ止まり、`references/workflow.md` の標準インストールパス案内を示します。
- この skill は environment bridge を持ちません。ResoniteLink が `localhost` を使う場合は、sender、listener、headless を同一 environment で動かし、その前提を run note に残します。

## Guide Surface

- Canonical guide: [references/workflow.md](./references/workflow.md)
- Japanese mirror: [references/workflow.ja.md](./references/workflow.ja.md)

次の内容は guide を使います。

- recommended dataset と fixture value
- fixed run worksheet と comparison checklist
- component discovery と BoxCollider inspection procedure
- version-scoped readback limit と reference artifact
- CLI / session tool script の direct command example

## Operator Surface

operator-facing direct command は次だけを使います。

- `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- discover-session ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- start-headless ...`
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- stop-headless ...`

sandboxed environment では、これらの direct command でも restore/build の escalation が必要になることがあります。`dotnet restore`、`dotnet run`、または `dotnet <script>.cs` が .NET first-use や permission setup で失敗したら、ad hoc workflow に置き換えず、同じ direct command を必要な sandbox escalation 付きで再実行します。

## Required Outputs

各 live run は次を要約します。

- listener endpoint
- slot-removal verification result
- post-removal pre-send dump result
- process status と exit code
- exact mode と mesh code
- 最後の timestamped `import` line
- 最後の timestamped `live` line
- `stderr` が空だったか
- world snapshot summary
- root dump paths
- observation timestamps
- conclusion が valid か contaminated か
