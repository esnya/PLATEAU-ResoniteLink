# Appearance Policy

This document defines the current supported scope for CityGML appearance handling in PLATEAU-ResoniteLink.

## Supported Path

- `GeoreferencedTexture` is supported only as part of the DEM-side georeferenced raster path already handled in #91.
- In that path, georeferenced raster metadata is used to resolve DEM terrain imagery and terrain texture overlays.
- That support is about DEM terrain imagery, not about general surface appearance projection.

## Explicitly Unsupported

- Non-DEM or building-surface `GeoreferencedTexture` projection is not supported.
- Parsed `GeoreferencedTexture` metadata may be retained for inspection or diagnostics, but it does not become a rendering contract for non-DEM surfaces.
- Do not infer UV projection, wrap mode behavior, border handling, alpha handling, or transparency semantics for non-DEM surfaces from `GeoreferencedTexture`.
- If a dataset relies on non-DEM `GeoreferencedTexture` for rendered appearance, treat that as out of scope unless a dedicated issue adds an explicit projection policy and tests.

## Relationship To Related Issues

- #91 established the supported DEM georeferenced-raster path.
- #128 covers `X3DMaterial` optical attributes such as `diffuseColor`, `ambientIntensity`, `emissiveColor`, `specularColor`, `shininess`, and `transparency`.
- #129 defines the supported `ParameterizedTexture` sampler semantics:
  - `wrapMode` values equivalent to repeat (`wrap`, `repeat`) use repeated sampling.
  - `wrapMode` values equivalent to clamp (`none`, `clamp`, `border`) use edge-clamped sampling.
  - `borderColor` is parsed for audit visibility but is not emitted as a Resonite border-color sampler; border-style sampling falls back to clamp.
- Texture alpha is preserved in dataset and atlas payloads. `X3DMaterial` `transparency` multiplies the material base alpha; atlas preparation may fill transparent RGB channels to reduce edge bleed without changing alpha.
- Neither #128 nor #129 imply support for non-DEM `GeoreferencedTexture` projection.

## Review Rule

- If future code changes touch non-DEM `GeoreferencedTexture`, they must either keep it parse-only or add an explicit rendering policy and matching tests in the same change.
