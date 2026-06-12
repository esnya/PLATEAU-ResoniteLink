# インポートパイプラインのデータ所有

このメモは、インポートパイプラインにおける現在のデータ所有境界を定義します。過去の要件文書ではありません。正確な挙動の一次 truth は引き続きコードであり、この文書は今後のリファクタリングで基準にするデータ契約と pure 変換の名前を置くためのものです。

以下の概念名がすべて現在の具象型として存在するわけではありません。現行実装が複数の概念を 1 つの record や static helper にまとめている場合、`現在のアンカー` 列に、その挙動を現在所有しているコードを示します。新しいコードで分割する場合は、その分割によって誤った所有や依存方向がより早く失敗する場合に限ります。

## 所有ルール

- source parsing は CityGML と dataset layout の事実を所有します。Resonite slot、component、RPC ID、live-send batching を知ってはいけません。
- application importing は target-neutral な city object、surface、material、geometry、projection、DEM の契約を所有します。
- Resonite target planning は Resonite material / component / slot plan を所有しますが、実行までは pure plan として保持します。
- transport execution は ResoniteLink call、batch response、asset upload、readback snapshot を所有します。
- diagnostics は canonical dump と recording を所有します。observable output の検証には使いますが、source model ではありません。

## データ契約

| 概念的データ型 | 概念的所有者 | 役割 | 現在のアンカー |
| --- | --- | --- | --- |
| `CityGmlSourceFile` / source-file unit | Source discovery と parsing | CityGML source file、package、matched mesh code、source-file root mesh context、parse stream boundary。 | `Application/Importing/LocalCityGmlParsedSourceFileModels.cs` の `SourceFileDescriptor`、`CachedSourceFileDescriptor`、`SourceFilePipeline`; `LocalCityGmlSourceFileDiscovery.cs` の discovery descriptor。 |
| CityGML object envelope | CityGML parser | projection 前の XML element identity、display name、package、source file、raw LOD / surface entry。 | 現在は `CityGmlSourceFileCityObjectProjection` と `ParsedCityObject` 作成に畳み込まれている。 |
| `BuildingAttributeContext` | CityGML attribute parser | 正規化済み PLATEAU building attribute。material、roof、projection policy は読むだけで、parse は所有しない。 | `Application/Importing` の `BuildingAttributeContext` と `BuildingAttributeParser`。 |
| `ParsedCityObject` | Application importing | Resonite 依存を持たない source-CRS 上の parsed city object。 | `LocalCityGmlParsedObjectModels.cs` の `ParsedCityObject`。 |
| `ParsedSurface` | Application importing | surface semantic、rings、texture payload、optical properties、base color を持つ source-side surface contract。 | `LocalCityGmlParsedObjectModels.cs` の `ParsedSurface` と `ParsedRing`。 |
| City-object origin、bounds、altitude metrics | Projection support | parsed geometry から得る pure metrics。projection、clipping、overlay、height policy が使う。 | `CityObjectOriginResolver`、`CityObjectGeographicBoundsResolver`、`CityObjectAltitudeMetricsResolver`。 |
| Projection frame | Projection layer | source CRS、global origin、local Cartesian frame、object / mesh-code frame。 | 現在は projection helper に渡される `GeodeticPoint`、`LocalCartesian`、`CoordinateReferenceSystem` と DEM frame record。 |
| DEM source-file frame と sampling draft | DEM projection | third-mesh output projection 前の source-file-wide TIN / surface sampling boundary。 | `DemSourceFileTerrainGridSamplingDraft`、`ConstructionCityObjectDraft`。 |
| DEM third-mesh / emission frame | DEM projection | DEM terrain object の third-mesh output identity、source-file sampling frame、output grid frame、height frame、Resonite emission placement。 | `DemTerrainObjectFrameResolver`、`CityGmlDemTerrainGridCityObjectProjection` の frame records。 |
| DEM terrain grid geometry と metrics | DEM projection | sample count、height range、world-base height、bounds、coverage、UV scale / offset、height samples。 | `DemTerrainGridProjectionBounds`、`TerrainGridGeometry`、`TerrainGridSampleCoverage`、`CityGmlDemTerrainGridCityObjectProjection`、`CityGmlDemTerrainGridSampler`。 |
| Terrain overlay coverage と assignment | Terrain overlay policy | contained / intersecting / none、split requirement、selected overlay、untextured fallback。 | `DemTerrainOverlayAssignment`、`TerrainOverlayMaterialSourcePartitioner`、`TerrainTextureOverlay`。 |
| Texture provider | Material / appearance layer | dataset payload、atlas output、generated terrain texture、bundled texture、prepared texture identity。 | `TexturePayload`、`ITextureImportSource`、`GeneratedTerrainTexture`、`BundledDefaultTextureAsset`、`TerrainTextureAssetGenerator`。 |
| Material identity | Application material layer | prepared texture URI から独立した common、dedicated、vertex-color、wireframe、projection、reuse scope、bundled family identity。 | `MaterialBinding`、`DefaultCommonMaterialMember`、`MaterialReuseScope`、`MaterialType`、`MaterialProjection`。 |
| Renderer binding | Resonite target planning | direct material reference、main texture override、terrain property block binding などの renderer-side input。 | `ResoniteSceneEmissionPlan.cs` の `PlannedRendererMaterialBinding` と派生型。 |
| Target material asset plan | Resonite target planning | Resonite material component plan、reuse scope、dedicated material slot 要否、texture import requirement。 | `PlannedMaterialAsset`、`PlannedDedicatedMaterialAsset`、`PlannedReusableMaterialAsset`、`ResoniteSceneMaterialPlanComposer`、`ResoniteMaterialPlanning`。 |
| Resonite emission plan | Resonite target planning | pure な planned slots、components、mesh assets、material bindings、ordering、batch-local references。 | `ResoniteSceneEmissionPlan.cs`、`ResoniteBatchEmissionPlanner`。 |
| Live-send queue item | Live-send runtime | live-send scheduling の runtime input unit。target class の nested detail に戻さない。 | `LiveSendQueuedCityObject`、`LiveSendQueuePlan`、`LiveSendRunPlan`。 |
| Non-DEM bake buffer result | Non-DEM bake buffer | bake-buffer read result を found / missing / completed などの型付き結果として表す。 | `INonDemSourceFileBakeEmitter`、`NonDemSourceFileBatching`、non-DEM bake models。 |
| Canonical scene dump | Diagnostics | execution 後の regression validation に使う observable output truth。 | `CanonicalSceneDumpSink`、`SceneSinkRecordingClientCanonicalDump`。 |

