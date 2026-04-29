# 道路ネットワーク境界

この文書は、共存挙動を実装する前に road-network support の表現境界を固定する。これは Issue #130 の仕様カットである。Issue #131 は、この出力とより詳細な transport 関連出力の間の重複抑制、優先順位、merge 挙動を担当する。

## 観測された現状

現在の import pipeline は `udx/<package>/...` 配下の CityGML source file を発見し、PLATEAU package name を正規化し、package scope の `ImportedObjectUnit` を target-neutral な construction geometry として stream してから Resonite target layer に渡す。

road 関連 package の扱いは、すでに package based である。

- `tran`、`rwy`、`squr`、`trk` は `RoadPackageNames` である。
- `wwy` は path-like だが road package ではない。
- default material selection は road package と path-like package を bundled road material family に割り当てる。
- CityGML reader は現時点で polygon と triangle surface を投影する。PLATEAU transportation file を既存の topological road graph として扱ってはいない。
- transportation surface には、Resonite emission 前の target-neutral な terrain alignment 挙動がすでにある。

PLATEAU SDK for Unity は road-network generation を imported road model からの派生処理として説明している。road mesh geometry から road structure、lane、sidewalk、intersection、lane connection を推定し、自動生成される network は推定であると説明している。この repository でもその分離を維持する。source CityGML road geometry は入力証拠であり、それ自体を road-network graph としない。

参考:

- PLATEAU SDK for Unity road-network manual: https://project-plateau.github.io/PLATEAU-SDK-for-Unity/manual/RoadNetwork.html
- PLATEAU RoadNetwork Generator overview: https://project-plateau.github.io/PLATEAU-RoadNetwork-Generator/index.html

## 決定

road network は derived transport abstraction として表現する。

raw source graph ではない。

- PLATEAU CityGML transportation package content には road surface、marking、より詳細な structure が含まれうるが、現在の importer が信頼して扱えるのは source-file と city-object surface stream である。
- SDK と揃う挙動は、権威ある road topology の直接再利用ではなく、road model evidence からの生成である。

Resonite-specific target model でもない。

- coexistence decision は ResoniteLink なしで test できるよう、target emission 前に実行できなければならない。
- geometry、provenance、coverage、generated network semantics は、target adapter が変換するまで target-neutral に保つ。

この abstraction は importing/application 側の boundary に置き、後続 policy に必要な provenance を持つ。

- source package name
- source file relative path
- matched mesh code と、利用可能なら resolved actual mesh code
- source object id または stable object key
- selected LOD または inferred source-detail level
- source coordinates または geodetic coordinates の road-space coverage footprint
- その source evidence が所有する generated road-network elements

## Source Abstraction

source abstraction は概念上 `RoadNetworkSource` である。最初の実装名が別名であってもよいが、これは選択された road-package CityGML evidence に対する read-side contract であり、target output contract ではない。

これは現在の CityGML import と同じ discovery window から作る。

- requested mesh code と requested package だけを見る。
- normalized road package、つまり `tran`、`rwy`、`squr`、`trk` だけを消費する。
- 既存の parsed road surface、road marking detection、object id、source file descriptor、LOD selection、CRS conversion を使ってよい。
- Resonite slot name、material asset identity、target batching state、coexistence decision を source model に持ち込んではならない。
- generated road-network elements が merge または simplify されても source provenance を保持する。

source abstraction には推定事実を含めてよい。ただし、それは inferred として label する。例として lane count、sidewalk presence、road-axis shape、intersection membership、road-space coverage がある。package name、source file、object id、LOD、CRS、mesh code のような source fact は inferred fact と区別したまま保持する。

## Output Unit

road-network output unit は road-space unit であり、dataset 全体でも individual target slot でもない。

最小の ownership unit は、1つの source road object から派生した generated road-network unit、または mesh-area filtering のために source object が split された場合の stable source-object group である。その unit は次を所有する。

- source provenance
- road-space coverage footprint
- road、way、lane、sidewalk、intersection、track などの generated network primitives
- その generated network unit に付随する generated visual geometry、marking、debug geometry

実装がこれを既存の `ImportedObjectUnit` 経由で投影する場合、descriptor は source-file/package/LOD scope のままとし、各 generated `ImportedCityObject` は stable road-network object key と source provenance を保持しなければならない。将来 dedicated road-network unit type を追加する場合でも、target-specific field なしで target-neutral construction geometry に変換できる必要がある。

後続変更で graph-wide optimization を明示的に導入しない限り、output unit を global road graph にしない。global connectivity は derived view として計算してよいが、ownership と coexistence は road-space unit へ trace できる必要がある。

## Ownership Boundary

road-network generator が所有するもの:

- road-package source evidence から target-neutral road-network unit を導出すること
- source provenance と inferred-vs-source label を保持すること
- deterministic object key と deterministic ordering を生成すること
- coexistence policy に必要な coverage metadata を出力すること

既存の CityGML source discovery と parsing pipeline が所有するもの:

- source file enumeration
- package normalization
- mesh-code selection
- CRS parsing と validation
- source object parsing

target adapter が所有するもの:

- target-neutral generated geometry から Resonite construction data への変換
- material と asset emission
- target-specific slot hierarchy と live-send behavior

Issue #131 が所有するもの:

- detailed transport output が road-network unit を suppress、replace、coexist のどれにするかの決定
- generated road-network unit と detailed transport-related output の road-space coverage 比較
- operator から見える precedence behavior の文書化

## #131 の積み上げ方

#131 はこの boundary を消費し、再定義しない。

推奨される build path:

1. road-network unit を code 実装する場合、road-space coverage の executable metadata を追加する。
2. coexistence は target emission 前、できれば source composition または object-unit optimization path で実行する。
3. detailed transport-related output と road-network unit は Resonite slot name ではなく coverage と provenance で比較する。
4. 後続の受け入れ済み design が sub-unit splitting を導入しない限り、policy は road-space unit 全体に適用する。
5. detailed-output precedence と merge rule は #131 documentation と tests に置き、この #130 boundary document には入れない。

#131 の未決事項:

- どの detailed transport package と object class が road-network unit を suppress するか。
- detailed output が常に勝つのか、または設定された detail level や coverage threshold に到達した場合だけ勝つのか。
- road-network unit から生成された marking を network unit と一緒に suppress するか。
- suppressed-unit count を CLI progress と datasource summary にどう出すか。

## Non-Goals

この文書は次を実装しない。

- road-network generation
- road-network export
- coexistence または precedence rule
- duplicate suppression
- Resonite-specific slot layout
- 将来 rename される概念の compatibility alias
