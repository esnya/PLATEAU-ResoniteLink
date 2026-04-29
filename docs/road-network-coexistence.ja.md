# Road Network Coexistence

この文書は、generated road-network output と、より詳細な transport 関連 output の間にある Issue #131 の coexistence と precedence policy を定義する。[road-network-boundary.md](road-network-boundary.md) を前提にし、road-network generation は実装しない。

## Current Execution Surface

現在の import pipeline は、Resonite target emission の前に package scope の `ImportedObjectUnit` を出力する。各 unit は source file、normalized package name、LOD level、利用可能な matched mesh code によって scope される。

現在の object-unit optimization path は target-neutral である。

- `StreamingImportedSceneSource` は projected city object を source file と LOD ごとにまとめる。
- `IImportedObjectUnitOptimizer` は target sink が受け取る前の stream を変換する。
- `CompositeImportedObjectUnitOptimizer` は登録された optimizer を順に適用する。
- 現時点で登録されている optimizer は dynamic material UV metadata を normalize する。road-network coexistence filtering は実行しない。

generated road-network unit が coverage metadata を持った時点では、この optimizer seam が executable coexistence policy を追加する正しい場所である。それまでは、この文書を policy contract とし、runtime suppression がすでに行われるとは示さない。

## Transport Detail Classes

road-network coexistence は、generated road-network unit と detailed transport-related output を road space 上でだけ比較する。

road package は次の通り。

- `tran`
- `rwy`
- `squr`
- `trk`

`wwy` は path-like であり road-family material を共有しうるが、road package ではない。そのため default では generated road-network unit を suppress しない。

detailed transport-related output とは、generated road-network unit と同じ road-space area を同等以上の source-detail level で表す road package 由来の source CityGML geometry である。例として、road surface geometry、road marking、roadside structure、railway または track surface、generated unit の road-space footprint と重なる square/open-space transportation surface がある。

## Precedence Policy

detailed transport-related output は、重なる road-space unit に対して generated road-network output より優先される。

policy は次の通り。

- generated road-network unit と detailed road-package output が同じ road-space unit を cover する場合、detailed output を emit し、generated road-network unit を suppress する。
- overlap が部分的な場合でも、後続の accepted design が sub-unit splitting を導入しない限り、policy は generated road-network unit 全体に適用する。
- generated road-network unit と重なる detailed road-package output が無い場合、generated road-network unit を emit する。
- 重なる output が non-road package output だけの場合、後続の package-specific policy がない限り、両方を保持する。
- coverage metadata が欠けていて overlap を評価できない場合、generated road-network unit を保持し、silent suppression ではなく metadata 欠落を report する。

suppression は、その unit が所有する generated road-network unit、generated visual geometry、generated marking、generated debug geometry、generated network primitive を取り除く。source CityGML geometry は取り除かない。

## Merge Policy

default の merge behavior は no merge である。

generated road-network unit と detailed transport-related output は、別 unit として coexist するか、generated road-network unit が suppress される。provenance retention を伴う明示的な graph-wide または sub-unit merge design が後続変更で導入されない限り、source CityGML transport geometry と generated road-network primitive を単一 output unit に結合しない。

connectivity view は analysis 用の derived view として作ってよいが、ownership を変えてはならない。suppression と operator reporting は generated road-network unit と、優先された detailed source output へ trace できるままにする。

## Comparison Inputs

coexistence comparison には target-neutral evidence を使う。

- normalized package name
- source file relative path
- matched mesh code と、利用可能なら resolved actual mesh code
- stable source object id または generated road-network object key
- selected LOD または source-detail level
- source coordinates または geodetic coordinates の road-space coverage footprint
- fact が source-observed か inferred か

次で比較してはならない。

- Resonite slot name
- material asset identity
- live-send batching state
- target hierarchy layout
- generated display name だけ

## Operator-Visible Behavior

operator は selected package と coverage から emitted output を予測できる必要がある。

- road package を選択すると detailed road-package output が生成されうる。
- 将来 road-network generator を有効化すると、detailed road-package output に cover されていない road-space area に generated road-network unit が生成されうる。
- 両方の output が同じ road-space unit を claim する場合、detailed road-package output が勝ち、generated unit は suppress される。
- `wwy` output は path-like だが road package set 外であるため、default では generated road-network unit と coexist できる。
- runtime suppression を実装したら suppression count を report する。その report は suppressed generated unit、勝った detailed source package、判断に使った source file または object evidence を識別する。

## Non-Goals

この文書は次を定義しない。

- road-network generation algorithm
- lane、sidewalk、intersection inference
- Resonite slot layout
- target material assignment
- graph-wide optimization
- sub-unit splitting
- road-network unit と coverage metadata が存在する前の runtime suppression 実装