## Pure 変換

| Pure 変換 | 概念的所有者 | 境界ルール | 現在のアンカー |
| --- | --- | --- | --- |
| `XElement -> BuildingAttributeContext` | `BuildingAttributeParser` | XML code、sentinel value、metrics の parse のみ。material や target behavior を選ばない。 | `BuildingAttributeParser.Parse`。 |
| `XElement + source-file unit -> CityGML object envelope` | CityGML object parser | identity、package、source file、LOD / surface entry で止める。projection しない。 | 現在は `CityGmlSourceFileCityObjectProjection.Parse` に畳み込まれている。 |
| CityGML object envelope -> `ParsedCityObject` | Parsed object factory | source mesh / bounds filtering の後で source-CRS object と surfaces を作る。 | `CityGmlSourceFileCityObjectProjection`、`CityGmlParsedSurfaceReader`。 |
| `ParsedCityObject ->` origin / bounds / altitude metrics | Projection support resolvers | 副作用や target knowledge なしで derived value を返す。 | `CityObject*Resolver` helpers。 |
| `ParsedCityObject -> ConstructionCityObjectDraft` | Application projection preparation | tessellation 前に project-stage face role と generated-surface draft を解く。 | `ConstructionCityObjectDraft.FromParsedCityObject`、`GeneratedLod1RoofCityObjectFactory`。 |
| `ParsedSurface[] -> ConstructionFace[]` | Surface projection policy | culling、roof generation、material role decision、tessellation の共通入力を作る。 | `ConstructionCityObjectDraft`、`CityGmlSurfaceProjectionPolicy`。 |
| `ParsedSurface + material context -> resolved surface material` | Surface material resolver | target material planning 前に semantic、payload、default material、overlay、role を結合する。 | `ResolvedSurfaceMaterial`、`ResolvedMaterial`、`CityGmlSurfaceMaterialResolver`。 |
| Resolved surface material -> material identity | Material identity selector | prepared texture URI に依存せず common / dedicated / vertex / wireframe identity を選ぶ。 | `MaterialBinding`、`DefaultMaterialResolver`、`CommonMaterialCatalog`。 |
| Resolved surface material -> texture provider | Texture provider resolver | material identity を変えず、dataset、atlas、generated terrain、bundled、または texture なしを選ぶ。 | `TexturePayload`、`TerrainTextureAssetGenerator`、non-DEM bake models。 |
| Material identity + texture provider -> renderer binding | Renderer binding composer | direct material binding、main texture override、terrain property-block input を合成する。 | `ResoniteSceneMaterialPlanComposer`。 |
| Renderer binding + target capabilities -> target material asset plan | Resonite material planner | material component、reusable asset、dedicated slot、texture member の pure plan を作る。 | `PlannedSceneMaterialPlan`、`ResoniteMaterialPlanning`。 |
| `ParsedSurface[] + terrain overlay policy -> terrain overlay assignment` | Terrain overlay policy | upload や RPC なしで contained / intersecting / none、split requirement、fallback を決める。 | `DemTerrainOverlayAssignment`、`TerrainOverlayMaterialSourcePartitioner`。 |
| Terrain overlay + mesh context -> terrain overlay mesh code | Terrain overlay mesh-code resolver | actual / requested / requested-bounds fallback order を決定的に保つ。 | `TerrainOverlayMaterialSourcePartitioner`、`TerrainOverlayMeshCodeResolver`。 |
| DEM source-file surfaces + source-file frame -> DEM sampling draft | DEM projection | per-third-mesh output 前に source-file-wide buffer boundary を明示する。 | `CityGmlParsedCityObjectProjection.CreateDemSourceFileTerrainGridSamplingDraft`。 |
| DEM sampling draft + third mesh -> DEM terrain grid object | DEM projection | source-file frame を third-mesh output object と frame に変換する。 | `CityGmlParsedCityObjectProjection.ProjectTerrainMeshModeCityObject`。 |
| DEM terrain object -> `TerrainGridGeometry` | DEM terrain grid projector | sample count、height range、world-base height、coverage、UV contract を作る。 | `CityGmlDemTerrainGridCityObjectProjection.TryProject`。 |
| Imported object stream -> counting stream | Import orchestration | stream を pre-read / materialize せずに unit 数を数える。 | `CountingImportedObjectUnitStream`。 |
| Resonite emission plan -> batch operations | Resonite batch composer | 実行前に RPC-ready operation list を作る。slot / component ID 解決は batch response 後だけにする。 | `PlannedBatchEmission`、`ResoniteBatchEmissionPlanner`、`PlannedBatchEmissionInterpreter`。 |
| Batch response -> canonical batch entity map | Target execution | response から created slot / component locator を解く。 | `CanonicalBatchEntityMap`。 |
| Observed slot snapshot + source-root request -> source-root placement | Setup / readback interpretation | slot creation なしで exact、ancestor、deterministic ambiguity policy を解く。 | `ResoniteSceneSlotSnapshot`、`ResoniteSceneSetupInterpreter`。 |
| Source-root placement + city-object transform -> object slot hierarchy | Resonite placement planner | RPC 前に local position と parent scope を決める。 | `ResoniteBatchEmissionPlanner`、`ResoniteSceneCreationModels`。 |

