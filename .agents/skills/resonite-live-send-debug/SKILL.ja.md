---
name: resonite-live-send-debug
description: 実際の ResoniteLink session に対して PLATEAU-ResoniteLink の live-send 再現と調査を行う skill。simulated test ではなく machine-level の検証が必要なときに使い、listener discovery、run cleanup、log 採取、生成された Resonite world state の確認を扱います。
---

# Resonite Live Send Debug

この skill は実際の ResoniteLink run にだけ使ってください。まず local test を優先し、論点が live session、destructive cleanup cycle、または生成された Resonite world state に依存するときだけこの skill に切り替えます。

この file は、この repository における live-send workflow の Coding Agent entrypoint であり、public helper command surface に対する authoritative な live-send workflow reference です。詳細な運用 guidance は [references/workflow.md](./references/workflow.md) に集約し、この file には trigger、guardrail、output contract を残します。

## When To Use

- 実際の ResoniteLink listener に対する live-send 再現。
- log、process state、または live world の結果を観測しないと成立しない検証。
- headless session 起動、session cleanup、root dump を含む検証ループ。

## When Not To Use

- code-only review、static log 読み、documentation 作業。
- live session なしで十分に証明できる local/unit/integration test。
- 現在の dataset root を破壊できない task。

## Guardrails

- cleanup は destructive として扱う。live dataset root を消し、この repo から起動した matching live-send CLI process を止め、local runtime artifact を削除しうる。
- 自分で live send を実行できるなら、user に代行させない。
- 比較対象の dataset root について cleanup が確認できるまでは run を比較しない。
- successful な最終 `DatasetRoot` は、明示的な cleanup 指示がない限り残す。
- interrupted / partial run の結論は、cleanup と post-run state の両方が確認できるまでは provisional とする。
- exact runtime behavior、fixture、environment selection、reference value が重要なときは、仮定を転記せず [references/workflow.md](./references/workflow.md) を使う。

## Guide Surface

- canonical guide: [references/workflow.md](./references/workflow.md)
- Japanese mirror: [references/workflow.ja.md](./references/workflow.ja.md)

guide は次の用途で使います。

- 推奨 dataset と fixture 値
- 環境依存の実行判断
- fixed run worksheet と comparison checklist
- component discovery と BoxCollider inspection 手順
- version-scoped な readback limitation と reference artifact

## Public Helper Commands

operator-facing な helper script として直接使うのは次の 6 本だけです。

- `scripts/discover-session.ps1`
- `scripts/start-headless-session.ps1`
- `scripts/stop-headless-session.ps1`
- `scripts/cleanup-session.ps1`
- `scripts/dump-root-session.ps1`
- `scripts/run-live-send.ps1`

shared な Windows build resolver は internal helper であり、operator-facing command surface には含めません。

## Required Outputs

各 live run は次で要約してください。

- listener endpoint
- cleanup verification result
- process status と exit code
- exact な mode と mesh code
- 最後の timestamped `import` line
- 最後の timestamped `live` line
- `stderr` が空だったか
- world snapshot summary
- root dump path
- 観測 timestamp
- 結論が valid か contaminated か
