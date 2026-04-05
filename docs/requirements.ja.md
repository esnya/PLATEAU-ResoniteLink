# Requirements

## Product Goal

PLATEAU のデータセットを ResoniteLink 経由で Resonite に取り込む。

## First Functional Slice

- 入口は CLI とする。
- 利用者は少なくとも `dataset` と `mesh-code` を指定できる。
- コマンドは、対応する展開済み `udx/<package>/` 配下で、メッシュコードがファイル名またはネストされたディレクトリに現れるローカル PLATEAU CityGML に対する Resonite 構築契約を決定的に生成できる。
- importer は建物だけでなく、Unity SDK と公式 CityGML 命名規則で使われる公式 PLATEAU `udx/<package>/` prefix 群を扱えること。
- 構築契約は将来のライブ Resonite アダプターでもそのまま利用できる形に保つ。
- ローカルデータセットを最初の実装対象としつつ、入力モデルはサーバー由来データにも拡張可能にする。
- 参照先 texture asset が存在する場合、構築契約は詳細モデルの `ParameterizedTexture` を含む CityGML appearance binding を保持する。
- ライブ経路では mesh / texture に公式の ResoniteLink asset import message を使う。
- ライブ経路は、大きい mesh code でも送信前に全件をメモリ保持しないよう、city object を非同期に逐次送信できること。

## Non-Goals For Bootstrap

- GUI の提供
- データセット全体の一括取り込み最適化
- マシン固有の設定や手作業前提の運用

## Acceptance Signals

- 同じ入力から同じ構築計画が得られる。
- CLI の入力検証が自動テストで担保されている。
- live Resonite import により、指定 mesh code の mesh、material、renderer、collider を確認できる。
- フォーマット、Analyzer、ビルド、テストを CI で一貫して検証できる。
