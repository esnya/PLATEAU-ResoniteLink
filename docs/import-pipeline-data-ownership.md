# Import Pipeline Data Ownership

This note defines the current ownership boundaries for import-pipeline data. It is not a historical requirements document. Code remains the primary truth for exact behavior; this document names the data contracts and pure transformations that should guide future refactoring.

Not every conceptual name below is a concrete type today. When the current implementation still combines several concepts in one record or static helper, the `Current anchors` column points to the code that currently owns the behavior. New code should split along these boundaries only when the split makes invalid ownership or dependency direction fail earlier.

## Ownership Rules

- Source parsing owns CityGML and dataset-layout facts. It must not know Resonite slots, components, RPC IDs, or live-send batching.
- Application importing owns target-neutral city-object, surface, material, geometry, projection, and DEM contracts.
- Resonite target planning owns Resonite material/component/slot plans, but should keep them as pure plans until execution.
- Transport execution owns ResoniteLink calls, batch responses, asset uploads, and readback snapshots.
- Diagnostics own canonical dumps and recordings. They verify observable output, but they are not the source model.

## Data Contracts

| Conceptual data type | Conceptual owner | Role | Current anchors |
| --- | --- | --- | --- |
| `CityGmlSourceFile` / source-file unit | Source discovery and parsing | CityGML source file, package, matched mesh code, source-file root mesh context, and parse stream boundary. | `SourceFileDescriptor`, `CachedSourceFileDescriptor`, `SourceFilePipeline` in `Application/Importing/LocalCityGmlParsedSourceFileModels.cs`; discovery descriptors in `LocalCityGmlSourceFileDiscovery.cs`. |
| CityGML object envelope | CityGML parser | XML-element identity, display name, package, source file, and raw LOD/surface entry before projection. | Currently folded into `CityGmlSourceFileCityObjectProjection` and `ParsedCityObject` creation. |
| `BuildingAttributeContext` | CityGML attribute parser | Normalized PLATEAU building attributes. Material, roof, and projection policy read it; they do not own parsing. | `BuildingAttributeContext` and `BuildingAttributeParser` in `Application/Importing`. |
| `ParsedCityObject` | Application importing | Parsed source-CRS city object with no Resonite dependency. | `ParsedCityObject` in `LocalCityGmlParsedObjectModels.cs`. |
| `ParsedSurface` | Application importing | Source-side surface contract: semantic, rings, texture payload, optical properties, base color. | `ParsedSurface` and `ParsedRing` in `LocalCityGmlParsedObjectModels.cs`. |
| City-object origin, bounds, and altitude metrics | Projection support | Pure metrics derived from parsed geometry and used by projection, clipping, overlays, and height policies. | `CityObjectOriginResolver`, `CityObjectGeographicBoundsResolver`, `CityObjectAltitudeMetricsResolver`. |
| Projection frame | Projection layer | Source CRS, global origin, local Cartesian frame, and per-object mesh-code frame. | Currently passed as `GeodeticPoint`, `LocalCartesian`, `CoordinateReferenceSystem`, and DEM frame records in projection helpers. |
| DEM source-file frame and sampling draft | DEM projection | Source-file-wide TIN/surface sampling boundary before third-mesh output projection. | `DemSourceFileTerrainGridSamplingDraft`, `ConstructionCityObjectDraft`. |
| DEM third-mesh and emission frames | DEM projection | Third-mesh output identity, source-file sampling frame, output grid frame, height frame, and Resonite emission placement. | `DemTerrainObjectFrameResolver`, `CityGmlDemTerrainGridCityObjectProjection` frame records. |
| DEM terrain grid geometry and metrics | DEM projection | Sample counts, height range, world-base height, bounds, coverage, UV scale/offset, and height samples. | `DemTerrainGridProjectionBounds`, `TerrainGridGeometry`, `TerrainGridSampleCoverage`, `CityGmlDemTerrainGridCityObjectProjection`, `CityGmlDemTerrainGridSampler`. |
| Terrain overlay coverage and assignment | Terrain overlay policy | Contained/intersecting/none coverage, split requirement, selected overlay, or untextured fallback. | `DemTerrainOverlayAssignment`, `TerrainOverlayMaterialSourcePartitioner`, `TerrainTextureOverlay`. |
| Texture provider | Material and appearance layer | Dataset payload, atlas output, generated terrain texture, bundled texture, or already prepared texture identity. | `TexturePayload`, `ITextureImportSource`, `GeneratedTerrainTexture`, `BundledDefaultTextureAsset`, `TerrainTextureAssetGenerator`. |
| Material identity | Application material layer | Common, dedicated, vertex-color, wireframe, projection, reuse scope, and bundled family identity independent of prepared texture URI. | `MaterialBinding`, `DefaultCommonMaterialMember`, `MaterialReuseScope`, `MaterialType`, `MaterialProjection`. |
| Renderer binding | Resonite target planning | Renderer-side input such as direct material reference, main texture override, and terrain property block binding. | `PlannedRendererMaterialBinding` and subclasses in `ResoniteSceneEmissionPlan.cs`. |
| Target material asset plan | Resonite target planning | Resonite material component plan, reuse scope, dedicated material slot need, and texture import requirements. | `PlannedMaterialAsset`, `PlannedDedicatedMaterialAsset`, `PlannedReusableMaterialAsset`, `ResoniteSceneMaterialPlanComposer`, `ResoniteMaterialPlanning`. |
| Resonite emission plan | Resonite target planning | Pure planned slots, components, mesh assets, material bindings, ordering, and batch-local references. | `ResoniteSceneEmissionPlan.cs`, `ResoniteBatchEmissionPlanner`. |
| Live-send queue item | Live-send runtime | Runtime input unit for live-send scheduling. It should not become nested target-class detail. | `LiveSendQueuedCityObject`, `LiveSendQueuePlan`, `LiveSendRunPlan`. |
| Non-DEM bake buffer result | Non-DEM bake buffer | Typed found/missing/completed bake-buffer read result. | `INonDemSourceFileBakeEmitter`, `NonDemSourceFileBatching`, non-DEM bake models. |
| Canonical scene dump | Diagnostics | Observable output truth for regression validation after execution. | `CanonicalSceneDumpSink`, `SceneSinkRecordingClientCanonicalDump`. |

