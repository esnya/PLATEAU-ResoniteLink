# Material Selection Strategy

この文書は、拡張済み bundled default material candidate の選択方針を記録します。
runtime selection に接続する別変更が入るまでは、documentation と test guidance のみです。

## Current Contract

- source の `ParameterizedTexture` やその他の dataset appearance lane は、
  bundled fallback material より優先する。この方針は、city object に利用可能な
  source texture がない場合だけに適用する。
- 既存の deterministic SHA-256 variant selection contract は、同等入力に対して
  再現可能なままにする。新しい grouping input は selection key を拡張できるが、
  既存 fallback selection を時刻依存や処理順依存にしてはいけない。
- 現在の bundled fallback family は `BundledDefaultMaterialFamilies` にある。
  building の UV fallback は `facade` family、non-UV building fallback は
  `roof` family、road/path-like package は `road`、vegetation は
  `vegetation`、city furniture は `city-furniture`、generic fallback は
  `other` を使う。
- common material setup は現在、package-scoped family variant を列挙し、
  UV と triplanar の common material binding を作り、さらに shared generic
  albedo と vertex-color common material を作る。

## Height Signals

building material grouping は、利用可能な最も信頼できる height signal から導出する。
優先順位は次の通り。

1. CityGML の明示的な measured height が存在し、正値の場合。
2. measured height がない場合、地上階 1 階あたり 3.5 m の estimate で換算した
   CityGML storey count。
3. metadata がない場合、geometry または bbox height。
4. 正の height を観測できない場合は `unknown`。

選択された height signal とその source は、strategy boundary の test で観測可能にする。
selection policy は material texture name や candidate ordering から height を推測しない。

## Candidate Groups

height-aware building fallback は、stable selection の前に candidate を group 化する。

- `unknown`: current-equivalent な保守的 fallback。height 欠落で出力が変わらないよう、
  既存の facade と roof runtime candidate set を使う。
- `low`: 10 m 以下の building。小さめの facade pattern と tiled/concrete roof candidate を優先する。
- `low-mid`: 10 m 超、31 m 以下の building。現在の facade candidate に、少数の neutral な
  wall/facade alternative を混ぜる。
- `mid-high`: 31 m 超、60 m 以下の building。大きめの facade surface と控えめな
  roof/asphalt candidate を優先する。
- `high`: 60 m 超の building。facade pool は狭く規則的に保つ。将来の package signal が
  根拠を与えない限り、住宅的すぎる material や小さな tile roof material は避ける。

収集済み candidate inventory には AmbientCG Facade001、Facade005、Facade006、
Facade018A、Facade019A、Facade020A、asphalt roof variants、roofing tile variants、
TextureCan Others0021、Others0022、Others0025、Others0026、Others0029 が含まれる。
provenance は引き続き `THIRD_PARTY_LICENSES/` に置く。

## Fallback Behavior

- dataset texture と vertex-color lane は bundled material selection で置き換えない。
- height が欠落または不正な場合は `unknown` group を使う。これは current-equivalent な
  facade または roof fallback pool と一致しなければならない。
- group candidate が欠落している場合は、その package の current family pool に fallback する。
- unknown package は、package catalog change が明示的に map しない限り、既存の
  `other` fallback behavior を維持する。
- wireframe overlay package は wireframe のままとし、bundled material candidate selection には
  参加しない。

## Diversity And Pool Cap

candidate group は意図的に小さく保つ。test でより広い pool の必要性を文書化しない限り、
height group は runtime stable selection に facade candidate を最大 4 個、roof candidate を最大 4 個まで公開する。

selection は dataset、mesh-code、city-object、package、projection、height-group、
surface-role の粒度で stable にする。収集済み asset が多いという理由だけで、近隣 object が
不自然にばらついて見えてはいけない。より細かい neighborhood coherence が必要な場合は、
処理順に頼らず、明示的な neighborhood または tile grouping input を追加する。

## Common Material Warmup

common material warmup は setup contract であり、per-object repair path ではない。
expanded candidate pool は、全 building dataset に対して収集済み material asset を
すべて既定で作成することを要求してはいけない。

runtime implementation は、次のいずれかの制約を満たす。

- active な per-package candidate union を cap し、`CommonMaterialCatalog` が setup 中に
  package-scoped common material set 全体を列挙できるようにする。
- または、object emission が始まる前に、その run で必要な candidate family と variant だけを
  列挙する、別の明示的な setup-time discovery result を導入する。

別の design change が bootstrap test と live-send failure behavior を更新しない限り、
setup planning の代替として lazy runtime common-material creation を追加しない。
appearance lane は、common-material warmup に引き込まれず、dedicated material flow を
使えるままにする。

## Test Guidance

implementation test では次を cover する。

- height signal priority と `unknown` fallback。
- equivalent key に対する stable selection。
- 各 height group と surface role の pool cap enforcement。
- bundled fallback に対する source appearance precedence。
- common material enumeration が意図した active candidate union に収まり、
  収集済み provenance asset すべてを誤って warmup しないこと。
