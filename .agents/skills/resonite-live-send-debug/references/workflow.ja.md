# Workflow

この guide は `SKILL.md` が発火した後に使ってください。

この file は repo-local live-send skill の単一 operational guide surface です。fixture 値、環境依存の判断、comparison worksheet、version-scoped な runtime note はここに置き、`SKILL.md` に重複させません。

## Defaults

- 別の fixture が必要でない限り、`plateau-20202-matsumoto-shi-2020` と mesh `54372778` / `54372788` を使います。
- `frn` または city-furniture validation が必要なときだけ Yokohama mesh `53391530` に切り替えます。
- これらは selector であり、cache path の保証ではありません。cleanup や send の前に actual local source path を確認してください。
- destructive step の前に、requested dataset root が local に存在し、requested mesh が current local evidence または fixture で裏付けられていることを確認してください。

## Environment Selection

- ad hoc command ではなく bundled helper script を使います。
- helper 実行は `pwsh.exe -NoProfile -File ...` による PowerShell 7 を優先します。
- PowerShell 7 が使えるなら helper script に Windows PowerShell 5.1 を使いません。現在の helper surface は `ConvertFrom-Json -Depth` のような PowerShell 7 で安定する挙動に依存しており、現行の Windows execution policy では `.ps1` の直接実行が止まることがあります。
- target listener が WSL から `localhost` で到達できない場合は Windows 側で helper を実行します。
- listener が同一ホストで、WSL から `localhost` 到達が実測で確認できているなら WSL 起点 sender も有効です。
- reverse proxy や bridge が listener 目線で許容可能な host に変換するなら、到達性と session identity の両方が確認できた IP 経路も有効です。
- Windows-only / WSL-only の固定ルールにはせず、実測の reachability と session identity で判断します。
- root dump と destructive cleanup は、official REPL prompt loop ではなく bundled な repo-local session tool を使います。
- session target が既知の自動化経路では、`ws://host:port/` の explicit endpoint を優先します。
- sandbox 付きの Codex 環境では、helper が CLI や session tool の restore / build を行うため昇格実行が必要になることがあります。.NET first-use や permission setup で helper が失敗した場合は、ad hoc command へ置き換えるのではなく helper 自体を昇格付きで再実行します。

## Agent Guardrails

- comparison rerun の前には listener discovery をやり直し、`sessionName`、`sessionID`、`linkPort` を記録します。
- listener port、process ID、log path、session identity を推測しません。discovery 出力、helper stdout、CLI log を使います。
- cleanup は destructive です。dataset root、matching live-send CLI process、local runtime artifact を消しえます。
- final successful `DatasetRoot` は、user が明示的に cleanup を求めない限り残します。
- `stdout` を解釈する前に `stderr` を見ます。`stderr` が空でも stalled 判定前に timestamp 付き log 読みを最低 2 回取ります。

## Fixed Run Worksheet

comparison run の間では、次の事実を固定するか、変更したら明示的に更新してください。

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

disposable な headless validation では、次の operator sequence を優先します。

1. `start-headless-session.ps1`
2. `dump-root-session.ps1 -Label baseline`
3. `cleanup-session.ps1`
4. `run-live-send.ps1`
5. `dump-root-session.ps1 -Label after-send`
6. `stop-headless-session.ps1`

長時間 run を待ちながら最新の `import` / `live` 行を見たい、明白な log error で早く失敗させたい、または memory cap 超過で kill したい場合は、`run-live-send.ps1` の代わりに `run-live-send-monitored.ps1` を使います。

`19001` で Matsumoto `54372778 -> 54372788` の fixed base/append validation を行う場合は、次の順で helper を実行します。

1. `cleanup-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Dataset plateau-20202-matsumoto-shi-2020`
2. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-baseappend-baseline`
3. `run-live-send.ps1 -RepoPath <repo> -ResoniteLinkPort 19001 -LocalSourcePath <archive> -Dataset plateau-20202-matsumoto-shi-2020 -MeshCode 54372778 -DemTerrainMode heightmap -Connections 1 -LogPrefix matsumoto-base-heightmap-19001`
4. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-base-heightmap-after-send`
5. `run-live-send.ps1 -RepoPath <repo> -ResoniteLinkPort 19001 -LocalSourcePath <archive> -Dataset plateau-20202-matsumoto-shi-2020 -MeshCode 54372788 -DemTerrainMode heightmap -Connections 1 -LogPrefix matsumoto-append-heightmap-19001`
6. `dump-root-session.ps1 -RepoPath <repo> -Endpoint ws://localhost:19001/ -Label matsumoto-append-heightmap-after-send`

## Component Type Discovery