## Pure Transformations

| Pure transformation | Conceptual owner | Boundary rule | Current anchors |
| --- | --- | --- | --- |
| `XElement -> BuildingAttributeContext` | `BuildingAttributeParser` | Parse XML codes, sentinel values, and metrics only. Do not select material or target behavior. | `BuildingAttributeParser.Parse`. |
| `XElement + source-file unit -> CityGML object envelope` | CityGML object parser | Stop at identity, package, source file, and LOD/surface entry. Do not project. | Currently folded into `CityGmlSourceFileCityObjectProjection.Parse`. |
| CityGML object envelope -> `ParsedCityObject` | Parsed object factory | Create source-CRS object and surfaces after source mesh/bounds filtering. | `CityGmlSourceFileCityObjectProjection`, `CityGmlParsedSurfaceReader`. |
| `ParsedCityObject ->` origin/bounds/altitude metrics | Projection support resolvers | Return derived values with no side effects or target knowledge. | `CityObject*Resolver` helpers. |
| `ParsedCityObject -> ConstructionCityObjectDraft` | Application projection preparation | Resolve project-stage face roles and generated-surface drafts before tessellation. | `ConstructionCityObjectDraft.FromParsedCityObject`, `GeneratedLod1RoofCityObjectFactory`. |
| `ParsedSurface[] -> ConstructionFace[]` | Surface projection policy | Produce shared input for culling, roof generation, material role decisions, and tessellation. | `ConstructionCityObjectDraft`, `CityGmlSurfaceProjectionPolicy`. |
| `ParsedSurface + material context -> resolved surface material` | Surface material resolver | Combine semantic, payload, default material, overlay, and role before target material planning. | `ResolvedSurfaceMaterial`, `ResolvedMaterial`, `CityGmlSurfaceMaterialResolver`. |
| Resolved surface material -> material identity | Material identity selector | Choose common/dedicated/vertex/wireframe identity without depending on prepared texture URI. | `MaterialBinding`, `DefaultMaterialResolver`, `CommonMaterialCatalog`. |
| Resolved surface material -> texture provider | Texture provider resolver | Choose dataset, atlas, generated terrain, bundled, or no texture provider without changing material identity. | `TexturePayload`, `TerrainTextureAssetGenerator`, non-DEM bake models. |
| Material identity + texture provider -> renderer binding | Renderer binding composer | Compose direct material binding, main texture override, and terrain property-block inputs. | `ResoniteSceneMaterialPlanComposer`. |
| Renderer binding + target capabilities -> target material asset plan | Resonite material planner | Create a pure plan for material components, reusable assets, dedicated slots, and texture members. | `PlannedSceneMaterialPlan`, `ResoniteMaterialPlanning`. |
| `ParsedSurface[] + terrain overlay policy -> terrain overlay assignment` | Terrain overlay policy | Decide contained/intersecting/none, split requirement, and fallback without upload or RPC. | `DemTerrainOverlayAssignment`, `TerrainOverlayMaterialSourcePartitioner`. |
| Terrain overlay + mesh context -> terrain overlay mesh code | Terrain overlay mesh-code resolver | Keep actual/requested/requested-bounds fallback order deterministic. | `TerrainOverlayMaterialSourcePartitioner`, `TerrainOverlayMeshCodeResolver`. |
| DEM source-file surfaces + source-file frame -> DEM sampling draft | DEM projection | Make the source-file-wide buffer boundary explicit before per-third-mesh output. | `CityGmlParsedCityObjectProjection.CreateDemSourceFileTerrainGridSamplingDraft`. |
| DEM sampling draft + third mesh -> DEM terrain grid object | DEM projection | Convert source-file frame into third-mesh output object and frame. | `CityGmlParsedCityObjectProjection.ProjectTerrainMeshModeCityObject`. |
| DEM terrain object -> `TerrainGridGeometry` | DEM terrain grid projector | Produce sample count, height range, world-base height, coverage, and UV contract. | `CityGmlDemTerrainGridCityObjectProjection.TryProject`. |
| Imported object stream -> counting stream | Import orchestration | Count streamed units without pre-reading or materializing the stream. | `CountingImportedObjectUnitStream`. |
| Resonite emission plan -> batch operations | Resonite batch composer | Build RPC-ready operation lists before execution; resolve slot/component IDs only after the batch response. | `PlannedBatchEmission`, `ResoniteBatchEmissionPlanner`, `PlannedBatchEmissionInterpreter`. |
| Batch response -> canonical batch entity map | Target execution | Decode created slot/component locators from the response. | `CanonicalBatchEntityMap`. |
| Observed slot snapshot + source-root request -> source-root placement | Setup/readback interpretation | Resolve exact, ancestor, and deterministic ambiguity policy without creating slots. | `ResoniteSceneSlotSnapshot`, `ResoniteSceneSetupInterpreter`. |
| Source-root placement + city-object transform -> object slot hierarchy | Resonite placement planner | Decide local position and parent scope before RPC. | `ResoniteBatchEmissionPlanner`, `ResoniteSceneCreationModels`. |

