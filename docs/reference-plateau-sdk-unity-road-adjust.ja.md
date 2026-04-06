# Reference: PLATEAU-SDK-for-Unity Road Adjust

このリポジトリでは `PLATEAU-SDK-for-Unity` 自体は直接 vendor しません。

upstream リポジトリは MIT ライセンスです。このリポジトリで改変移植したコードに対応する
upstream MIT ライセンス全文は次に配置します。

- `THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt`

## 現時点の部分取り込み候補

- `Runtime/RoadAdjust/RnmModelAdjuster.cs`
  - 依存が比較的軽く、最小のジオメトリ調整単位です。
- `Runtime/RoadAdjust/RoadMarking/RoadMarkingGenerator.cs`
  - 道路標示生成の元実装ですが、Unity の道路ネットワーク型への依存が強いです。
- `Runtime/RoadAdjust/RoadNetworkToMesh/RoadNetworkToMesh.cs`
  - 道路メッシュ生成の主入口ですが、現行の .NET パイプラインへ直接持ち込むには Unity 依存が強すぎます。

## 取り込み方針

- canonical な upstream source として upstream repository を参照することを優先します。
- まとめてファイルを複製するより、小さく適応移植することを優先します。
- substantial portion をコピーする場合は、コピー先ファイルまたは隣接する帰属情報に upstream の MIT notice を残します。
- Unity 固有の実行時依存は application / domain layer に持ち込まないようにします。
