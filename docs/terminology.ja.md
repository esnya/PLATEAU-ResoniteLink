# 用語

この文書は、`#126` と `#127` で追跡している用語移行に向けて、正規の用語と rename 境界を固定するためのものです。

## 目的

- 移行が入る前に、新しい曖昧な名称の追加を止める
- 正規の用語と、当面残す legacy surface を分離する
- 後続の rename cut を alias なしの atomic な変更にする

## ルール

- 英語ドキュメントを正規の wording source として扱う
- 移行中に互換 alias を導入しない。用語を実際に変更する場合は、ディレクトリ、ファイル名、namespace、type、docs、CLI surface を同じ cut で更新する
- この文書以降、code、docs、tests、issue text に曖昧な裸の用語を新規追加しない

## 正規の用語

| 用語 | 正規の意味 | 補足 |
| --- | --- | --- |
| `mesh` | geometry mesh data | PLATEAU の mesh-code selection を bare な `mesh` で表さない |
| `mesh code` | PLATEAU の geographic mesh selector | 既存の public surface `--mesh-code` は rename cut まで grandfathered とする |
| `source` | input location または retrieval source | dataset/archive/raster の path や URL にだけ使う |
| `origin` | provenance または geodetic reference origin | `object origin`、`file origin`、`geodetic origin` のような qualified form を優先する |
| `build` | legacy な import/execution term に限る | 新しい名称では `import`、`compose`、`emit`、または phase-specific な動詞を優先する |
| `bootstrap` | qualified setup phase label に限る | `discovery bootstrap` や `scene bootstrap` のように必ず qualifier を付ける |
| `profile` | configurable behavior または budget profile | 関係のない folder や namespace の generic な grouping word として使わない |
| `PLATEAU package` | `bldg`、`dem`、`tran` のような PLATEAU dataset package | dependency package と衝突しうる場面では、`package` を必ず修飾する |
| `NuGet package` | dependency package | PLATEAU package と衝突しうる場面では、`package` を必ず修飾する |

## 当面許容する legacy surface

これらの名前は dedicated rename migration が入るまでは許容しますが、新しい API や docs に複製してはいけません。

- CLI の `build` command と関連する `SceneBuild*` types
- `PlateauMeshCode`
- `ResoniteLocalOrigin`
- provenance を意味している既存の `Source*` field / type 名
- `Tests.Profiles` folder / namespace grouping

## 新規利用の禁止

- PLATEAU の mesh-code selection を意味するときに bare な `mesh` を使わない
- provenance を意味するときに `source` を使わず、意図が `origin` ならそれを使う
- phase qualifier なしの bare `bootstrap` を使わない
- 実際の configurable-profile の意味がないのに、pipeline や grouping の名前へ `profile` を使わない
- PLATEAU package または dependency package を意味するときに bare な `package` を使わない
- 既存の grandfathered surface を拡張する場合を除き、新しい import-phase 名へ `build` を持ち込まない

## 移行境界

`#126` は用語と rename map の定義だけを行い、コードの rename はしません。

`#127` で承認済みの移行を atomic に適用します。

- code、directories、namespaces、docs、CLI terms を 1 cut で更新する
- legacy name は alias で残さずに除去する
- terminology を変えても behavior は維持する
