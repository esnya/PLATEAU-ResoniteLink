using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    ITerrainTextureAssetGenerator TerrainTextureAssetGenerator,
    Execution.IResoniteSceneSetupInterpreter SceneSetupInterpreter,
    Execution.IResoniteDatasetLicenseWriter DatasetLicenseWriter,
    Execution.IResoniteGeometryAssetAssembler GeometryAssetAssembler,
    Execution.IResoniteMaterialPlanning MaterialPlanning,
    IResoniteCommonMaterialSetupPreparer CommonMaterialSetupPreparer,
    Execution.IResoniteBatchEmissionPlanner BatchEmissionPlanner,
    Execution.IResoniteSceneBatchEmitter BatchEmitter,
    Execution.IResoniteSlotCreator SlotCreator,
    IResoniteBufferedCityObjectBakerFactory CityObjectBakerFactory);