- live inspection で正確な component type 名が必要な場合は、推測より ResoniteLink reflection を優先します。
- 第一経路は local の ResoniteLink library または official REPL helper で接続し、まず `GetComponentTypeList`、次に候補へ `GetComponentDefinition` を使うことです。
- 可能なら category query を使います。`GetComponentTypeList("*")` は narrower category が不明な場合に限り使い、session が空 list を返した事実も記録します。
- reflection が使えない、または有用な結果を返さない場合は、fallback として root dump 内の既存 `componentType` を証拠に使います。
- `Texture2D Metadata` のような UI label と runtime type string は別物として扱います。picker 表示名だけで `AddComponent` の型名を確定しません。

## BoxCollider Bounds Inspection

- imported slot に BoxCollider probe を付けて occupancy を見たい場合、または position / mesh regression を比較したい場合はこの手順を使います。
- 前提は successful な live send と post-send root dump です。failed / partial run のまま bounds inspection に進みません。
- まず dump で target slot の存在を確認し、dataset root 名、slot 名、slot tag、slot transform、既存 collider 証拠を記録します。
- 現在確認できている Matsumoto run では、imported slot に `[FrooxEngine]FrooxEngine.MeshCollider` が付き、`[FrooxEngine]FrooxEngine.BoxCollider` probe も受け付けました。これは current evidence であり、永続保証ではありません。
- 既存 collider の証拠だけで十分なら、world を mutate せず dump 証拠を優先します。user が BoxCollider ベースの bounds probe を明示したときだけ mutate します。
- BoxCollider probe が必要なら、official REPL または同等の reflection 可能な ResoniteLink client で接続し、追加前に受け付けられる BoxCollider runtime type を解決します。
  1. session が受け付ける最も狭い collider-oriented filter で `GetComponentTypeList` を引きます。
  2. 候補 runtime type に対して `GetComponentDefinition` を実行し、session が受け付ける exact な type string を記録します。
- runtime type が確定したら target slot に BoxCollider probe を追加し、session が見せる callable / member surface から bounds 由来 update path を確認します。
- probe 手順では `SetFromLocalBounds` を優先します。現在観測している workspace session では `SetFromLocalBoundsPrecise` は unit bounds しか返さず評価経路として使えないため、使いません。user が world-space comparison を明示しない限り、`SetFromLocalBounds` を基本経路とします。
- global-bounds helper を default とみなしません。実際に使った callable path を記録し、`SetFromGlobalBounds` は明示的な代替として扱います。
- local-bounds update 後は、BoxCollider state と slot transform をセットで採取します。`Size` と `Offset` は slot-local occupancy として扱い、world-space 解釈が必要なときだけ slot transform と組み合わせます。
- 標準手順では、readback 後に BoxCollider probe を削除して inspected world を probe 追加前の状態へ戻します。manual follow-up のために意図的に残す場合だけ、その逸脱を run note に明記します。
- 現在の workspace session には exploratory validation のため意図的に残した BoxCollider probe があります。これは temporary evidence であり、通常 cleanup policy ではありません。
- session に使える local-bounds update path が見えないなら、その session では自動 BoxCollider-based bounds readback workflow は未証明として止めます。
- reflection で有用な component type や callable surface が得られない場合は、root-dump evidence に戻り、BoxCollider bounds path は未検証として扱います。推測した型名や method 名で埋めません。

## Bounds Regression Checklist

- Identity check:
  dataset、mesh code、slot tag、slot 名、対象 slot が DEM / atlas bake / mesh bake / その他どれかを記録します。
- Structural check:
  期待した slot が期待した dataset branch 配下に存在するか、probe 追加前に renderer と collider component があるかを確認します。
- Local occupancy check:
  `SetFromLocalBounds` 実行後の BoxCollider `Size` と `Offset` を記録します。
- Placement check:
  後比較で slot misplacement と geometry extent 変化を分離できるよう、slot transform と BoxCollider local 値をセットで残します。
- Rotation check:
  slot rotation が identity でない場合は記録します。回転している slot の bounds は collider 値だけでは解釈できません。
- Category comparison:
  同種同士で比較します。DEM は DEM、atlas-baked building slot は atlas-baked building slot、mesh-baked slot は mesh-baked slot 同士で比べます。
- Expected-shape check:
  DEM では near-zero thickness、異常な vertical extent、急な XY shrink/stretch を見ます。
  building では unit scale への collapse、slot origin に対する大きな offset drift、horizontal footprint と height の大きな入れ替わりを見ます。
- Run-to-run comparison:
  まず同じ slot tag を run 間で比較します。tag が無い場合だけ名前ベースに落とします。