## Impure 境界

| Impure 処理 | 所有者 | 境界 |
| --- | --- | --- |
| archive extraction と source-file IO | Dataset source resolver | parsed model は source facts と stream を受け取る。IO details を parsed city object に漏らさない。 |
| texture download、image generation、asset upload | Texture provider executor と Resonite texture uploader | texture-provider identity を選んだ後に実行する。planning と upload を分ける。 |
| Resonite RPC | Live-send execution と batch emitter | `ResoniteSceneEmissionPlan` / planned batch output だけを実行する。 |
| existing slot / component readback | Setup interpreter と slot repository | observed snapshot を planning に渡す。pure planning の中に readback を隠さない。 |
| canonical dump generation | Diagnostics sink | execution 後の observable regression truth を記録する。runtime model を dump shape に依存させない。 |
| logging と progress | `ILogger` と progress view | domain value と pure transformation から直接 progress や logging を呼ばない。 |

## リファクタリング指針

- source parsing や material policy から target dependency を取り除ける場合は、小さな record や resolver の追加を優先します。
- 既存 helper の rename は、新しい名前が ownership を明確にし、まだコードが提供していない挙動を示唆しない場合に限ります。
- naming や ownership だけを取り締まるテストは追加しません。可能な限り type、project dependency、centralized analyzer / build policy で表現します。
- 複数の実行経路が同じ概念を emit する場合は、経路ごとの補正ではなく等価な output contract を比較します。
- regression では、提案した原因を反証できる emitted payload、readback、dump artifact を定義してから fix 完了と見なします。
