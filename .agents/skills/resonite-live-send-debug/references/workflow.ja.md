# Workflow

`SKILL.md` が trigger された後はこの guide を使います。

この file は repo-local live-send skill の単一 operational guide surface です。fixture value、comparison worksheet、version-scoped runtime note は `SKILL.md` に重複させず、ここに集約します。

## Defaults

- task で別 fixture が必要でない限り、run ごとに default live fixture をランダムに 1 つ選びます:
  `plateau-20202-matsumoto-shi-2020` の mesh `54372778` / `54372788`、または current operator evidence で解決した GeoTIFF 付き mesh を使う `plateau-13213-higashimurayama-shi-2020` です。
- removal や send の前に、どちらの fixture branch を選んだかを run note に記録します。
- `frn` または city-furniture validation のときだけ Yokohama mesh `53391530` に切り替えます。
- これらの default は selector であり、cache path の保証ではありません。removal や send の前に actual resolved local source path を確認します。
- destructive step の前に、requested dataset root が local に存在し、requested mesh が current local evidence または fixture で support されていることを確認します。

## Agent Guardrails

- comparison rerun ごとに listener discovery をやり直し、`sessionName`、`sessionID`、`linkPort` を記録します。
- listener port、process ID、log path、session identity を推測しません。discovery output、direct command の stdout、CLI log を使います。
- `dump-slot` と `remove-slot` は thin primitive として扱います。dataset root、shared assets、common materials の naming semantics を tool surface に埋め込みません。
- slot removal は destructive とみなします。current world の live content を削除し得ます。
- user が明示的に removal を要求しない限り、最後に成功した `DatasetRoot` は残します。
- 同じ session に対する cleanup、root dump、live-send command は並列実行しません。removal、post-removal verification dump、各 send は直列に実行し、base-state evidence を汚しません。
- `stdout` を解釈する前に `stderr` を確認します。`stderr` が空でも stalled と判断する前に timestamp 付き log read を少なくとも 2 回取ります。
- public operator surface は direct `dotnet` command に限定します。.ps1 wrapper、project-based session tool、cross-environment bridge guidance は再導入しません。
- `dump-slot --root-child-name` と `remove-slot --root-child-name` は `Root` 直下の exact direct child だけを解決します。0 件は fail、複数件も mutate せず fail にします。
- ResoniteLink が `localhost` を使う場合は、sender、listener、headless を同一 environment で動かします。その前提は skill 内で吸収せず、run note に明記します。
- clean base から resend するつもりの run では、removal の直後に pre-send root dump を取り、その dump に stale dataset content が残っていれば contaminated run と扱います。

## Headless Launcher Path Guide

- `--headless-path` がある場合は、explicit な launcher root または launcher file として扱います。誤った path を無関係な local copy へ黙って置き換えません。
- resolver が確認するのは、指定された file または directory と、その近傍の次の候補だけです。
  `Resonite.dll`、`Resonite.exe`、`Headless/Resonite.dll`、`Headless/Resonite.exe`
- Windows で `--headless-path` を省略した場合、tool が自動で確認するのは標準 Steam install root だけです。
  `C:\Program Files (x86)\Steam\steamapps\common\Resonite`
- machine に separate headless-only install がある場合は、その install root、またはその `Resonite.exe` / `Resonite.dll` を `--headless-path` に渡します。
- configured path に accepted launcher candidate が無ければ、別 directory を推測せず、missing launcher をそのまま報告して止まります。
- headless startup が version-matched runtime file、writable metadata cache、machine-local auth/config に依存する場合は、copy された sandbox より installed app tree を優先します。

## Fixed Run Worksheet

comparison run 間でこれらの事実を固定するか、明示的に更新します。

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

1. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- start-headless --runtime-root <headless-runtime> --state-path <headless-runtime>/active-session.json --resonitelink-port 19001`
2. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot --runtime-root <headless-runtime> --output <repo>/runtime/windows/resonite/root-dumps/baseline.json`
3. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:19001/ --root-child-name "PLATEAU plateau-20202-matsumoto-shi-2020"`
4. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/post-removal-pre-send.json`
5. `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset plateau-20202-matsumoto-shi-2020 --mesh-code 54372778 --citygml-source <archive> --work-root <repo>/runtime/windows/resonite --dem-terrain-mode heightmap --resonitelink-port 19001 --resonitelink-connections 1`
6. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/after-send.json`
7. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- stop-headless --runtime-root <headless-runtime> --state-path <headless-runtime>/active-session.json`

固定 Matsumoto `54372778 -> 54372788` の base/append validation を `19001` で行うときは、direct command をこの順で実行します。

1. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:19001/ --root-child-name "PLATEAU plateau-20202-matsumoto-shi-2020"`
2. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/matsumoto-post-removal-pre-send.json`
3. `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset plateau-20202-matsumoto-shi-2020 --mesh-code 54372778 --citygml-source <archive> --work-root <repo>/runtime/windows/resonite --dem-terrain-mode heightmap --resonitelink-port 19001 --resonitelink-connections 1`
4. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/matsumoto-base-heightmap-after-send.json`
5. `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset plateau-20202-matsumoto-shi-2020 --mesh-code 54372788 --citygml-source <archive> --work-root <repo>/runtime/windows/resonite --dem-terrain-mode heightmap --resonitelink-port 19001 --resonitelink-connections 1`
6. `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:19001/ --slot-id Root --output <repo>/runtime/windows/resonite/root-dumps/matsumoto-append-heightmap-after-send.json`

world に `PLATEAU Shared Assets` や `Common Materials` のような stale root が追加で残っている場合は、root dump を見て exact な slot ID または exact な root-child name を選び、`remove-slot` で 1 つずつ除去します。それらの name を stable API とみなしません。

## Component Type Discovery

- live inspection で exact component type name が必要なときは、guess ではなく ResoniteLink reflection を優先します。
- primary path は local ResoniteLink library または official REPL helper に接続し、まず `GetComponentTypeList`、次に candidate に対する `GetComponentDefinition` を使います。
- 可能なら category query を使います。`GetComponentTypeList("*")` は narrower category が分からない場合に限り、session が empty list を返した事実も記録します。
- reflection が unavailable か有用な情報を返さない場合は、fallback evidence source として slot dump 内の既存 `componentType` を調べます。
- UI label と runtime type string を混同しません。`Texture2D Metadata` のような picker label は exact `AddComponent` type name の証拠にはなりません。

## BoxCollider Bounds Inspection

- imported slot に BoxCollider probe を付けて bounds を read back し、rendered occupancy や position/mesh regression を見積もる必要があるときにこの procedure を使います。
- successful live send と post-send slot dump を起点にします。failed run や partial run では bounds inspection を始めません。
- まず target slot が dump に既に存在することを確認し、その identity を記録します。dataset root name、slot name、slot tag、slot transform、既存 collider evidence を含めます。
- 現在観測している Matsumoto run では、imported slot に `[FrooxEngine]FrooxEngine.MeshCollider` があり、さらに `[FrooxEngine]FrooxEngine.BoxCollider` probe も受け付けました。これは timeless guarantee ではなく current evidence として扱います。
- target slot に既に十分な collider shape があるなら、まず dump-based evidence を優先します。user が明示的に BoxCollider-based bounds probe を求めた場合だけ world を mutate します。
- BoxCollider probe が必要な場合は、official REPL または同等の reflection-capable ResoniteLink client を通じて accepted BoxCollider runtime type を解決してから追加します。
  1. session が support する最も narrow な collider-oriented filter で `GetComponentTypeList` を呼ぶ。
  2. candidate runtime type に対して `GetComponentDefinition` を実行し、session が accept した exact type string を記録する。
- runtime type が確認できたら、target slot に BoxCollider probe を追加し、session が expose する callable/member surface から bounds-derived update path を調べます。
- probe step は `SetFromLocalBounds` を優先します。現在観測している workspace session では `SetFromLocalBoundsPrecise` は unit bounds を返して usable occupancy にならなかったため、user が explicit に world-space comparison を望まない限り baseline にはしません。
- global-bounds helper が default だと決めつけません。使った exact callable path を記録し、`SetFromGlobalBounds` は explicit alternative として扱います。
- local-bounds update 実行後は、BoxCollider state を slot transform と一緒に記録します。`Size` と `Offset` は slot-local occupancy として扱い、world-space interpretation が必要なときだけ slot transform と組み合わせます。
- standard procedure は readback 後に BoxCollider probe を remove して、inspected world を pre-probe state に戻すことです。manual follow-up のため intentionally 残す場合は、その deviation を run note に明記します。
- 現在の workspace session には exploratory validation 由来で意図的に残された BoxCollider probe があります。これは baseline cleanup policy ではなく temporary evidence とみなします。
- session が usable な local-bounds update path を expose しない場合は止めて、automatic な BoxCollider-based bounds readback workflow は current session では証明できなかったと報告します。
- reflection が有用な component type や callable surface を返さない場合は、slot-dump evidence に戻し、BoxCollider bounds path は unverified と扱います。guess で type や method 名を埋めません。

## Bounds Regression Checklist

- Identity check:
  dataset、mesh code、slot tag、slot name、inspection 対象が DEM / atlas bake / mesh bake / その他 emitted category のどれかを記録する。
- Structural check:
  expected slot が expected dataset branch 配下に存在すること、および probe を追加する前に expected renderer / collider component があるかを確認する。
- Local occupancy check:
  `SetFromLocalBounds` 後の BoxCollider `Size` と `Offset` を記録する。
- Placement check:
  slot transform と BoxCollider local value を一緒に記録し、後の比較で slot misplacement と geometry extent change を切り分けられるようにする。
- Rotation check:
  slot が identity-aligned でないなら slot rotation を記録する。rotated slot の bounds regression は collider value だけでは解釈できない。
- Category comparison:
  compare like with like。DEM は DEM 同士、atlas-baked building slot は atlas-baked building slot 同士、mesh-baked slot は mesh-baked slot 同士で比較する。
- Expected-shape check:
  DEM では near-zero thickness、implausibly large vertical extent、急な XY shrink/stretch を警戒する。
  building では unit scale への collapse、slot origin に対する大きな offset drift、footprint と height の大きな入れ替わりを警戒する。
- Run-to-run comparison:
  まず同じ slot tag を比較する。tag が unavailable な場合だけ name-based matching に fallback する。
- Cleanup check:
  readback 記録後、run を intentional に preserve するのでなければ probe component を remove する。

## Matsumoto Reference Values

- この section の値は Matsumoto live check の current reference data であり、strict golden number ではありません。
- 目的は unit scale への collapse、axis swap、急激な offset 爆発、category-mismatched geometry のような extreme unintended change を検出することです。pass/fail threshold ではなく comparison seed です。
- まず同じ slot tag を run 間で比較します。異なる mesh code や emitted category を同じ expected shape として比較しません。

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
  `SetFromLocalBounds` 後の薄い DEM sheet です。片軸が near zero に collapse するのは expected ですが、急な thickness growth や XY collapse は suspicious です。

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
  低い高さで広い atlas-baked building aggregate です。unit scale への collapse、大きな origin drift、高さと footprint の大きな swap は suspicious です。

- current reference snapshot の artifact path:
  `runtime/windows/resonite/root-dumps/workspace-matsumoto-local-bounds-eval-20260418-180506.json`

## RawOutput Readback Limits

- 現在観測している `Resonite 2026.4.16.1327` と `ResoniteLink 0.13.1.0` の組み合わせでは、metadata component 上の `RawOutput` member は、target asset reference が正しく set されて後で poll し直しても readable value を返しませんでした。
- 確認できた例は `StaticTexture2D` に付いた `[FrooxEngine]FrooxEngine.Texture2DAssetMetadata` と `[FrooxEngine]FrooxEngine.BitmapAssetMetadata` です。
- これは timeless rule ではなく version-specific evidence です。Resonite または ResoniteLink が変わったら、その制限に依存する前に再検証します。

## Required Artifacts

この skill 配下には、少なくとも次の tracked file がある前提です。

- `tools/session-tool.cs`
- `src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj`

direct `dotnet` 実行では、session tool script や CLI が on demand で rebuild されます。dump、removal、headless、live-send command に fresh local build output が含まれるのは expected execution path です。

## Read-Only Inspection

- `dump-slot` が書き出す JSON を primary read artifact とします。
- `jq` は post-dump inspection 用の optional convenience です。cleanup convergence や slot selection を `jq` 依存にしません。
- Example:
  `jq '.Slot.children[] | { id: .id, name: .name.value }' <dump>.json`

## Direct Command Surface

- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- discover-session`
  UDP `12512` から live ResoniteLink announcement を取得します。
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- start-headless --runtime-root <headless-runtime> --state-path <headless-runtime>/active-session.json [--headless-path <headless>] --resonitelink-port <port>`
  disposable な headless session を直接起動し、announcement された ResoniteLink port を検証します。launcher は指定 file または directory と、その近傍の少数の標準候補から解決し、`.dll` launcher は environment の `dotnet` command 経由で起動します。
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- stop-headless --runtime-root <headless-runtime> --state-path <headless-runtime>/active-session.json`
  experiment 用に起動した tracked headless PID、または明示指定した PID を停止します。
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:<port>/ --slot-id Root --output <dump>.json`
  tracked された session、または明示 endpoint の session から recursive slot snapshot を取得します。
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- dump-slot ws://localhost:<port>/ --root-child-name "PLATEAU plateau-20202-matsumoto-shi-2020" --depth 1`
  `Root` 直下の exact direct child を解決して、その slot を dump します。
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:<port>/ --slot-id <slot-id>`
  明示した slot を 1 つ remove します。
- `dotnet .agents/skills/resonite-live-send-debug/tools/session-tool.cs -- remove-slot ws://localhost:<port>/ --root-child-name "PLATEAU plateau-20202-matsumoto-shi-2020"`
  `Root` 直下の exact direct child を 1 つ解決して remove します。これは operator workflow の convenience であり、semantic cleanup API ではありません。
