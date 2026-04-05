# デフォルトマテリアル

テクスチャを持たない PLATEAU 地物には、地物タイプごとにデフォルトマテリアルを割り当てる PLATEAU SDK for Unity の考え方に合わせて、同梱デフォルトテクスチャを適用するようにした。各ファミリには複数のバリエーションを持たせ、city object / material key から決定的に 1 つ選ぶので、再インポートしても結果は安定する。

## カテゴリ対応

- `building`: `bldg`, `ubld`
- `road`: `tran`, `rwy`, `squr`, `trk`
- `other`: それ以外の package

現在の ResoniteLink 経路では、フォールバックをマテリアル意図ごとに分ける。

- データセット由来の詳細テクスチャは UV ベースの `PBS_Metallic` を維持する
- 未テクスチャの建物側面は UV ベースの facade texture を使う
- 未テクスチャの屋根、道路、その他 package は `PBS_TriplanarMetallic` を使う
- `area`、`luse`、`fld`、`ifld`、`rfld`、`lsld`、`tnm`、`htd`、`urf` のような直接の設置物ではない重ね合わせデータは `WireframeMaterial` を使う

建物の facade 判定は、`bldg:WallSurface` などの CityGML thematic surface を優先し、その文脈がない場合だけ polygon の向きから推定するようにした。fallback 経路では、ほぼ垂直な面を facade UV とみなし、roof / ground 系の semantic は triplanar を維持する。

生成する facade UV は、各 polygon を `0..1` に正規化するのではなく、固定の繰り返し密度を使う。これにより、同梱 facade texture を大きい壁面でもタイル表示できる。
さらに、建物壁面の fallback は facade 専用アセットのみにした。レンガ系マテリアルは壁 fallback では選ばれない。物理的な繰り返しスケールは Material 側で持ち、生成 facade UV は壁ローカルのまま、縦方向だけは壁の下端と上端が repeat 境界に乗るようにそろえる。

## 同梱アセット

リポジトリには AmbientCG の CC0 2K albedo texture を次の対応で同梱する。

- `facade`:
  `Facade018C_2K-JPG_Color.jpg`, `Facade019A_2K-JPG_Color.jpg`, `Facade020A_2K-JPG_Color.jpg`
- `roof`:
  `Concrete012_2K-JPG_Color.jpg`, `Concrete033_2K-JPG_Color.jpg`
- `road`:
  `Asphalt002_2K-JPG_Color.jpg`, `Road006_2K-JPG_Color.jpg`
- `other`:
  `Concrete012_2K-JPG_Color.jpg`, `Ground054_2K-JPG_Color.jpg`

Sources:

- `Facade018C`: <https://ambientcg.com/view?id=Facade018C>
- `Facade019A`: <https://ambientcg.com/view?id=Facade019A>
- `Facade020A`: <https://ambientcg.com/view?id=Facade020A>
- `Concrete012`: <https://ambientcg.com/view?id=Concrete012>
- `Concrete033`: <https://ambientcg.com/view?id=Concrete033>
- `Asphalt002`: <https://ambientcg.com/view?id=Asphalt002>
- `Road006`: <https://ambientcg.com/view?id=Road006>
- `Ground054`: <https://ambientcg.com/view?id=Ground054>

現状は albedo map のみを同梱する。live material builder 側で、上記の経路に応じて `PBS_Metallic`、`PBS_TriplanarMetallic`、または `WireframeMaterial` に接続する。