## Impure Boundaries

| Impure processing | Owner | Boundary |
| --- | --- | --- |
| Archive extraction and source-file IO | Dataset source resolver | Parsed models receive source facts and streams; IO details do not leak into parsed city objects. |
| Texture download, image generation, and asset upload | Texture provider executor and Resonite texture uploader | Execute after texture-provider identity is selected. Planning remains separate from upload. |
| Resonite RPC | Live-send execution and batch emitter | Execute `ResoniteSceneEmissionPlan` / planned batch output only. |
| Existing slot/component readback | Setup interpreter and slot repository | Pass observed snapshots into planning. Do not hide readback inside pure planning. |
| Canonical dump generation | Diagnostics sink | Record observable regression truth after execution. Runtime models must not depend on dump shape. |
| Logging and progress | `ILogger` and progress view | Domain values and pure transformations do not call progress or logging directly. |

## Refactoring Guidance

- Prefer adding a small record or resolver when it removes a target dependency from source parsing or material policy.
- Prefer renaming an existing helper only when the new name clarifies ownership and does not imply behavior that the code does not yet provide.
- Do not add tests that only police naming or ownership. Express ownership with types, project dependencies, and centralized analyzer/build policy where possible.
- When two execution paths emit the same concept, compare equivalent output contracts instead of compensating path by path.
- For regressions, define the observable emitted payload, readback, or dump artifact that can disprove the proposed cause before treating a fix as complete.