- `dotnet run --project src/PlateauResoniteLink.Cli/PlateauResoniteLink.Cli.csproj -- import --dataset <dataset> --mesh-code <mesh> --citygml-source <archive-or-udx> --work-root <repo>/runtime/windows/resonite --dem-terrain-mode <heightmap|mesh> --resonitelink-port <port> --resonitelink-connections <n>`
  `runtime/windows/resonite` 配下へ explicit log を出しながら、direct live send を 1 回実行します。

## Visual Review Procedure

この section は、slot metadata だけではなく rendered image そのものが必要な評価で使います。
観点ごとの画角手順を固定するためのもので、
`C:\Users\esnya\.codex\skills\resonite-visual-eval\SKILL.md` の zero-base capture path と
併用する前提です。

capture target は狭く保ちます。退行が object-local なときに、dataset root 全体を
visual judgment の default target にしてはいけません。

### Preconditions

- まず 1 回 live send を完了し、結果の dataset root を world に残しておきます。
- 最初に representative target slot を解決します。
  dataset root ではなく、疑いのある issue が明瞭に見える `AtlasBake` または
  `MeshBake` slot を優先します。
- chosen slot で trivial な bounds しか得られない場合は、意図した object cluster を
  囲う explicit な `BoundsMin` / `BoundsMax` に切り替えます。

