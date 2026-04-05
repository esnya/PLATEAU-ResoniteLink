# Reference: PLATEAU-SDK-for-UNITY

このプロジェクトは `PLATEAU-SDK-for-UNITY` をインポート振る舞いの参照実装とみなす。

## この時点で合わせること

- データセットを明示的に選ぶこと。
- 空間単位の選択を、PLATEAU の正式名称である `mesh-code` として扱うこと。
- Unity 側の package 対応がそのまま持ち込めるよう、`udx/<package>/` の公式 PLATEAU package 命名に従うこと。
- Unity 側の `dem` 地形テクスチャに合わせ、航空写真や地図タイルを Web Mercator の共有 overlay として結合し、繰り返し前提の代替 texture ではなく、その geographic overlay に対して DEM の UV を割り当てること。
- `DatasetSourceConfigLocal` と `DatasetSourceConfigRemote` の両方を視野に入れ、`LocalSourcePath` や `ServerUrl` のような Unity SDK 側の名称を import model でも踏襲すること。
- UI 先行ではなく、まずインポート契約と処理境界を固めること。
- 大きいデータは、ひとつの全件バッチを前提にせず、tile や city object を増分的に流せる構造を優先すること。

## 今後合わせる候補

- Unity 側で使われている package / mesh-code の概念を、ad-hoc な CLI 用語を増やさずに Resonite import flow へどう写像するか。
- 地理座標、標高、mesh code 原点などの扱いをどう Resonite 空間へ正規化するか。
- テクスチャ、属性情報、LOD 選択などを CLI 契約へどう拡張するか。

## 参照先

- GitHub repository: <https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity>
- Documentation portal: <https://project-plateau.github.io/PLATEAU-SDK-for-Unity/>
