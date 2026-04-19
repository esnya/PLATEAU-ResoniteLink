namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ITerrainTextureAssetGenerator TerrainTextureAssetGenerator,
    Execution.IResoniteSceneBootstrapInterpreter SceneBootstrapInterpreter,
    Execution.IResoniteGeometryAssetAssembler GeometryAssetAssembler,
    Execution.IResoniteMaterialPlanning MaterialPlanning,
    Execution.IResoniteBatchEmissionPlanner BatchEmissionPlanner,
    Execution.IResoniteSceneBatchEmitter BatchEmitter,
    Execution.IResoniteSlotCreator SlotCreator,
    IResoniteBufferedCityObjectBakerFactory CityObjectBakerFactory)
{
    internal ResoniteLiveSceneImportDependencies(
        ILiveSendClientSession clientSession,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
        : this(
            clientSession,
            terrainTextureAssetGenerator,
            new Execution.ResoniteSceneBootstrapInterpreter(new Execution.ResoniteSceneSlotLocator()),
            new Execution.ResoniteGeometryAssetAssembler(),
            new Execution.ResoniteMaterialPlanning(),
            new Execution.ResoniteBatchEmissionPlanner(),
            new Execution.PlannedBatchEmissionInterpreter(),
            new Execution.ResoniteSlotCreator(),
            new ResoniteBufferedCityObjectBakerFactory())
    {
    }
}
