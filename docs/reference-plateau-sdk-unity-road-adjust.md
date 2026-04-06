# Reference: PLATEAU-SDK-for-Unity Road Adjust

This repository does not vendor `PLATEAU-SDK-for-Unity` directly.

The upstream repository is MIT-licensed. A local copy of the upstream MIT license
used for adapted code in this repository is stored at:

- `THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt`

## Current Partial-Integration Targets

- `Runtime/RoadAdjust/RnmModelAdjuster.cs`
  - Smallest geometry-adjustment unit with relatively light dependencies.
- `Runtime/RoadAdjust/RoadMarking/RoadMarkingGenerator.cs`
  - Source of road-marking generation behavior, but strongly coupled to Unity road-network types.
- `Runtime/RoadAdjust/RoadNetworkToMesh/RoadNetworkToMesh.cs`
  - Main road-mesh generation entrypoint, but too Unity-specific for direct import into the current .NET pipeline.

## Import Policy

- Prefer referencing the upstream repository as the canonical source.
- Prefer small, adapted ports over bulk file copies.
- When copying substantial portions, preserve the upstream MIT notice in the copied file or adjacent attribution.
- Keep Unity-specific runtime dependencies out of the application and domain layers.
