# Workflow

この reference は `SKILL.md` が発火した後に使ってください。

この file は Coding Agent 向けの補助メモです。`SKILL.md` の補助として使い、廃止した tracked live-testing document を前提にしないでください。

## Defaults

- 別の dataset が必要でない限り、`plateau-20202-matsumoto-shi-2020` と mesh `54372778` / `54372788` を使う。
- `frn` 検証が必要なときだけ Yokohama mesh `53391530` に切り替える。
- これらは selector であり cache path の保証ではない。cleanup や send の前に actual local source path を確認する。

## Agent Guardrails

- ad hoc command ではなく `.agents/skills/resonite-live-send-debug/scripts/` 配下の bundled helper script を優先する。
- WSL から listener に `localhost` で到達できないなら Windows 側 helper script を使う。
- comparison rerun の前には listener discovery をやり直し、`sessionName`、`sessionID`、`linkPort` を run note に残す。
- listener port、process ID、log path、session identity を推測しない。discovery の出力、helper stdout、CLI log を使う。
- cleanup は destructive。dataset root、matching live-send CLI process、local runtime artifact を消しうる。
- successful validation 後の final `DatasetRoot` は、user が明示しない限り残す。
- `stdout` を解釈する前に `stderr` を見る。`stderr` が空でも stalled 判定前に timestamp 付き log 読みを最低 2 回取る。

## Component Type Discovery

- live inspection で正確な component type 名が必要な場合、推測ではなく ResoniteLink reflection を優先する。
- 第一経路は、local の ResoniteLink library か official REPL helper で接続し、まず `GetComponentTypeList`、次に候補へ `GetComponentDefinition` を使うこと。
- 可能なら category を絞って query する。`GetComponentTypeList("*")` は narrower category が不明な場合に限り使い、session が空 list を返した事実も記録する。
- reflection が使えない、または有用な結果を返さない場合は、fallback として root dump 内の既存 `componentType` 値を証拠に使う。
- `Texture2D Metadata` のような UI label と runtime type string は別物として扱う。picker 表示名だけで `AddComponent` の型名を確定しない。

## RawOutput Readback Limits

- 現時点で観測した `Resonite 2026.4.16.1327` と `ResoniteLink 0.13.1.0` の組み合わせでは、metadata component 上の `RawOutput` member は、target asset reference を正しく設定して再pollしても Link 経由では読める値にならなかった。
- 確認した対象は、`StaticTexture2D` に付けた `[FrooxEngine]FrooxEngine.Texture2DAssetMetadata` と `[FrooxEngine]FrooxEngine.BitmapAssetMetadata`。
- これはバージョン依存の観測事実として扱い、永続的な一般則とはみなさない。Resonite または ResoniteLink が変わったら再検証すること。

## Required Artifacts

この skill 配下に次の file がある前提です。

- `tools/ResoniteAdmin/ResoniteAdmin.csproj`

helper script は admin utility や CLI binary を必要に応じて build します。dump / cleanup helper では fresh な Windows build output が前提です。

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
- `scripts/windows-build-tools.ps1`
  他の helper script から使う Windows 側 `dotnet` / ResoniteAdmin build 解決 helper。
