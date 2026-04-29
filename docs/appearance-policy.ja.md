# Appearance Policy

この文書は、PLATEAU-ResoniteLink における CityGML appearance 処理の現在の対応範囲を定義します。

## 対応済みの経路

- `GeoreferencedTexture` は、#91 で既に扱っている DEM 側の georeferenced raster 経路に限って対応します。
- その経路では、georeferenced raster のメタデータを使って DEM terrain imagery と terrain texture overlay を解決します。
- ここでの対応は DEM の terrain imagery に関するものであり、一般の surface appearance projection ではありません。

## 明示的な未対応

- 非 DEM、または building-surface の `GeoreferencedTexture` projection は未対応です。
- 解析した `GeoreferencedTexture` メタデータは、inspection や diagnostics のために保持してよいですが、非 DEM surface の rendering contract にはなりません。
- 非 DEM surface に対して、`GeoreferencedTexture` から UV projection、wrap mode、border handling、alpha handling、transparency semantics を推測してはいけません。
- もし dataset が rendered appearance として非 DEM `GeoreferencedTexture` を前提にしているなら、専用の issue で explicit な projection policy と tests を追加するまで out of scope です。

## 関連 Issue との関係

- #91 が、対応済みの DEM georeferenced-raster 経路を定義しました。
- #128 は、`diffuseColor`、`ambientIntensity`、`emissiveColor`、`specularColor`、`shininess`、`transparency` などの `X3DMaterial` optical attributes を扱います。
- #129 は、対応する `ParameterizedTexture` sampler semantics を定義します。
  - repeat 相当の `wrapMode` (`wrap`, `repeat`) は repeated sampling として扱います。
  - clamp 相当の `wrapMode` (`none`, `clamp`, `border`) は edge-clamped sampling として扱います。
  - `borderColor` は audit visibility のため parse しますが、Resonite の border-color sampler としては emit しません。border-style sampling は clamp fallback です。
- texture alpha は dataset / atlas payload 上で保持します。`X3DMaterial` の `transparency` は material base alpha に乗算します。atlas preparation は edge bleed 低減のため transparent RGB channels を埋めることがありますが、alpha は変更しません。
- #128 と #129 のどちらも、非 DEM `GeoreferencedTexture` projection の対応を意味しません。

## レビュー基準

- 今後 non-DEM `GeoreferencedTexture` に触れる code change は、parse-only のまま維持するか、同じ変更内で explicit な rendering policy と tests を追加する必要があります。
