namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    ITerrainTextureAssetGenerator TerrainTextureAssetGenerator,
    Execution.IResoniteSceneBootstrapInterpreter SceneBootstrapInterpreter,
    Execution.IResoniteDatasetLicenseWriter DatasetLicenseWriter,
    Execution.IResoniteGeometryAssetAssembler GeometryAssetAssembler,
    Execution.IResoniteMaterialPlanning MaterialPlanning,
    Execution.IResoniteBatchEmissionPlanner BatchEmissionPlanner,
    Execution.IResoniteSceneBatchEmitter BatchEmitter,
    Execution.IResoniteSlotCreator SlotCreator,
    IResoniteBufferedCityObjectBakerFactory CityObjectBakerFactory)
{
    internal ResoniteLiveSceneImportDependencies(
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator,
        Execution.IResoniteSceneBootstrapInterpreter sceneBootstrapInterpreter,
        Execution.IResoniteGeometryAssetAssembler geometryAssetAssembler,
        Execution.IResoniteMaterialPlanning materialPlanning,
        Execution.IResoniteBatchEmissionPlanner batchEmissionPlanner,
        Execution.IResoniteSceneBatchEmitter batchEmitter,
        Execution.IResoniteSlotCreator slotCreator,
        IResoniteBufferedCityObjectBakerFactory cityObjectBakerFactory)
        : this(
            clientSession,
            diagnostics,
            terrainTextureAssetGenerator,
            sceneBootstrapInterpreter,
            new Execution.ResoniteDatasetLicenseWriter(),
            geometryAssetAssembler,
            materialPlanning,
            batchEmissionPlanner,
            batchEmitter,
            slotCreator,
            cityObjectBakerFactory)
    {
    }

    internal ResoniteLiveSceneImportDependencies(
        ILiveSendClientSession clientSession,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
        : this(
            clientSession,
            ResoniteLinkSendDiagnostics.Disabled,
            terrainTextureAssetGenerator,
            new Execution.ResoniteSceneBootstrapInterpreter(
                new Execution.ResoniteSceneSlotLocator(),
                new Execution.ResoniteMaterialPlanning(),
                new Execution.ResoniteSceneAnchorResolver()),
            new Execution.ResoniteDatasetLicenseWriter(),
            new Execution.ResoniteGeometryAssetAssembler(),
            new Execution.ResoniteMaterialPlanning(),
            new Execution.ResoniteBatchEmissionPlanner(),
            new Execution.PlannedBatchEmissionInterpreter(),
            new Execution.ResoniteSlotCreator(),
            new ResoniteBufferedCityObjectBakerFactory())
    {
    }
}
