namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    ITerrainTextureAssetGenerator TerrainTextureAssetGenerator,
    Execution.IResoniteSceneBootstrapInterpreter SceneBootstrapInterpreter,
    Execution.IResoniteGeometryAssetAssembler GeometryAssetAssembler,
    Execution.IResoniteMaterialPlanning MaterialPlanning,
    Execution.IResoniteBatchEmissionPlanner BatchEmissionPlanner,
    Execution.IResoniteSceneBatchEmitter BatchEmitter,
    Execution.IResoniteSlotCreator SlotCreator,
    IResoniteBufferedCityObjectBakerFactory CityObjectBakerFactory)
{
}
