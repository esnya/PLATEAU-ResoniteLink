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

## BoxCollider Bounds Inspection

- imported slot に BoxCollider probe を付けて occupancy を見たいとき、または mesh / 位置ずれ退行を比較したいときは、この手順を使う。
- 前提は successful な live send と post-send root dump。failed / partial run のまま Bounds 検査に進まない。
- まず dump で target slot の存在を確認し、dataset root 名、slot 名、slot tag、slot transform、既存 collider の有無を控える。
- 現在確認できている Matsumoto run では、imported slot には `[FrooxEngine]FrooxEngine.MeshCollider` が付き、さらに imported slot へ `[FrooxEngine]FrooxEngine.BoxCollider` probe を追加して使えることも確認できている。これは現時点の証拠であり、永続保証ではない。
- target slot に既存 collider の証拠だけで足りるなら、まず dump 証拠を優先する。user が BoxCollider ベースの Bounds probe を明示的に求めたときだけ world を mutate する。
- BoxCollider probe が必要なら、official REPL または同等の reflection 可能な ResoniteLink client で接続し、追加前に受け付けられる BoxCollider runtime type を解決する。
  1. session が受け付ける最も狭い collider 系 filter で `GetComponentTypeList` を引く。
  2. 候補 runtime type に対して `GetComponentDefinition` を実行し、session が受け付ける exact な type string を記録する。
- runtime type が確定したら target slot に BoxCollider probe を追加し、session から見える callable / member surface を確認して、bounds 由来 update 経路を洗う。
- probe 手順では `SetFromLocalBounds` または `SetFromLocalBoundsPrecise` を優先する。user が world-space 比較を明示しない限り、これを基本経路とする。
- global-bounds helper を既定にしない。実際に使った callable path を記録し、`SetFromGlobalBounds` は明示的な代替手段として扱う。
- local-bounds update 実行後は、BoxCollider state と slot transform をセットで採取する。`Size` と `Offset` は slot-local occupancy として扱い、world-space 解釈が必要な場合だけ slot transform と組み合わせる。
- 標準手順では、readback 後に BoxCollider probe を削除して inspected world を probe 追加前の状態へ戻す。manual follow-up のために意図的に残す場合だけ、その逸脱を run note に明記する。
- 現在の workspace session には exploratory validation のため意図的に残してある BoxCollider probe が含まれる。これは一時的な証拠であり、通常 cleanup policy ではない。
- session に使える local-bounds update 経路が見えないなら、自動 BoxCollider Bounds 採取 workflow はその session では未証明として止める。
- reflection で有用な component type や callable surface が得られない場合は、root dump の証拠に戻り、BoxCollider Bounds 経路は未検証として扱う。推測した型名や method 名で埋めない。

## Bounds Regression Checklist

- BoxCollider readback 後に何を見るかは次の checklist で決める。`Size` だけを見て終わらせない。
- Identity check:
  dataset、mesh code、slot tag、slot 名、検査対象が DEM / atlas bake / mesh bake / その他どれかを記録する。
- Structural check:
  期待した slot が期待した dataset branch 配下に存在するか、probe 追加前に renderer と collider が揃っているかを確認する。
- Local occupancy check:
  `SetFromLocalBounds` または `SetFromLocalBoundsPrecise` 実行後の BoxCollider `Size` と `Offset` を記録する。
- Placement check:
  後比較で slot 自体の位置ずれと geometry extent の変化を分離できるよう、slot transform と BoxCollider local 値を必ずセットで残す。
- Rotation check:
  slot rotation が identity でない場合は必ず記録する。回転している slot の Bounds は collider 値だけでは解釈できない。
- Category comparison:
  同種同士で比較する。DEM は DEM、atlas-baked building slot は atlas-baked building slot、mesh-baked slot は mesh-baked slot 同士で比べる。
- Expected-shape check:
  DEM では厚みが極端に 0 に近いか、縦方向が異常に大きいか、XY が急に縮む・伸びるかを見る。
  building では unit-scale への急な collapse、slot origin に対する大きな offset drift、平面 footprint と高さの大きな入れ替わりを見る。
- Run-to-run comparison:
  まず同じ slot tag を run 間で比較する。tag が無い場合だけ名前ベースに落とす。
- Cleanup check:
  readback 記録後は、manual inspection のために残すと明記している場合を除き probe component を削除する。

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
