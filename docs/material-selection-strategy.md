# Material Selection Strategy

This document records the intended selection strategy for expanded bundled
default material candidates. It is documentation and test guidance only until a
separate implementation change wires the strategy into runtime selection.

## Current Contract

- Source `ParameterizedTexture` and other dataset appearance lanes win over
  bundled fallback materials. The strategy here applies only when a city object
  has no usable source texture.
- The existing deterministic SHA-256 variant selection contract must remain
  reproducible for equivalent inputs. New grouping inputs can extend the
  selection key, but must not make existing fallback selection time-dependent or
  process-order-dependent.
- Current bundled fallback families are listed in
  `BundledDefaultMaterialFamilies`. Building UV fallback uses the `facade`
  family, non-UV building fallback uses the `roof` family, road/path-like
  packages use `road`, vegetation uses `vegetation`, city furniture uses
  `city-furniture`, and generic fallback uses `other`.
- Common material setup currently enumerates package-scoped family variants and
  creates both UV and triplanar common material bindings, plus shared generic
  albedo and vertex-color common materials.

## Height Signals

Building material grouping should derive height from the most reliable
available signal, in this priority order:

1. CityGML explicit measured height, when present and positive.
2. CityGML storey count converted with a 3.5 m per above-ground storey estimate,
   when the measured height is absent.
3. Geometry or bbox height, when metadata is unavailable.
4. `unknown`, when no positive height can be observed.

The selected height signal and its source should be observable in tests at the
strategy boundary. The selection policy must not infer height from material
texture names or candidate ordering.

## Candidate Groups

Height-aware building fallback should group candidates before stable selection:

- `unknown`: current-equivalent conservative fallback. Use the existing facade
  and roof runtime candidate sets so missing height does not change output.
- `low`: buildings up to 10 m. Prefer smaller facade patterns and tiled or
  concrete roof candidates.
- `low-mid`: buildings over 10 m and up to 31 m. Mix current facade candidates with a
  small number of neutral wall/facade alternatives.
- `mid-high`: buildings over 31 m and up to 60 m. Prefer larger facade surfaces and
  subdued roof/asphalt candidates.
- `high`: buildings over 60 m. Keep the facade pool narrow and regular; avoid
  highly residential or small-tile roof materials unless a future package signal
  justifies them.

The collected candidate inventory includes AmbientCG Facade001, Facade005,
Facade006, Facade018A, Facade019A, Facade020A, asphalt roof variants, roofing
tile variants, and TextureCan Others0021, Others0022, Others0025, Others0026,
and Others0029. Provenance remains in `THIRD_PARTY_LICENSES/`.

## Fallback Behavior

- Dataset textures and vertex-color lanes are not replaced by bundled material
  selection.
- Missing or invalid height uses the `unknown` group, which must match the
  current-equivalent facade or roof fallback pool.
- Missing group candidates fall back to the current family pool for the package.
- Unknown packages keep the existing `other` fallback behavior unless a package
  catalog change explicitly maps them.
- Wireframe overlay packages remain wireframe and do not participate in bundled
  material candidate selection.

## Diversity And Pool Cap

Candidate groups should be intentionally small. A height group should expose no
more than four facade candidates and four roof candidates to runtime stable
selection unless a test documents why a wider pool is needed.

Selection should remain stable at dataset, mesh-code, city-object, package,
projection, height-group, and surface-role granularity. Nearby objects should
not appear randomly noisy only because many collected assets exist. If finer
neighborhood coherence is needed, add an explicit neighborhood or tile grouping
input rather than relying on process order.

## Common Material Warmup

Common material warmup is a setup contract, not a per-object repair path.
Expanded candidate pools must not require creating every collected material
asset for every building dataset by default.

Runtime implementation should satisfy one of these constraints:

- Keep the active per-package candidate union capped, so `CommonMaterialCatalog`
  can enumerate the complete package-scoped common material set during setup.
- Or introduce a separate, explicit setup-time discovery result that lists only
  the candidate families and variants required for the run before object
  emission begins.

Do not add lazy runtime common-material creation as a substitute for setup
planning unless a separate design change updates bootstrap tests and live-send
failure behavior. Appearance lanes must remain able to use their dedicated
material flow without being pulled into common-material warmup.

## Test Guidance

Implementation tests should cover:

- Height signal priority and `unknown` fallback.
- Stable selection for equivalent keys.
- Pool cap enforcement for each height group and surface role.
- Source appearance precedence over bundled fallback.
- Common material enumeration staying within the intended active candidate
  union, with no accidental warmup of all collected provenance assets.
