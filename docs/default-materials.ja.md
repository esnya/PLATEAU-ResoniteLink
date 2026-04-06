# デフォルトマテリアル

テクスチャを持たない PLATEAU 地物には、地物タイプごとにデフォルトマテリアルを割り当てる PLATEAU SDK for Unity の考え方に合わせて、同梱デフォルトテクスチャを適用するようにした。fallback ファミリは 1 つ以上のバリエーションを持てるようにし、city object / material key から決定的に 1 つ選ぶので、再インポートしても結果は安定する。

## カテゴリ対応

現在の package bucket は、Unity SDK に合わせた命名で importer が対応する公式 package 候補全体から整理している。

- `building`: `bldg`, `ubld`
- `road`: `tran`, `rwy`, `squr`, `trk`
- `wireframe overlay`: `area`, `fld`, `htd`, `ifld`, `lsld`, `luse`, `rfld`, `tnm`, `urf`
- `vegetation`: `veg`
- `other solid fallback`: `brid`, `cons`, `frn`, `gen`, `tun`, `unf`, `wtr`, `wwy`
- `special case`: `dem` は生成 terrain overlay 経路を維持し、同梱 fallback family は使わない

現在の ResoniteLink 経路では、フォールバックをマテリアル意図ごとに分ける。

- データセット由来の詳細テクスチャは UV ベースの `PBS_Metallic` を維持する
- 未テクスチャの建物側面は UV ベースの facade texture を使う
- 未テクスチャの屋根、道路、その他 package は `PBS_TriplanarMetallic` を使う
- 未テクスチャの植生は、元データに `X3DMaterial.diffuseColor` があれば `PBS_VertexColorMetallic` を使い、無ければ他の textureless package と同じ default material fallback を使う
- `area`、`luse`、`fld`、`ifld`、`rfld`、`lsld`、`tnm`、`htd`、`urf` のような直接の設置物ではない重ね合わせデータは `WireframeMaterial` を使う

package から material 方針への対応は `PlateauPackageCatalog` に集約してあり、サポート対象の non-`dem` package が必ずちょうど 1 つの material bucket に入ることをテストで固定している。これにより、Unity SDK 側の package 対応と fallback policy のズレを見つけやすくしている。

建物の facade 判定は、`bldg:WallSurface` などの CityGML thematic surface を優先し、その文脈がない場合だけ polygon の向きから推定するようにした。fallback 経路では、ほぼ垂直な面を facade UV とみなし、roof / ground 系の semantic は triplanar を維持する。

生成する facade UV は、各 polygon を `0..1` に正規化するのではなく、固定の繰り返し密度を使う。これにより、同梱 facade texture を大きい壁面でもタイル表示できる。
さらに、建物壁面の fallback は facade 専用アセットのみにした。レンガ系マテリアルは壁 fallback では選ばれない。物理的な繰り返しスケールは Material 側で持ち、生成 facade UV は壁ローカルのまま、縦方向だけは壁の下端と上端が repeat 境界に乗るようにそろえる。

## 同梱アセット

リポジトリには AmbientCG の CC0 2K material を次の対応で同梱する。

- `facade`:
  `Facade018A_2K-JPG_Color.jpg`, `Facade019A_2K-JPG_Color.jpg`, `Facade020A_2K-JPG_Color.jpg`
- `roof`:
  `Concrete012_2K-JPG_Color.jpg`, `Concrete033_2K-JPG_Color.jpg`
- `road`:
  `Asphalt020L_2K-JPG_Color.jpg`, `Asphalt023L_2K-JPG_Color.jpg`
- `other`:
  `Concrete012_2K-JPG_Color.jpg`, `Ground054_2K-JPG_Color.jpg`

Sources:

- `Facade018A`: <https://ambientcg.com/view?id=Facade018A>
- `Facade019A`: <https://ambientcg.com/view?id=Facade019A>
- `Facade020A`: <https://ambientcg.com/view?id=Facade020A>
- `Concrete012`: <https://ambientcg.com/view?id=Concrete012>
- `Concrete033`: <https://ambientcg.com/view?id=Concrete033>
- `Asphalt020L`: <https://ambientcg.com/view?id=Asphalt020L>
- `Asphalt023L`: <https://ambientcg.com/view?id=Asphalt023L>
- `Ground054`: <https://ambientcg.com/view?id=Ground054>

リポジトリに残すのは、live material builder が直接使う最終マップだけに絞る。

- `*_Color.jpg`: albedo
- `*_NormalGL.jpg`: normal map
- `*_Height.jpg`: parallax 用の height map
- `*_Metallic.png`: 元 material に roughness がある場合の Resonite packed metallic map
- facade fallback では facade 系の `*_Emission.jpg` を残すが、`018A/019A/020A` の各ファミリ内で emissive pixel の面積が最小になるサブバリエーションを選び直している

同梱 metallic map は、Resonite wiki の `PBS_Metallic` に合わせて、R に metallic、G に occlusion または height、A に smoothness を入れる。さらに、同梱 `HeightMap` を割り当てる場合は、parallax が強すぎないように live builder 側で `HeightScale` を `0.002` に下げる。
