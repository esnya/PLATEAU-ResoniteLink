# 用語

この文書は current main に対するリネームマップです。Issue #165 以降の用語移行では、この英語版を正とします。

## ルール

- dataset、package、mesh-code、tile、adapter の概念名は PLATEAU SDK for Unity の用語に寄せます。
- 概念名を変えるときは、directory、file name、namespace、CLI help、docs、tests、resources を同じ変更単位でそろえます。
- repository 内部概念の rename では互換 alias を追加しません。古い名前は、外部入力、serialized data、履歴文書の境界にだけ残します。
- adapter 境界より上では target-neutral な名前を使います。Resonite 固有語は Resonite target、transport、live-send adapter 配下に閉じます。

## Canonical Map

| 現在または曖昧な用語 | canonical term | 対象 | migration rule |
| --- | --- | --- | --- |
| 日本標準地域メッシュを指す `tile` | `mesh-code` | CLI、docs、source discovery、dataset selection | user-facing text と internal identifier を rename します。`--tile` は migration cut で alias を消すまで deprecated external CLI alias としてだけ残します。 |
| PLATEAU mesh-code area を指す `mesh` | `mesh-code` または `mesh-code bounds` | discovery、filtering、source grouping | geography に `mesh` を使いません。`mesh` は renderable geometry payload のみに予約します。 |
| 単独の `source` | `CityGML source`、`GeoTIFF source`、`terrain texture source`、`dataset source` | CLI、import requests、source adapters | 型名や境界名で明示できていない限り、source kind を修飾します。 |
| 地理座標を指す `origin` | `geo origin` または `geodetic origin` | projection、local-coordinate conversion | `local origin` は projection 後の target/local scene anchor だけに使います。 |
| object/source ownership を指す `origin` | `source unit` または `source file` | parsed objects、bake scope、provenance | logical ownership は `source unit`、物理 CityGML file identity は `source file` とします。 |
| import preparation を指す `build` | `prepare`、`plan`、`bake`、`emit` | import pipeline、target execution | phase-specific verb を選びます。generic lifecycle phase として `build` を使いません。 |
| long-lived import state を指す `bootstrap` | `discovery`、`parsed`、`prepared`、`plan` | setup/discovery、result models | `bootstrap` は pre-streaming setup が固定 scene/session context を作る場合だけに残します。 |
| runtime policy を指す `profile` | `policy`、`preset`、`material profile` | import options、material defaults、budget settings | `profile` は named reusable material/budget preset のみに使い、任意の policy object には使いません。 |
| `package` | `PLATEAU package` または `package name` | `bldg`、`tran`、`dem` などの UDX package concepts | `package` は PLATEAU/UDX package name に使います。import code では file archive や dependency package に流用しません。 |
| `adapter` | `source adapter`、`target adapter`、`transport adapter` | boundary implementations | boundary の側を修飾します。 |

## Grandfathered Surfaces

次の名前は、専用 migration で取り除くまで残せます。

- 古い flag から canonical flag へ誘導するためだけにある deprecated CLI alias と error message。
- 過去の issue、PR、changelog、release note。
- third-party name、upstream schema name、package ID、外部定義の file path。
- ResoniteLink、CityGML、GeoTIFF、その他 external format が要求する serialized name または wire name。

## #165 の Cut Boundary

Issue #165 は repository-owned names だけを移行します。明示的な CLI alias removal を除き、external schema name、third-party asset path、user data format は変更しません。

最初の migration cut では次を行います。

- mesh-code concept に対する `tile` の deprecated internal use を取り除く。
- CityGML、GeoTIFF、terrain texture、dataset の境界をまたぐ曖昧な `source` identifier を分ける。
- generic な `origin` identifier を `geo origin`、`geodetic origin`、`local origin`、`source unit`、`source file` のいずれかへ置き換える。
- generic な `build` と misplaced `bootstrap` concept を phase-specific name に置き換える。
- 各 rename 後の directory ownership と namespace を一致させる。