- Cleanup check:
  readback 記録後は、manual inspection のために残すと明記している場合を除き probe component を削除します。

## Matsumoto Reference Values

- この section の値は Matsumoto live check の current reference data であり、strict な golden number ではありません。
- 目的は、unit scale への collapse、axis swap、offset の急激な発散、category 不一致 geometry といった極端な変化を拾うことです。pass/fail threshold ではなく comparison seed として使います。
- 比較は同じ slot tag を run 間で突き合わせることを優先します。異なる mesh code や emitted category を同じ expected shape として比較しません。

- Reference sample A:
  dataset `plateau-20202-matsumoto-shi-2020`
  mesh code `54372778`
  category `DEM`
  slot tag `54372778|dem|none|udx_dem_543727_dem_6697_55_op_gml_dem_d0d95755_3366_4fa2_8c49_9c304fb295ce`
  slot position `{"x":3934.2598,"y":612.1313,"z":2310.8608}`
  slot rotation `{"x":0.7071068,"y":0.0,"z":0.0,"w":0.7071068}`
  local BoxCollider offset `{"x":0.0,"y":0.0,"z":0.0}`
  local BoxCollider size `{"x":1123.9403,"y":924.78,"z":0.0}`
  interpretation:
  `SetFromLocalBounds` 後の薄い DEM sheet として読みます。1 軸がほぼ 0 なのは想定内で、厚みの急増や XY collapse は suspicious です。

- Reference sample B:
  dataset `plateau-20202-matsumoto-shi-2020`
  mesh code `54372788`
  category `AtlasBake`
  slot tag `54372788|bldg|2|atlasbake:54372788:bldg:06927625:61717c92b772:2:0003`
  slot position `{"x":-574.12836,"y":590.0462,"z":-467.71707}`
  slot rotation `{"x":0.0,"y":0.0,"z":0.0,"w":1.0}`
  local BoxCollider offset `{"x":434.89987,"y":13.178907,"z":463.00485}`
  local BoxCollider size `{"x":869.79974,"y":26.357815,"z":926.0097}`
  interpretation:
  平面的に広く高さの低い atlas-baked building aggregate として読みます。unit scale への collapse、大きな origin drift、高さと footprint の大きな入れ替わりは suspicious です。

- current reference snapshot の artifact path:
  `runtime/windows/resonite/root-dumps/workspace-matsumoto-local-bounds-eval-20260418-180506.json`

## RawOutput Readback Limits

- 現時点で観測した `Resonite 2026.4.16.1327` と `ResoniteLink 0.13.1.0` の組み合わせでは、metadata component 上の `RawOutput` member は、target asset reference を正しく設定して再 poll しても Link 経由では読める値になりませんでした。
- 確認した対象は、`StaticTexture2D` に付けた `[FrooxEngine]FrooxEngine.Texture2DAssetMetadata` と `[FrooxEngine]FrooxEngine.BitmapAssetMetadata` です。
- これは version-specific evidence として扱い、永続的な一般則とはみなしません。Resonite または ResoniteLink が変わったら再検証してください。

## Required Artifacts

この skill 配下に次の file がある前提です。

- `tools/ResoniteSessionTool/ResoniteSessionTool.csproj`

helper script は thin な session tool や CLI binary を必要に応じて build します。dump / cleanup helper では fresh な Windows build output が expected execution path の一部です。

## Read-Only Inspection

- `dump-root-session.ps1` が書き出す JSON を primary な read artifact として扱います。
- `jq` は post-dump inspection のための optional convenience に限定します。cleanup convergence や slot 選択を `jq` 必須にしません。
- 例:
  `jq '.Root.Children[] | { id: .ID, name: .Name.Value }' runtime/windows/resonite/root-dumps/<dump>.json`

## Public Helper Commands

- `scripts/discover-session.ps1`
  UDP `12512` の live ResoniteLink announcement を取得します。
- `scripts/start-headless-session.ps1`
  disposable な Windows headless session を直接起動し、announcement された ResoniteLink port を検証します。
- `scripts/stop-headless-session.ps1`
  experiment 用に起動した tracked headless PID、または明示指定した PID を停止します。
- `scripts/dump-root-session.ps1`
  tracked 済み、または明示指定した session から再帰的な Root snapshot を採取します。
- `scripts/cleanup-session.ps1`
  live world から dataset root を削除し、残存 CLI process を停止し、local runtime artifact を消します。
- `scripts/run-live-send.ps1`
  Windows 側で 1 回の live send を explicit log 付きで起動します。

`scripts/windows-build-tools.ps1` は internal shared helper であり、operator-facing command surface には含めません。
