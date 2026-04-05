# デフォルトマテリアル

テクスチャを持たない PLATEAU 地物には、地物タイプごとにデフォルトマテリアルを割り当てる PLATEAU SDK for Unity の考え方に合わせて、同梱デフォルトテクスチャを適用するようにした。各ファミリには複数のバリエーションを持たせ、city object / material key から決定的に 1 つ選ぶので、再インポートしても結果は安定する。

## カテゴリ対応

- `building`: `bldg`, `ubld`
- `road`: `tran`, `rwy`, `squr`, `trk`
- `other`: それ以外の package

現在の ResoniteLink 経路では、フォールバックを投影方式で分ける。

- データセット由来の詳細テクスチャは UV ベースの `PBS_Metallic` を維持する
- 未テクスチャの建物側面は UV ベースの facade texture を使う
- 未テクスチャの屋根、道路、その他 package は `PBS_TriplanarMetallic` を使う

建物の facade 判定は現時点では polygon の向きから推定している。ほぼ垂直な面を facade とみなし、それ以外は triplanar に倒す。

## 同梱アセット

リポジトリには AmbientCG の CC0 2K albedo texture を次の対応で同梱する。

- `facade`:
  `PaintedPlaster012_2K-JPG_Color.jpg`, `Facade019A_2K-JPG_Color.jpg`, `Bricks074_2K-JPG_Color.jpg`
- `roof`:
  `Concrete012_2K-JPG_Color.jpg`, `Concrete033_2K-JPG_Color.jpg`
- `road`:
  `Asphalt002_2K-JPG_Color.jpg`, `Road006_2K-JPG_Color.jpg`
- `other`:
  `Concrete012_2K-JPG_Color.jpg`, `Ground054_2K-JPG_Color.jpg`

Sources:

- `PaintedPlaster012`: <https://ambientcg.com/view?id=PaintedPlaster012>
- `Facade019A`: <https://ambientcg.com/view?id=Facade019A>
- `Bricks074`: <https://ambientcg.com/view?id=Bricks074>
- `Concrete012`: <https://ambientcg.com/view?id=Concrete012>
- `Concrete033`: <https://ambientcg.com/view?id=Concrete033>
- `Asphalt002`: <https://ambientcg.com/view?id=Asphalt002>
- `Road006`: <https://ambientcg.com/view?id=Road006>
- `Ground054`: <https://ambientcg.com/view?id=Ground054>

現状は albedo map のみを同梱する。live material builder 側で、上記の経路に応じて `PBS_Metallic` または `PBS_TriplanarMetallic` に接続する。
