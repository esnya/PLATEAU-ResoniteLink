# デフォルトマテリアル

テクスチャを持たない PLATEAU 地物には、地物タイプごとにデフォルトマテリアルを割り当てる PLATEAU SDK for Unity の考え方に合わせて、同梱デフォルトテクスチャを適用するようにした。fallback ファミリは 1 つ以上のバリエーションを持てるようにし、city object / material key から決定的に 1 つ選ぶので、再インポートしても結果は安定する。

## カテゴリ対応

現在の package bucket は、Unity SDK に合わせた命名で importer が対応する公式 package 候補全体から整理している。

- `building`: `bldg`, `ubld`
- `road`: `tran`, `rwy`, `squr`, `trk`
- `wireframe overlay`: `area`, `fld`, `htd`, `ifld`, `lsld`, `luse`, `rfld`, `tnm`, `urf`
- `vegetation`: `veg`
- `city furniture`: `frn`
- `other solid fallback`: `brid`, `cons`, `gen`, `tun`, `unf`, `wtr`, `wwy`
- `special case`: `dem` は生成 terrain overlay 経路を維持し、同梱 fallback family は使わない

package から material 方針への対応は `PlateauPackageCatalog` に集約してあり、サポート対象の non-`dem` package が必ずちょうど 1 つの material bucket に入ることをテストで固定している。これにより、Unity SDK 側の package 対応と fallback policy のズレを見つけやすくしている。`frn` は汎用 `other` bucket ではなく、専用の `city-furniture` fallback family に入る。
2026-03-12 公開の `PLATEAU-SDK-for-Unity` [`v4.2.0`](https://github.com/Project-PLATEAU/PLATEAU-SDK-for-Unity/releases/tag/v4.2.0) 時点では、Unity 側の `PredefinedCityModelPackage.CityFurniture` は `PlateauDefaultCityFurniture` に対応し、見た目は generic metal 系になっている。ResoniteLink もその見た目に寄せるが、チェックインする fallback texture data 自体は Unity SDK の asset をコピーせず、AmbientCG から直接取得したものを使う。

## `dem` Terrain Imagery メモ

`dem` は出所管理が必要な special case である。現在の既定 generated terrain overlay は `LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureUrlTemplate` に固定しており、Geospatial Information Authority of Japan (GSI, 国土地理院) の seamless photo tile endpoint を使う。

- `https://cyberjapandata.gsi.go.jp/xyz/seamlessphoto/{z}/{x}/{y}.jpg`

この imagery はリポジトリへ同梱していない。CLI が public な GSI tile service から必要時に取得し、DEM terrain texture を生成する。

公開・再配布・派生物の公開では、次を確認すること。

- 正式な利用案内の入口は GSI の tile list: <https://maps.gsi.go.jp/development/>
- GSI Maps の利用規約では、tile 利用は国土地理院コンテンツ利用規約に従うこと、また tile によっては第三者権利や個別法令上の制約がありうることが明示されている: <https://maps.gsi.go.jp/help/termsofuse.html>
- seamless photo tile の項目では、データソースを `全国最新写真（シームレス）` とし、元データ構成と承認情報の確認先として公式の caution PDF を案内している: <https://cyberjapandata.gsi.go.jp/legend/seamlessphoto_precaution.pdf>
- seamless photo の一部範囲には、地方公共団体などが作成したオルソ画像が含まれる。その範囲では追加の出典記載や複製・利用制限がかかることがあるため、実際に出荷・公開する coverage に対して最新の公式注記を確認すること。

この依存関係のローカル追跡メモは `THIRD_PARTY_LICENSES/gsi-seamlessphoto.txt` に置く。

## `frn` サンプリングメモ

Unity SDK の `TestDataTokyoMini` にある `frn` fixture をサンプリングすると、詳細な city furniture はかなり texture 主導だった。

- `53394525_frn_6697_sjkms_op.gml` には 2 つの `frn:CityFurniture` object があり、`lod2Geometry` を使っている
- `ParameterizedTexture` は 34 件あり、参照する texture image は 17 種類
- `X3DMaterial` は含まれない
- 481 polygon のうち 467 polygon には明示的な texture target があり、未割当の少数 polygon は水平面、斜面、垂直面にまたがる

このサンプルから、現在の方針は妥当だと判断できる。

- dataset texture がある場合は UV ベースの dataset material を維持する
- `frn` の一部 polygon が未テクスチャでも、building の facade/roof 分岐には寄せず、triplanar fallback で受ける

## 同梱アセット

リポジトリには次の fallback material を同梱する。

- `facade`:
  `Facade001_2K-JPG_Color.jpg`、`Facade012_2K-JPG_Color.jpg`、`Facade016_2K-JPG_Color.jpg` と、
  zero-emission の `Facade018A_2K-JPG_Color.jpg`、`Facade019A_2K-JPG_Color.jpg`、`Facade020A_2K-JPG_Color.jpg`
- `roof`:
  `Concrete012_2K-JPG_Color.jpg`, `Concrete033_2K-JPG_Color.jpg`
- `road`:
  `Asphalt020L_2K-JPG_Color.jpg`, `Asphalt023L_2K-JPG_Color.jpg`
- `city-furniture`:
  `Metal032_2K-JPG_Color.jpg`
- `other`:
  `Concrete012_2K-JPG_Color.jpg`, `Ground054_2K-JPG_Color.jpg`

Sources:

- `Facade001`: <https://ambientcg.com/view?id=Facade001>
- `Facade012`: <https://ambientcg.com/view?id=Facade012>
- `Facade016`: <https://ambientcg.com/view?id=Facade016>
- `Facade018A`: <https://ambientcg.com/view?id=Facade018A>
- `Facade019A`: <https://ambientcg.com/view?id=Facade019A>
- `Facade020A`: <https://ambientcg.com/view?id=Facade020A>
- `Concrete012`: <https://ambientcg.com/view?id=Concrete012>
- `Concrete033`: <https://ambientcg.com/view?id=Concrete033>
- `Asphalt020L`: <https://ambientcg.com/view?id=Asphalt020L>
- `Asphalt023L`: <https://ambientcg.com/view?id=Asphalt023L>
- `Ground054`: <https://ambientcg.com/view?id=Ground054>
- `Metal032`: <https://ambientcg.com/view?id=Metal032>

現在このリポジトリに同梱している fallback texture は、すべて AmbientCG 由来で、ライセンスは CC0 1.0 である。
ローカルの追跡用メモは `THIRD_PARTY_LICENSES/ambientCG-CC0-1.0.txt` に置いている。

チェックイン済み asset family ごとの取得元:

- `default-materials/facade/Facade001_2K-JPG_*` -> AmbientCG `Facade001` -> <https://ambientcg.com/view?id=Facade001>
- `default-materials/facade/Facade012_2K-JPG_*` -> AmbientCG `Facade012` -> <https://ambientcg.com/view?id=Facade012>
- `default-materials/facade/Facade016_2K-JPG_*` -> AmbientCG `Facade016` -> <https://ambientcg.com/view?id=Facade016>
- `default-materials/facade/Facade018A_2K-JPG_*` -> AmbientCG `Facade018A` -> <https://ambientcg.com/view?id=Facade018A>
- `default-materials/facade/Facade019A_2K-JPG_*` -> AmbientCG `Facade019A` -> <https://ambientcg.com/view?id=Facade019A>
- `default-materials/facade/Facade020A_2K-JPG_*` -> AmbientCG `Facade020A` -> <https://ambientcg.com/view?id=Facade020A>
- `default-materials/roof/Concrete012_2K-JPG_*` -> AmbientCG `Concrete012` -> <https://ambientcg.com/view?id=Concrete012>
- `default-materials/roof/Concrete033_2K-JPG_*` -> AmbientCG `Concrete033` -> <https://ambientcg.com/view?id=Concrete033>
- `default-materials/road/Asphalt020L_2K-JPG_*` -> AmbientCG `Asphalt020L` -> <https://ambientcg.com/view?id=Asphalt020L>
- `default-materials/road/Asphalt023L_2K-JPG_*` -> AmbientCG `Asphalt023L` -> <https://ambientcg.com/view?id=Asphalt023L>
- `default-materials/other/Ground054_2K-JPG_*` -> AmbientCG `Ground054` -> <https://ambientcg.com/view?id=Ground054>
- `default-materials/other/Concrete012_2K-JPG_*` -> AmbientCG `Concrete012` -> <https://ambientcg.com/view?id=Concrete012>
- `default-materials/city-furniture/Metal032_2K-JPG_*` -> AmbientCG `Metal032` -> <https://ambientcg.com/view?id=Metal032>

`city-furniture` の metallic map は upstream のファイルをそのまま置いているわけではない。`Metal032_2K-JPG_Metallic.png` は AmbientCG の `Metal032_2K-JPG_Metalness.jpg` と `Metal032_2K-JPG_Roughness.jpg` から Resonite `PBS_Metallic` 向けに再pack したもの。

リポジトリに残すのは、live material builder が直接使う最終マップだけに絞る。

- `*_Color.jpg`: albedo
- `*_NormalGL.jpg`: normal map
- `*_Height.jpg`: parallax 用の height map
- `*_Metallic.png`: 元 material に roughness がある場合の Resonite packed metallic map
- facade fallback では source family が持つ場合だけ `*_Emission.jpg` を残す。active variant set は AmbientCG の facade substance family ごとに見直し、各系統で最も発光の弱い代表だけを残す。採用するのは `FacadeSubstance001` の `Facade001`、`FacadeSubstance002` の `Facade012`、`FacadeSubstance003` の `Facade016`、および emissive pixel coverage がゼロの `Facade018A`、`Facade019A`、`Facade020A`

同梱 metallic map は、Resonite wiki の `PBS_Metallic` に合わせて、R に metallic、G に occlusion または height、A に smoothness を入れる。さらに、同梱 `HeightMap` を割り当てる場合は、parallax が強すぎないように live builder 側で `HeightScale` を `0.002` に下げる。