### View 1: Oblique Overview

subject visibility と gross proportion を最初に確認する view です。

- Recommended target:
  representative な `AtlasBake` building slot を 1 つ、または小さな建物 1 棟と
  隣接する高い建物 1 棟を含む tight な explicit bounds。
- Recommended starting direction:
  `ViewDirection = 0,-0.8,-0.6`
- Recommended framing:
  建物全高を frame に入れ、sky と ground の reference も含めます。
- この view で見ること:
  - object が visible で、occlusion していないか
  - facade repetition が見た目の階数に対して明らかに過密でないか
  - 建物上端と texture 上端、建物下端と texture 下端が coarse に揃っているか
  - 小さな建物が不自然な階数で描画されていないか

### View 2: Facade Front Close-Up

単一 facade の階数、縦位相、灰色壁 collapse を確認する view です。

- Recommended target:
  現在の world orientation から正面に近く見える facade を持つ `AtlasBake` または
  `MeshBake` slot を 1 つ。
- Recommended framing:
  1 枚の wall plane が frame の大半を占めるようにします。可能なら建物上端と下端の
  両方を入れます。
- facade pattern を目視で数えられるよう、急角度ではなく front または slight-oblique を優先します。
- この view で見ること:
  - window や facade band が 1 floor あたり 1 回程度で繰り返しているか
  - facade 上端が texture 上端と揃っているか
  - facade 下端が texture 下端と揃っているか
  - flat gray または near-uniform wall に collapse した領域がないか
  - bundled facade variant ごとの intended aspect ratio が維持されているか

top と bottom の両方を readability を保って同時に入れられない場合は、
同じ wall に対して 2 枚の close-up を撮ります。

- roofline 寄りの 1 枚
- ground line 寄りの 1 枚

これらは同じ object に対する 1 組の review pair として扱います。

### View 3: Side or Corner Mid Shot

texture-phase 問題と silhouette / proportion 問題を切り分けるための view です。

- Recommended framing:
  建物の corner を 1 つ入れ、2 面の facade plane を同時に見せます。
- この view で見ること:
  - repetition density が隣接 facade plane でも一貫しているか
  - 縦位相が visible な corner をまたいでも揃っているか
  - horizontal scaling が boundary fit を強制せず material の aspect ratio を保っているか

### View 4: Underside / Bottom-Face Check

底面除去の検証時だけ使う view です。

- Recommended target:
  上と同じ representative building slot、または underside を見やすい isolated な
  小さめの building。
- Recommended framing:
  object footprint より下へ回り込み、silhouette を失わない程度に upward へ tilt
  して underside を見ます。
- この view で見ること:
  - 大きな水平 bottom cap が残っていないか
  - expected な side wall だけが下から見えているか
  - 見えている underside が actual な bottom face なのか、それとも facade recess や
    roof overhang 由来なのか

render が mostly sky になったり subject を失った場合は、距離を離すのではなく
bounds を tighten してください。

### Review Order

facade regression では、次の固定順で見ます。

1. Oblique Overview
2. Facade Front Close-Up
3. Side or Corner Mid Shot

底面除去を確認するときだけ Underside / Bottom-Face Check を追加します。

### Reporting Template

各 capture では次を記録します。

- target slot id または explicit bounds
- view label
- camera pose
- rendered image path
- observed facts
- likely interpretation
- still-unconfirmed items

image facts と interpretation は分けて書きます。例えば:

- fact:
  手前の小さい建物に facade row が約 5 段見える
- interpretation:
  これは意図した 1-2 階より多い可能性が高い
- unconfirmed:
  その object の正確な CityGML `storeysAboveGround` はまだ照合していない
