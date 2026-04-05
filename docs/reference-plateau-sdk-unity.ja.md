# Reference: PLATEAU-SDK-for-UNITY

このプロジェクトは `PLATEAU-SDK-for-UNITY` をインポート振る舞いの参照実装とみなす。

## この時点で合わせること

- データセットを明示的に選ぶこと。
- 空間単位の選択を、PLATEAU の正式名称である `mesh-code` として扱うこと。
- ローカルデータセットとサーバー由来データセットの両方を視野に入れた入力モデルにしておくこと。
- UI 先行ではなく、まずインポート契約と処理境界を固めること。

## 今後合わせる候補

- Unity 側で使われている package / mesh-code の概念を、ad-hoc な CLI 用語を増やさずに Resonite 構築契約へどう写像するか。
- 地理座標、標高、mesh code 原点などの扱いをどう Resonite 空間へ正規化するか。
- テクスチャ、属性情報、LOD 選択などを CLI 契約へどう拡張するか。

## 参照先

- GitHub repository: <https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity>
- Documentation portal: <https://project-plateau.github.io/PLATEAU-SDK-for-Unity/>
